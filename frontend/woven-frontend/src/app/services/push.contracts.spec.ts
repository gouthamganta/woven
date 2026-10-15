import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { PushService } from './push.service';
import { environment } from '../../environments/environment';

describe('Push subscription client boundaries', () => {
  let service: PushService;
  let http: HttpTestingController;
  let subscription: { endpoint: string; toJSON: ReturnType<typeof vi.fn>; unsubscribe: ReturnType<typeof vi.fn> };
  let worker: { register: ReturnType<typeof vi.fn>; getRegistration: ReturnType<typeof vi.fn>; ready: Promise<unknown> };
  let registration: { pushManager: { getSubscription: ReturnType<typeof vi.fn>; subscribe: ReturnType<typeof vi.fn> } };
  let permission: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    localStorage.setItem('accessToken', 'unit-token'); TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); service = TestBed.inject(PushService); http = TestBed.inject(HttpTestingController);
    subscription = { endpoint: 'https://unit.invalid/push', toJSON: vi.fn().mockReturnValue({ endpoint: 'https://unit.invalid/push', keys: { p256dh: 'unit-p256dh', auth: 'unit-auth' } }), unsubscribe: vi.fn().mockResolvedValue(true) };
    registration = { pushManager: { getSubscription: vi.fn().mockResolvedValue(subscription), subscribe: vi.fn().mockResolvedValue(subscription) } };
    worker = { register: vi.fn().mockResolvedValue(registration), getRegistration: vi.fn().mockResolvedValue(registration), ready: Promise.resolve(registration) };
    permission = vi.fn().mockResolvedValue('granted'); vi.stubGlobal('navigator', { serviceWorker: worker, userAgent: 'Unit browser' }); vi.stubGlobal('Notification', { requestPermission: permission });
    vi.spyOn(service, 'isSupported').mockResolvedValue(true);
  });
  afterEach(() => { http.verify(); vi.restoreAllMocks(); vi.unstubAllGlobals(); localStorage.clear(); });
  async function flushKey() {
    await vi.waitFor(() => { const requests = http.match(environment.apiUrl + '/me/vapid-public-key'); if (!requests.length) throw new Error('Awaiting local key request'); expect(requests).toHaveLength(1); expect(requests[0].request.headers.get('Authorization')).toBe('Bearer unit-token'); requests[0].flush({ publicKey: 'AAECAw' }); });
    await Promise.resolve(); await Promise.resolve();
  }
  it('reports no subscription when unsupported, unregistered or not subscribed', async () => {
    vi.mocked(service.isSupported).mockResolvedValue(false); expect(await service.isSubscribed()).toBe(false); vi.mocked(service.isSupported).mockResolvedValue(true); worker.getRegistration.mockResolvedValueOnce(undefined); expect(await service.isSubscribed()).toBe(false); registration.pushManager.getSubscription.mockResolvedValueOnce(null); expect(await service.isSubscribed()).toBe(false); expect(await service.isSubscribed()).toBe(true);
  });
  it('does not request browser permission when unsupported and does not subscribe after permission denial', async () => {
    vi.mocked(service.isSupported).mockResolvedValue(false); expect(await service.register()).toBe(false); expect(permission).not.toHaveBeenCalled(); vi.mocked(service.isSupported).mockResolvedValue(true); permission.mockResolvedValue('denied'); expect(await service.register()).toBe(false); expect(worker.register).not.toHaveBeenCalled();
  });
  it('registers the worker and persists only the subscription transport fields', async () => {
    const promise = service.register(); await flushKey();
    await vi.waitFor(() => { const requests = http.match(environment.apiUrl + '/me/push-subscription'); if (!requests.length) throw new Error('Awaiting local registration request'); expect(requests[0].request.body).toEqual({ endpoint: 'https://unit.invalid/push', p256dh: 'unit-p256dh', auth: 'unit-auth', userAgent: 'Unit browser' }); expect(requests[0].request.headers.get('Authorization')).toBe('Bearer unit-token'); requests[0].flush({}); });
    expect(await promise).toBe(true); expect(worker.register).toHaveBeenCalledWith('/push-sw.js', { scope: '/' }); expect([...new Uint8Array(registration.pushManager.subscribe.mock.calls[0][0].applicationServerKey)]).toEqual([0, 1, 2, 3]);
  });
  it('preserves browser subscription rejection without storing a successful server registration', async () => {
    registration.pushManager.subscribe.mockRejectedValue(new Error('Unit subscription rejected')); const promise = service.register(); const assertion = expect(promise).rejects.toThrow('Unit subscription rejected'); await flushKey(); await assertion; http.expectNone(environment.apiUrl + '/me/push-subscription');
  });
  it('preserves server registration failure instead of reporting success', async () => {
    const promise = service.register(); const assertion = expect(promise).rejects.toMatchObject({ status: 500 }); await flushKey();
    await vi.waitFor(() => { const requests = http.match(environment.apiUrl + '/me/push-subscription'); if (!requests.length) throw new Error('Awaiting local registration'); requests[0].flush({}, { status: 500, statusText: 'Unit failure' }); }); await assertion;
  });
  it('does nothing on unsubscribe when unsupported or the browser registration/subscription is absent', async () => {
    vi.mocked(service.isSupported).mockResolvedValue(false); await service.unregister(); vi.mocked(service.isSupported).mockResolvedValue(true); worker.getRegistration.mockResolvedValueOnce(undefined); await service.unregister(); registration.pushManager.getSubscription.mockResolvedValueOnce(null); await service.unregister(); expect(subscription.unsubscribe).not.toHaveBeenCalled();
  });
  for (const rejected of [false, true]) {
    it(`unsubscribes locally and tolerates server deletion ${rejected ? 'failure' : 'success'}`, async () => {
      const promise = service.unregister();
      await vi.waitFor(() => { const requests = http.match(environment.apiUrl + '/me/push-subscription'); if (!requests.length) throw new Error('Awaiting local delete'); expect(requests[0].request.method).toBe('DELETE'); expect(requests[0].request.body).toEqual({ endpoint: 'https://unit.invalid/push' }); if (rejected) requests[0].flush({}, { status: 500, statusText: 'Unit failure' }); else requests[0].flush({}); }); await promise; expect(subscription.unsubscribe).toHaveBeenCalledTimes(1);
    });
  }
});
