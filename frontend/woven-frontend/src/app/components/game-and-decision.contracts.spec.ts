import { ChangeDetectorRef } from '@angular/core';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { TrialDecisionComponent } from './trial-decision/trial-decision.component';
import { UnmatchRatingComponent } from './unmatch-rating/unmatch-rating.component';
import { GameMessageCardComponent } from './game-message-card/game-message-card.component';
import { InlineGamePlayerComponent } from './inline-game-player/inline-game-player.component';
import { GamesService, GameRoundResponse } from '../services/games.service';
import { ChatNoteOverlayComponent } from '../pages/moments/chat-note-overlay.component';

describe('Game and decision control contracts', () => {
  beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-09T12:00:00Z')); vi.spyOn(console, 'log').mockImplementation(() => {}); vi.spyOn(console, 'error').mockImplementation(() => {}); });
  afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); });
  const cdr = () => ({ markForCheck: vi.fn() } as unknown as ChangeDetectorRef);

  for (const decision of ['CONTINUE', 'BLOCK'] as const) {
    it(`emits ${decision} only once and prevents competing trial decisions`, () => {
      const page = new TrialDecisionComponent(); const emitted = vi.fn(); page.decided.subscribe(emitted);
      if (decision === 'CONTINUE') page.onContinue(); else page.onBlock();
      page.onContinue(); page.onBlock(); page.onEnd(); page.submitWithReason(); expect(emitted).toHaveBeenCalledTimes(1); expect(emitted).toHaveBeenCalledWith({ decision }); page.ngOnDestroy();
    });
  }
  it('submits an optional end reason after the countdown and stops on destruction', async () => {
    const page = new TrialDecisionComponent(); const emitted = vi.fn(); page.decided.subscribe(emitted); page.onEnd(); expect(page.showReasonPicker).toBe(true);
    await vi.advanceTimersByTimeAsync(30000); expect(emitted).toHaveBeenCalledWith({ decision: 'END', endReason: undefined }); page.ngOnDestroy();
    const canceled = new TrialDecisionComponent(); const second = vi.fn(); canceled.decided.subscribe(second); canceled.onEnd(); canceled.ngOnDestroy(); await vi.advanceTimersByTimeAsync(30000); expect(second).not.toHaveBeenCalled();
  });
  it('sends the selected reason before timeout without later duplicate emissions', async () => {
    const page = new TrialDecisionComponent(); const emitted = vi.fn(); page.decided.subscribe(emitted); page.onEnd();
    page.selectReason('not_my_type'); page.submitWithReason();
    expect(emitted).toHaveBeenCalledWith({ decision: 'END', endReason: 'not_my_type' }); await vi.advanceTimersByTimeAsync(30000); expect(emitted).toHaveBeenCalledTimes(1); page.ngOnDestroy();
  });
  for (const rating of [-100, 0, 100]) {
    it(`preserves the private ${rating} unmatch rating and supports skip/cancel`, () => {
      const page = new UnmatchRatingComponent(); const confirmed = vi.fn(); const cancelled = vi.fn(); page.confirmed.subscribe(confirmed); page.cancelled.subscribe(cancelled);
      page.rating = rating; expect(page.sliderFillPercent).toBe(Math.abs(rating) / 2); page.submit(); page.skip(); page.cancel();
      expect(confirmed.mock.calls).toEqual([[rating], [undefined]]); expect(cancelled).toHaveBeenCalledTimes(1);
    });
  }

  it('requires trimmed note length and a nonbusy state before emitting a ChatNote', () => {
    const page = new ChatNoteOverlayComponent(cdr()); const emitted = vi.fn(); page.submitted.subscribe(emitted);
    page.noteText = ' '.repeat(30); page.onSend(); page.noteText = 'x'.repeat(19); page.onSend(); expect(emitted).not.toHaveBeenCalled();
    page.noteText = ` ${'x'.repeat(20)} `; page.onSend(); expect(emitted).toHaveBeenCalledWith('x'.repeat(20)); page.submitting = true; page.onSend(); expect(emitted).toHaveBeenCalledTimes(1);
  });
  it('supports note starters, bridge suggestions and cancellation without submitting', () => {
    const page = new ChatNoteOverlayComponent(cdr()); const back = vi.fn(); page.back.subscribe(back);
    page.applyBridgeQuestion(); expect(page.noteText).toBe(''); page.card = { userId: 2, fullName: 'Unit', bridgeQuestion: 'Unit bridge question' }; page.applyBridgeQuestion(); expect(page.noteText).toBe('Unit bridge question');
    page.toggleDropdown(); expect(page.showDropdown).toBe(true); page.applyStarter('Unit starter'); expect(page.showDropdown).toBe(false); expect(page.noteText).toBe('Unit starter'); page.onInput(); page.onBack(); expect(page.noteText).toBe(''); expect(back).toHaveBeenCalledTimes(1);
  });

  for (const status of ['PENDING', 'ACTIVE', 'COMPLETED']) {
    for (const initiator of [false, true]) {
      it(`shows correct ${status} controls for the ${initiator ? 'initiator' : 'receiver'}`, () => {
        const page = new GameMessageCardComponent(); page.meUserId = 1; page.message = { senderUserId: initiator ? 1 : 2, body: 'Unit game', createdAt: '2026-10-09', meta: { sessionId: 'session', gameType: 'KNOW_ME', status, expiresAt: '2026-10-09T12:10:00Z' } };
        expect(page.title).toBe('KNOW ME'); expect(page.isInitiator).toBe(initiator); expect(page.shouldShowAcceptReject).toBe(status === 'PENDING' && !initiator); expect(page.shouldShowWaiting).toBe(status === 'PENDING' && initiator);
        expect(page.shouldShowPlay).toBe(status === 'ACTIVE'); expect(page.shouldShowResults).toBe(status === 'COMPLETED');
      });
    }
  }
  it('suppresses expired/missing-session actions and emits identifiers for selected game actions', () => {
    const page = new GameMessageCardComponent(); page.meUserId = 1; page.message = { senderUserId: 2, body: 'Unit', createdAt: '2026-10-09' };
    const accept = vi.fn(), reject = vi.fn(), play = vi.fn(), result = vi.fn(); page.accept.subscribe(accept); page.reject.subscribe(reject); page.play.subscribe(play); page.openResult.subscribe(result);
    expect(page.gameType).toBe('GAME'); expect(page.status).toBe(''); expect(page.expiresAt).toBeNull(); expect(page.isExpired).toBe(false);
    page.onAccept(); page.onReject(); page.onPlay(); page.onOpenResult(); expect(accept).not.toHaveBeenCalled(); expect(play).not.toHaveBeenCalled();
    page.message.meta = { sessionId: 'session', gameType: 'RED_GREEN_FLAG', status: 'pending', expiresAt: '2026-10-09T11:00:00Z' };
    expect(page.isExpired).toBe(true); expect(page.shouldShowAcceptReject).toBe(false); page.onAccept(); page.onReject(); page.onPlay(); page.onOpenResult();
    expect(accept).toHaveBeenCalledWith('session'); expect(reject).toHaveBeenCalledWith('session'); expect(play).toHaveBeenCalledWith({ sessionId: 'session', gameType: 'RED_GREEN_FLAG' }); expect(result).toHaveBeenCalledWith('session');
  });

  describe('Inline game player', () => {
    function setup() {
      const round: GameRoundResponse = { roundNumber: 1, totalRounds: 2, questions: [{ id: 'q1', text: 'Their choice', options: [{ id: 'A', text: 'A', isCorrect: false }], difficulty: 'EASY', category: 'unit' }], timeLimit: 90, isGuesser: true, hasAnswered: false, waitingForOther: false };
      const games = { getCurrentRound: vi.fn().mockReturnValue(of(round)), submitGuesses: vi.fn().mockReturnValue(of({ status: 'WAITING_FOR_TARGET' })), submitTargetAnswers: vi.fn().mockReturnValue(of({ status: 'WAITING_FOR_TARGET' })) };
      const page = new InlineGamePlayerComponent(games as unknown as GamesService, cdr()); page.sessionId = 'session'; return { page, games, round };
    }
    it('loads questions and requires every answer while supporting string and object options', () => {
      const { page } = setup(); expect(page.canSubmit()).toBe(false); expect(page.getQuestionText('Their choice')).toBe('Their choice'); page.ngOnInit(); expect(page.loading).toBe(false); expect(page.canSubmit()).toBe(false);
      expect(page.getOptionText({ id: 'A', text: 'A' })).toBe('A');
      // A malformed wire response can lack fields despite the declared type.
      expect(page.getOptionText({} as Parameters<InlineGamePlayerComponent['getOptionText']>[0])).toBe('');
      page.selectAnswer('q1', 'A'); expect(page.isSelected('q1', 'A')).toBe(true); expect(page.canSubmit()).toBe(true); page.ngOnDestroy();
    });
    it('uses second-person questions for the target and preserves guesser wording', () => {
      const { page, round } = setup(); page.round = round; expect(page.getQuestionText('Their plans')).toBe('Their plans'); round.isGuesser = false;
      expect(page.getQuestionText('Their plans: They invite them. Are they ready?')).toBe('Your plans: You invite you. Are you ready?'); expect(page.getQuestionText('')).toBe(''); page.ngOnDestroy();
    });
    for (const isGuesser of [false, true]) {
      for (const status of ['WAITING_FOR_TARGET', 'NEXT_ROUND', 'GAME_COMPLETE']) {
        it(`handles ${status} when ${isGuesser ? 'guessing' : 'answering as target'}`, async () => {
          const { page, games, round } = setup(); round.isGuesser = isGuesser; page.round = round; page.answers = { q1: 'A' };
          games.submitGuesses.mockReturnValue(of({ status })); games.submitTargetAnswers.mockReturnValue(of({ status })); const completed = vi.fn(), close = vi.fn(); page.completed.subscribe(completed); page.close.subscribe(close);
          page.submitAnswers(); expect(isGuesser ? games.submitGuesses : games.submitTargetAnswers).toHaveBeenCalledWith('session', { q1: 'A' }); expect(page.loading).toBe(false);
          await vi.advanceTimersByTimeAsync(1000);
          if (status === 'GAME_COMPLETE') { expect(completed).toHaveBeenCalledTimes(1); expect(close).toHaveBeenCalledTimes(1); }
          else if (status === 'NEXT_ROUND') expect(games.getCurrentRound).toHaveBeenCalled(); else expect(page.waitingForOther).toBe(true);
          page.ngOnDestroy();
        });
      }
    }
    it('rejects incomplete submissions and exposes request failure without staying busy', () => {
      const { page, games, round } = setup(); page.submitAnswers(); expect(games.submitGuesses).not.toHaveBeenCalled(); page.round = round; page.answers = { q1: 'A' };
      games.submitGuesses.mockReturnValue(throwError(() => new Error('Unit failure'))); page.submitAnswers(); expect(page.error).toBe('Could not submit answers'); expect(page.loading).toBe(false);
      games.getCurrentRound.mockReturnValue(throwError(() => new Error('Unit failure'))); page.loadRound(); expect(page.error).toBe('Could not load round'); expect(page.loading).toBe(false); page.ngOnDestroy();
    });
    it('polls an answered round and reloads after the round changes', async () => {
      const { page, games, round } = setup(); round.hasAnswered = true; page.ngOnInit(); expect(page.waitingForOther).toBe(true);
      games.getCurrentRound.mockReturnValue(of({ ...round, roundNumber: 2, hasAnswered: false })); await vi.advanceTimersByTimeAsync(2000); expect(page.round?.roundNumber).toBe(2); expect(page.waitingForOther).toBe(false); page.ngOnDestroy();
    });
    for (const status of [400, 404, 500]) {
      it(`handles polling status${status} without inventing completed games on transient errors`, async () => {
        const { page, games, round } = setup(); page.round = round; const completed = vi.fn(), close = vi.fn(); page.completed.subscribe(completed); page.close.subscribe(close);
        games.getCurrentRound.mockReturnValue(throwError(() => ({ status }))); page.startPolling(); await vi.advanceTimersByTimeAsync(2000);
        expect(completed).toHaveBeenCalledTimes(status === 500 ? 0 : 1); expect(close).toHaveBeenCalledTimes(status === 500 ? 0 : 1); page.onClose(); expect(close).toHaveBeenCalled(); page.ngOnDestroy();
      });
    }
  });
});
