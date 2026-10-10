import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ChatService } from './chat.service';
import { environment } from '../../environments/environment';

describe('Chat client contracts', () => {
  let service: ChatService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ChatService] });
    service = TestBed.inject(ChatService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('loads an empty chat list without manufacturing conversations', async () => {
    const result = firstValueFrom(service.list());
    const request = http.expectOne(`${environment.apiUrl}/chats`);
    expect(request.request.method).toBe('GET');
    request.flush({ count: 0, chats: [] });
    expect(await result).toEqual({ count: 0, chats: [] });
  });

  it('starts the selected match and preserves the returned thread identifier', async () => {
    const result = firstValueFrom(service.start('synthetic-match'));
    const request = http.expectOne(`${environment.apiUrl}/chats/start`);
    expect(request.request.body).toEqual({ matchId: 'synthetic-match' });
    request.flush({ threadId: 'synthetic-thread', matchId: 'synthetic-match' });
    expect((await result).threadId).toBe('synthetic-thread');
  });

  it('preserves both partner ChatNotes and chronological messages in a thread response', async () => {
    const response = { threadId: 'thread', matchId: 'match', balloonState: 'ACTIVE', messages: [], chatNotes: [
      { fromUserId: 1, noteText: 'Synthetic opening note A', choice: 'MAGICAL', createdAt: '2026-10-08T00:00:00Z' },
      { fromUserId: 2, noteText: 'Synthetic opening note B', choice: 'LOGICAL', createdAt: '2026-10-08T00:01:00Z' },
    ] };
    const result = firstValueFrom(service.thread('thread'));
    http.expectOne(`${environment.apiUrl}/chats/thread`).flush(response);
    expect(await result).toEqual(response);
  });

  for (const body of ['', '   ', '\n\t', 'x'.repeat(1001)]) {
    it(`rejects a ${body.length}-character invalid message before network access`, () => {
      expect(() => service.send('thread', body)).toThrow();
      http.expectNone(`${environment.apiUrl}/chats/thread/messages`);
    });
  }

  for (const length of [1, 1000]) {
    it(`sends a trimmed message at the ${length}-character boundary`, async () => {
      const result = firstValueFrom(service.send('thread', `  ${'x'.repeat(length)} \n`));
      const request = http.expectOne(`${environment.apiUrl}/chats/thread/messages`);
      expect(request.request.body).toEqual({ body: 'x'.repeat(length) });
      request.flush({ status: 'SENT', messageId: 'message', createdAt: '2026-10-08T00:00:00Z' });
      expect((await result).status).toBe('SENT');
    });
  }

  for (const decision of ['CONTINUE', 'END', 'BLOCK'] as const) {
    it(`submits ${decision} without silently changing the trial decision`, async () => {
      const result = firstValueFrom(service.trialDecision('thread', decision));
      const request = http.expectOne(`${environment.apiUrl}/chats/thread/trial-decision`);
      expect(request.request.body).toEqual({ decision });
      request.flush({ status: 'accepted' });
      await result;
    });
  }

  it('includes an explicitly supplied end reason', async () => {
    const result = firstValueFrom(service.trialDecision('thread', 'END', 'not_a_fit'));
    const request = http.expectOne(`${environment.apiUrl}/chats/thread/trial-decision`);
    expect(request.request.body).toEqual({ decision: 'END', endReason: 'not_a_fit' });
    request.flush({ status: 'accepted' });
    await result;
  });

  it('preserves server rejection instead of inventing a successful send or retry', async () => {
    const result = firstValueFrom(service.send('thread', 'Synthetic message'));
    const assertion = expect(result).rejects.toMatchObject({ status: 403 });
    http.expectOne(`${environment.apiUrl}/chats/thread/messages`).flush({ error: 'FORBIDDEN' }, { status: 403, statusText: 'Forbidden' });
    await assertion;
    http.expectNone(`${environment.apiUrl}/chats/thread/messages`);
  });

  it('submits a chosen date idea with its index and text', async () => {
    const result = firstValueFrom(service.expressDateInterest('thread', 1, 'Synthetic park'));
    const request = http.expectOne(`${environment.apiUrl}/chats/thread/date-interest`);
    expect(request.request.body).toEqual({ ideaIndex: 1, ideaText: 'Synthetic park' });
    request.flush({ mutualInterest: false });
    expect(await result).toEqual({ mutualInterest: false });
  });

  it('sends an uploaded voice reference and duration', async () => {
    const result = firstValueFrom(service.sendVoiceMessage('thread', '/synthetic.webm', 3));
    const request = http.expectOne(`${environment.apiUrl}/chats/thread/voice-message`);
    expect(request.request.body).toEqual({ audioUrl: '/synthetic.webm', durationSecs: 3 });
    request.flush({ messageId: 'voice', createdAt: '2026-10-08T00:00:00Z' });
    expect((await result).messageId).toBe('voice');
  });

  it('records listening on the selected message only', async () => {
    const result = firstValueFrom(service.voiceListened('thread', 'voice'));
    const request = http.expectOne(`${environment.apiUrl}/chats/thread/messages/voice/voice-listened`);
    expect(request.request.body).toEqual({});
    request.flush({ ok: true });
    await result;
  });
});
