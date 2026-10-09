# Frontend Push Notifications

**Last Updated:** 2026-08-17  
**Service:** `PushNotificationService`  
**Location:** `frontend/woven-frontend/src/app/services/push-notification.service.ts`

---

## Overview

The frontend push notification system handles browser-native push subscriptions through the `PushNotificationService`. This service manages:

1. **Service worker registration** (`/service-worker.js`)
2. **Permission requests** (browser notification permission)
3. **VAPID key fetching** (from backend)
4. **Push subscription creation** (via Push API)
5. **Subscription sync** (send to backend for storage)

**Design principle:** Graceful degradation. App works fully without push enabled, but users miss background notifications.

---

## Architecture

```
User opens app
      ↓
PushNotificationService.initialize()
      ↓
Register service worker → Request permission → Subscribe to push
      ↓
POST /push-notifications/subscribe (send subscription to backend)
      ↓
Backend stores in user_push_subscriptions table
      ↓
[Subscribed — ready to receive push]
```

---

## PushNotificationService API

### `initialize(): Promise<boolean>`

**Purpose:** Complete push setup flow (register SW + request permission + subscribe).

**Returns:**
- `true` — Push enabled successfully
- `false` — Browser unsupported, permission denied, or error occurred

**Flow:**
1. Check browser support (`serviceWorker` + `PushManager`)
2. Register service worker at `/service-worker.js` (scope: `/`)
3. Wait for service worker ready
4. Request notification permission (`Notification.requestPermission()`)
5. If granted, call `subscribe()` internally
6. Return success/failure

**Usage:**
```typescript
// In app initialization (e.g., after login)
const success = await this.pushNotificationService.initialize();
if (success) {
  console.log('Push notifications enabled');
} else {
  console.warn('Push notifications unavailable');
}
```

**When to call:** Once per app session, after user logs in (or on app load if already logged in).

**Error handling:** Catches all errors, logs to console, returns `false` (never throws).

---

### `subscribe(): Promise<void>`

**Purpose:** Create push subscription and send to backend.

**Throws:** If service worker not registered or subscription fails.

**Flow:**
1. GET `/push-notifications/vapid-public-key` → fetch VAPID public key
2. Convert base64url key → `Uint8Array` (via `urlBase64ToUint8Array`)
3. Call `pushManager.subscribe({ userVisibleOnly: true, applicationServerKey })`
4. Browser returns subscription object with `endpoint`, `p256dh`, `auth` keys
5. Convert keys to base64 (via `arrayBufferToBase64`)
6. POST `/push-notifications/subscribe` with subscription data
7. Backend stores subscription (idempotent — returns existing if duplicate)

