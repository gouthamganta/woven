import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class PushNotificationService {
  private readonly apiUrl = environment.apiUrl;
  private registration: ServiceWorkerRegistration | null = null;

  constructor(private http: HttpClient) {}

  /**
   * Initialize push notifications for the current user.
   * Registers service worker, requests permission, subscribes to push.
   */
  async initialize(): Promise<boolean> {
    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
      console.warn('[PushNotification] Browser does not support push notifications');
      return false;
    }

    try {
      // Register service worker
      this.registration = await navigator.serviceWorker.register('/service-worker.js', {
        scope: '/'
      });
      console.log('[PushNotification] Service worker registered');

      // Wait for service worker to be ready
      await navigator.serviceWorker.ready;

      // Request permission
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') {
        console.log('[PushNotification] Permission denied');
        return false;
      }

      // Subscribe to push notifications
      await this.subscribe();
      return true;
    } catch (error) {
      console.error('[PushNotification] Initialization failed:', error);
      return false;
    }
  }

  /**
   * Subscribe to push notifications.
   * Gets VAPID public key from backend and creates push subscription.
   */
  async subscribe(): Promise<void> {
    if (!this.registration) {
      throw new Error('Service worker not registered');
    }

    try {
      // Get VAPID public key from backend
      const response = await firstValueFrom(
        this.http.get<{ publicKey: string }>(`${this.apiUrl}/push-notifications/vapid-public-key`)
      );

      const publicKey = response.publicKey;

      // Convert VAPID key to Uint8Array
      const applicationServerKey = this.urlBase64ToUint8Array(publicKey);

      // Create push subscription
      const subscription = await this.registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: applicationServerKey as BufferSource
      });

      // Send subscription to backend
      await firstValueFrom(
        this.http.post(`${this.apiUrl}/push-notifications/subscribe`, {
          endpoint: subscription.endpoint,
          p256dh: this.arrayBufferToBase64(subscription.getKey('p256dh')!),
          auth: this.arrayBufferToBase64(subscription.getKey('auth')!)
        })
      );

      console.log('[PushNotification] Subscribed successfully');
    } catch (error) {
      console.error('[PushNotification] Subscription failed:', error);
      throw error;
    }
  }

  /**
   * Unsubscribe from push notifications.
   */
  async unsubscribe(): Promise<void> {
    if (!this.registration) {
      return;
    }

    try {
      const subscription = await this.registration.pushManager.getSubscription();
      if (!subscription) {
        return;
      }

      // Unsubscribe from backend
      await firstValueFrom(
        this.http.post(`${this.apiUrl}/push-notifications/unsubscribe`, {
          endpoint: subscription.endpoint
        })
      );

      // Unsubscribe locally
      await subscription.unsubscribe();
      console.log('[PushNotification] Unsubscribed successfully');
    } catch (error) {
      console.error('[PushNotification] Unsubscribe failed:', error);
      throw error;
    }
  }

  /**
   * Check if push notifications are currently subscribed.
   */
  async isSubscribed(): Promise<boolean> {
    if (!this.registration) {
      return false;
    }

    const subscription = await this.registration.pushManager.getSubscription();
    return subscription !== null;
  }

  /**
   * Check if push notifications are supported by the browser.
   */
  isSupported(): boolean {
    return 'serviceWorker' in navigator && 'PushManager' in window;
  }

  /**
   * Get current notification permission status.
   */
  getPermissionStatus(): NotificationPermission {
    return Notification.permission;
  }

  // Helper: Convert VAPID key from base64 to Uint8Array
  private urlBase64ToUint8Array(base64String: string): Uint8Array {
    const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    const base64 = (base64String + padding)
      .replace(/\-/g, '+')
      .replace(/_/g, '/');

    const rawData = window.atob(base64);
    const outputArray = new Uint8Array(rawData.length);

    for (let i = 0; i < rawData.length; ++i) {
      outputArray[i] = rawData.charCodeAt(i);
    }

    return outputArray;
  }

  // Helper: Convert ArrayBuffer to base64
  private arrayBufferToBase64(buffer: ArrayBuffer): string {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    return window.btoa(binary);
  }
}
