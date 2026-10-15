import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { PushNotificationService } from './push-notification.service';
import { environment } from '../../environments/environment';

describe('Legacy push registration and subscription contracts', () => {
  let service: PushNotificationService;
  let http: HttpTestingController;
  let registration: any;
  let subscription: any;
  let register: ReturnType<typeof vi.fn>;
  let permission: ReturnType<typeof vi.fn>;
  const workerDescriptor = Object.getOwnPropertyDescriptor(navigator, 'serviceWorker');
  const setWorker = (value: unknown) => Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value });
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController); service = new PushNotificationService(TestBed.inject(HttpClient));
    subscription = { endpoint: 'https://synthetic.invalid/push', getKey: vi.fn((key: string) =>
      new Uint8Array(key === 'auth' ? [3, 4] : [1, 2]).buffer), unsubscribe: vi.fn().mockResolvedValue(true) };
    registration = { pushManager: { subscribe: vi.fn().mockResolvedValue(subscription), getSubscription: vi.fn().mockResolvedValue(subscription) } };
    register = vi.fn().mockResolvedValue(registration); permission = vi.fn().mockResolvedValue('granted');
    setWorker({ register, ready: Promise.resolve(registration) });
    vi.stubGlobal('PushManager', class {});
    vi.stubGlobal('Notification', { requestPermission: permission, permission: 'default' });
    vi.spyOn(console, 'log').mockImplementation(() => {});
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });
  afterEach(() => {
    http.verify(); vi.restoreAllMocks(); vi.unstubAllGlobals();
    if (workerDescriptor) Object.defineProperty(navigator, 'serviceWorker', workerDescriptor);
    else delete (navigator as any).serviceWorker;
  });
  const url = (path: string) => `${environment.apiUrl}/push-notifications/${path}`;
  async function flushKey() {
    await vi.waitFor(() => http.expectOne(url('vapid-public-key')).flush({ publicKey: 'AQI' }));
  }
  it('fails locally when subscribing before service-worker registration', async () => {
    await expect(service.subscribe()).rejects.toThrow('Service worker not registered');
    http.expectNone(url('vapid-public-key'));
  });
  it('returns false for browsers without PushManager without requesting permission', async () => {
    delete (window as any).PushManager;
    expect(service.isSupported()).toBe(false); expect(await service.initialize()).toBe(false);
    expect(register).not.toHaveBeenCalled(); expect(permission).not.toHaveBeenCalled();
  });
  it('returns false when service-worker registration fails', async () => {
    register.mockRejectedValue(new Error('synthetic registration failure'));
    expect(await service.initialize()).toBe(false); expect(permission).not.toHaveBeenCalled();
  });
  for (const value of ['denied', 'default']) {
    it(`does not subscribe when notification permission is ${value}`, async () => {
      permission.mockResolvedValue(value); expect(await service.initialize()).toBe(false);
      expect(register).toHaveBeenCalledWith('/service-worker.js', { scope: '/' });
      http.expectNone(url('vapid-public-key')); expect(registration.pushManager.subscribe).not.toHaveBeenCalled();
    });
  }
  it('decodes the VAPID key and sends endpoint and both encoded subscription keys', async () => {
    const pending = service.initialize(); await flushKey();
    await vi.waitFor(() => {
      const req = http.expectOne(url('subscribe'));
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ endpoint: subscription.endpoint, p256dh: 'AQI=', auth: 'AwQ=' }); req.flush({});
    });
    expect(await pending).toBe(true);
    const options = registration.pushManager.subscribe.mock.calls[0][0];
    expect(options.userVisibleOnly).toBe(true); expect(Array.from(options.applicationServerKey)).toEqual([1, 2]);
    expect(await service.isSubscribed()).toBe(true);
  });
  it('returns false when the VAPID endpoint fails during initialization', async () => {
    const pending = service.initialize();
    await vi.waitFor(() => http.expectOne(url('vapid-public-key')).flush({}, { status: 503, statusText: 'Unavailable' }));
    expect(await pending).toBe(false); expect(registration.pushManager.subscribe).not.toHaveBeenCalled();
  });
  it('propagates subscription-provider failures without saving a nonexistent subscription', async () => {
    (service as any).registration = registration;
    registration.pushManager.subscribe.mockRejectedValue(new Error('synthetic push failure'));
    const assertion = expect(service.subscribe()).rejects.toThrow('synthetic push failure');
    http.expectOne(url('vapid-public-key')).flush({ publicKey: 'AQI' }); await assertion;
    http.expectNone(url('subscribe'));
  });
  it('propagates a rejected backend subscription save', async () => {
    (service as any).registration = registration;
    const assertion = expect(service.subscribe()).rejects.toMatchObject({ status: 500 });
    http.expectOne(url('vapid-public-key')).flush({ publicKey: 'AQI' });
    await vi.waitFor(() => http.expectOne(url('subscribe')).flush({}, { status: 500, statusText: 'Server Error' }));
    await assertion;
  });
  it('reports no subscription before registration and safely ignores unsubscribe', async () => {
    expect(await service.isSubscribed()).toBe(false); await service.unsubscribe();
    http.expectNone(url('unsubscribe'));
  });
  it('ignores unsubscribe when the browser has no subscription', async () => {
    (service as any).registration = registration; registration.pushManager.getSubscription.mockResolvedValue(null);
    expect(await service.isSubscribed()).toBe(false); await service.unsubscribe();
    http.expectNone(url('unsubscribe')); expect(subscription.unsubscribe).not.toHaveBeenCalled();
  });
  it('removes the backend subscription before removing the browser subscription', async () => {
    (service as any).registration = registration; const pending = service.unsubscribe();
    await vi.waitFor(() => {
      const req = http.expectOne(url('unsubscribe')); expect(req.request.body).toEqual({ endpoint: subscription.endpoint });
      expect(subscription.unsubscribe).not.toHaveBeenCalled(); req.flush({});
    });
    await pending; expect(subscription.unsubscribe).toHaveBeenCalledOnce();
  });
  it('retains the browser subscription when the backend unsubscribe fails', async () => {
    (service as any).registration = registration;
    const assertion = expect(service.unsubscribe()).rejects.toMatchObject({ status: 503 });
    await vi.waitFor(() => http.expectOne(url('unsubscribe')).flush({}, { status: 503, statusText: 'Unavailable' }));
    await assertion; expect(subscription.unsubscribe).not.toHaveBeenCalled();
  });
  it('propagates browser unsubscribe failures after a successful backend response', async () => {
    (service as any).registration = registration; subscription.unsubscribe.mockRejectedValue(new Error('synthetic local failure'));
    const assertion = expect(service.unsubscribe()).rejects.toThrow('synthetic local failure');
    await vi.waitFor(() => http.expectOne(url('unsubscribe')).flush({})); await assertion;
  });
  it('reports supported browser features and the current permission value', () => {
    expect(service.isSupported()).toBe(true); expect(service.getPermissionStatus()).toBe('default');
  });
  it('converts URL-safe base64 key bytes without changing the binary values', () => {
    expect(Array.from((service as any).urlBase64ToUint8Array('-_8'))).toEqual([251, 255]);
  });
});