**Payload sent to backend:**
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/abc123...",
  "p256dh": "BG8x...",  // Public key for encryption (base64)
  "auth": "..."         // Auth secret (base64)
}
```

**Usage:**
```typescript
// Manual subscription (e.g., settings page "Enable Notifications" button)
try {
  await this.pushNotificationService.subscribe();
  console.log('Subscribed successfully');
} catch (error) {
  console.error('Subscription failed:', error);
}
```

**When to call:**
- Automatically called by `initialize()` if permission granted
- Manually called if user re-enables push after disabling

---

### `unsubscribe(): Promise<void>`

**Purpose:** Remove push subscription (both locally and from backend).

**Throws:** On HTTP failure (not on "already unsubscribed").

**Flow:**
1. Get current subscription (`pushManager.getSubscription()`)
2. If none exists, return early (no-op)
3. POST `/push-notifications/unsubscribe` with `{ endpoint }`
4. Backend removes subscription from DB
5. Call `subscription.unsubscribe()` locally (browser-side cleanup)

**Usage:**
```typescript
// Settings page "Disable Notifications" button
await this.pushNotificationService.unsubscribe();
console.log('Unsubscribed successfully');
```

**When to call:** User explicitly disables push in settings, or on logout (optional cleanup).

---

### `isSubscribed(): Promise<boolean>`

**Purpose:** Check if user currently has an active push subscription.

**Returns:**
- `true` — Subscription exists
- `false` — No subscription or service worker not registered

**Usage:**
```typescript
// Settings page: show "Enable" vs "Disable" button
const subscribed = await this.pushNotificationService.isSubscribed();
if (subscribed) {
  // Show "Disable Notifications" button
} else {
  // Show "Enable Notifications" button
}
```

**Note:** Checks local browser state only (does not verify backend has subscription).

---

### `isSupported(): boolean`

**Purpose:** Check if browser supports push notifications.

**Returns:**
- `true` — Browser has `serviceWorker` and `PushManager` APIs
- `false` — Unsupported browser (old browsers, HTTP-only sites)

**Usage:**
```typescript
// Settings page: hide push toggle if unsupported
if (!this.pushNotificationService.isSupported()) {
  console.warn('Push notifications not supported');
  // Hide push settings UI
}
```

**Supported browsers:**
- Chrome 42+, Firefox 44+, Safari 16+, Edge 17+
- HTTPS required (or localhost for dev)

---

### `getPermissionStatus(): NotificationPermission`

**Purpose:** Get current notification permission state.

**Returns:**
- `"granted"` — User allowed notifications
- `"denied"` — User blocked notifications
- `"default"` — Not asked yet (or reset)

**Usage:**
```typescript
const permission = this.pushNotificationService.getPermissionStatus();
if (permission === 'denied') {
  // Show instructions to re-enable in browser settings
}
```

**Note:** Once denied, `requestPermission()` won't show prompt again (user must re-enable in browser settings).

---

## Permission Request Flow

### Best Practices

**1. Don't prompt immediately on app load**
- Bad UX: user doesn't trust app yet
- High denial rate (users reflexively block)

**2. Ask at a contextual moment**
- After first match received: "Get notified when someone likes you!"
- In settings: user actively wants to enable

**3. Explain value before asking**
- Modal: "Stay updated on new matches and messages, even when the app is closed."
- Button: "Enable Notifications" → then call `initialize()`

**4. Handle denial gracefully**
- Don't nag user (permission denial is sticky)
- Show how to re-enable in browser settings (not in-app)

### Example Implementation

```typescript
// In a component (e.g., onboarding, settings, or after first match)
async enablePushNotifications() {
  // Check support
  if (!this.pushNotificationService.isSupported()) {
    alert('Your browser does not support push notifications.');
    return;
  }

  // Check current permission
  const currentPermission = this.pushNotificationService.getPermissionStatus();
  if (currentPermission === 'denied') {
    alert('Notifications blocked. Please enable in browser settings.');
    return;
  }

  // Initialize (includes permission request if not granted)
  const success = await this.pushNotificationService.initialize();
  if (success) {
    alert('Notifications enabled!');
  } else {
    alert('Failed to enable notifications. Please try again.');
  }
}
```

---

## Service Worker Registration

### File Location

Service worker must be served from **public root**: `/service-worker.js`

**Angular config:** Place file in `public/` directory (served as static asset).

**Registration:**
```typescript
this.registration = await navigator.serviceWorker.register('/service-worker.js', {
  scope: '/'  // Controls all app routes
});
```

**Scope:** `/` means service worker can intercept all requests under the origin (required for deep linking to `/chats`, `/moments`, etc.).

**HTTPS requirement:** Service workers only work on HTTPS (or localhost for dev).

### Lifecycle

```
1. navigator.serviceWorker.register('/service-worker.js')
   ↓
2. Browser downloads service-worker.js
   ↓
3. 'install' event fires in service worker
   ↓
4. 'activate' event fires (service worker now active)
   ↓
5. Service worker controls all pages under scope '/'
   ↓
6. Push subscription created (via pushManager.subscribe)
   ↓
   [Service worker waits for push events]
```

**Updates:** If `service-worker.js` changes, browser auto-downloads new version, but old version stays active until all tabs close (unless `skipWaiting()` called).

---

## VAPID Key Handling

### Fetching Public Key

**Endpoint:** `GET /push-notifications/vapid-public-key` (unauthenticated)

**Response:**
```json
{
  "publicKey": "BG8x..."  // Base64url-encoded (65 bytes uncompressed)
}
```

**Why public?** VAPID public key is not sensitive (like a domain name). Private key stays secret on backend.

### Conversion to Uint8Array

Browser's `subscribe()` method requires VAPID key as `Uint8Array`, not base64 string.

**Conversion function:**
```typescript
private urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding)
    .replace(/\-/g, '+')
    .replace(/_/g, '/');

  const rawData = window.atob(base64);  // Decode base64 → binary string
  const outputArray = new Uint8Array(rawData.length);

  for (let i = 0; i < rawData.length; ++i) {
    outputArray[i] = rawData.charCodeAt(i);
  }

  return outputArray;
}
```

**Why base64url?** VAPID spec uses URL-safe base64 (`-` and `_` instead of `+` and `/`).

---

## Subscription Object

### Browser-Generated Subscription

When `pushManager.subscribe()` succeeds, browser returns a `PushSubscription` object:

```typescript
{
  endpoint: "https://fcm.googleapis.com/fcm/send/abc123...",  // Push service URL
  keys: {
    p256dh: ArrayBuffer,  // Public key for encryption (ECDH P-256)
    auth: ArrayBuffer     // Auth secret for encryption
  }
}
```

**Fields:**
- `endpoint` — URL of browser's push service (FCM for Chrome, APNs for Safari, etc.)
- `p256dh` — Elliptic curve public key for encrypting push payloads
- `auth` — Secret for authenticating encrypted payloads

### Converting to Backend Format

Backend expects base64-encoded keys (not raw ArrayBuffers).

**Conversion:**
```typescript
const subscription = await this.registration.pushManager.subscribe(...);

