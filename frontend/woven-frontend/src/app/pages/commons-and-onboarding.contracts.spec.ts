import { ChangeDetectorRef, NgZone } from '@angular/core';
import { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { CommonsPageComponent } from './commons/commons.page';
import { CommonsService, CommonsTile } from '../services/commons.service';
import { FoundationalComponent } from '../onboarding/foundational.component';
import { OnboardingService } from '../onboarding/onboarding.service';
import { PhotosPageComponent } from './onboarding/photos/photos.page';

describe('Commons and onboarding code contracts', () => {
  let cdr: ChangeDetectorRef;
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };
  beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-09T12:00:00Z')); sessionStorage.clear(); cdr = { markForCheck: vi.fn(), detectChanges: vi.fn() } as unknown as ChangeDetectorRef; router = { navigateByUrl: vi.fn().mockResolvedValue(true) }; vi.spyOn(console, 'log').mockImplementation(() => {}); vi.spyOn(console, 'error').mockImplementation(() => {}); });
  afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals(); sessionStorage.clear(); });
  const navigation = () => router as unknown as Router;
  const tile = (id = 'tile'): CommonsTile => ({ tileId: id, contentType: 'text', contentText: 'Unit text', mediaUrl: null, createdAt: '2026-10-09T12:00:00Z', similarity: 0.7 });

  describe('Commons', () => {
    function setup() {
      const api = { getFeed: vi.fn().mockReturnValue(of({ sessionId: 'session', tiles: [tile()] })), refresh: vi.fn().mockReturnValue(of({})), recordView: vi.fn().mockReturnValue(of({})), orbitTile: vi.fn().mockReturnValue(of({ mutualDetected: false })), createTile: vi.fn().mockReturnValue(of({})) };
      return { page: new CommonsPageComponent(api as unknown as CommonsService, cdr), api };
    }
    it('loads and persists a reusable feed session with pagination state', async () => {
      const { page, api } = setup(); await page.ngOnInit(); expect(page.sessionId).toBe('session'); expect(page.tiles).toHaveLength(1); expect(page.hasMore).toBe(false); expect(page.energyPct).toBe(99);
      expect(Object.values(sessionStorage)).toContain('session'); page.ngOnDestroy();
    });
    it('appends subsequent pages and ignores overlapping or completed pagination', async () => {
      const { page, api } = setup(); api.getFeed.mockReturnValueOnce(of({ sessionId: 'session', tiles: Array.from({ length: 20 }, (_, i) => tile(String(i))) })); await page.loadFeed(); expect(page.hasMore).toBe(true);
      await page.loadMore(); expect(page.page).toBe(2); expect(page.tiles).toHaveLength(21); expect(api.getFeed).toHaveBeenLastCalledWith(2, 'session'); await page.loadMore(); expect(api.getFeed).toHaveBeenCalledTimes(2);
      page.hasMore = true; page.loadingMore = true; await page.loadMore(); expect(api.getFeed).toHaveBeenCalledTimes(2); page.ngOnDestroy();
    });
    for (const status of [429, 500]) {
      it(`handles feed status${status} without leaving loading indicators active`, async () => {
        const { page, api } = setup(); api.getFeed.mockReturnValue(throwError(() => ({ status }))); await page.loadFeed(); expect(page.loading).toBe(false);
        if (status === 429) { expect(page.energyDepleted).toBe(true); expect(page.energyPct).toBe(0); } else expect(page.error).not.toBe('');
        page.hasMore = true; await page.loadMore(); expect(page.loadingMore).toBe(false); if (status === 429) expect(page.hasMore).toBe(false); page.ngOnDestroy();
      });
    }
    it('refreshes once, clears old session/energy state and tolerates refresh rejection', async () => {
      const { page, api } = setup(); page.tilesViewedToday = 100; page.sessionId = 'old'; api.refresh.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.refresh();
      expect(api.getFeed).toHaveBeenCalledWith(1, undefined); expect(page.tilesViewedToday).toBe(1); expect(page.refreshing).toBe(false); page.refreshing = true; await page.refresh(); expect(api.refresh).toHaveBeenCalledTimes(1); page.ngOnDestroy();
    });
    it('records controlled dwell on close or destruction and tolerates view-write rejection', () => {
      const { page, api } = setup(); vi.spyOn(performance, 'now').mockReturnValueOnce(100).mockReturnValueOnce(500); page.openTile(tile()); page.closeDrawer(); expect(api.recordView).toHaveBeenCalledWith('tile', 400); expect(page.activeTile).toBeNull(); page.closeDrawer();
      api.recordView.mockReturnValue(throwError(() => new Error('Unit failure'))); vi.spyOn(performance, 'now').mockReturnValue(1000); page.openTile(tile('second')); page.ngOnDestroy(); expect(api.recordView).toHaveBeenCalledTimes(2);
    });
    for (const mutual of [false, true]) {
      it(`records a ${mutual ? 'mutual' : 'one-way'} orbit only once`, async () => {
        const { page, api } = setup(); api.orbitTile.mockReturnValue(of({ mutualDetected: mutual })); page.orbitTile(tile()); page.orbitTile(tile()); expect(api.orbitTile).toHaveBeenCalledTimes(1); expect(page.orbitedTiles.has('tile')).toBe(true); expect(page.orbitingTiles.size).toBe(0); await vi.advanceTimersByTimeAsync(2500); expect(page.toast).toBe(''); page.ngOnDestroy();
      });
    }
    for (const error of [{ error: { error: 'ALREADY_ORBITED' } }, { status: 429 }, { error: { error: 'CANNOT_ORBIT_OWN_TILE' } }, {}]) {
      it(`clears orbit busy state on ${JSON.stringify(error)}`, () => {
        const { page, api } = setup(); api.orbitTile.mockReturnValue(throwError(() => error)); page.orbitTile(tile()); expect(page.orbitingTiles.size).toBe(0); if ('error' in error && error.error?.error === 'ALREADY_ORBITED') expect(page.orbitedTiles.has('tile')).toBe(true); page.ngOnDestroy();
      });
    }
    it('guards an unresolved orbit and validates post lengths before forwarding trimmed content', async () => {
      const { page, api } = setup(); const pending = new Subject<object>(); api.orbitTile.mockReturnValue(pending); page.orbitTile(tile()); page.orbitTile(tile()); expect(api.orbitTile).toHaveBeenCalledTimes(1); pending.next({}); pending.complete();
      page.composeText = 'short'; await page.postTile(); page.composeText = 'x'.repeat(151); await page.postTile(); expect(api.createTile).not.toHaveBeenCalled();
      page.composeText = ' Unit tile content '; await page.postTile(); expect(api.createTile).toHaveBeenCalledWith('text', 'Unit tile content'); expect(page.composeText).toBe('');
      page.posting = true; page.composeText = 'Unit tile content'; await page.postTile(); expect(api.createTile).toHaveBeenCalledTimes(1); page.ngOnDestroy();
    });
    it('keeps failed post content and formats age, resonance and energy boundaries', async () => {
      const { page, api } = setup(); page.composeText = 'Unit tile content'; api.createTile.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.postTile(); expect(page.composeText).toBe('Unit tile content'); expect(page.posting).toBe(false); expect(page.toast).toContain('Could not post');
      expect(page.isResonant(tile())).toBe(true); expect(page.isResonant({ ...tile(), similarity: 0.64 })).toBe(false);
      expect(page.timeAgo('2026-10-09T12:00:00Z')).toBe('just now'); expect(page.timeAgo('2026-10-09T11:30:00Z')).toBe('30m ago'); expect(page.timeAgo('2026-10-09T10:00:00Z')).toBe('2h ago'); expect(page.timeAgo('2026-10-07T12:00:00Z')).toBe('2d ago'); page.tilesViewedToday = 200; expect(page.energyPct).toBe(0); page.ngOnDestroy();
    });
  });

  describe('Foundational answers', () => {
    function setup() {
      const api = { getState: vi.fn().mockReturnValue(of({ profileStatus: 'FOUNDATIONAL_DUE', version: 2, hardBlock: false, allowSkip: true })), getFoundationalQuestions: vi.fn().mockReturnValue(of({ version: 2, questions: Array.from({ length: 5 }, (_, i) => ({ id: `q${i}`, text: `Unit question${i}` })) })), submitFoundationalAnswers: vi.fn().mockReturnValue(of({})), deferFoundational: vi.fn().mockReturnValue(of({})) };
      const page = new FoundationalComponent(api as unknown as OnboardingService, navigation(), cdr, { run: (fn: () => unknown) => fn() } as unknown as NgZone); return { page, api };
    }
    it('loads a due set and aligns five answer identifiers while retaining skip policy', async () => {
      const { page } = setup(); expect(page.currentQuestion()).toBeNull(); expect(page.currentAnswer()).toBe(''); await page.ngOnInit(); expect(page.loading).toBe(false); expect(page.hasQuestions()).toBe(true); expect(page.version).toBe(2); expect(page.allowSkip).toBe(true); expect(page.hardBlock).toBe(false); expect(page.currentQuestion()?.id).toBe('q0');
      page.answers[0].answer = 'x'.repeat(30); expect(page.currentLen()).toBe(30); expect(page.remaining()).toBe(370); page.next(); expect(page.currentIndex).toBe(1); page.back(); page.back(); expect(page.currentIndex).toBe(0);
    });
    it('requires the first onboarding set and exposes loading failures', async () => {
      const { page, api } = setup(); api.getState.mockReturnValue(of({ profileStatus: 'INCOMPLETE' })); api.getFoundationalQuestions.mockReturnValue(of({})); await page.ngOnInit(); expect(page.hardBlock).toBe(true); expect(page.allowSkip).toBe(false); expect(page.hasQuestions()).toBe(false);
      api.getState.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.ngOnInit(); expect(page.loading).toBe(false); expect(page.error).toContain('Could not load');
    });
    for (const length of [0, 29, 30, 400, 401]) {
      it(`enforces a ${length}-character answer at navigation and submission`, async () => {
        const { page, api } = setup(); await page.ngOnInit(); for (const answer of page.answers) answer.answer = 'x'.repeat(length); expect(page.canNext()).toBe(length >= 30 && length <= 400);
        await page.submit(); if (length >= 30 && length <= 400) expect(api.submitFoundationalAnswers).toHaveBeenCalled(); else { expect(api.submitFoundationalAnswers).not.toHaveBeenCalled(); expect(page.error).not.toBe(''); }
      });
    }
    it('rejects missing answer sets and duplicate identifiers', async () => {
      const { page, api } = setup(); await page.submit(); expect(page.error).toContain('not ready'); await page.ngOnInit(); page.answers = []; expect(page.canSubmit()).toBe(false); await page.submit(); expect(page.error).toContain('not ready');
      await page.ngOnInit(); for (const answer of page.answers) { answer.questionId = 'duplicate'; answer.answer = 'x'.repeat(30); } await page.submit(); expect(page.error).toContain('Duplicate'); expect(api.submitFoundationalAnswers).not.toHaveBeenCalled();
    });
    it('submits trimmed answers, uses backend or fallback routes and recovers save failure', async () => {
      const { page, api } = setup(); await page.ngOnInit(); for (const answer of page.answers) answer.answer = ` ${'x'.repeat(30)} `; expect(page.canSubmit()).toBe(true); await page.submit(); expect(api.submitFoundationalAnswers.mock.calls[0][0].answers[0].answer).toBe('x'.repeat(30)); expect(router.navigateByUrl).toHaveBeenCalledWith('/home');
      api.submitFoundationalAnswers.mockReturnValue(of({ nextRoute: '/onboarding/review' })); await page.submit(); expect(router.navigateByUrl).toHaveBeenLastCalledWith('/onboarding/review'); api.submitFoundationalAnswers.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.submit(); expect(page.saving).toBe(false); expect(page.error).toContain('Could not save');
    });
    it('defers only when permitted and recovers rejection', async () => {
      const { page, api } = setup(); await page.defer(); expect(api.deferFoundational).not.toHaveBeenCalled(); await page.ngOnInit(); await page.defer(); expect(router.navigateByUrl).toHaveBeenCalledWith('/home');
      api.deferFoundational.mockReturnValue(of({ nextRoute: '/moments' })); await page.defer(); expect(router.navigateByUrl).toHaveBeenLastCalledWith('/moments'); api.deferFoundational.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.defer(); expect(page.saving).toBe(false); expect(page.error).toContain('Could not defer');
    });
  });

  describe('Photo selection', () => {
    function setup() { const api = { savePhotos: vi.fn().mockReturnValue(of({})) }; return { page: new PhotosPageComponent(api as unknown as OnboardingService, navigation()), api }; }
    it('requires the first three photos, supports optional skip and bounds navigation', () => {
      const { page } = setup(); expect(page.canProceed).toBe(false); page.next(); expect(page.error).not.toBe(''); page.skipOptional(); expect(page.stepIndex).toBe(0);
      for (let i = 0; i < 3; i++) { page.slotAt(i).dataUrl = `data:image/png;base64,unit${i}`; page.next(); } expect(page.isOptionalStep).toBe(true); expect(page.canProceed).toBe(true); page.slotAt(3).caption = 'Unit'; page.skipOptional(); expect(page.slotAt(3).caption).toBe('');
      page.jumpTo(-1); page.jumpTo(7); expect(page.stepIndex).toBe(4); page.jumpTo(0); expect(page.shellTitle).toBe('Photo 1'); expect(page.stepNumberForShell).toBe(1); expect(page.stepLabelForShell).toContain('1/6'); expect(page.progressText).toContain('Step'); page.toggleWhy(); expect(page.showWhy).toBe(true); expect(page.shellSubtitle).not.toBe('');
      page.stepIndex = 6; expect(page.isSummary).toBe(true); expect(page.canProceed).toBe(true); expect(page.shellTitle).toBe('Review your photos'); expect(page.stepNumberForShell).toBe(6); expect(page.stepLabelForShell).toBe('Photos'); expect(page.progressText).toContain('Done'); page.goBack(); expect(page.stepIndex).toBe(5);
    });
    it('clears only the selected photo and uses browser history on the first step', () => {
      const { page } = setup(); page.slotAt(0).dataUrl = 'unit'; page.slotAt(1).dataUrl = 'other'; page.slotAt(0).caption = 'Unit'; page.removeCurrent(); expect(page.slotAt(0).dataUrl).toBeNull(); expect(page.slotAt(0).caption).toBe(''); expect(page.slotAt(1).dataUrl).toBe('other');
      const back = vi.spyOn(window.history, 'back').mockImplementation(() => {}); page.goBack(); expect(back).toHaveBeenCalled();
    });
    it('rejects missing/nonimage files before reading and handles controlled file-reader callbacks', () => {
      const { page } = setup(); const target = { files: null, value: 'unit' }; page.onFileChange({ target } as unknown as Event); target.files = [] as any; page.onFileChange({ target } as unknown as Event);
      const wrong = { files: [new File(['unit'], 'unit.txt', { type: 'text/plain' })], value: 'unit' }; page.onFileChange({ target: wrong } as unknown as Event); expect(page.error).toContain('image'); expect(wrong.value).toBe('');
      let reader: any; class UnitReader { result = 'data:image/png;base64,unit'; onload?: () => void; onerror?: () => void; constructor() { reader = this; } readAsDataURL = vi.fn(); }
      vi.stubGlobal('FileReader', UnitReader); const input = { files: [new File(['unit'], 'unit.png', { type: 'image/png' })], value: 'unit' }; page.onFileChange({ target: input } as unknown as Event); reader.onload(); expect(page.slotAt(0).dataUrl).toBe('data:image/png;base64,unit');
      reader.result = ''; reader.onload(); reader.onerror(); expect(page.error).toContain('read');
    });
    it('submits three ordered url/caption fields and recovers server failure', async () => {
      const { page, api } = setup(); await page.saveAndContinue(); expect(api.savePhotos).not.toHaveBeenCalled();
      for (let i = 0; i < 3; i++) { page.slotAt(i).dataUrl = `unit${i}`; page.slotAt(i).caption = 'x'.repeat(50); } await page.saveAndContinue();
      const payload = api.savePhotos.mock.calls[0][0]; expect(payload.photos).toHaveLength(3); expect(payload.photos[0]).toEqual({ url: 'unit0', caption: 'x'.repeat(40), sortOrder: 1 }); expect(router.navigateByUrl).toHaveBeenCalledWith('/onboarding/intent'); expect(page.saving).toBe(false);
      api.savePhotos.mockReturnValue(throwError(() => ({ error: { error: 'UNIT_REJECTED' } }))); await page.saveAndContinue(); expect(page.error).toBe('UNIT_REJECTED'); expect(page.saving).toBe(false);
    });
  });
});
