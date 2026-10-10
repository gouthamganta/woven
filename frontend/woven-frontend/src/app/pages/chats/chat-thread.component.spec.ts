import { ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ChatThreadComponent } from './chat-thread.component';
import { ChatService, ChatThreadResponse } from '../../services/chat.service';
import { MatchesService } from '../../services/matches.service';
import { MediaService } from '../../services/media.service';
import { GamesService } from '../../services/games.service';
import { NewChatMessageEvent, RealtimeService } from '../../services/realtime.service';

describe('Chat thread feature contracts', () => {
  let page: ChatThreadComponent;
  let chat: Record<string, ReturnType<typeof vi.fn>>;
  let matches: Record<string, ReturnType<typeof vi.fn>>;
  let games: Record<string, ReturnType<typeof vi.fn>>;
  let media: { uploadVoiceNote: ReturnType<typeof vi.fn> };
  let route: { snapshot: any };
  let router: { navigateByUrl: ReturnType<typeof vi.fn>; events: Subject<NavigationEnd> };
  let events: Subject<NewChatMessageEvent>;
  let response: ChatThreadResponse;
  beforeEach(() => {
    vi.useFakeTimers(); vi.setSystemTime(new Date('2026-10-09T12:00:00Z')); localStorage.clear();
    for (const method of ['error', 'warn', 'log'] as const) vi.spyOn(console, method).mockImplementation(() => {});
    response = { threadId: 'thread', matchId: 'match', matchType: 'PURE', balloonState: 'ACTIVE', meUserId: 1, other: { userId: 2, fullName: 'Synthetic' }, messages: [] };
    chat = Object.fromEntries(['thread', 'send', 'trialDecision', 'expressDateInterest', 'sendVoiceMessage', 'voiceListened'].map(name => [name, vi.fn().mockReturnValue(of({}))]));
    chat['thread'].mockImplementation(() => of(response));
    matches = Object.fromEntries(['pop', 'unmatch', 'block'].map(name => [name, vi.fn().mockReturnValue(of({}))]));
    games = Object.fromEntries(['acceptSession', 'rejectSession', 'getResult', 'createSession'].map(name => [name, vi.fn().mockReturnValue(of({}))]));
    media = { uploadVoiceNote: vi.fn().mockResolvedValue({ fileUrl: '/synthetic.webm', durationSecs: 2 }) };
    route = { snapshot: { paramMap: { get: (key: string) => key === 'threadId' ? 'thread' : null }, children: [] } };
    router = { navigateByUrl: vi.fn().mockResolvedValue(true), events: new Subject<NavigationEnd>() };
    events = new Subject<NewChatMessageEvent>();
    page = new ChatThreadComponent(route as unknown as ActivatedRoute, router as unknown as Router, chat as unknown as ChatService,
      media as unknown as MediaService, matches as unknown as MatchesService, games as unknown as GamesService,
      { markForCheck: vi.fn(), detectChanges: vi.fn() } as unknown as ChangeDetectorRef, { newChatMessage$: events } as unknown as RealtimeService);
  });
  afterEach(() => { page.ngOnDestroy(); vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); localStorage.clear(); document.getElementById('msgsEnd')?.remove(); });
  async function tick() { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); }

  it('loads a nested route once and subscribes/unsubscribes to navigation and messages', async () => {
    const parent = { paramMap: { get: () => null }, children: [] as any[] };
    const child = { paramMap: { get: () => 'thread' }, children: [], parent }; parent.children.push(child); route.snapshot = child;
    page.ngOnInit(); await tick(); expect(chat['thread']).toHaveBeenCalledWith('thread'); expect(page.loading).toBe(false); expect(page.isBalloonStage).toBe(true);
    router.events.next(new NavigationEnd(1, '/chats/thread', '/chats/thread')); await tick(); expect(chat['thread']).toHaveBeenCalledTimes(1);
    page.ngOnDestroy(); expect(events.observed).toBe(false); expect(router.events.observed).toBe(false);
  });

  it('rejects an absent route identifier without requesting a thread', async () => {
    route.snapshot.paramMap.get = () => null; page.ngOnInit(); await tick(); expect(page.error).toBe('Missing thread id'); expect(chat['thread']).not.toHaveBeenCalled();
    await page.refreshSilent(); expect(chat['thread']).not.toHaveBeenCalled();
  });

  it('accepts only current-thread realtime events and deduplicates message identifiers', async () => {
    page.ngOnInit(); await tick(); const event = { threadId: 'thread', messageId: 'message', senderUserId: 2, body: 'Synthetic text', createdAt: '2026-10-09T12:00:00Z' };
    events.next({ ...event, threadId: 'other' }); events.next(event); events.next(event); expect(page.data?.messages).toHaveLength(1);
    const anchor = document.createElement('div'); anchor.id = 'msgsEnd'; anchor.scrollIntoView = vi.fn(); document.body.appendChild(anchor);
    page.ngAfterViewChecked(); await vi.advanceTimersByTimeAsync(0); expect(anchor.scrollIntoView).toHaveBeenCalled();
    page.ngAfterViewChecked(); expect(page.data?.messages[0].body).toBe('Synthetic text');
  });

  it('exposes load failure and supports a silent refresh using the route identifier', async () => {
    chat['thread'].mockReturnValueOnce(throwError(() => ({ status: 500, message: 'Synthetic outage' })));
    await page.load('thread'); expect(page.loading).toBe(false); expect(page.error).toBe('Could not load chat.');
    await page.refreshSilent(); expect(page.data?.threadId).toBe('thread');
  });

  it('handles absent data and formats basic labels, roles and message types', () => {
    expect(page.titleName()).toBe('Chat'); expect(page.matchTypeLabel()).toBe(''); expect(page.countdownSafe(null)).toBe('00:00'); expect(page.statusText()).toBe('Balloon');
    expect(page.isMine(1)).toBe(false); expect(page.isSystem(null)).toBe(false); expect(page.isGame(null)).toBe(false); expect(page.isVoiceMessage(null)).toBe(false); expect(page.getVoiceDuration(null)).toBe('0:00');
    page.data = response; expect(page.titleName()).toBe('Synthetic'); expect(page.matchTypeLabel()).toContain('Pure'); page.data.matchType = 'EDGE'; expect(page.matchTypeLabel()).toContain('Edge');
    expect(page.isMine(1)).toBe(true); expect(page.isMine(2)).toBe(false); expect(page.isSystem({ messageType: 'system' })).toBe(true); expect(page.isSystem({ messageType: 'CHAT' })).toBe(false);
    expect(page.isGame({ messageType: 'game' })).toBe(true); expect(page.isGame({ meta: { sessionId: 'session', gameType: 'KNOW_ME' } })).toBe(true); expect(page.isGame({ body: '🎮 Synthetic invite' })).toBe(true); expect(page.isGame({ body: 'ordinary text' })).toBe(false);
    expect(page.isVoiceMessage({ messageType: 'voice' })).toBe(true); expect(page.getVoiceDuration({ meta: { durationSecs: 61 } })).toBe('1:01'); page.recordingMs = 61000; expect(page.formatRecordingTime()).toBe('1:01');
    page.back(); page.viewProfile(); expect(router.navigateByUrl.mock.calls.map(c => c[0])).toEqual(['/chats', '/matches/match/profile']);
  });

  it('keeps date ideas hidden until Find Love and the local ten-minute delay have elapsed', () => {
    expect(page.shouldShowDateIdeaBox()).toBe(false); expect(page.shouldRevealDateIdea()).toBe(false); expect(page.dateIdeaCountdown()).toBe(''); expect(page.revealedIdeas).toEqual([]);
    page.data = { ...response, dateIdea: 'Synthetic park' }; expect(page.revealedIdeas).toEqual(['Synthetic park']); expect(page.shouldRevealDateIdea()).toBe(false);
    page.data.findLoveAt = '2026-10-09T12:00:00Z'; expect(page.shouldShowDateIdeaBox()).toBe(true); expect(page.shouldRevealDateIdea()).toBe(false); expect(page.dateIdeaCountdown()).toBe('10:00');
    page.now += 600000; expect(page.shouldRevealDateIdea()).toBe(true); expect(page.dateIdeaCountdown()).toBe('00:00');
    page.data.dateIdeas = ['One', 'Two']; expect(page.revealedIdeas).toEqual(['One', 'Two']);
  });

  it('handles malformed persisted date-idea timestamps and countdown urgency boundaries', () => {
    page.data = { ...response, findLoveAt: '2026-10-09T12:01:01Z' }; expect(page.isCountdownUrgent()).toBe(true); expect(page.statusText()).toContain('01:01'); expect(page.countdownSafe(page.data.findLoveAt)).toBe('01:01');
    page.data.findLoveAt = '2026-10-09T12:03:00Z'; expect(page.isCountdownUrgent()).toBe(false);
    page.data.findLoveAt = '2026-10-09T11:59:00Z'; localStorage.setItem('woven:dateIdeaUnlockAt:thread', 'malformed'); expect(page.shouldRevealDateIdea()).toBe(false); expect(page.dateIdeaCountdown()).toBe('10:00'); expect(page.statusText()).toBe('Find Love');
    expect(page.countdown('2026-10-09T11:59:00Z')).toBe('00:00');
  });

  it('transitions the balloon and unlock animation on scheduled time boundaries', async () => {
    response.findLoveAt = '2026-10-09T12:00:01Z'; page.ngOnInit(); await tick();
    await vi.advanceTimersByTimeAsync(1000); expect(page.justTransitionedToFindLove).toBe(true); expect(page.isFindLoveStage).toBe(true);
    await vi.advanceTimersByTimeAsync(2000); expect(page.justTransitionedToFindLove).toBe(false);
    await vi.advanceTimersByTimeAsync(598000); expect(page.dateIdeaJustUnlocked).toBe(true);
    await vi.advanceTimersByTimeAsync(2000); expect(page.dateIdeaJustUnlocked).toBe(false);
  });

  for (const isUserA of [false, true]) {
    it(`opens an undecided trial decision for user${isUserA ? 'A' : 'B'} only`, () => {
      page.checkTrialStatus(); page.data = { ...response, isTrial: true, trialSecondsLeft: 0, canMakeDecision: true, isUserA };
      page.checkTrialStatus(); expect(page.showTrialDecision).toBe(true); expect(page.statusText()).toBe('Trial Ended');
      page.showTrialDecision = false; page.data.userADecision = 'CONTINUE'; page.data.userBDecision = 'CONTINUE'; page.checkTrialStatus(); expect(page.showTrialDecision).toBe(false);
      page.trialSecondsLeft = 30; expect(page.statusText()).toContain('00:30'); expect(page.getTrialProgress()).toBe(50); expect(page.formatTrialTime(61)).toBe('1:01');
      page.trialSecondsLeft = 100; expect(page.getTrialProgress()).toBe(0); page.trialSecondsLeft = -1; expect(page.getTrialProgress()).toBe(100);
    });
  }

  for (const status of ['MATCH_BLOCKED', 'MATCH_ENDED', 'MATCH_CONTINUES', 'WAITING']) {
    it(`handles a ${status} trial outcome without making up a server decision`, async () => {
      page.data = response; chat['trialDecision'].mockReturnValue(of({ status })); await page.onTrialDecision({ decision: 'CONTINUE' });
      expect(chat['trialDecision']).toHaveBeenCalledWith('thread', 'CONTINUE', undefined); expect(page.showTrialDecision).toBe(false);
      await vi.advanceTimersByTimeAsync(800);
      if (['MATCH_BLOCKED', 'MATCH_ENDED'].includes(status)) expect(router.navigateByUrl).toHaveBeenCalledWith('/chats');
      else expect(chat['thread']).toHaveBeenCalled();
    });
  }

  it('ignores absent trial data and keeps failed decisions visibly unsuccessful', async () => {
    await page.onTrialDecision({ decision: 'END' }); expect(chat['trialDecision']).not.toHaveBeenCalled();
    page.data = response; chat['trialDecision'].mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.onTrialDecision({ decision: 'END' }); expect(page.toast).toBe('Could not submit decision.');
  });

  it('does not send without a thread, blank text or while a send is in progress', async () => {
    await page.send(); page.data = response; page.body = '  '; await page.send(); page.body = 'Synthetic'; page.sending = true; await page.send(); expect(chat['send']).not.toHaveBeenCalled();
  });

  it('sends trimmed text optimistically then refreshes the server message list', async () => {
    page.data = response; page.body = ' Synthetic message '; const sending = page.send();
    expect(page.data.messages[0].body).toBe('Synthetic message'); expect(page.body).toBe(''); expect(page.sending).toBe(true);
    await vi.advanceTimersByTimeAsync(150); await sending; expect(chat['send']).toHaveBeenCalledWith('thread', 'Synthetic message'); expect(page.sending).toBe(false); expect(page.data?.messages).toEqual([]);
  });

  it('removes a rejected optimistic send and re-enables sending', async () => {
    page.data = response; page.body = 'Synthetic'; chat['send'].mockReturnValue(throwError(() => new Error('Synthetic failure'))); await page.send();
    expect(page.data.messages).toEqual([]); expect(page.toast).toBe('Could not send.'); expect(page.sending).toBe(false);
  });

  for (const status of ['TRIAL_STARTED', 'POPPED']) {
    it(`handles ${status} pop results after the animation`, async () => {
      page.data = response; matches['pop'].mockReturnValue(of({ status })); const action = page.popBalloon(); const ignored = page.popBalloon(); await ignored;
      await vi.advanceTimersByTimeAsync(600); await action; expect(matches['pop']).toHaveBeenCalledTimes(1); expect(page.isPopping).toBe(false);
      if (status === 'TRIAL_STARTED') expect(chat['thread']).toHaveBeenCalled(); else expect(router.navigateByUrl).toHaveBeenCalledWith('/chats');
    });
  }

  it('does not pop absent data or a missing match and exposes rejection', async () => {
    await page.popBalloon(); page.data = { ...response, matchId: '' }; await page.popBalloon(); expect(matches['pop']).not.toHaveBeenCalled();
    page.data = response; matches['pop'].mockReturnValue(throwError(() => new Error('Synthetic failure'))); const action = page.popBalloon(); await vi.advanceTimersByTimeAsync(600); await action; expect(page.toast).toBe('Could not pop.'); expect(page.isPopping).toBe(false);
  });

  it('shows and cancels unmatch confirmation before issuing any request', async () => {
    await page.unmatch(); expect(page.showUnmatchRating).toBe(false); page.data = response; page.showMore = true; await page.unmatch(); expect(page.showUnmatchRating).toBe(true); expect(page.showMore).toBe(false);
    page.cancelUnmatch(); expect(page.showUnmatchRating).toBe(false); expect(matches['unmatch']).not.toHaveBeenCalled();
  });

  for (const failed of [false, true]) {
    it(`clears unmatch busy state after ${failed ? 'rejection' : 'confirmed success'}`, async () => {
      page.data = response; if (failed) matches['unmatch'].mockReturnValue(throwError(() => new Error('Synthetic failure')));
      const action = page.confirmUnmatch(undefined); await vi.advanceTimersByTimeAsync(500); await action;
      expect(matches['unmatch']).toHaveBeenCalledWith('match', undefined); await vi.advanceTimersByTimeAsync(800);
      if (failed) { expect(page.toast).toContain('Could not unmatch'); expect(page.isUnmatching).toBe(false); expect(router.navigateByUrl).not.toHaveBeenCalled(); }
      else expect(router.navigateByUrl).toHaveBeenCalledWith('/chats');
    });
  }

  for (const failed of [false, true]) {
    it(`handles block ${failed ? 'rejection' : 'success'} without overlapping requests`, async () => {
      page.data = response; if (failed) matches['block'].mockReturnValue(throwError(() => new Error('Synthetic failure')));
      const action = page.block(); await page.block(); await vi.advanceTimersByTimeAsync(400); await action; expect(matches['block']).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(800);
      if (failed) { expect(page.isBlocking).toBe(false); expect(router.navigateByUrl).not.toHaveBeenCalled(); }
      else expect(router.navigateByUrl).toHaveBeenCalledWith('/chats');
    });
  }

  it('toggles menus, handles outside clicks and supports valid game event shapes', () => {
    page.toggleMore(new MouseEvent('click')); expect(page.showMore).toBe(true); page.onDocClick(); expect(page.showMore).toBe(false); page.onDocClick();
    page.toggleGamePicker(); expect(page.showGamePicker).toBe(true); page.openGamePlayer(null); expect(page.showGamePlayer).toBe(false);
    page.openGamePlayer({ detail: { sessionId: 'session', gameType: 'KNOW_ME' } }); expect(page.currentGameSessionId).toBe('session'); expect(page.currentGameType).toBe('KNOW_ME');
    page.closeGamePlayer(); expect(page.showGamePlayer).toBe(false); expect(page.currentGameSessionId).toBeNull();
    page.openGamePlayer({ sessionId: 'other' }); expect(page.currentGameType).toBe('GAME');
  });

  for (const failed of [false, true]) {
    it(`handles game create/accept/reject/result ${failed ? 'failures' : 'successes'} locally`, async () => {
      page.data = response; const alert = vi.fn(); vi.stubGlobal('alert', alert);
      if (failed) for (const fn of Object.values(games)) fn.mockReturnValue(throwError(() => new Error('Synthetic failure')));
      else games['getResult'].mockReturnValue(of({ gameType: 'KNOW_ME', yourScore: 1, theirScore: 0, aiInsight: 'Unit fixture insight' }));
      await page.createGame('KNOW_ME'); expect(games['createSession']).toHaveBeenCalledWith('match', 'KNOW_ME');
      await page.onAcceptGame('session'); await vi.advanceTimersByTimeAsync(500); await page.onRejectGame('session'); await page.onOpenGame('session');
      if (failed) { expect(page.toast).toBe('Result not available yet'); expect(alert).not.toHaveBeenCalled(); }
      else { expect(alert).toHaveBeenCalled(); page.onGameCompleted(); await tick(); expect(chat['thread']).toHaveBeenCalled(); }
    });
  }

  it('ignores game creation without a selected match and guards duplicate date-interest submissions', () => {
    page.createGame('KNOW_ME'); expect(games['createSession']).not.toHaveBeenCalled(); page.planIt(0); expect(chat['expressDateInterest']).not.toHaveBeenCalled();
    page.data = { ...response, dateIdeas: ['Synthetic park'] }; chat['expressDateInterest'].mockReturnValue(of({ mutualInterest: true })); page.planIt(9); page.planIt(0); page.planIt(0);
    expect(chat['expressDateInterest']).toHaveBeenCalledTimes(1); expect(page.mutualInterest).toBe(true);
    page.selectedIdeaIndex = null; chat['expressDateInterest'].mockReturnValue(throwError(() => new Error('Synthetic failure'))); page.planIt(0); expect(page.selectedIdeaIndex).toBeNull();
  });

  it('records partner voice listening once and ignores own or unidentified messages', () => {
    page.onVoiceEnded({ messageId: 'voice', senderUserId: 2 }); expect(chat['voiceListened']).not.toHaveBeenCalled(); page.data = response;
    page.onVoiceEnded({}); page.onVoiceEnded({ messageId: 'mine', senderUserId: 1 }); page.onVoiceEnded({ messageId: 'voice', senderUserId: 2 }); page.onVoiceEnded({ messageId: 'voice', senderUserId: 2 });
    expect(chat['voiceListened']).toHaveBeenCalledTimes(1); expect(chat['voiceListened']).toHaveBeenCalledWith('thread', 'voice');
  });

  it('recovers microphone denial and ignores redundant stop requests', async () => {
    vi.stubGlobal('navigator', { mediaDevices: { getUserMedia: vi.fn().mockRejectedValue(new Error('Synthetic permission denial')) } });
    await page.startRecording(); expect(page.toast).toBe('Microphone access denied'); expect(page.isRecording).toBe(false); page.stopRecording();
  });
});
