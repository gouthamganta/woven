# Notifications API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Web Push notifications for new matches, messages, and moments. Uses VAPID protocol (Web Push standard).

**Key Concepts:**
- **VAPID** — Voluntary Application Server Identification for Web Push
- **Subscription** — Browser push subscription (endpoint + keys)
- **Service Worker** — Client-side worker handles push events

**Source:** [`backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs`](../../backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs)

---

## Endpoints

### GET /push-notifications/vapid-public-key

**Description:** Get VAPID public key for client subscription.

**Authentication:** None (public endpoint)

**Response (200 OK):**
```json
{
  "publicKey": "BKxJ8..."
}
```

**Usage:** Client uses this key when calling `pushManager.subscribe()`.

**Source:** [`PushNotificationEndpoints.cs:13-15`](../../backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs)

---

### POST /push-notifications/subscribe

**Description:** Register a browser push subscription.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/...",
  "p256dh": "BKxJ8...",
  "auth": "4vQK9..."
}
```

**Fields:**
- `endpoint` — Push service URL (from browser `PushSubscription`)
- `p256dh` — Public key (base64)
- `auth` — Auth secret (base64)

**Response (200 OK) — New Subscription:**
```json
{
  "status": "subscribed",
  "id": "uuid"
}
```

**Response (200 OK) — Already Exists:**
```json
{
  "status": "already_subscribed",
  "id": "uuid"
}
```

**Errors:**
- `400 INVALID_SUBSCRIPTION` — Missing required fields

**Source:** [`PushNotificationEndpoints.cs:34-72`](../../backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs)

---

### POST /push-notifications/unsubscribe

**Description:** Remove a push subscription.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/..."
}
```

**Response (200 OK):**
```json
{
  "status": "unsubscribed"
}
```

**Response (200 OK) — Not Found:**
```json
{
  "status": "not_found"
}
```

**Source:** [`PushNotificationEndpoints.cs:74-99`](../../backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs)

---

## Push Event Types

**Triggered by `NotificationService`:**

| Event | Trigger | Payload |
|-------|---------|---------|
| `MomentReceived` | New match created | Match details |
| `NewChatMessage` | Message sent | Sender name, preview |
| `SendPush` | Manual trigger | Custom payload |

**Source:** [`NotificationService.cs`](../../backend/WovenBackend/Services/NotificationService.cs)

---

## Client Implementation

### Service Worker Registration

**Frontend:** [`frontend/woven-frontend/public/service-worker.js`](../../frontend/woven-frontend/public/service-worker.js)

```javascript
self.addEventListener('push', (event) => {
  const data = event.data.json();
  
  const options = {
    body: data.body,
    icon: data.icon || '/icon-192.png',
    badge: '/badge-72.png',
    data: {
      url: data.url || '/'
    }
  };
  
  event.waitUntil(
    self.registration.showNotification(data.title, options)
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  event.waitUntil(
    clients.openWindow(event.notification.data.url)
  );
});
```

---

### Subscribe Flow

**Frontend:** [`frontend/src/app/services/push-notification.service.ts`](../../frontend/woven-frontend/src/app/services/push-notification.service.ts)

```typescript
async function subscribeUser() {
  // 1. Get VAPID public key
  const { publicKey } = await fetch('/push-notifications/vapid-public-key').then(r => r.json());
  
  // 2. Request permission
  const permission = await Notification.requestPermission();
  if (permission !== 'granted') return;
  
  // 3. Subscribe via service worker
  const registration = await navigator.serviceWorker.ready;
  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(publicKey)
  });
  
  // 4. Send subscription to server
  await fetch('/push-notifications/subscribe', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      endpoint: subscription.endpoint,
      p256dh: arrayBufferToBase64(subscription.getKey('p256dh')),
      auth: arrayBufferToBase64(subscription.getKey('auth'))
    })
  });
}
```

---

## VAPID Configuration

**Keys stored in:** Azure Key Vault (production) / User Secrets (local)

**Environment Variables:**
- `VAPID_PUBLIC_KEY` — Public key (shared with client)
- `VAPID_PRIVATE_KEY` — Private key (server-only)
- `VAPID_SUBJECT` — `mailto:support@wooven.me` or site URL

**Service:** [`WebPushService.cs`](../../backend/WovenBackend/Services/PushNotifications/WebPushService.cs)

---

## Database Schema

**Table:** `user_push_subscriptions`

**Columns:**
- `id` — Primary key (UUID)
- `user_id` — Foreign key to users
- `endpoint` — Push service URL
- `p256dh` — Public key
- `auth` — Auth secret
- `user_agent` — Browser user agent (for debugging)
- `created_at` — Subscription timestamp

**Indexes:**
- Unique on `(user_id, endpoint)` — Prevent duplicate subscriptions

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs`
- `backend/WovenBackend/Services/PushNotifications/WebPushService.cs`
- `backend/WovenBackend/Services/NotificationService.cs`
- `frontend/woven-frontend/public/service-worker.js`
- `frontend/woven-frontend/src/app/services/push-notification.service.ts`
