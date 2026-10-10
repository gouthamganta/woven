import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { Observable } from 'rxjs';
import { RealtimeService } from './realtime.service';
import { environment } from '../../environments/environment';
import * as signalR from '@microsoft/signalr';

const transport = { handlers: new Map<string, (value: any) => void>(), start: vi.fn(), stop: vi.fn(), withUrl: vi.fn(), build: vi.fn() };

describe('Realtime client connection contracts', () => {
  beforeEach(() => {
    transport.handlers.clear(); transport.start.mockReset().mockResolvedValue(undefined); transport.stop.mockReset().mockResolvedValue(undefined); transport.withUrl.mockClear(); transport.build.mockClear(); localStorage.clear();
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'withUrl').mockImplementation(function(this: signalR.HubConnectionBuilder, ...args: any[]) { transport.withUrl(...args); return this; });
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockImplementation(() => { transport.build(); return { on: (event: string, handler: (value: any) => void) => transport.handlers.set(event, handler), start: transport.start, stop: transport.stop } as unknown as signalR.HubConnection; });
  });
  afterEach(() => { vi.restoreAllMocks(); localStorage.clear(); });
  function create(platform = 'browser') { TestBed.configureTestingModule({ providers: [RealtimeService, { provide: PLATFORM_ID, useValue: platform }] }); return TestBed.inject(RealtimeService); }
  it('does not open network transport in server rendering', () => { const service = create('server'); service.start(); service.stop(); expect(transport.build).not.toHaveBeenCalled(); });
  it('starts once, reads credentials on each connection attempt, and permits restart after logout', async () => {
    const service = create(); service.stop(); service.start(); service.start(); expect(transport.build).toHaveBeenCalledTimes(1); expect(transport.start).toHaveBeenCalledTimes(1);
    expect(transport.withUrl.mock.calls[0][0]).toBe(environment.apiUrl + '/hubs/woven'); const options = transport.withUrl.mock.calls[0][1]; expect(options.accessTokenFactory()).toBe(''); localStorage.setItem('accessToken', 'unit-one'); expect(options.accessTokenFactory()).toBe('unit-one'); localStorage.setItem('accessToken', 'unit-two'); expect(options.accessTokenFactory()).toBe('unit-two');
    service.stop(); service.stop(); expect(transport.stop).toHaveBeenCalledTimes(1); service.start(); expect(transport.build).toHaveBeenCalledTimes(2); service.stop(); await Promise.resolve();
  });
  for (const [event, subject] of [['DeckReady', 'deckReady$'], ['MomentReceived', 'momentReceived$'], ['MomentExpired', 'momentExpired$'], ['GameInviteReceived', 'gameInviteReceived$'], ['GameStarted', 'gameStarted$'], ['GameCompleted', 'gameCompleted$'], ['NewChatMessage', 'newChatMessage$']] as const) {
    it(`forwards only the ${event} payload to its corresponding observable`, () => {
      const service = create(); const received = vi.fn(); (service[subject] as Observable<unknown>).subscribe(received); service.start(); const payload = { unit: event }; transport.handlers.get(event)!(payload); expect(received).toHaveBeenCalledTimes(1); expect(received).toHaveBeenCalledWith(payload); service.stop();
    });
  }
  it('unwraps the server chat-message envelope', () => { const service = create(); const received = vi.fn(); service.newChatMessage$.subscribe(received); service.start(); const payload = { threadId: 'unit', messageId: 'message' }; transport.handlers.get('NewChatMessage')!({ payload }); expect(received).toHaveBeenCalledWith(payload); service.stop(); });
  it('contains asynchronous transport start/stop failures', async () => { const warning = vi.spyOn(console, 'warn').mockImplementation(() => {}); transport.start.mockRejectedValue(new Error('Unit connection failure')); transport.stop.mockRejectedValue(new Error('Unit stop failure')); const service = create(); service.start(); await Promise.resolve(); expect(warning).toHaveBeenCalled(); service.stop(); await Promise.resolve(); });
});
