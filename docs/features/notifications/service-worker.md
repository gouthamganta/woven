# Service Worker Implementation

**Last Updated:** 2026-08-17  
**File:** `frontend/woven-frontend/public/service-worker.js`  
**Scope:** `/` (controls all app routes)

---

## What is a Service Worker?

A Service Worker is a JavaScript file that runs **separately from the main browser thread** in the background. It acts as a proxy between the web app and the network, enabling features like:

- **Push notifications** (even when app is closed)
- **Offline caching** (future: cache API responses for offline mode)
- **Background sync** (future: queue actions when offline, sync when online)

**Key characteristics:**
- Runs in background (not attached to a specific page)
- Cannot access DOM directly
- Event-driven (wakes up on events like `push`, `notificationclick`)
- HTTPS-only (or localhost for dev)

---

## Woven's Service Worker

### File Location

```
frontend/woven-frontend/public/service-worker.js
```

**Must be served from public root** (`/service-worker.js`) so it can control all app routes (scope: `/`).

**Registration:**
```typescript
// Frontend: push-notification.service.ts
const registration = await navigator.serviceWorker.register('/service-worker.js', {
  scope: '/'
});
```

---

## Event Handlers

### 1. `push` Event

**When:** Backend sends Web Push notification → browser wakes up service worker.

**Flow:**
1. Browser's push service receives notification from backend
2. Browser wakes up service worker (even if app is closed)
3. Service worker's `push` event fires
4. Service worker decrypts payload (auto-handled by browser)
5. Service worker calls `showNotification()` to display notification

**Code:**
```javascript
self.addEventListener('push', (event) => {
  if (!event.data) {
    return;  // No payload, nothing to show
  }

  try {
    // Parse JSON payload (auto-decrypted by browser)
    const data = event.data.json();
    const { title, body, icon, url } = data;

    // Notification options
    const options = {
      body: body || '',
      icon: icon || '/assets/icon-192.png',
      badge: '/assets/badge-72.png',  // Small icon in notification tray
      vibrate: [200, 100, 200],  // Vibration pattern (ms)
      data: {
        url: url || '/',  // Deep link to open on click
        timestamp: Date.now()
      },
      actions: [
        { action: 'open', title: 'Open' },
        { action: 'close', title: 'Dismiss' }
      ],
      requireInteraction: false,  // Auto-dismiss after timeout
      silent: false  // Play notification sound
    };

    // Display notification (required — all pushes must show notification)
    event.waitUntil(
      self.registration.showNotification(title, options)
    );
  } catch (err) {
    console.error('[ServiceWorker] Push event error:', err);
  }
});
```

**Payload structure:**
```json
{
  "title": "New message",
  "body": "Hey, how's it going?",
  "icon": "/assets/icon-192.png",
  "url": "/chats/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "data": null
}
```

**Sent by:** `WebPushService.SendToUserAsync()` (backend).

---

### 2. `notificationclick` Event

**When:** User clicks notification (or action button).

**Flow:**
1. User clicks notification in OS notification center
2. Service worker's `notificationclick` event fires
3. Service worker closes notification
4. Service worker opens app to specified URL (or focuses existing window)

**Code:**
```javascript
self.addEventListener('notificationclick', (event) => {
  // Close notification
  event.notification.close();

  // If user clicked "Dismiss" button, do nothing
  if (event.action === 'close') {
    return;
  }

  // Get URL to open (from notification data)
  const urlToOpen = event.notification.data?.url || '/';

  event.waitUntil(
    // Find existing app windows
    clients.matchAll({ type: 'window', includeUncontrolled: true })
      .then((clientList) => {
        // If app already open, focus it
        for (const client of clientList) {
          if (client.url.includes(urlToOpen) && 'focus' in client) {
            return client.focus();
          }
        }

        // Otherwise, open new window
        if (clients.openWindow) {
          return clients.openWindow(urlToOpen);
        }
      })
  );
});
```