const payload = {
  endpoint: subscription.endpoint,
  p256dh: this.arrayBufferToBase64(subscription.getKey('p256dh')!),
  auth: this.arrayBufferToBase64(subscription.getKey('auth')!)
};

// POST to backend
await this.http.post(`${apiUrl}/push-notifications/subscribe`, payload);
```

**Base64 conversion function:**
```typescript
private arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return window.btoa(binary);  // Encode binary → base64
}
```

---

## Error Handling

### Browser Support Check

```typescript
if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
  console.warn('[PushNotification] Browser does not support push notifications');
  return false;
}
```

**Unsupported browsers:**
- Internet Explorer (all versions)
- Safari < 16 (desktop)
- Safari < 16.4 (iOS, requires PWA install)

**Fallback:** App works normally, user just doesn't get background notifications.

---

### Permission Denied

```typescript
const permission = await Notification.requestPermission();
if (permission !== 'granted') {
  console.log('[PushNotification] Permission denied');
  return false;
}
```

**Handling denial:**
- **Don't retry immediately** (annoying UX)
- **Show instructions** to re-enable in browser settings (browser-specific)
- **Store preference** to not prompt again (avoid nagging)

**Re-enabling:** User must go to browser settings → Site Settings → Notifications → Allow.

---

### Subscription Failure

```typescript
try {
  await this.subscribe();
  console.log('[PushNotification] Subscribed successfully');
} catch (error) {
  console.error('[PushNotification] Subscription failed:', error);
  throw error;
}
```

**Common causes:**
- Service worker not registered (call `initialize()` first)
- VAPID key fetch failed (backend down or CORS issue)
- Browser push service unavailable (rare)

**Recovery:** Retry on next app open (subscription is idempotent).

---

### Backend Sync Failure

```typescript
// POST /push-notifications/subscribe
await firstValueFrom(
  this.http.post(`${this.apiUrl}/push-notifications/subscribe`, {
    endpoint: subscription.endpoint,
    p256dh: ...,
    auth: ...
  })
);
```

**Possible errors:**
- 401 Unauthorized (JWT expired or invalid)
- 400 Bad Request (invalid subscription format)
- 500 Internal Server Error (backend issue)

**Recovery:**
- 401 → re-login (JWT refresh)
- 400 → log error, do NOT retry (malformed subscription)
- 500 → retry with exponential backoff (transient failure)

---

## Testing

### Local Development

1. **Check browser support:**
   ```typescript
   console.log('Service Worker:', 'serviceWorker' in navigator);
   console.log('Push Manager:', 'PushManager' in window);
   console.log('Permission:', Notification.permission);
   ```

2. **Register service worker manually:**
   ```typescript
   const reg = await navigator.serviceWorker.register('/service-worker.js');
   console.log('Registration:', reg);
   ```

3. **Subscribe to push:**
   ```typescript
   await this.pushNotificationService.initialize();
   const subscribed = await this.pushNotificationService.isSubscribed();
   console.log('Subscribed:', subscribed);
   ```

4. **Check subscription in DevTools:**
   - Chrome → DevTools → Application → Service Workers (should show `service-worker.js` active)
   - Application → Push Messaging (should show subscription endpoint + keys)

5. **Trigger test push:**
   - Backend: `await _webPush.SendToUserAsync(userId, "Test", "Body", ...);`
   - Notification should appear in OS notification center

---

### Production Testing

1. **Check HTTPS:** Service workers require HTTPS (not HTTP).

2. **Check service worker served from root:** `/service-worker.js` (not `/assets/service-worker.js`).

3. **Monitor logs:**
   ```
   [PushNotification] Service worker registered
   [PushNotification] Subscribed successfully
   ```

4. **Check backend logs:**
   ```sql
   SELECT * FROM user_push_subscriptions WHERE user_id = ?;
   ```

---

## Integration Points

### After Login

```typescript
// In app.component.ts or login handler
async onLoginSuccess() {
  // Wait for user to be authenticated
  await this.authService.loadUser();

  // Enable push notifications (if permission already granted)
  if (this.pushNotificationService.getPermissionStatus() === 'granted') {
    await this.pushNotificationService.initialize();
  }
}
```

---

### Settings Page

```typescript
// settings.component.ts
pushEnabled = false;
pushSupported = false;

async ngOnInit() {
  this.pushSupported = this.pushNotificationService.isSupported();
  if (this.pushSupported) {
    this.pushEnabled = await this.pushNotificationService.isSubscribed();
  }
}

