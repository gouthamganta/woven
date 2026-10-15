import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, Observable } from 'rxjs';
import { MomentsService } from './moments.service';
import { MatchesService } from './matches.service';
import { CommonsService } from './commons.service';
import { TilesService } from './tiles.service';
import { GamesService } from './games.service';
import { CoachingService } from './coaching.service';
import { SupportService } from './support.service';
import { PulseService } from './pulse.service';
import { PendingTaskService } from './pending-task.service';
import { FeedbackService } from './feedback.service';
import { environment } from '../../environments/environment';

type Contract = { name: string; call: () => Observable<unknown>; path: string; method: string; body?: unknown; query?: Record<string, string> };
describe('Client API transport contracts', () => {
  let http: HttpTestingController;
  beforeEach(() => { localStorage.setItem('accessToken', 'unit-token'); TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); http = TestBed.inject(HttpTestingController); });
  afterEach(() => { http.verify(); localStorage.clear(); });
  const get = <T>(type: new (...args: any[]) => T) => TestBed.inject(type);
  const contracts: Contract[] = [
    { name: 'deck', call: () => get(MomentsService).getMoments(), path: '/moments', method: 'GET' },
    { name: 'drawn', call: () => get(MomentsService).getLikedYou(), path: '/moments/liked-you', method: 'GET' },
    { name: 'pass', call: () => get(MomentsService).respond({ targetUserId: 2, choice: 'PASS', source: 'TODAY' }), path: '/moments/respond', method: 'POST', body: { targetUserId: 2, choice: 'PASS', source: 'TODAY' } },
    { name: 'note choice', call: () => get(MomentsService).choose({ targetUserId: 2, choice: 'MAGICAL', noteText: 'Controlled opening note', source: 'LIKED_YOU' }), path: '/moments/choose', method: 'POST', body: { targetUserId: 2, choice: 'MAGICAL', noteText: 'Controlled opening note', source: 'LIKED_YOU' } },
    { name: 'matches', call: () => get(MatchesService).list(), path: '/matches', method: 'GET' },
    { name: 'profile access', call: () => get(MatchesService).profileAccess('match'), path: '/matches/match/profile-access', method: 'GET' },
    { name: 'matched profile', call: () => get(MatchesService).profile('match'), path: '/matches/match/profile', method: 'GET' },
    { name: 'pop', call: () => get(MatchesService).pop('match'), path: '/matches/match/pop', method: 'POST', body: {} },
    { name: 'unmatch without rating', call: () => get(MatchesService).unmatch('match'), path: '/matches/match/unmatch', method: 'POST', body: {} },
    { name: 'unmatch with neutral rating', call: () => get(MatchesService).unmatch('match', 0), path: '/matches/match/unmatch', method: 'POST', body: { rating: 0 } },
    { name: 'block', call: () => get(MatchesService).block('match'), path: '/matches/match/block', method: 'POST', body: {} },
    { name: 'flag', call: () => get(MatchesService).flag('match', 'uncomfortable'), path: '/matches/match/flag', method: 'POST', body: { reason: 'uncomfortable' } },
    { name: 'feed first page', call: () => get(CommonsService).getFeed(), path: '/commons', method: 'GET', query: { page: '1' } },
    { name: 'feed existing session', call: () => get(CommonsService).getFeed(2, 'session'), path: '/commons', method: 'GET', query: { page: '2', sessionId: 'session' } },
    { name: 'feed refresh', call: () => get(CommonsService).refresh(), path: '/commons/refresh', method: 'POST', body: {} },
    { name: 'view unspecified duration', call: () => get(CommonsService).recordView('tile'), path: '/commons/tile/view', method: 'POST', body: {} },
    { name: 'view zero duration', call: () => get(CommonsService).recordView('tile', 0), path: '/commons/tile/view', method: 'POST', body: { durationMs: 0 } },
    { name: 'Commons text tile', call: () => get(CommonsService).createTile('text', 'Unit thought'), path: '/tiles', method: 'POST', body: { contentType: 'text', contentText: 'Unit thought', mediaUrl: null } },
    { name: 'Commons media tile', call: () => get(CommonsService).createTile('photo', undefined, '/unit'), path: '/tiles', method: 'POST', body: { contentType: 'photo', contentText: null, mediaUrl: '/unit' } },
    { name: 'orbit', call: () => get(CommonsService).orbitTile('tile'), path: '/orbit/tile', method: 'POST', body: {} },
    { name: 'own tiles', call: () => get(TilesService).getMine(), path: '/tiles/mine', method: 'GET' },
    { name: 'create own text tile', call: () => get(TilesService).create('text', 'Unit thought'), path: '/tiles', method: 'POST', body: { contentType: 'text', contentText: 'Unit thought', mediaUrl: null } },
    { name: 'create own media tile', call: () => get(TilesService).create('photo', undefined, '/unit'), path: '/tiles', method: 'POST', body: { contentType: 'photo', contentText: null, mediaUrl: '/unit' } },
    { name: 'highlight', call: () => get(TilesService).highlight('tile', 3), path: '/tiles/tile/highlight', method: 'POST', body: { slotNumber: 3 } },
    { name: 'unhighlight', call: () => get(TilesService).unhighlight('tile'), path: '/tiles/tile/highlight', method: 'DELETE' },
    { name: 'received orbits', call: () => get(TilesService).getReceivedOrbits(), path: '/orbit/received', method: 'GET' },
    { name: 'game availability', call: () => get(GamesService).getAvailability('match'), path: '/games/matches/match/availability', method: 'GET' },
    { name: 'game invitation', call: () => get(GamesService).createSession('match', 'KNOW_ME'), path: '/games/matches/match/sessions', method: 'POST', body: { gameType: 'KNOW_ME' } },
    { name: 'game accept', call: () => get(GamesService).acceptSession('session'), path: '/games/sessions/session/accept', method: 'POST', body: {} },
    { name: 'game reject', call: () => get(GamesService).rejectSession('session'), path: '/games/sessions/session/reject', method: 'POST', body: {} },
    { name: 'game round alias', call: () => get(GamesService).getCurrentRound('session'), path: '/games/sessions/session/round', method: 'GET' },
    { name: 'game guesses alias', call: () => get(GamesService).submitGuesses('session', { q1: 'A' }), path: '/games/sessions/session/answers', method: 'POST', body: { answers: { q1: 'A' } } },
    { name: 'game target answers', call: () => get(GamesService).submitTargetAnswers('session', { q1: 'B' }), path: '/games/sessions/session/target-answers', method: 'POST', body: { answers: { q1: 'B' } } },
    { name: 'game result', call: () => get(GamesService).getResult('session'), path: '/games/sessions/session/result', method: 'GET' },
    { name: 'coaching summary', call: () => get(CoachingService).getCurrentSummary(), path: '/coaching/current-summary', method: 'GET' },
    { name: 'coaching dismiss', call: () => get(CoachingService).dismiss(1), path: '/coaching/1/dismiss', method: 'POST', body: {} },
    { name: 'coaching opt out', call: () => get(CoachingService).optOut(), path: '/coaching/opt-out', method: 'POST', body: {} },
    { name: 'coaching opt in', call: () => get(CoachingService).optIn(), path: '/coaching/opt-in', method: 'POST', body: {} },
    { name: 'support history', call: () => get(SupportService).chat([{ role: 'user', content: 'Unit question' }]), path: '/support/chat', method: 'POST', body: { messages: [{ role: 'user', content: 'Unit question' }] } },
    { name: 'pulse current', call: () => get(PulseService).getCurrent(), path: '/intake/dynamic/current', method: 'GET' },
    { name: 'pulse answers', call: () => get(PulseService).submit({ d1_battery: 'high', d2_tone: 'playful', d3_role: 'driver' }), path: '/intake/dynamic', method: 'PUT', body: { answers: { d1_battery: 'high', d2_tone: 'playful', d3_role: 'driver' } } },
    { name: 'pending tasks', call: () => get(PendingTaskService).getPendingTasks(), path: '/me/pending-tasks', method: 'GET' },
    { name: 'interaction', call: () => get(PendingTaskService).logInteraction('UnitEvent', { key: 'value' }), path: '/me/interaction-log', method: 'POST', body: { eventType: 'UnitEvent', context: { key: 'value' } } },
    { name: 'feedback prompt', call: () => get(FeedbackService).getPrompt(), path: '/me/feedback-prompt', method: 'GET' },
    { name: 'feedback response', call: () => get(FeedbackService).submit('match', { metInPerson: false }), path: '/matches/match/feedback', method: 'POST', body: { metInPerson: false } },
  ];
  for (const contract of contracts) {
    it(`preserves the ${contract.name} route, method, payload and response`, async () => {
      const promise = firstValueFrom(contract.call()); const request = http.expectOne(r => r.url === environment.apiUrl + contract.path);
      expect(request.request.method).toBe(contract.method);
      if (contract.body !== undefined) expect(request.request.body).toEqual(contract.body);
      for (const [key, value] of Object.entries(contract.query || {})) expect(request.request.params.get(key)).toBe(value);
      // Transport ownership only: DTO validation and server behavior are separate suites.
      const response = { unitMarker: contract.name }; request.flush(response); expect(await promise).toEqual(response);
    });
    it(`propagates a ${contract.name} rejection without inventing success or retry`, async () => {
      const promise = firstValueFrom(contract.call()); const assertion = expect(promise).rejects.toMatchObject({ status: 403, error: { error: 'UNIT_FORBIDDEN' } });
      http.expectOne(r => r.url === environment.apiUrl + contract.path).flush({ error: 'UNIT_FORBIDDEN' }, { status: 403, statusText: 'Forbidden' }); await assertion; http.expectNone(r => r.url === environment.apiUrl + contract.path);
    });
  }
  it('cancels an unsubscribed feed request', () => {
    const subscription = get(CommonsService).getFeed().subscribe(); const request = http.expectOne(r => r.url === environment.apiUrl + '/commons'); subscription.unsubscribe(); expect(request.cancelled).toBe(true);
  });
  it('reads current feedback credentials separately for each request', async () => {
    for (const token of ['unit-one', 'unit-two']) { localStorage.setItem('accessToken', token); const promise = firstValueFrom(get(FeedbackService).getPrompt()); const request = http.expectOne(environment.apiUrl + '/me/feedback-prompt'); expect(request.request.headers.get('Authorization')).toBe('Bearer ' + token); request.flush({ hasPendingPrompt: false }); await promise; }
  });
});
