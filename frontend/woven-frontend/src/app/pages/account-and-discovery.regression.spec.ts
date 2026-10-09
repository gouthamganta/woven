import { ChangeDetectorRef } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ProfilePageComponent } from './profile/profile';
import { SettingsPageComponent } from './settings/settings';
import { MyTilesPageComponent } from './my-tiles/my-tiles.page';
import { MatchProfilePreviewPageComponent } from './matches/match-profile-preview.page';
import { ChatsListComponent } from './chats/chats-list.component';
import { OnboardingService } from '../onboarding/onboarding.service';
import { TilesService, MyTile } from '../services/tiles.service';
import { MatchesService } from '../services/matches.service';
import { ChatListItem, ChatService } from '../services/chat.service';
import { RealtimeService, NewChatMessageEvent } from '../services/realtime.service';
import { PushService } from '../services/push.service';
import { CoachingService } from '../services/coaching.service';

describe('Account and discovery view contracts', () => {
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };
  let cdr: ChangeDetectorRef;
  beforeEach(() => {
    vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-09T12:00:00Z')); localStorage.clear();
    router = { navigateByUrl: vi.fn().mockResolvedValue(true) };
    cdr = { markForCheck: vi.fn(), detectChanges: vi.fn() } as unknown as ChangeDetectorRef;
  });
  afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); localStorage.clear(); });
  const navigation = () => router as unknown as Router;
  const tile = (id: string, overrides: Partial<MyTile> = {}): MyTile => ({
    id, contentType: 'text', contentText: 'Synthetic tile text', mediaUrl: null,
    createdAt: '2026-10-09T10:00:00Z', expiresAt: '2026-10-11T10:00:00Z',
    isExpired: false, isHighlighted: false, isModerated: false, highlightSlot: null, ...overrides,
  });

  describe('Own profile', () => {
    it('sorts photos and highlights, excludes expired recent content and limits the recent list', async () => {
      const review = { publicPreview: { photos: [{ url: '/second', sortOrder: 2 }, { url: '/first', sortOrder: 0 }] } };
      const tiles = [tile('h2', { isHighlighted: true, highlightSlot: 2 }), tile('h1', { isHighlighted: true, highlightSlot: 1 }), tile('expired', { isExpired: true }),
        ...Array.from({ length: 7 }, (_, i) => tile(`recent${i}`))];
      const page = new ProfilePageComponent(navigation(), { getReview: () => of(review) } as unknown as OnboardingService,
        { getMine: () => of({ tiles }) } as unknown as TilesService, cdr);
      await page.ngOnInit();
      expect(page.primaryPhoto?.url).toBe('/first'); expect(page.highlights.map(t => t.id)).toEqual(['h1', 'h2']);
      expect(page.recentTiles).toHaveLength(5); expect(page.recentTiles.some(t => t.id === 'expired')).toBe(false); expect(page.loading).toBe(false);
    });

    for (const shape of ['self', 'flat', 'empty']) {
      it(`handles the ${shape} review response without inventing photos`, async () => {
        const response = shape === 'self' ? { self: { photos: [{ url: '/self' }] } } : shape === 'flat' ? { photos: [{ url: '/flat' }] } : {};
        const page = new ProfilePageComponent(navigation(), { getReview: () => of(response) } as unknown as OnboardingService,
          { getMine: () => throwError(() => new Error('Synthetic tiles failure')) } as unknown as TilesService, cdr);
        await page.ngOnInit(); expect(page.loading).toBe(false); expect(page.error).toBe('');
        expect(page.primaryPhoto?.url ?? null).toBe(shape === 'empty' ? null : `/${shape}`);
      });
    }

    it('surfaces profile failure and leaves the loading state', async () => {
      const page = new ProfilePageComponent(navigation(), { getReview: () => throwError(() => new Error('Synthetic failure')) } as unknown as OnboardingService,
        { getMine: () => of({ tiles: [] }) } as unknown as TilesService, cdr);
      await page.ngOnInit(); expect(page.loading).toBe(false); expect(page.error).toContain("Couldn't load");
    });

    it('formats supplied public chips and opens the requested edit/settings destinations', () => {
      const page = new ProfilePageComponent(navigation(), {} as OnboardingService, {} as TilesService, cdr);
      expect(page.chips).toEqual([]);
      page.profile = { optionalPublic: [{ key: 'job', value: 'Engineer' }, { key: 'custom', value: 'Value' }, { key: '', value: 'Invalid' }, { key: 'pets', value: '' }] };
      expect(page.chips).toEqual([{ label: 'Job', value: 'Engineer' }, { label: 'custom', value: 'Value' }]);
      page.goEdit(); page.goTiles(); page.goSettings(); expect(router.navigateByUrl.mock.calls.map(c => c[0])).toEqual(['/onboarding/review', '/you/tiles', '/you/settings']);
    });
  });

  describe('Account settings', () => {
    function setup() {
      const http = { get: vi.fn().mockReturnValue(of([])), delete: vi.fn().mockReturnValue(of({})) };
      const push = { isSupported: vi.fn().mockResolvedValue(true), isSubscribed: vi.fn().mockResolvedValue(false), register: vi.fn().mockResolvedValue(true), unregister: vi.fn().mockResolvedValue(undefined) };
      const coaching = { optIn: vi.fn().mockReturnValue(of({})), optOut: vi.fn().mockReturnValue(of({})) };
      const page = new SettingsPageComponent(navigation(), http as unknown as HttpClient, cdr, push as unknown as PushService, coaching as unknown as CoachingService);
      return { page, http, push, coaching };
    }

    for (const supported of [false, true]) {
      it(`initializes notification state when browser support is ${supported}`, async () => {
        const { page, push } = setup(); push.isSupported.mockResolvedValue(supported); push.isSubscribed.mockResolvedValue(true);
        await page.ngOnInit(); expect(page.notifSupported).toBe(supported); expect(page.notifEnabled).toBe(supported);
        expect(push.isSubscribed).toHaveBeenCalledTimes(supported ? 1 : 0);
      });
    }

    it('loads blocks once, preserves the current token header and unblocks only the selected user', () => {
      const { page, http } = setup(); localStorage.setItem('accessToken', 'synthetic');
      const a = { userId: 2, name: 'Synthetic A', photo: null, blockedAt: '2026-10-09' };
      const b = { ...a, userId: 3 }; http.get.mockReturnValue(of([a, b]));
      page.toggleBlocks(); page.toggleBlocks(); page.toggleBlocks(); expect(http.get).toHaveBeenCalledTimes(1);
      expect(http.get.mock.calls[0][1].headers.Authorization).toBe('Bearer synthetic');
      page.unblock(a); expect(page.blockedUsers).toEqual([b]); expect(page.unblocking).toBeNull();
    });

    it('clears busy indicators without removing a block after a rejected request', () => {
      const { page, http } = setup(); http.get.mockReturnValue(throwError(() => new Error('Synthetic failure')));
      page.toggleBlocks(); expect(page.loadingBlocks).toBe(false);
      const user = { userId: 2, name: 'Synthetic', photo: null, blockedAt: '2026-10-09' }; page.blockedUsers = [user];
      http.delete.mockReturnValue(throwError(() => new Error('Synthetic failure'))); page.unblock(user);
      expect(page.blockedUsers).toEqual([user]); expect(page.unblocking).toBeNull();
    });

    it('toggles coaching only after successful saves and ignores competing changes', async () => {
      const { page, coaching } = setup(); await page.toggleCoaching(); expect(page.coachingEnabled).toBe(false); await page.toggleCoaching(); expect(page.coachingEnabled).toBe(true);
      coaching.optOut.mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.toggleCoaching(); expect(page.coachingEnabled).toBe(true); expect(page.coachingPending).toBe(false);
      page.coachingPending = true; coaching.optOut.mockClear(); await page.toggleCoaching(); expect(coaching.optOut).not.toHaveBeenCalled();
    });

    it('registers and unregisters notifications, respects unsupported/pending state and recovers rejection', async () => {
      const { page, push } = setup(); await page.toggleNotif(); expect(page.notifEnabled).toBe(true); await page.toggleNotif(); expect(page.notifEnabled).toBe(false);
      page.notifSupported = false; push.register.mockClear(); await page.toggleNotif(); expect(push.register).not.toHaveBeenCalled();
      page.notifSupported = true; page.notifPending = true; await page.toggleNotif(); expect(push.register).not.toHaveBeenCalled();
      page.notifPending = false; push.register.mockRejectedValue(new Error('Synthetic rejection'));
      await expect(page.toggleNotif()).rejects.toThrow('Synthetic rejection'); expect(page.notifPending).toBe(false); expect(page.notifEnabled).toBe(false);
    });

    it('keeps the session after account deletion fails and logs out only after confirmed success', () => {
      const { page, http } = setup(); localStorage.setItem('accessToken', 'synthetic'); localStorage.setItem('user', '{}'); localStorage.setItem('theme', 'dark');
      page.startDelete(); expect(page.deleteStep).toBe(1); page.cancelDelete(); expect(page.deleteStep).toBe(0);
      http.delete.mockReturnValueOnce(throwError(() => new Error('Synthetic failure'))); page.confirmDelete();
      expect(page.deleting).toBe(false); expect(localStorage.getItem('accessToken')).toBe('synthetic'); expect(router.navigateByUrl).not.toHaveBeenCalled();
      page.confirmDelete(); expect(localStorage.getItem('accessToken')).toBeNull(); expect(localStorage.getItem('user')).toBeNull(); expect(localStorage.getItem('theme')).toBe('dark');
      expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    });

    it('performs explicit logout and back navigation', () => {
      const { page } = setup(); localStorage.setItem('accessToken', 'synthetic'); localStorage.setItem('user', '{}');
      page.back(); page.logout(); expect(localStorage.getItem('accessToken')).toBeNull(); expect(localStorage.getItem('user')).toBeNull();
      expect(router.navigateByUrl.mock.calls.map(c => c[0])).toEqual(['/you', '/login']);
    });
  });

  describe('Own tiles', () => {
    function setup() {
      const api = { getMine: vi.fn().mockReturnValue(of({ tiles: [] })), getReceivedOrbits: vi.fn().mockReturnValue(of([])), create: vi.fn().mockReturnValue(of({ tileId: 'new' })), highlight: vi.fn().mockReturnValue(of({})), unhighlight: vi.fn().mockReturnValue(of({})) };
      return { page: new MyTilesPageComponent(api as unknown as TilesService, navigation(), cdr), api };
    }

    it('separates active/expired/highlighted content and aggregates orbit counts per tile', async () => {
      const { page, api } = setup(); api.getMine.mockReturnValue(of({ tiles: [tile('active'), tile('expired', { isExpired: true }), tile('h2', { isHighlighted: true, highlightSlot: 2 }), tile('h1', { isHighlighted: true, highlightSlot: 1 })] }));
      api.getReceivedOrbits.mockReturnValue(of([{ tileId: 'active' }, { tileId: 'active' }, { tileId: 'h1' }])); await page.ngOnInit();
      expect(page.active.map(t => t.id)).toEqual(['active']); expect(page.expired.map(t => t.id)).toEqual(['expired']); expect(page.highlighted.map(t => t.id)).toEqual(['h1', 'h2']);
      expect([...page.occupiedSlots]).toEqual([1, 2]); expect(page.orbitMap.get('active')).toBe(2); expect(page.loading).toBe(false);
    });

    it('tolerates unavailable orbit counts but exposes tile-loading failure', async () => {
      const { page, api } = setup(); api.getReceivedOrbits.mockReturnValue(throwError(() => new Error('Synthetic outage')));
      await page.ngOnInit(); expect(page.loading).toBe(false); expect(page.toast).toBe('');
      api.getMine.mockReturnValue(throwError(() => new Error('Synthetic outage'))); await page.ngOnInit(); expect(page.toast).toContain('Could not load');
    });

    for (const length of [0, 9, 151]) {
      it(`does not post invalid ${length}-character content`, async () => {
        const { page, api } = setup(); page.composeText = 'x'.repeat(length); await page.post(); expect(api.create).not.toHaveBeenCalled();
      });
    }

    for (const length of [10, 150]) {
      it(`posts trimmed ${length}-character content and refreshes the list`, async () => {
        const { page, api } = setup(); page.composeText = ` ${'x'.repeat(length)} `; await page.post();
        expect(api.create).toHaveBeenCalledWith('text', 'x'.repeat(length)); expect(page.composeText).toBe(''); expect(page.posting).toBe(false); expect(api.getMine).toHaveBeenCalled();
        await vi.advanceTimersByTimeAsync(2500); expect(page.toast).toBe('');
      });
    }

    it('preserves the draft on failed posting and prevents overlapping submissions', async () => {
      const { page, api } = setup(); page.composeText = 'Synthetic tile text'; page.posting = true; await page.post(); expect(api.create).not.toHaveBeenCalled();
      page.posting = false; api.create.mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.post(); expect(page.composeText).toBe('Synthetic tile text'); expect(page.toast).toContain('Could not post'); expect(page.posting).toBe(false);
    });

    it('pins only the chosen tile/slot, closes the picker and permits unpinning', async () => {
      const { page, api } = setup(); await page.confirmPin(1); expect(api.highlight).not.toHaveBeenCalled();
      page.openSlotPicker(tile('selected')); page.slotWorking = 2; await page.confirmPin(1); expect(api.highlight).not.toHaveBeenCalled(); page.slotWorking = null;
      await page.confirmPin(3); expect(api.highlight).toHaveBeenCalledWith('selected', 3); expect(page.pickingFor).toBeNull(); expect(page.working).toBeNull();
      await page.unpinTile(tile('selected')); expect(api.unhighlight).toHaveBeenCalledWith('selected'); expect(page.working).toBeNull();
    });

    it('recovers rejected pin/unpin operations and does not overlap a working operation', async () => {
      const { page, api } = setup(); page.openSlotPicker(tile('selected')); api.highlight.mockReturnValueOnce(throwError(() => ({ error: { error: 'SLOT_OCCUPIED' } })));
      await page.confirmPin(1); expect(page.toast).toBe('SLOT_OCCUPIED'); expect(page.working).toBeNull();
      page.openSlotPicker(tile('selected')); api.highlight.mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.confirmPin(1); expect(page.toast).toBe('Could not pin tile');
      page.working = 'other'; await page.unpinTile(tile('selected')); expect(api.unhighlight).not.toHaveBeenCalled();
      page.working = null; api.unhighlight.mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.unpinTile(tile('selected')); expect(page.toast).toBe('Could not unpin'); expect(page.working).toBeNull();
    });

    it('formats expiry boundaries and opens the own-profile destination', () => {
      const { page } = setup(); expect(page.hoursLeft('2026-10-09T11:59:59Z')).toBe('Expiring'); expect(page.hoursLeft('2026-10-09T12:30:00Z')).toBe('30m left'); expect(page.hoursLeft('2026-10-09T14:00:00Z')).toBe('2h left');
      page.back(); expect(router.navigateByUrl).toHaveBeenCalledWith('/you');
    });
  });

  describe('Match profile preview', () => {
    function setup(id: string | null = 'match') {
      const api = { profile: vi.fn().mockReturnValue(of({ matchId: 'match', accessLevel: 'LIMITED', publicPreview: {} })) };
      const route = { snapshot: { paramMap: { get: () => id } } } as unknown as ActivatedRoute;
      return { page: new MatchProfilePreviewPageComponent(route, navigation(), api as unknown as MatchesService), api };
    }
    it('rejects a missing route identifier before requesting a profile', async () => {
      const { page, api } = setup(null); await page.ngOnInit(); expect(page.error).toBe('Missing match id'); expect(page.loading).toBe(false); expect(api.profile).not.toHaveBeenCalled();
    });
    it('loads the selected match and clears prior caption state', async () => {
      const { page, api } = setup(); page.captionText = 'stale'; page.captionOpenIndex = 1; await page.ngOnInit();
      expect(api.profile).toHaveBeenCalledWith('match'); expect(page.captionText).toBe(''); expect(page.captionOpenIndex).toBeNull(); expect(page.loading).toBe(false);
    });
    for (const error of [{ error: { error: 'ACCESS_DENIED' } }, { error: { message: 'Synthetic message' } }, { message: 'Synthetic failure' }, {}]) {
      it(`surfaces a ${JSON.stringify(error)} failure without staying in loading`, async () => {
        const { page, api } = setup(); api.profile.mockReturnValue(throwError(() => error)); await page.ngOnInit(); expect(page.error).not.toBe(''); expect(page.loading).toBe(false);
      });
    }
    it('supports empty preview data, caption toggling, field labels and access hints', () => {
      const { page } = setup(); expect(page.publicName).toBe('—'); expect(page.publicAge).toBe(''); expect(page.publicPhotos).toEqual([]); expect(page.publicOptional).toEqual([]); expect(page.basicsChips).toEqual([]); expect(page.accessHint).toBe('');
      page.toggleCaption(0, ' Synthetic caption '); expect(page.captionText).toBe('Synthetic caption'); page.toggleCaption(0); expect(page.captionOpenIndex).toBeNull(); page.toggleCaption(1); expect(page.captionText).toBe('No caption added.');
      expect(page.prettyKey('pref_love_language')).toBe('Love Language'); expect(page.prettyKey('')).toBe('Field'); page.back(); expect(router.navigateByUrl).toHaveBeenCalledWith('/chats');
    });
    it('normalizes supplied preview fields and sorted valid photos without inventing missing values', () => {
      const { page } = setup(); page.data = { matchId: 'match', accessLevel: 'FULL', reason: 'mutual', publicPreview: { name: ' Synthetic ', age: 25, gender: ' nonbinary ', location: ' City ', bio: ' Bio ', intent: { primaryIntent: 'friendship', openness: ['long_term'] },
        optionalPublic: [{ key: 'job', value: 'Engineer' }, { key: '', value: 'ignored' }], photos: [{ url: '/two', sortOrder: 2 }, { url: '/one', sortOrder: 1 }, { url: '', sortOrder: 0 }] } };
      expect(page.publicName).toBe('Synthetic'); expect(page.publicAge).toBe('25'); expect(page.publicBio).toBe('Bio'); expect(page.publicIntentOpennessLine).toBe('Open to: long_term');
      expect(page.basicsChips).toEqual(['nonbinary', 'City', 'friendship']); expect(page.publicPhotos.map(p => p.url)).toEqual(['/one', '/two']); expect(page.publicOptional).toEqual([{ key: 'job', value: 'Engineer' }]); expect(page.accessHint).toContain('Full');
      page.data.accessLevel = 'LIMITED'; expect(page.accessHint).toContain('Limited');
    });
  });

  describe('Conversation list', () => {
    function setup() {
      const messages = new Subject<NewChatMessageEvent>();
      const api = { list: vi.fn().mockReturnValue(of({ chats: [], meUserId: 1 })) };
      const matches = { pop: vi.fn().mockReturnValue(of({})) };
      const page = new ChatsListComponent(api as unknown as ChatService, matches as unknown as MatchesService, navigation(), cdr,
        { newChatMessage$: messages } as unknown as RealtimeService);
      return { page, api, matches, messages };
    }
    const conversation = (override: Partial<ChatListItem> = {}): ChatListItem => ({ threadId: 'thread', matchId: 'match', matchType: 'PURE', ...override });
    it('reorders a known thread on a realtime message and unsubscribes on destruction', async () => {
      const { page, api, messages } = setup(); api.list.mockReturnValue(of({ chats: [conversation(), conversation({ threadId: 'other' })], meUserId: 1 })); await page.ngOnInit();
      messages.next({ threadId: 'other', messageId: 'message', body: 'Synthetic text', senderUserId: 2, createdAt: '2026-10-09T12:00:00Z' });
      expect(page.chats[0].threadId).toBe('other'); expect(page.unreadThreadIds.has('other')).toBe(true);
      messages.next({ threadId: 'unknown', messageId: 'ignored', body: 'Synthetic', senderUserId: 2, createdAt: '2026-10-09T12:00:00Z' }); expect(page.chats).toHaveLength(2);
      page.open(page.chats[0]); expect(page.unreadThreadIds.has('other')).toBe(false); expect(router.navigateByUrl).toHaveBeenCalledWith('/chats/other');
      await vi.advanceTimersByTimeAsync(1000); expect(page.now).toBe(Date.now()); page.ngOnDestroy(); expect(messages.observed).toBe(false);
    });
    it('handles empty responses and recovers loading errors', async () => {
      const { page, api } = setup(); api.list.mockReturnValue(of({})); await page.load(); expect(page.chats).toEqual([]); expect(page.meUserId).toBe(0); expect(page.countText).toBe('0 balloons');
      api.list.mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.load(); expect(page.error).toBe('Could not load balloons.'); expect(page.loading).toBe(false); page.ngOnDestroy();
    });
    it('formats trial and Find Love states with countdown boundaries', () => {
      const { page } = setup(); expect(page.statusLine(conversation({ isTrial: true, trialSecondsLeft: 61 }))).toContain('01:01'); expect(page.statusLine(conversation({ isTrial: true }))).toContain('Trial ended');
      expect(page.statusLine(conversation())).toContain('warming up'); expect(page.findLoveChip(conversation())).toBe('Balloon'); expect(page.findLoveChip(conversation({ isTrial: true }))).toBe('Trial');
      const future = conversation({ findLoveAt: '2026-10-09T12:01:01Z' }); expect(page.statusLine(future)).toContain('01:01'); expect(page.findLoveChip(future)).toContain('Opens'); expect(page.isFindLoveReady(future)).toBe(false);
      const ready = conversation({ findLoveAt: '2026-10-09T12:00:00Z' }); expect(page.statusLine(ready)).toContain('open'); expect(page.findLoveChip(ready)).toBe('Find Love'); expect(page.isFindLoveReady(ready)).toBe(true); expect(page.countdown('2026-10-09T11:00:00Z')).toBe('00:00');
      expect(page.canPop(conversation())).toBe(true); expect(page.canPop(ready)).toBe(false); expect(page.canPop(conversation({ isTrial: true }))).toBe(false); page.ngOnDestroy();
    });
    it('distinguishes own/partner turns, fallback names and recent presence boundaries', () => {
      const { page } = setup(); page.meUserId = 1; expect(page.headline(conversation())).toBe('Someone'); expect(page.isMyTurn(conversation())).toBe(false); expect(page.isOnline(conversation())).toBe(false); expect(page.lastSeenLabel(conversation())).toBeNull();
      const other = { userId: 2, fullName: 'Synthetic', lastActiveAt: '2026-10-09T11:59:00Z' }; const item = conversation({ other, lastMessage: { body: 'Synthetic', senderUserId: 2, createdAt: '2026-10-09T12:00:00Z' } });
      expect(page.headline(item)).toBe('Synthetic'); expect(page.isMyTurn(item)).toBe(true); expect(page.isOnline(item)).toBe(true); expect(page.lastSeenLabel(item)).toBe('Online now');
      other.lastActiveAt = '2026-10-09T11:30:00Z'; expect(page.lastSeenLabel(item)).toBe('Active 30m ago'); other.lastActiveAt = '2026-10-09T10:00:00Z'; expect(page.lastSeenLabel(item)).toBe('Active 2h ago'); other.lastActiveAt = '2026-10-07T12:00:00Z'; expect(page.lastSeenLabel(item)).toBeNull(); page.ngOnDestroy();
    });
    it('pops the selected eligible match, ignores invalid identifiers and recovers failure', async () => {
      const { page, matches } = setup(); const event = new MouseEvent('click'); await page.pop(conversation({ findLoveAt: '2026-10-09T12:00:00Z' }), event); await page.pop(conversation({ matchId: '' }), event); expect(matches.pop).not.toHaveBeenCalled();
      await page.pop(conversation(), event); expect(matches.pop).toHaveBeenCalledWith('match'); expect(page.isBusy(conversation())).toBe(false);
      matches.pop.mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.pop(conversation(), event); expect(page.error).toBe('Could not pop balloon.');
      page.viewProfile(conversation({ matchId: '' }), event); expect(router.navigateByUrl).not.toHaveBeenCalled(); page.viewProfile(conversation(), event); expect(router.navigateByUrl).toHaveBeenCalledWith('/matches/match/profile'); page.ngOnDestroy();
    });
    it('ignores overlapping pop requests while the current operation is unresolved', async () => {
      const { page, matches } = setup(); const pending = new Subject<object>(); matches.pop.mockReturnValue(pending); const first = page.pop(conversation(), new MouseEvent('click'));
      expect(page.isBusy(conversation())).toBe(true); await page.pop(conversation({ matchId: 'other' }), new MouseEvent('click')); expect(matches.pop).toHaveBeenCalledTimes(1);
      pending.next({}); pending.complete(); await first; expect(page.isBusy(conversation())).toBe(false); page.chats = [conversation()]; expect(page.countText).toBe('1 balloon'); page.ngOnDestroy();
    });
  });
});
