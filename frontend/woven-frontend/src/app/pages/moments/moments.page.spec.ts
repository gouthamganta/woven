import { ChangeDetectorRef } from '@angular/core';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { MomentsPageComponent } from './moments.page';
import { MomentsCard, MomentsService } from '../../services/moments.service';
import { ChatService } from '../../services/chat.service';

describe('Moments interaction state machine', () => {
  const card: MomentsCard = { userId: 2, fullName: 'Synthetic Adult', profilePhoto: '/synthetic.jpg' };
  let page: MomentsPageComponent;
  let moments: { getMoments: ReturnType<typeof vi.fn>; getLikedYou: ReturnType<typeof vi.fn>; respond: ReturnType<typeof vi.fn>; choose: ReturnType<typeof vi.fn> };
  let chat: { start: ReturnType<typeof vi.fn> };
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    vi.useFakeTimers();
    moments = {
      getMoments: vi.fn().mockReturnValue(of({ cards: [card], budget: { totalUsed: 0, totalRemaining: 5, totalCap: 5 }, sparkBalance: 5, moodLine: 'Synthetic mood' })),
      getLikedYou: vi.fn().mockReturnValue(of({ cards: [{ ...card, likedAt: '2026-10-08', expiresInHours: 72 }] })),
      respond: vi.fn().mockReturnValue(of({ status: 'PASSED' })),
      choose: vi.fn().mockReturnValue(of({ status: 'WAITING_FOR_OTHER_NOTE', sparkBalance: 4 })),
    };
    chat = { start: vi.fn().mockReturnValue(of({ threadId: 'synthetic-thread' })) };
    router = { navigateByUrl: vi.fn().mockResolvedValue(true) };
    page = new MomentsPageComponent(moments as unknown as MomentsService, chat as unknown as ChatService,
      router as unknown as Router, { markForCheck: vi.fn() } as unknown as ChangeDetectorRef);
  });
  afterEach(() => { page.ngOnDestroy(); vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); });

  it('loads the deck, budget and balance and clears previous response visibility', async () => {
    page.respondedUserIds.add(2);
    await page.ngOnInit();
    expect(page.visibleTodayCards).toEqual([card]);
    expect(page.budget?.totalRemaining).toBe(5);
    expect(page.sparkBalance).toBe(5);
    expect(page.loading).toBe(false);
    expect(page.cardShownAt.has(2)).toBe(true);
  });

  it('renders an empty response without stale cards or budget', async () => {
    moments.getMoments.mockReturnValue(of({}));
    await page.loadToday();
    expect(page.todayCards).toEqual([]);
    expect(page.budget).toBeNull();
    expect(page.sparkBalance).toBeNull();
    expect(page.moodLine).toBeNull();
  });

  it('leaves loading state and exposes a retryable deck error on failure', async () => {
    moments.getMoments.mockReturnValue(throwError(() => new Error('Synthetic outage')));
    await page.loadToday();
    expect(page.loading).toBe(false);
    expect(page.error).toContain("Couldn't load");
  });

  it('loads Drawn once and reuses it when switching tabs', async () => {
    await page.switchTab('liked-you');
    await page.switchTab('today');
    await page.switchTab('liked-you');
    expect(moments.getLikedYou).toHaveBeenCalledTimes(1);
    expect(page.visibleLikedYouCards).toHaveLength(1);
  });

  it('allows retry after a Drawn loading failure', async () => {
    moments.getLikedYou.mockReturnValueOnce(throwError(() => new Error('Synthetic outage')));
    await page.switchTab('liked-you');
    expect(page.likedYouLoaded).toBe(false);
    expect(page.loadingLikedYou).toBe(false);
    expect(page.likedYouError).not.toBe('');
    await page.switchTab('liked-you');
    expect(page.likedYouLoaded).toBe(true);
    expect(page.likedYouError).toBe('');
  });

  it('handles an empty Drawn response', async () => {
    moments.getLikedYou.mockReturnValue(of({}));
    await page.loadLikedYou();
    expect(page.visibleLikedYouCards).toEqual([]);
  });

  for (const [action, choice] of [['magical', 'MAGICAL'], ['logical', 'LOGICAL']] as const) {
    it(`opens a ${choice} note after the animation and prevents competing clicks`, async () => {
      await page.loadToday();
      page.choose(card, action);
      page.choose({ ...card, userId: 3 }, 'magical');
      expect(page.overlayCard).toBeNull();
      await vi.advanceTimersByTimeAsync(420);
      expect(page.overlayCard?.userId).toBe(2);
      expect(page.overlayChoice).toBe(choice);
      expect(page.overlaySource).toBe('TODAY');
      expect(page.overlayTimeOnCardMs).toBe(420);
      expect(moments.choose).not.toHaveBeenCalled();
    });

    it(`opens a Drawn ${choice} note without inventing time-on-card`, async () => {
      const drawn = { ...card, likedAt: '2026-10-08', expiresInHours: 72 };
      page.chooseLikedYou(drawn, action);
      page.chooseLikedYou({ ...drawn, userId: 3 }, action);
      await vi.advanceTimersByTimeAsync(420);
      expect(page.overlayCard?.userId).toBe(2);
      expect(page.overlayChoice).toBe(choice);
      expect(page.overlaySource).toBe('LIKED_YOU');
      expect(page.overlayTimeOnCardMs).toBeNull();
    });
  }

  it('backs out of a note without spending or hiding the candidate', async () => {
    page.choose(card, 'magical');
    await vi.advanceTimersByTimeAsync(420);
    page.onOverlayBack();
    expect(page.overlayCard).toBeNull();
    expect(page.overlayChoice).toBeNull();
    expect(moments.choose).not.toHaveBeenCalled();
    expect(page.respondedUserIds.size).toBe(0);
  });

  it('ignores a submission with no selected note/card', async () => {
    await page.onOverlaySubmit('Synthetic opening note');
    expect(moments.choose).not.toHaveBeenCalled();
  });

  it('sends PASS after the animation with measured dwell and completes the deck', async () => {
    await page.loadToday();
    page.choose(card, 'pass');
    await vi.advanceTimersByTimeAsync(260);
    expect(moments.respond).toHaveBeenCalledWith({ targetUserId: 2, choice: 'PASS', source: 'TODAY', timeOnCardMs: 260 });
    expect(page.visibleTodayCards).toEqual([]);
    expect(page.deckCompleted).toBe(true);
  });

  it('restores the card and incomplete deck when PASS fails', async () => {
    await page.loadToday();
    moments.respond.mockReturnValue(throwError(() => new Error('Synthetic rejection')));
    page.choose(card, 'pass');
    await vi.advanceTimersByTimeAsync(260);
    expect(page.visibleTodayCards).toEqual([card]);
    expect(page.toast).toContain('Try again');
    expect(page.deckCompleted).toBe(false);
  });

  for (const source of ['TODAY', 'LIKED_YOU'] as const) {
    it(`restores ${source} visibility after a rejected note`, async () => {
      await page.loadToday();
      page.overlayCard = card;
      page.overlayChoice = 'MAGICAL';
      page.overlaySource = source;
      moments.choose.mockReturnValue(throwError(() => new Error('Synthetic rejection')));
      await page.onOverlaySubmit('Synthetic opening note');
      expect(source === 'TODAY' ? page.respondedUserIds.has(2) : page.respondedLikedYouIds.has(2)).toBe(false);
      expect(page.toast).toContain('Try again');
    });
  }

  for (const [status, type] of [['PURE_MATCH_CREATED', 'PURE'], ['EDGE_MATCH_CREATED', 'EDGE']]) {
    it(`starts the thread after ${status}`, async () => {
      page.overlayCard = card;
      page.overlayChoice = 'MAGICAL';
      moments.choose.mockReturnValue(of({ status, matchType: type, matchId: 'match', sparkBalance: 0 }));
      await page.onOverlaySubmit('Synthetic opening note');
      expect(chat.start).toHaveBeenCalledWith('match');
      expect(router.navigateByUrl).toHaveBeenCalledWith('/chats/synthetic-thread');
      expect(page.sparkBalance).toBe(0);
    });
  }

  for (const choice of ['MAGICAL', 'LOGICAL'] as const) {
    for (const status of ['WAITING_FOR_OTHER_NOTE', 'CHOICE_RECORDED']) {
      it(`records ${choice}/${status} without starting a chat early`, async () => {
        page.overlayCard = card;
        page.overlayChoice = choice;
        page.overlaySource = 'LIKED_YOU';
        moments.choose.mockReturnValue(of({ status }));
        await page.onOverlaySubmit('Synthetic opening note');
        expect(page.respondedLikedYouIds.has(2)).toBe(true);
        expect(chat.start).not.toHaveBeenCalled();
        expect(page.toast).not.toBe('');
        await vi.advanceTimersByTimeAsync(2500);
        expect(page.toast).toBe('');
      });
    }
  }

  it('exposes chat-start failure without navigating to an invented thread', async () => {
    page.overlayCard = card;
    page.overlayChoice = 'MAGICAL';
    moments.choose.mockReturnValue(of({ status: 'PURE_MATCH_CREATED', matchId: 'match' }));
    chat.start.mockReturnValue(throwError(() => new Error('Synthetic chat outage')));
    await page.onOverlaySubmit('Synthetic opening note');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(page.toast).toContain('Try again');
  });

  it('opens the photo gallery only after a held press and supports closing', async () => {
    page.onPhotoLongPressStart({ ...card, photos: ['/one', '/two'] }, new Event('pointerdown'));
    await vi.advanceTimersByTimeAsync(500);
    expect(page.galleryPhotos).toEqual(['/one', '/two']);
    page.setGalleryIndex(1);
    expect(page.galleryIndex).toBe(1);
    page.closeGallery();
    expect(page.galleryPhotos).toEqual([]);
    expect(page.galleryIndex).toBe(0);
  });

  it('cancels an interrupted hold and tolerates profiles without photos', async () => {
    page.onPhotoLongPressStart(card, new Event('pointerdown'));
    page.onPhotoLongPressEnd();
    page.onPhotoLongPressEnd();
    await vi.advanceTimersByTimeAsync(500);
    expect(page.galleryPhotos).toEqual([]);
    page.onPhotoLongPressStart({ userId: 3, fullName: 'Synthetic' }, new Event('pointerdown'));
    await vi.advanceTimersByTimeAsync(500);
    expect(page.galleryPhotos).toEqual([]);
  });

  it('falls back to the profile photo and formats optional location and expiry boundaries', () => {
    expect(page.hasCinematic(card)).toBe(false);
    expect(page.getCinematicPhoto(card)).toBe('/synthetic.jpg');
    expect(page.getCinematicPhoto({ userId: 3, fullName: 'Synthetic' })).toBe('');
    expect(page.locationLine(card)).toBe('');
    expect(page.locationLine({ ...card, location: { city: ' City ', state: ' State ' } })).toBe('City, State');
    expect(page.expiryLabel(0)).toBe('Expiring soon');
    expect(page.expiryLabel(23)).toBe('23h left');
    expect(page.expiryLabel(72)).toBe('3d left');
  });

  it('cycles cinematic photos and stops its repeating timer on destruction', async () => {
    const cinematic = { ...card, kenBurnsPhotoUrls: ['/one', '/two'] };
    moments.getMoments.mockReturnValue(of({ cards: [cinematic] }));
    await page.ngOnInit();
    expect(page.hasCinematic(cinematic)).toBe(true);
    expect(page.getCinematicPhoto(cinematic)).toBe('/one');
    await vi.advanceTimersByTimeAsync(2500);
    expect(page.getCinematicPhoto(cinematic)).toBe('/two');
    page.ngOnDestroy();
    await vi.advanceTimersByTimeAsync(5000);
    expect(page.getCinematicPhoto(cinematic)).toBe('/two');
  });
});