**Example URL routing:**
- Notification URL: `/chats/abc123`
- User clicks → service worker opens app to that chat thread
- If app already open to that thread → just focuses window

---

### 3. `notificationclose` Event

**When:** User dismisses notification without clicking (swipes away, clicks X).

**Code:**
```javascript
self.addEventListener('notificationclose', (event) => {
  console.log('[ServiceWorker] Notification closed:', event.notification.data);
  // No action needed — just logging for analytics
});
```

**Use cases:**
- Track notification dismissal rate (future: send analytics event)
- Clean up notification-specific state (none currently)

---

### 4. `install` Event

**When:** Service worker is installed for the first time (or updated version deployed).

**Code:**
```javascript
self.addEventListener('install', (event) => {
  console.log('[ServiceWorker] Installing...');
  self.skipWaiting();  // Activate immediately (don't wait for old SW to release)
});
```

**`skipWaiting()` behavior:**
- **Without:** New service worker waits until all app tabs are closed before activating
- **With:** New service worker activates immediately, takes over from old version

**Why immediate activation?** Push notification fixes need to deploy instantly (can't wait for users to close all tabs).

---

### 5. `activate` Event

**When:** Service worker transitions from "waiting" to "active" state.

**Code:**
```javascript
self.addEventListener('activate', (event) => {
  console.log('[ServiceWorker] Activating...');
  event.waitUntil(self.clients.claim());  // Take control of all pages immediately
});
```

**`clients.claim()` behavior:**
- **Without:** Service worker only controls pages loaded AFTER activation
- **With:** Service worker controls all existing pages immediately

**Why immediate claim?** Ensures push notifications work on first page load (no refresh needed).

---

## Notification Options

### Required Fields

```javascript
{
  body: "Message text",  // Main notification content
}
```

### Optional Fields

```javascript
{
  icon: "/assets/icon-192.png",  // Large icon (192x192 recommended)
  badge: "/assets/badge-72.png",  // Small icon for notification tray (72x72)
  vibrate: [200, 100, 200],  // Vibration pattern: [on, off, on] in ms
  timestamp: Date.now(),  // When event occurred (default: now)
  requireInteraction: false,  // true = notification stays until user dismisses
  silent: false,  // true = no sound/vibration
  renotify: false,  // true = re-notify on tag collision (see below)
  tag: "chat-thread-123",  // Notification group (replaces older notification with same tag)
  data: { url: "/chats/123" },  // Custom data (accessible in click handler)
  actions: [
    { action: 'open', title: 'Open', icon: '/icons/open.png' },
    { action: 'close', title: 'Dismiss' }
  ]
}
```

### Actions (Buttons)

Notifications can have up to **2 action buttons** (Android) or **unlimited** (desktop, but 2 recommended for UX).

**Woven's actions:**
- `open` — Opens app to specified URL (default if notification clicked without action)
- `close` — Dismisses notification (no action)

**Future actions:**
- `reply` — Inline reply input (requires background sync)
- `mark-read` — Mark message as read without opening

---

## Notification Tagging

**Problem:** Sending 3 separate notifications from same chat thread = 3 notifications in tray (cluttered).

**Solution:** Use `tag` field to group/replace notifications.

```javascript
const options = {
  body: "New message",
  tag: `chat-thread-${threadId}`,  // Replace previous notification with same tag
  renotify: true  // Re-vibrate/sound even if replacing old notification
};
```

**Example:**
1. User receives message 1 → notification shown (tag: `chat-thread-abc`)
2. User receives message 2 → replaces notification 1 (same tag)
3. User sees only 1 notification: "2 new messages"

**Current implementation:** Does NOT use tags (each notification separate). **TODO:** Add batching + tagging.

---

## Deep Linking

**Goal:** Clicking notification opens app to the relevant screen (not just home screen).

**Implementation:**
```javascript
// Backend sets URL in payload
await _webPush.SendToUserAsync(
    userId: 42,
    title: "New message",
    body: "Hey!",
    url: "/chats/abc123",  // ← Deep link
    ct: ct
);

// Service worker opens to that URL
const urlToOpen = event.notification.data?.url || '/';
clients.openWindow(urlToOpen);
```

**URL patterns:**
- New match: `/moments`
- New message: `/chats/{threadId}`
- Game invite: `/chats/{threadId}` (game UI in chat)
- General push: `/` (home screen)

**Angular routing:** App's router matches URL → navigates to correct component.

---

## Icon Assets

### `icon` (Large Icon)

- **Size:** 192x192px (recommended), up to 512x512px
- **Format:** PNG with transparency
- **Location:** `/assets/icon-192.png`
- **Displays:** In notification card (left side, large)

### `badge` (Small Icon)

- **Size:** 72x72px (Android), 96x96px (Chrome)
- **Format:** PNG, monochrome (white icon on transparent)
- **Location:** `/assets/badge-72.png`
- **Displays:** In notification tray (when collapsed), status bar

**Woven's assets:**
```
public/
  assets/
    icon-192.png   ← Woven logo, 192x192, full color
    badge-72.png   ← Woven logo, 72x72, monochrome (white on transparent)
```

**Future:** Use dynamic icons (e.g., sender's profile photo as `icon`).

---

## Lifecycle

```
1. navigator.serviceWorker.register('/service-worker.js')
   ↓
2. 'install' event fires
   ↓
3. self.skipWaiting() → activate immediately
   ↓
4. 'activate' event fires
   ↓
5. self.clients.claim() → control all pages
   ↓
   [Service worker active]
   ↓
6. Backend sends push → browser wakes up service worker
   ↓
7. 'push' event fires
   ↓
8. self.registration.showNotification() → notification appears
   ↓
9. User clicks notification
   ↓
10. 'notificationclick' event fires
   ↓
11. clients.openWindow(url) → app opens
```

---

## Debugging

### Chrome DevTools

1. **Check registration:**
   - DevTools → Application → Service Workers
   - Should show `service-worker.js` (Status: activated and is running)

2. **Check push subscription:**
   - Application → Push Messaging
   - Should show endpoint + keys

3. **Simulate push event:**
   - Application → Service Workers → "Push" button
   - Enter JSON payload:
     ```json
     {"title": "Test", "body": "This is a test", "url": "/"}
     ```
   - Click "Push" → notification should appear

4. **Check notification history:**
   - Application → Notifications
   - Shows all notifications (active + dismissed)

5. **Check console logs:**
   - Service worker has separate console (not same as main app)
   - Filter: `[ServiceWorker]`

### Common Issues

**Issue:** Service worker not registering

**Cause:** Path mismatch or HTTPS requirement.

**Fix:**
- Service worker must be at `/service-worker.js` (public root)
- HTTPS required (or localhost)
- Check browser console for registration error

---

**Issue:** Push event not firing

**Cause:** Subscription invalid/expired, or backend not sending.

**Fix:**
1. Check subscription exists: `registration.pushManager.getSubscription()`
2. Check backend logs: `[WebPush] Sent notification | UserId=...`
3. Check browser console: `[ServiceWorker] Push event error: ...`

---

**Issue:** Notification not showing

**Cause:** `showNotification()` not called, or payload invalid.

**Fix:**
- Service worker MUST call `showNotification()` on every push event (browser requirement)
- Check payload structure (must have `title` field)
- Check icon paths (404 = broken image in notification)

---

**Issue:** Clicking notification does nothing

**Cause:** `notificationclick` handler missing or URL invalid.

**Fix:**
- Check handler is registered: `self.addEventListener('notificationclick', ...)`
- Check URL is valid (must be same-origin or absolute URL)
- Check browser console for `clients.openWindow()` error

---

## Security

### Service Worker Scope

**Scope:** `/` (controls all app routes)

**Why full scope?** Notification deep links can navigate to any app route (`/chats`, `/moments`, etc.).

**Risk:** Malicious service worker could intercept all network requests.

**Mitigation:**
- Service worker served from same origin (cannot be cross-origin)
- HTTPS-only (prevents MITM attacks)
- Reviewed code (no dynamic `importScripts()` or `eval()`)

### Notification Payload

**Encryption:** End-to-end encrypted (backend → browser push service → service worker).

**Trust:** Service worker receives decrypted payload from browser (trusted channel).

**Validation:** None currently (payload structure trusted). **Future:** Validate payload schema before displaying.

---

## Performance

### Service Worker Wake-Up

**Latency:**
- Service worker already running: <10ms
- Service worker idle (needs wake-up): 50-200ms
- Service worker terminated (needs restart): 100-500ms

**Battery impact:** Minimal (service worker only runs during push event, then terminates).

### Notification Display

**Latency:**
- `showNotification()` call → OS notification: 10-50ms

**Resource usage:** Minimal (browser handles rendering).

---

## Offline Behavior

### Current Implementation

Service worker does NOT cache any assets (no offline mode).

**When offline:**
- App fails to load (white screen)
- Push notifications still work (browser queues until online)

### Future: Offline Caching

```javascript
// Cache API responses for offline mode
self.addEventListener('fetch', (event) => {
  event.respondWith(
    caches.match(event.request).then((response) => {
      return response || fetch(event.request);
    })
  );
});
```

**Benefits:**
- App shell loads instantly (cached HTML/CSS/JS)
- API responses cached for offline viewing (e.g., read old messages)

**Tradeoffs:**
- Increased complexity (cache invalidation, versioning)
- Storage quota limits (50MB-250GB depending on browser)

---

## Testing

### Unit Tests (Future)

Service workers are hard to unit test (no DOM, no global `window`). Options:
- **Workbox testing library** (Google's service worker framework)
- **Manual testing** in browser DevTools (current approach)

### Integration Tests

1. **Test push event:**
   ```javascript
   // Simulate push in DevTools
   self.dispatchEvent(new PushEvent('push', {
     data: new PushMessageData('{"title":"Test","body":"Body","url":"/"}')
   }));
   ```

2. **Test notification click:**
   ```javascript
   // Simulate click in DevTools
   self.dispatchEvent(new NotificationEvent('notificationclick', {
     notification: new Notification('Test', { data: { url: '/test' } })
   }));
   ```

### Production Monitoring

**Metrics to track:**
- Service worker registration success rate (target: >99%)
- Push event firing rate (should match backend send rate)
- Notification click-through rate (target: >10%)
- Notification dismissal rate (target: <50%)

**Logs:**
```javascript
// Send to analytics service (future)
self.addEventListener('push', (event) => {
  logEvent('push_received', { timestamp: Date.now() });
});

self.addEventListener('notificationclick', (event) => {
  logEvent('notification_clicked', { url: event.notification.data.url });
});
```

---

## Future Enhancements

1. **Notification batching:** Combine multiple messages ("3 new messages from Alice").
2. **Inline reply:** `actions: [{ action: 'reply', type: 'text' }]` for quick replies.
3. **Rich media:** Image attachments in notifications (`image` field).
4. **Notification grouping:** Use `tag` + `renotify` to group by thread.
5. **Background sync:** Queue actions when offline, sync when online.
6. **Offline caching:** Cache API responses for offline viewing.
7. **Push subscription refresh:** Auto-resubscribe on `pushsubscriptionchange` event.

---

## Related Documentation

- [README.md](./README.md) — Notification system overview
- [web-push.md](./web-push.md) — Web Push API, VAPID
- [frontend.md](./frontend.md) — Frontend push notification service
- [triggers.md](./triggers.md) — Notification event types

---

## References

- [Service Worker API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API)
- [Notification API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Notification)
- [Push API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
- [Workbox (Google)](https://developer.chrome.com/docs/workbox)