async togglePushNotifications() {
  if (this.pushEnabled) {
    await this.pushNotificationService.unsubscribe();
    this.pushEnabled = false;
  } else {
    const success = await this.pushNotificationService.initialize();
    this.pushEnabled = success;
  }
}
```

**UI:**
```html
<div *ngIf="pushSupported">
  <button (click)="togglePushNotifications()">
    {{ pushEnabled ? 'Disable' : 'Enable' }} Notifications
  </button>
</div>
<div *ngIf="!pushSupported">
  <p>Your browser does not support push notifications.</p>
</div>
```

---

### Onboarding Flow

```typescript
// onboarding/completion.component.ts
async onOnboardingComplete() {
  // Explain value prop
  const enablePush = await this.showPushPrompt();
  
  if (enablePush) {
    await this.pushNotificationService.initialize();
  }

  // Navigate to main app
  this.router.navigate(['/moments']);
}

private async showPushPrompt(): Promise<boolean> {
  // Show modal: "Stay updated on new matches!"
  // Return true if user clicks "Enable", false if "Skip"
}
```

---

## Security

### Service Worker Scope

**Scope:** `/` (controls all app routes).

**Risk:** Malicious service worker could intercept all network requests (MITM attack).

**Mitigation:**
- Service worker served from same origin (cannot be cross-origin)
- HTTPS-only (prevents MITM during SW download)
- Code reviewed (no dynamic `importScripts()` or `eval()`)

---

### Subscription Keys

**Keys stored in DB:**
- `endpoint` — Opaque URL (no PII)
- `p256dh` — Public encryption key (not sensitive)
- `auth` — Shared secret for encryption (not PII)

**Security:**
- Keys are user-specific (one user cannot decrypt another's push)
- Endpoint is time-limited (expires after 30-90 days of inactivity)
- No sensitive data in notification payloads (ephemeral content only)

---

## Performance

### Subscription Latency

**Typical flow:**
1. `initialize()` called → 50-200ms (service worker registration)
2. `requestPermission()` → blocking (waits for user interaction)
3. `subscribe()` → 100-300ms (VAPID key fetch + push subscription)
4. POST `/subscribe` → 50-150ms (backend storage)

**Total (excluding permission prompt):** 200-650ms.

**Optimization:** Call `initialize()` in background after login (non-blocking).

---

### Service Worker Registration

**First load:** 50-200ms (download + parse + install)

**Subsequent loads:** <10ms (cached, already active)

**Update check:** Browser auto-checks for updates every 24h (or on page refresh).

---

## Debugging

### Common Issues

**Issue:** `initialize()` returns `false`

**Causes:**
1. Browser unsupported (`isSupported() === false`)
2. Permission denied (`Notification.permission === "denied"`)
3. Service worker registration failed (check console for errors)
4. VAPID key fetch failed (backend down or CORS issue)

**Fix:** Check browser console for `[PushNotification]` logs.

---

**Issue:** Subscription succeeds but no notifications appear

**Causes:**
1. Service worker not handling `push` event (check `service-worker.js`)
2. Backend not sending push (check backend logs)
3. Subscription expired (410/404 error on backend, auto-removed)

**Fix:**
- DevTools → Application → Service Workers → simulate push event
- Check backend: `SELECT * FROM user_push_subscriptions WHERE user_id = ?`

---

**Issue:** Permission prompt doesn't appear

**Cause:** Permission already denied (browser blocked future prompts).

**Fix:**
- Check `Notification.permission` (if `"denied"`, cannot re-prompt)
- User must re-enable in browser settings → Site Settings → Notifications

---

## Future Enhancements

1. **Auto-resubscribe on expiration:** Handle `pushsubscriptionchange` event (re-subscribe when browser rotates endpoint).
2. **Notification preferences:** Let user opt-in/out per event type (e.g., disable game invites, keep new messages).
3. **Quiet hours:** Don't send push during user-specified hours (e.g., 23:00-07:00).
4. **Subscription analytics:** Track permission grant rate, denial rate, active subscription rate.
5. **Inline replies:** Use notification action buttons to reply without opening app (requires background sync).

---

## Related Documentation

- [README.md](./README.md) — Notification system overview
- [web-push.md](./web-push.md) — Web Push API, VAPID, encryption
- [service-worker.md](./service-worker.md) — Service worker implementation
- [backend.md](./backend.md) — Backend services and endpoints
- [triggers.md](./triggers.md) — Notification event types

---

## References

- [Push API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
- [Notification API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Notification)
- [Service Worker API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API)
- [VAPID (RFC 8292)](https://datatracker.ietf.org/doc/html/rfc8292)
