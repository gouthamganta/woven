import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly api = environment.apiUrl;

  constructor(private http: HttpClient) {}

  async isSupported(): Promise<boolean> {
    return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
  }

  async isSubscribed(): Promise<boolean> {
    if (!await this.isSupported()) return false;
    const reg = await navigator.serviceWorker.getRegistration('/push-sw.js');
    if (!reg) return false;
    const sub = await reg.pushManager.getSubscription();
    return sub !== null;
  }

  async register(): Promise<boolean> {
    if (!await this.isSupported()) return false;

    const permission = await Notification.requestPermission();
    if (permission !== 'granted') return false;

    const reg = await navigator.serviceWorker.register('/push-sw.js', { scope: '/' });
    await navigator.serviceWorker.ready;

    const { publicKey } = await firstValueFrom(
      this.http.get<{ publicKey: string }>(`${this.api}/me/vapid-public-key`, {
        headers: this.authHeaders()
      })
    );

    const sub = await reg.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: this.urlBase64ToUint8Array(publicKey).buffer as ArrayBuffer,
    });

    const json = sub.toJSON();
    await firstValueFrom(
      this.http.post(`${this.api}/me/push-subscription`, {
        endpoint: json.endpoint,
        p256dh: json.keys?.['p256dh'],
        auth: json.keys?.['auth'],
        userAgent: navigator.userAgent,
      }, { headers: this.authHeaders() })
    );

    return true;
  }

  async unregister(): Promise<void> {
    if (!await this.isSupported()) return;
    const reg = await navigator.serviceWorker.getRegistration('/push-sw.js');
    if (!reg) return;

    const sub = await reg.pushManager.getSubscription();
    if (!sub) return;

    const endpoint = sub.endpoint;
    await sub.unsubscribe();

    await firstValueFrom(
      this.http.delete(`${this.api}/me/push-subscription`, {
        headers: this.authHeaders(),
        body: { endpoint },
      })
    ).catch(() => {});
  }

  private authHeaders(): HttpHeaders {
    const token = localStorage.getItem('accessToken') ?? '';
    return new HttpHeaders({ Authorization: `Bearer ${token}` });
  }

  private urlBase64ToUint8Array(base64String: string): Uint8Array {
    const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(base64);
    return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
  }
}
