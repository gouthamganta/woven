# Notifications System

**Last Updated:** 2026-08-17  
**Status:** Production-ready  
**Owner:** Backend Team

---

## Overview

Woven's notification system delivers real-time updates to users through two parallel channels:

1. **SignalR (WebSocket)** — Real-time in-app notifications for active users
2. **Web Push API** — Background push notifications for inactive users (browser-native, works when app is closed)

All notifications are triggered by the same events and routed through `NotificationService`, which handles both channels simultaneously.

---

## Architecture

```
Event Trigger (e.g., new chat message)
        ↓
  NotificationService
        ↓
   ┌────────────────┐
   │                │
SignalR            Web Push
(in-app)          (background)
   │                │
   └────────────────┘
        ↓
    User Device
```

### Components

| Component | Purpose | Location |
|---|---|---|
| `NotificationService` | Orchestrates both SignalR + Web Push | `backend/WovenBackend/Services/NotificationService.cs` |
| `WebPushService` | Handles Web Push delivery via VAPID | `backend/WovenBackend/Services/PushNotifications/WebPushService.cs` |
| `PushNotificationEndpoints` | REST API for subscription management | `backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs` |
| `push-notification.service.ts` | Frontend subscription manager | `frontend/woven-frontend/src/app/services/` |
| `service-worker.js` | Handles push events in browser | `frontend/woven-frontend/public/service-worker.js` |
| `UserPushSubscription` | DB entity for push subscriptions | `backend/WovenBackend/data/Entities/` |

---

## Notification Channels

### 1. SignalR (In-App)

- **Transport:** WebSocket (fallback to long polling)
- **Delivery:** Real-time, to connected clients only
- **Security:** HMAC-SHA256 signed payloads (prevents tampering)
- **User grouping:** `user:{userId}` groups via `WovenHub`
- **Persistence:** None (ephemeral, lost if user offline)

**When to use:** User is active in the app, needs instant UI updates (e.g., new message appears in chat thread).

### 2. Web Push (Background)

- **Transport:** HTTP/2 push via browser's push service (FCM for Chrome, APNs for Safari)
- **Delivery:** Background, works when app is closed
- **Security:** VAPID authentication (Voluntary Application Server Identification)
- **User grouping:** Subscription per device (one user can have multiple subscriptions)
- **Persistence:** Queued by browser until delivered (max TTL 4 weeks)

**When to use:** User is offline or app is closed, needs to be notified to return (e.g., new match, new message).

---

## Event → Notification Flow

### Example: New Chat Message

```csharp
// 1. Event occurs (new message sent)
await _notificationService.NewChatMessageAsync(
    recipientUserId: 42,
    threadId: threadId,
    messageId: messageId,
    body: "Hey, how's it going?",
    senderUserId: 7,
    createdAt: DateTimeOffset.UtcNow,
    ct: ct
);

// 2. NotificationService sends to both channels
// SignalR → instant UI update for active users
await _hub.Clients.Group("user:42").SendAsync("NewChatMessage", payload);

// Web Push → background notification for inactive users
await _webPush.SendToUserAsync(
    userId: 42,
    title: "New message",
    body: "Hey, how's it going?",
    url: "/chats/{threadId}"
);
```

**Result:**
- If user 42 is **active** → message appears instantly in chat thread (SignalR)
- If user 42 is **inactive** → browser shows notification (Web Push), clicking opens chat

---

## Supported Notification Types

See [triggers.md](./triggers.md) for full list. Summary:

| Event | In-App (SignalR) | Background (Web Push) |
|---|---|---|
| New match (MomentReceived) | ✅ | ✅ |
| New chat message | ✅ | ✅ |
| General push (SendPush) | ✅ | ✅ |
| Deck ready | ✅ | ❌ |
| Match expired | ✅ | ❌ |
| Game invite/start/complete | ✅ | ❌ |
| Friend bridge events | ✅ | ❌ |
| Season events | ✅ | ❌ |

**Rule:** Only high-priority, user-actionable events trigger Web Push (to avoid notification fatigue).

---

## Security

### SignalR Payloads

All SignalR payloads are HMAC-SHA256 signed:

```json
{
  "payload": { "matchId": "...", "fromUserId": 7 },
  "signature": "a1b2c3d4e5f6..."
}
```

- **Key derivation:** `EncryptionService.DeriveKey("signing-v1")` (HKDF from master secret)
- **Verification:** Frontend must verify signature before trusting payload (prevents malicious WebSocket messages)

### Web Push

- **VAPID keys:** Public/private keypair for authentication
- **Subscription keys:** Each subscription has unique `p256dh` (encryption) + `auth` (authentication) keys
- **Payload encryption:** End-to-end encrypted by Web Push API (backend → browser push service → user device)

---

## Database Schema

### Table: `user_push_subscriptions`

