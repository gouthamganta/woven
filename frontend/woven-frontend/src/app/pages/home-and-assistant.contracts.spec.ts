import { ChangeDetectorRef, ElementRef } from '@angular/core';
import { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { HomeComponent } from './home/home';
import { WovenAssistantComponent } from '../components/woven-assistant/woven-assistant.component';
import { PulseService, PulseState } from '../services/pulse.service';
import { RealtimeService } from '../services/realtime.service';
import { PushService } from '../services/push.service';
import { FeedbackService } from '../services/feedback.service';
import { CoachingService } from '../services/coaching.service';
import { SupportService } from '../services/support.service';
import { PendingTaskService, PendingTask } from '../services/pending-task.service';

describe('Home and assistant feature contracts', () => {
  beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-09T12:00:00Z')); localStorage.clear(); });
  afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); localStorage.clear(); });
  const cdr = () => ({ markForCheck: vi.fn() } as unknown as ChangeDetectorRef);
  const pulse = (): PulseState => ({ cycleId: 'cycle', cycleStartUtc: '2026-10-09T00:00:00Z', cycleEndUtc: '2026-10-10T00:00:00Z', answered: false, answers: {}, questions: [] });

  describe('Home', () => {
    function setup() {
      const router = { url: '/moments', navigateByUrl: vi.fn().mockResolvedValue(true) };
      const api = { getCurrent: vi.fn().mockReturnValue(of(pulse())), submit: vi.fn().mockReturnValue(of({})) };
      const realtime = { start: vi.fn(), stop: vi.fn() };
      const push = { isSupported: vi.fn().mockResolvedValue(true), isSubscribed: vi.fn().mockResolvedValue(false), register: vi.fn().mockResolvedValue(true) };
      const feedback = { getPrompt: vi.fn().mockReturnValue(of({ hasPendingPrompt: false })), submit: vi.fn().mockReturnValue(of({})) };
      const coaching = { getCurrentSummary: vi.fn().mockReturnValue(of(null)) };
      const page = new HomeComponent(router as unknown as Router, api as unknown as PulseService, realtime as unknown as RealtimeService, push as unknown as PushService, feedback as unknown as FeedbackService, coaching as unknown as CoachingService, cdr());
      return { page, router, api, realtime, push, feedback, coaching };
    }
    for (const [path, label] of [['moments', 'Moments'], ['commons', 'Commons'], ['chats', 'Chats'], ['you', 'You']]) {
      it(`identifies ${path} navigation and nested routes`, () => {
        const { page, router } = setup(); router.url = `/${path}/nested`; expect(page.activeLabel).toBe(label); expect(page.isActive(`/${path}`)).toBe(true); page.go(path as 'moments' | 'commons' | 'chats' | 'you'); expect(router.navigateByUrl).toHaveBeenCalledWith(`/${path}`); router.url = '/unknown'; expect(page.activeLabel).toBe(''); expect(page.isActive(`/${path}`)).toBe(false); page.ngOnDestroy();
      });
    }
    it('loads an unanswered pulse, subscribes realtime and cleans up the ticker', async () => {
      const { page, realtime } = setup(); await page.ngOnInit(); expect(realtime.start).toHaveBeenCalledTimes(1); expect(page.pulseSheetOpen).toBe(true); expect(page.canEditPulse()).toBe(true); expect(page.countdownText).toContain('12h'); await vi.advanceTimersByTimeAsync(15000); page.ngOnDestroy(); expect(realtime.stop).toHaveBeenCalledTimes(1);
    });
    it('handles absent pulse, failed loading and sheet toggles', async () => {
      const { page, api } = setup(); page.openPulse(); expect(page.pulseSheetOpen).toBe(false); expect(page.canEditPulse()).toBe(false); page.updateCountdown(); expect(page.countdownText).toBe(''); expect(page.summaryText()).toContain('Quick'); expect(page.toneHint()).toContain('10 seconds');
      api.getCurrent.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.refreshPulse(); expect(page.pulseError).toContain("couldn't load"); page.openHowItWorks(); expect(page.howItWorksOpen).toBe(true); page.closeHowItWorks(); expect(page.howItWorksOpen).toBe(false); page.ngOnDestroy();
    });
    for (const tone of ['playful', 'serious', 'calm'] as const) {
      it(`summarizes an answered ${tone} pulse and blocks changes until reset`, async () => {
        const { page, api } = setup(); const state = pulse(); state.answered = true; state.answers = { d1_battery: 'high', d2_tone: tone, d3_role: 'driver' }; state.questions = [{ id: 'd1_battery', text: 'Unit', options: [{ key: 'high', label: 'High' }] }]; api.getCurrent.mockReturnValue(of(state)); await page.refreshPulse(); expect(page.summaryText()).toContain('High'); expect(page.summaryText()).toContain(tone); expect(page.toneHint()).toContain(tone === 'playful' ? 'lighter' : tone === 'serious' ? 'meaningful' : 'relaxed'); expect(page.canEditPulse()).toBe(false); state.cycleEndUtc = '2026-10-09T12:00:00Z'; expect(page.canEditPulse()).toBe(true); page.updateCountdown(); expect(page.countdownText).toBe('Reset now'); state.cycleEndUtc = '2026-10-11T13:00:00Z'; page.updateCountdown(); expect(page.countdownText).toContain('2d'); page.ngOnDestroy();
      });
    }
    it('saves an editable pulse, ignores locked/absent data and recovers rejection', async () => {
      const { page, api } = setup(); const answers = { d1_battery: 'high', d2_tone: 'playful', d3_role: 'driver' } as const; await page.submitPulse(answers); expect(api.submit).not.toHaveBeenCalled(); page.pulse = pulse(); await page.submitPulse(answers); expect(api.submit).toHaveBeenCalledWith(answers); expect(page.savingPulse).toBe(false); expect(page.pulseSheetOpen).toBe(false); api.submit.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.submitPulse(answers); expect(page.pulseError).toContain('Could not save'); expect(page.savingPulse).toBe(false); page.pulse!.answered = true; api.submit.mockClear(); await page.submitPulse(answers); expect(api.submit).not.toHaveBeenCalled(); page.ngOnDestroy();
    });
    for (const mode of ['unsupported', 'subscribed', 'already-asked', 'eligible']) {
      it(`shows notification nudge only for ${mode} eligibility`, async () => {
        const { page, push } = setup(); if (mode === 'unsupported') push.isSupported.mockResolvedValue(false); if (mode === 'subscribed') push.isSubscribed.mockResolvedValue(true); if (mode === 'already-asked') localStorage.setItem('woven:pushAsked', '1'); await page.ngOnInit(); await Promise.resolve(); await Promise.resolve(); expect(page.showPushNudge).toBe(mode === 'eligible'); if (mode === 'eligible') { await page.enablePush(); expect(push.register).toHaveBeenCalled(); expect(localStorage.getItem('woven:pushAsked')).toBe('1'); } page.dismissPushNudge(); expect(page.showPushNudge).toBe(false); page.ngOnDestroy();
      });
    }
    for (const met of [false, true]) {
      it(`submits ${met ? 'met' : 'not-met'} feedback with the correct private star value`, async () => {
        const { page, feedback } = setup(); await page.submitFeedback(); expect(feedback.submit).not.toHaveBeenCalled(); page.feedbackPrompt = { hasPendingPrompt: true, matchId: 'match' }; await page.submitFeedback(); expect(feedback.submit).not.toHaveBeenCalled(); page.setFeedbackMet(met); page.setFeedbackStars(4); await page.submitFeedback(); expect(feedback.submit).toHaveBeenCalledWith('match', { metInPerson: met, stars: met ? 4 : null }); expect(page.feedbackPrompt).toBeNull(); expect(page.feedbackSubmitting).toBe(false); page.ngOnDestroy();
      });
    }
    it('loads feedback/coaching cards and preserves feedback on a failed save', async () => {
      const { page, feedback, coaching } = setup(); feedback.getPrompt.mockReturnValue(of({ hasPendingPrompt: true, matchId: 'match' })); coaching.getCurrentSummary.mockReturnValue(of({ id: 1, summaryText: 'Unit reflection' })); await page.ngOnInit(); expect(page.feedbackPrompt?.matchId).toBe('match'); expect(page.coachingSummary?.id).toBe(1); page.onCoachingDismissed(); expect(page.coachingSummary).toBeNull(); page.setFeedbackMet(false); feedback.submit.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.submitFeedback(); expect(page.feedbackPrompt).not.toBeNull(); expect(page.feedbackSubmitting).toBe(false); page.feedbackSubmitting = true; feedback.submit.mockClear(); await page.submitFeedback(); expect(feedback.submit).not.toHaveBeenCalled(); page.dismissFeedback(); expect(page.feedbackPrompt).toBeNull(); page.ngOnDestroy();
    });
    it('tolerates background feedback/coaching failures without inventing cards', async () => {
      const { page, feedback, coaching } = setup(); feedback.getPrompt.mockReturnValue(throwError(() => new Error('Unit failure'))); coaching.getCurrentSummary.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.ngOnInit(); expect(page.feedbackPrompt).toBeNull(); expect(page.coachingSummary).toBeNull(); page.ngOnDestroy();
    });
  });

  describe('Assistant', () => {
    function setup(platform = 'browser') {
      const support = { chat: vi.fn().mockReturnValue(of({ reply: 'Controlled unit reply' })) };
      const pending = { getPendingTasks: vi.fn().mockReturnValue(of({ tasks: [] })), logInteraction: vi.fn().mockReturnValue(of({ logged: true })) };
      const page = new WovenAssistantComponent(support as unknown as SupportService, pending as unknown as PendingTaskService, cdr(), platform as unknown as object);
      return { page, support, pending };
    }
    const task = (type: PendingTask['type']): PendingTask => ({ type, priority: 1, data: { cycleId: 'cycle', summaryId: 1, summaryText: 'Unit reflection' } });
    for (const [type, glow] of [['date_feedback', 'red'], ['weekly_coaching', 'gold'], ['daily_pulse', 'white']] as const) {
      it(`loads ${type} badge and selects its ${glow} glow`, async () => {
        const { page, pending } = setup(); pending.getPendingTasks.mockReturnValue(of({ tasks: [task(type)] })); await page.ngOnInit(); expect(page.badgeCount).toBe(1); expect(page.glowState).toBe(glow); await page.open(); expect(page.isOpen).toBe(true); if (type === 'daily_pulse') expect(page.pulseState.cycleId).toBe('cycle'); if (type === 'weekly_coaching') expect(page.coachingSummary.summaryText).toBe('Unit reflection'); await page.toggle(); expect(page.isOpen).toBe(false);
      });
    }
    it('skips browser setup on SSR and tolerates task-list rejection', async () => {
      const server = setup('server'); await server.page.ngOnInit(); expect(server.pending.getPendingTasks).not.toHaveBeenCalled(); const { page, pending } = setup(); pending.getPendingTasks.mockReturnValue(throwError(() => new Error('Unit failure'))); await page.checkPendingTasks(); expect(page.badgeCount).toBe(0); expect(page.glowState).toBe('none');
    });
    it('opens empty chat, focuses after animation and logs the open once', async () => {
      const { page, pending } = setup(); const input = document.createElement('input'); input.focus = vi.fn(); page.inputEl = new ElementRef(input); await page.toggle(); await vi.advanceTimersByTimeAsync(350); expect(input.focus).toHaveBeenCalled(); expect(pending.logInteraction).toHaveBeenCalledWith('AssistantChatOpened', { hasPendingTasks: false }); page.close(); expect(page.isOpen).toBe(false);
    });
    it('sends trimmed history and appends the controlled reply without overlapping requests', async () => {
      const { page, support } = setup(); page.draft = '   '; await page.send(); expect(support.chat).not.toHaveBeenCalled(); const reply = new Subject<{ reply: string }>(); support.chat.mockReturnValue(reply); page.draft = ' Unit question '; const send = page.send(); page.draft = 'Another'; await page.send(); expect(support.chat).toHaveBeenCalledTimes(1); expect(page.history[0]).toEqual({ role: 'user', content: 'Unit question' }); reply.next({ reply: 'Unit answer' }); reply.complete(); await send; expect(page.history[1]).toEqual({ role: 'assistant', content: 'Unit answer' }); expect(page.thinking).toBe(false);
      const list = document.createElement('div'); Object.defineProperty(list, 'scrollHeight', { value: 100 }); page.messageList = new ElementRef(list); page.ngAfterViewChecked(); expect(list.scrollTop).toBe(100); page.ngAfterViewChecked();
    });
    it('records a visible controlled error and releases the thinking state on rejection', async () => {
      const { page, support } = setup(); support.chat.mockReturnValue(throwError(() => new Error('Unit failure'))); page.draft = 'Unit question'; await page.send(); expect(page.history[1].content).toContain('Something went wrong'); expect(page.thinking).toBe(false); page.ngAfterViewChecked();
    });
    for (const action of ['onPulseSaved', 'onPulseSkipped', 'onCoachingDismissed', 'onCoachingSkipped'] as const) {
      it(`logs ${action} and advances the pending task queue`, async () => {
        const { page, pending } = setup(); page.pendingTasks = [task('daily_pulse'), task('weekly_coaching')]; page.showingPulse = true; page.showingCoaching = true; page.coachingSummary = { id: 1 }; if (action === 'onPulseSaved') await page.onPulseSaved({ unit: true }); else await page[action](); expect(pending.logInteraction).toHaveBeenCalled(); expect(page.pendingTasks).toHaveLength(1); expect(page.badgeCount).toBe(1); expect(page.glowState).toBe('gold'); await page.advanceToNextPendingOrChat(); expect(page.badgeCount).toBe(0); expect(page.glowState).toBe('none'); page.onPulseClosed(); expect(page.isOpen).toBe(false);
      });
    }
    it('does not convert drag gestures into assistant open clicks', async () => {
      const { page } = setup(); const added: Array<[string, EventListenerOrEventListenerObject]> = []; const originalAdd = document.addEventListener.bind(document);
      vi.spyOn(document, 'addEventListener').mockImplementation((name, listener, options) => { added.push([name, listener]); originalAdd(name, listener, options); });
      page.onDrag(new MouseEvent('mousemove')); page.endDrag(); page.startDrag(new MouseEvent('mousedown', { clientX: 20, clientY: 20 })); page.onDrag(new MouseEvent('mousemove', { clientX: 9999, clientY: -9999 })); expect(page.orbX).toBeLessThanOrEqual(window.innerWidth - 56); expect(page.orbY).toBeLessThanOrEqual(window.innerHeight - 56); page.endDrag(); const click = new MouseEvent('click', { cancelable: true }); page.handleClick(click); expect(page.isOpen).toBe(false); expect(click.defaultPrevented).toBe(true);
      // Cleanup captured listener identities independently of implementation.
      for (const [name, listener] of added) document.removeEventListener(name, listener);
      const server = setup('server'); server.page.startDrag(new MouseEvent('mousedown')); expect(server.page.isDragging).toBe(false);
    });
  });
});