```sql
CREATE TABLE user_push_subscriptions (
    id           UUID PRIMARY KEY,
    user_id      INTEGER NOT NULL REFERENCES users(id),
    endpoint     VARCHAR(2048) NOT NULL,  -- Browser push service URL
    p256dh       VARCHAR(512) NOT NULL,   -- Public key for encryption
    auth         VARCHAR(256) NOT NULL,   -- Auth secret for decryption
    user_agent   VARCHAR(512),            -- Device info (optional)
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX idx_user_push_subscriptions_user_endpoint 
    ON user_push_subscriptions(user_id, endpoint);
```

**Notes:**
- One user can have multiple subscriptions (desktop + mobile, or multiple browsers)
- `endpoint` is unique per browser/device (provided by browser's push service)
- Expired subscriptions auto-removed on 410 Gone or 404 Not Found response

---

## Configuration

### Backend (`appsettings.json` / Azure Key Vault)

```json
{
  "Vapid": {
    "PublicKey": "BG8x...",  // Public VAPID key (safe to expose)
    "PrivateKey": "...",     // Private VAPID key (SECRET)
    "Subject": "mailto:support@wooven.me"  // Contact email
  }
}
```

**Generation:** Use `WebPush.VapidHelper.GenerateVapidKeys()` (one-time setup).

### Frontend

No configuration needed. Service auto-fetches VAPID public key from `/push-notifications/vapid-public-key`.

---

## Usage Patterns

### Backend: Send Notification

```csharp
// Inject INotificationService
await _notificationService.MomentReceivedAsync(
    recipientUserId: userId,
    matchId: matchId,
    fromUserId: fromUserId,
    ct: ct
);
// Automatically sends to both SignalR + Web Push
```

### Frontend: Subscribe to Push

```typescript
// In app initialization (e.g., after login)
const success = await this.pushNotificationService.initialize();
if (success) {
  console.log('Push notifications enabled');
}
```

### Frontend: Unsubscribe

```typescript
await this.pushNotificationService.unsubscribe();
```

---

## Error Handling

### Backend

- **SignalR failures:** Logged as warnings, never throw (delivery is best-effort)
- **Web Push failures:**
  - `410 Gone` / `404 Not Found` → subscription auto-removed from DB
  - `429 Too Many Requests` → retry with exponential backoff (handled by `WebPush` library)
  - Other errors → logged, delivery skipped for that subscription

### Frontend

- **Service worker registration failure:** Graceful degradation (app works, no push notifications)
- **Permission denied:** User notified, push disabled (can re-enable in settings)
- **Subscription failure:** Logged to console, does not block app usage

---

## Testing

### Local Development

1. **Generate VAPID keys:**
   ```csharp
   var keys = VapidHelper.GenerateVapidKeys();
   Console.WriteLine($"Public: {keys.PublicKey}");
   Console.WriteLine($"Private: {keys.PrivateKey}");
   ```

2. **Add keys to User Secrets:**
   ```bash
   cd backend/WovenBackend
   dotnet user-secrets set "Vapid:PublicKey" "BG8x..."
   dotnet user-secrets set "Vapid:PrivateKey" "..."
   dotnet user-secrets set "Vapid:Subject" "mailto:dev@localhost"
   ```

3. **Test subscription flow:**
   - Open app in browser (must be HTTPS or localhost)
   - Click "Enable Notifications" (or auto-prompt on login)
   - Check browser DevTools → Application → Service Workers (should be registered)
   - Check Application → Push Messaging (should show subscription)

4. **Trigger test notification:**
   ```csharp
   await _notificationService.SendPushAsync(userId, "Test notification!", ct);
   ```

### Production

- **VAPID keys stored in Azure Key Vault** (see `SECRETS_SETUP.md`)
- **Service worker must be served from root path** (`/service-worker.js`)
- **HTTPS required** for Web Push (browsers block push on HTTP)

---

## Monitoring

### Metrics to Track

- **Subscription count per user** (avg should be ~1-2 per active user)
- **Push delivery success rate** (target: >95%)
- **410/404 removal rate** (indicates expired subscriptions, normal for inactive users)
- **Permission grant rate** (% of users who accept notification permission)

### Logs

```csharp
[WebPush] Sent notification | UserId={UserId} Endpoint={Endpoint}
[WebPush] Failed to send | StatusCode={StatusCode} UserId={UserId}
[WebPush] Removed expired subscription | Id={Id} UserId={UserId}
[Notify] DeckReady failed for user {UserId}
```

**Correlation:** All logs include `{CorrelationId}` for request tracing.

---

## Future Enhancements

- **Notification preferences** (let users opt-in/out of specific event types)
- **Quiet hours** (mute notifications during user-specified hours)
- **Notification batching** (combine multiple events into one push, e.g., "3 new messages")
- **Rich notifications** (images, action buttons beyond Open/Dismiss)
- **iOS/Android native push** (FCM/APNs for mobile apps)

---

## Related Documentation

- [web-push.md](./web-push.md) — Web Push API, VAPID, subscription flow
- [triggers.md](./triggers.md) — All notification event types
- [service-worker.md](./service-worker.md) — Service worker implementation
- [frontend.md](./frontend.md) — Frontend push notification service
- [backend.md](./backend.md) — Backend services and endpoints
- [SignalR Hub](./backend.md#signalr-hub) — Real-time communication (WovenHub)
