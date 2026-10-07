# Backend Notification Services

**Last Updated:** 2026-08-17  
**Services:** `NotificationService`, `WebPushService`  
**Endpoints:** `PushNotificationEndpoints`  
**Entity:** `UserPushSubscription`

---

## Overview

The backend notification system consists of three main components:

1. **NotificationService** — Orchestrates both SignalR (in-app) and Web Push (background) notifications
2. **WebPushService** — Handles Web Push delivery via VAPID protocol
3. **PushNotificationEndpoints** — REST API for subscription management

**Design principle:** Dual-channel delivery. Every high-priority event triggers both SignalR (instant, for active users) and Web Push (background, for inactive users).

---

## Architecture

```
Event occurs (e.g., new chat message)
        ↓
  NotificationService
        ↓
   ┌────────────────┐
   │                │
SignalR         WebPushService
(in-app)        (background)
   │                │
   └────────────────┘
        ↓
   User Device
```

**Components:**
- `NotificationService` — Facade, called by endpoints/workers
- `IHubContext<WovenHub>` — SignalR hub for real-time delivery
- `WebPushService` — Web Push client (VAPID + encryption)
- `UserPushSubscription` — DB entity storing user subscriptions
- `PushNotificationEndpoints` — HTTP API for subscription CRUD

---

## NotificationService

**Location:** `backend/WovenBackend/Services/NotificationService.cs`

**Interface:** `INotificationService`

**Dependencies:**
- `IHubContext<WovenHub>` — SignalR hub
- `IWebPushService` — Web Push delivery
- `IEncryptionService` — HMAC signing key derivation
- `ISecurityAuditService` — Security event logging
- `ILogger<NotificationService>`

---

### Signature System

All SignalR payloads are **HMAC-SHA256 signed** to prevent tampering.

**Why?** WebSocket messages can be spoofed by malicious browser extensions or MITM attacks. Signature proves payload authenticity.

**Signing flow:**
```csharp
private object Sign(object payload)
{
    var json = JsonSerializer.Serialize(payload);
    var sig = ComputeHmac(json, _signingKey);
    return new { payload, signature = sig };
}

internal static string ComputeHmac(string data, byte[] key)
{
    var bytes = Encoding.UTF8.GetBytes(data);
    var hash = HMACSHA256.HashData(key, bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
}
```

**Signed envelope:**
```json
{
  "payload": { "matchId": "...", "fromUserId": 42 },
  "signature": "a1b2c3d4e5f6..."
}
```

**Frontend verification:** Frontend must verify signature before trusting payload (see `woven.service.ts`).

---

### Key Derivation

Signing key derived from master encryption key:

```csharp
_signingKey = Convert.FromBase64String(enc.DeriveKey("signing-v1"));
```

**Method:** HKDF (HMAC-based Key Derivation Function) with context `"signing-v1"`.

**Benefits:**
- One master secret → multiple derived keys (signing, encryption, etc.)
- Key rotation without breaking other systems (bump version: `"signing-v2"`)

---

### Notification Methods

All methods follow the same pattern:

1. **Sign payload** (for SignalR)
2. **Send via SignalR** to user group (`user:{userId}`)
3. **Send via Web Push** (optional, only high-priority events)
4. **Catch and log errors** (delivery is best-effort, never throw)

---

#### `DeckReadyAsync`

**When:** User's daily deck generated (nightly at 06:00).

**SignalR:** ✅ | **Web Push:** ❌

```csharp
public async Task DeckReadyAsync(int userId, DateOnly date, CancellationToken ct = default)
{
    try
    {
        await Send(_hub.Clients.Group(WovenHub.UserGroup(userId)), "DeckReady",
            new { date = date.ToString("yyyy-MM-dd") }, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Notify] DeckReady failed for user {UserId}", userId);
    }
}
```

**Payload:**
```json
{
  "payload": { "date": "2026-08-17" },
  "signature": "..."
}
```

---

#### `MomentReceivedAsync`

**When:** User receives a new match.

**SignalR:** ✅ | **Web Push:** ✅

```csharp
public async Task MomentReceivedAsync(int recipientUserId, Guid matchId, int fromUserId, CancellationToken ct = default)
{
    try
    {
        await Send(_hub.Clients.Group(WovenHub.UserGroup(recipientUserId)), "MomentReceived",
            new { matchId, fromUserId }, ct);

        _ = _webPush.SendToUserAsync(recipientUserId,
            title: "You have a new match! 🎉",
            body: "Someone chose you — open Woven to see who.",
            url: "/moments",
            ct: ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Notify] MomentReceived failed for user {UserId}", recipientUserId);
    }
}
```

**SignalR payload:**
```json
{
  "payload": { "matchId": "3fa85f64-...", "fromUserId": 42 },
  "signature": "..."
}
```

**Web Push:** Background notification (works when app closed).

**Fire-and-forget:** `_ =` discard task (don't await, non-blocking).

---

#### `NewChatMessageAsync`

**When:** User receives a chat message.

**SignalR:** ✅ | **Web Push:** ✅

```csharp
public async Task NewChatMessageAsync(int recipientUserId, Guid threadId, Guid messageId,
    string body, int senderUserId, DateTimeOffset createdAt, CancellationToken ct = default)
{
    try
    {
        await Send(_hub.Clients.Group(WovenHub.UserGroup(recipientUserId)), "NewChatMessage",
            new { threadId, messageId, body, senderUserId, createdAt }, ct);

        _ = _webPush.SendToUserAsync(recipientUserId,
            title: "New message",
            body: body.Length > 80 ? body[..80] + "…" : body,
            url: $"/chats/{threadId}",
            ct: ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Notify] NewChatMessage failed for user {UserId}", recipientUserId);
    }
}
```

**Body truncation:** Push notification body limited to 80 chars (UX best practice).

**Deep link:** URL `/chats/{threadId}` opens app to that specific chat.

---

#### `SendPushAsync`

**When:** General-purpose push (admin trigger, ghost refunds, coaching nudges).

**SignalR:** ✅ | **Web Push:** ✅

```csharp
public async Task SendPushAsync(int userId, string message, CancellationToken ct = default)
{
    try
    {
        await Send(_hub.Clients.Group(WovenHub.UserGroup(userId)), "Push",
            new { message }, ct);

        _ = _webPush.SendToUserAsync(userId,
            title: "Woven",
            body: message,
            url: "/",
            ct: ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Notify] SendPush failed for user {UserId}", userId);
    }
}
```

**Use cases:**
- Ghost refund: "You got 0.5 sparks back"
- Coaching nudge: "Your match is waiting — say hi!"
- Admin announcement: "New feature: voice notes!"

---

#### Other Methods

See [triggers.md](./triggers.md) for full list:
- `MomentExpiredAsync` (SignalR only)
- `GameInviteReceivedAsync` (SignalR only)
- `GameStartedAsync` (SignalR only)
- `GameCompletedAsync` (SignalR only)
- `SendFriendBridgeProposalAsync` (SignalR only)
- `SendFriendBridgeActivatedAsync` (SignalR only)
- `SeasonResponseSubmittedAsync` (SignalR only)
- `NewSeasonStartedAsync` (SignalR only)

**Pattern:** Game/season events are low-priority (both users already in-app), so no Web Push.

---

### Error Handling

**Strategy:** Log and swallow. Never throw exceptions (delivery failures should not crash request).

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "[Notify] {Method} failed for user {UserId}", methodName, userId);
}
```

**Why swallow?** Notifications are non-critical. If SignalR fails (user offline), Web Push still works. If both fail, user sees update on next app open.

**Monitoring:** All failures logged with `{CorrelationId}` for tracing.

---

## WebPushService

**Location:** `backend/WovenBackend/Services/PushNotifications/WebPushService.cs`

**Interface:** `IWebPushService`

**Dependencies:**
- `WovenDbContext` — Database access
- `IConfiguration` — VAPID keys from config/Key Vault
- `ILogger<WebPushService>`
- `WebPushClient` — NuGet package `WebPush 1.0.13`

---

### Configuration

**VAPID keys loaded from config:**

```csharp
_publicKey = _config["Vapid:PublicKey"]
    ?? throw new InvalidOperationException("Vapid:PublicKey not configured");
_privateKey = _config["Vapid:PrivateKey"]
    ?? throw new InvalidOperationException("Vapid:PrivateKey not configured");
_subject = _config["Vapid:Subject"] ?? "mailto:support@wooven.me";
```

**Storage:**
- **Production:** Azure Key Vault (`Vapid:PublicKey`, `Vapid:PrivateKey`, `Vapid:Subject`)
- **Local dev:** User Secrets (never commit to git)

**VAPID subject:** Contact email for push service (RFC 8292 requirement).

---

### `GetPublicVapidKey()`

**Purpose:** Return VAPID public key for frontend subscription.

**Returns:** Base64url-encoded public key (65 bytes uncompressed ECDH P-256).

**Usage:**
```csharp
// In PushNotificationEndpoints
var publicKey = webPush.GetPublicVapidKey();
return Results.Ok(new { publicKey });
```

**Public key is safe to expose** (like a domain name, not sensitive).

---

### `SendToUserAsync()`

**Purpose:** Send push notification to all user's subscriptions.

**Signature:**
```csharp
public async Task SendToUserAsync(
    int userId,
    string title,
    string body,
    string? icon = null,
    string? url = null,
    string? data = null,
    CancellationToken ct = default)
```

**Parameters:**
- `userId` — Recipient user ID
- `title` — Notification title (required)
- `body` — Notification body (required)
- `icon` — Icon URL (default: `/assets/icon-192.png`)
- `url` — Deep link URL (default: `/`)
- `data` — Custom JSON data (optional)

---

### Flow

1. **Fetch subscriptions** from DB (`user_push_subscriptions` WHERE `user_id = ?`)
2. **Build JSON payload:**
   ```json
   {
     "title": "New message",
     "body": "Hey, how's it going?",
     "icon": "/assets/icon-192.png",
     "url": "/chats/abc123",
     "data": null
   }
   ```
3. **Send to all subscriptions in parallel:**
   ```csharp
   var tasks = subscriptions.Select(async sub =>
   {
       var subscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
       var vapidDetails = new VapidDetails(_subject, _publicKey, _privateKey);
       await _client.SendNotificationAsync(subscription, payload, vapidDetails, ct);
   });
   await Task.WhenAll(tasks);
   ```
4. **Handle errors** (410/404 = expired subscription → auto-remove from DB)
5. **Log results** (`[WebPush] Sent notification | UserId={UserId}`)

---

### Parallel Sending

**Why parallel?** One user can have multiple subscriptions (desktop + mobile).

**Latency:**
- 1 subscription: 100-200ms
- 3 subscriptions: 150-250ms (parallelized, not 300-600ms sequential)

**Code:**
```csharp
var tasks = subscriptions.Select(async sub => { ... });
await Task.WhenAll(tasks);  // Parallel, not sequential
```

---

### Auto-Cleanup

**Expired subscriptions** (user uninstalled browser, cleared data, or subscription rotated):

```csharp
catch (WebPushException ex)
{
    if (ex.StatusCode == System.Net.HttpStatusCode.Gone ||
        ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        _db.PushSubscriptions.Remove(sub);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[WebPush] Removed expired subscription | Id={Id} UserId={UserId}",
            sub.Id, userId);
    }
}
```

**Why auto-remove?** Subscriptions expire after 30-90 days of inactivity. Keeping expired subscriptions wastes DB space and slows down sending.

**Status codes:**
- `410 Gone` — Subscription explicitly expired
- `404 Not Found` — Subscription endpoint not found (browser uninstalled)
- `429 Too Many Requests` — Rate limit (handled by `WebPush` library with retry)

---

### Encryption

**Payload encryption:** Handled by `WebPush` library (AES-128-GCM, end-to-end encrypted).

**Flow:**
1. Library generates ephemeral ECDH keypair
2. Key agreement with subscription's `p256dh` public key
3. HKDF key derivation using shared secret + `auth`
4. AES-128-GCM encryption of JSON payload
5. HTTP/2 POST to browser's push service (FCM, APNs, etc.)

**Decryption:** Browser's push service passes encrypted payload to service worker, which auto-decrypts.

**Security:** Push service (FCM, APNs) cannot read payload (end-to-end encrypted).

---

### Logging

```csharp
_logger.LogInformation("[WebPush] Sent notification | UserId={UserId} Endpoint={Endpoint}",
    userId, sub.Endpoint);

_logger.LogWarning(ex, "[WebPush] Failed to send | StatusCode={StatusCode} UserId={UserId}",
    ex.StatusCode, userId);

_logger.LogInformation("[WebPush] Removed expired subscription | Id={Id} UserId={UserId}",
    sub.Id, userId);

_logger.LogInformation("[WebPush] Sent notification to {Count} subscriptions | UserId={UserId}",
    subscriptions.Count, userId);
```

**All logs include `{CorrelationId}`** (via `CorrelationIdMiddleware`).

---

## PushNotificationEndpoints

**Location:** `backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs`

**Endpoints:**
1. `GET /push-notifications/vapid-public-key` (unauthenticated)
2. `POST /push-notifications/subscribe` (authenticated)
3. `POST /push-notifications/unsubscribe` (authenticated)

---

### `GET /vapid-public-key`

**Purpose:** Return VAPID public key for frontend subscription.

**Auth:** None (public endpoint).

**Response:**
```json
{
  "publicKey": "BG8x..."
}
```

**Implementation:**
```csharp
private static IResult GetVapidPublicKey(IWebPushService webPush)
{
    var publicKey = webPush.GetPublicVapidKey();
    return Results.Ok(new { publicKey });
}
```

**Why unauthenticated?** Public key is needed before user can subscribe (can't subscribe without key, can't get JWT before subscribing).

---

### `POST /subscribe`

**Purpose:** Store user's push subscription.

**Auth:** Required (JWT Bearer token).

**Request body:**
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/abc123...",
  "p256dh": "BG8x...",  // Base64-encoded public key
  "auth": "..."         // Base64-encoded auth secret
}
```

**Response (success):**
```json
{
  "status": "subscribed",
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response (already exists):**
```json
{
  "status": "already_subscribed",
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

---

#### Implementation

```csharp
private static async Task<IResult> Subscribe(
    HttpContext http,
    WovenDbContext db,
    [FromBody] SubscribeRequest req,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);

    // Validate request
    if (string.IsNullOrWhiteSpace(req.Endpoint) ||
        string.IsNullOrWhiteSpace(req.P256dh) ||
        string.IsNullOrWhiteSpace(req.Auth))
    {
        return Results.BadRequest(new { error = "INVALID_SUBSCRIPTION" });
    }

    // Check if subscription already exists (idempotent)
    var existing = await db.PushSubscriptions
        .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == req.Endpoint, ct);

    if (existing != null)
    {
        return Results.Ok(new { status = "already_subscribed", id = existing.Id });
    }

    // Create new subscription
    var subscription = new UserPushSubscription
    {
        UserId = userId,
        Endpoint = req.Endpoint,
        P256dh = req.P256dh,
        Auth = req.Auth,
        UserAgent = http.Request.Headers.UserAgent.ToString()
    };

    db.PushSubscriptions.Add(subscription);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { status = "subscribed", id = subscription.Id });
}
```

**Idempotency:** Duplicate `(userId, endpoint)` returns existing subscription (safe to retry).

**User-Agent logging:** Optional, helps identify device type (Chrome desktop vs mobile Safari).

---

### `POST /unsubscribe`

**Purpose:** Remove user's push subscription.

**Auth:** Required (JWT Bearer token).

**Request body:**
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/abc123..."
}
```

**Response (success):**
```json
{
  "status": "unsubscribed"
}
```

**Response (not found):**
```json
{
  "status": "not_found"
}
```

---

#### Implementation

```csharp
private static async Task<IResult> Unsubscribe(
    HttpContext http,
    WovenDbContext db,
    [FromBody] UnsubscribeRequest req,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);

    if (string.IsNullOrWhiteSpace(req.Endpoint))
    {
        return Results.BadRequest(new { error = "INVALID_ENDPOINT" });
    }

    var subscription = await db.PushSubscriptions
        .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == req.Endpoint, ct);

    if (subscription == null)
    {
        return Results.Ok(new { status = "not_found" });
    }

    db.PushSubscriptions.Remove(subscription);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { status = "unsubscribed" });
}
```

**Idempotency:** Deleting non-existent subscription returns `"not_found"` (not an error).

---

## UserPushSubscription Entity

**Location:** `backend/WovenBackend/data/Entities/UserPushSubscription.cs`

**Table:** `user_push_subscriptions`

---

### Schema

```csharp
[Table("user_push_subscriptions")]
public class UserPushSubscription
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("endpoint")]
    [MaxLength(2048)]
    public string Endpoint { get; set; } = default!;

    [Column("p256dh")]
    [MaxLength(512)]
    public string P256dh { get; set; } = default!;

    [Column("auth")]
    [MaxLength(256)]
    public string Auth { get; set; } = default!;

    [Column("user_agent")]
    [MaxLength(512)]
    public string? UserAgent { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

---

### Fields

| Field | Type | Purpose |
|---|---|---|
| `Id` | `Guid` | Primary key (auto-generated) |
| `UserId` | `int` | Foreign key to `users` table |
| `Endpoint` | `string` | Browser push service URL (FCM, APNs, etc.) |
| `P256dh` | `string` | Public key for encrypting push payloads (base64) |
| `Auth` | `string` | Auth secret for encryption (base64) |
| `UserAgent` | `string?` | Device info (optional, for analytics) |
| `CreatedAt` | `DateTimeOffset` | Subscription timestamp |

---

### Indexes

**Unique index on `(user_id, endpoint)`:**

```sql
CREATE UNIQUE INDEX idx_user_push_subscriptions_user_endpoint 
    ON user_push_subscriptions(user_id, endpoint);
```

**Why unique?** One user can have multiple subscriptions (desktop + mobile), but same `(user, endpoint)` pair is duplicate.

**Benefits:**
- Prevents duplicate subscriptions (idempotent `POST /subscribe`)
- Fast lookup for unsubscribe (`WHERE user_id = ? AND endpoint = ?`)

---

### Multi-Device Support

**One user, multiple subscriptions:**

```sql
SELECT user_id, COUNT(*) 
FROM user_push_subscriptions 
GROUP BY user_id;

-- Example:
-- user_id | count
-- --------+------
--      42 |     3  (desktop Chrome + mobile Safari + Firefox)
```

**Delivery:** `WebPushService.SendToUserAsync` sends to **all subscriptions in parallel** (user gets notification on all devices).

**Deduplication:** Browser handles deduplication (if user saw notification on desktop, mobile won't show duplicate).

---

## Security

### VAPID Keys

**Private key protection:**
- Never commit to git (use User Secrets / Key Vault)
- Never log or expose in API responses
- Rotate every 1-2 years (requires all users to re-subscribe)

**Key derivation:** VAPID keys are NOT derived from master encryption key (separate keypair).

**Generation (one-time):**
```csharp
var keys = VapidHelper.GenerateVapidKeys();
Console.WriteLine($"Public: {keys.PublicKey}");
Console.WriteLine($"Private: {keys.PrivateKey}");
```

---

### Subscription Data

**Stored in plaintext:**
- `endpoint` — Opaque URL (no PII)
- `p256dh` — Public encryption key (not sensitive)
- `auth` — Shared secret (not PII, required for encryption)

**Why plaintext?** All three fields are required to send push (cannot hash or encrypt them).

**Security:**
- Endpoint is user-specific (one user cannot send to another's endpoint)
- Keys are time-limited (subscriptions expire after 30-90 days)
- No sensitive data in notification payloads (ephemeral content only)

---

### Authorization

**All endpoints require JWT auth** (except `GET /vapid-public-key`).

**User isolation:** `EndpointHelper.GetUserId(http.User)` ensures users can only manage their own subscriptions.

**SQL injection protection:** EF Core parameterizes all queries.

---

## Performance

### Batch Sending

**Parallel dispatch:**
```csharp
var tasks = subscriptions.Select(async sub => { ... });
await Task.WhenAll(tasks);
```

**Latency:**
- 1 subscription: 100-200ms
- 3 subscriptions: 150-250ms (parallelized)
- 10 subscriptions: 200-400ms (parallelized)

**Bottleneck:** Network latency to push services (FCM, APNs).

---

### Rate Limits

**Push service limits:**
- **FCM:** 1 million requests/min (per project)
- **APNs:** No public limit (throttles on abuse)

**Woven's scale:** <1000 active users → no risk of hitting limits.

**Future:** If >10k users, batch notifications (e.g., "3 new messages" instead of 3 separate pushes).

---

### Database Load

**Query pattern:**
```sql
SELECT * FROM user_push_subscriptions WHERE user_id = ?;
```

**Index:** `idx_user_push_subscriptions_user_endpoint` covers `user_id` (fast lookup).

**Typical result:** 1-3 rows per user (low overhead).

**Cleanup:** Expired subscriptions auto-removed on 410/404 (table stays small).

---

## Testing

### Local Development

1. **Generate VAPID keys:**
   ```csharp
   var keys = VapidHelper.GenerateVapidKeys();
   Console.WriteLine($"Public: {keys.PublicKey}");
   Console.WriteLine($"Private: {keys.PrivateKey}");
   ```

2. **Store in User Secrets:**
   ```bash
   cd backend/WovenBackend
   dotnet user-secrets set "Vapid:PublicKey" "BG8x..."
   dotnet user-secrets set "Vapid:PrivateKey" "..."
   dotnet user-secrets set "Vapid:Subject" "mailto:dev@localhost"
   ```

3. **Test subscription flow:**
   - Frontend: `await pushNotificationService.initialize()`
   - Backend logs: `[WebPush] Sent notification | UserId=1`

4. **Trigger test push:**
   ```csharp
   await _notificationService.SendPushAsync(userId, "Test notification!", ct);
   ```

5. **Check DB:**
   ```sql
   SELECT * FROM user_push_subscriptions WHERE user_id = 1;
   ```

---

### Production Testing

1. **Verify VAPID keys loaded:**
   ```csharp
   _logger.LogInformation("[WebPush] Initialized | PublicKey={Key}",
       _publicKey[..20] + "...");
   ```

2. **Monitor logs:**
   ```
   [WebPush] Sent notification | UserId=42 Endpoint=https://fcm.googleapis.com/...
   [WebPush] Removed expired subscription | Id=... UserId=42
   ```

3. **Check subscription count:**
   ```sql
   SELECT COUNT(*) FROM user_push_subscriptions;
   ```

4. **Test end-to-end:**
   - User A subscribes on device 1
   - User B sends message to User A
   - User A receives notification (both in-app SignalR + background Web Push)

---

## Monitoring

### Metrics to Track

- **Subscription count per user** (avg should be ~1-2)
- **Push delivery success rate** (target: >95%)
- **410/404 removal rate** (indicates expired subscriptions, normal for inactive users)
- **Notification latency** (target: <500ms from event to push service)

### Logs

```csharp
[Notify] MomentReceived failed for user {UserId}
[WebPush] Sent notification | UserId={UserId} Endpoint={Endpoint}
[WebPush] Failed to send | StatusCode={StatusCode} UserId={UserId}
[WebPush] Removed expired subscription | Id={Id} UserId={UserId}
```

**Correlation:** All logs include `{CorrelationId}` for request tracing.

---

## Troubleshooting

### Issue: No subscriptions for user

**Symptom:** `[WebPush] No subscriptions for user {UserId}`

**Cause:** User never subscribed (permission denied or not prompted).

**Fix:**
1. Frontend: Check `pushNotificationService.isSubscribed()` → false
2. Frontend: Call `pushNotificationService.initialize()` to prompt
3. Backend: Verify `SELECT * FROM user_push_subscriptions WHERE user_id = ?` → row exists

---

### Issue: 401 Unauthorized on push send

**Symptom:** `[WebPush] Failed to send | StatusCode=401`

**Cause:** VAPID authentication failure.

**Fix:**
1. Check VAPID keys match (public key in config === frontend fetched key)
2. Check `Vapid:Subject` is valid (`mailto:` or `https:`)
3. Regenerate VAPID keys if corrupted (requires all users to re-subscribe)

---

### Issue: 410 Gone / 404 Not Found

**Symptom:** `[WebPush] Removed expired subscription | Id={Id}`

**Cause:** Subscription expired (user uninstalled browser, cleared data, or subscription rotated).

**Fix:** Normal behavior. User needs to re-subscribe (happens automatically on next login if permission still granted).

---

### Issue: Notifications not received

**Symptom:** Backend logs show success, but user doesn't see notification.

**Causes:**
1. Service worker not registered (check `navigator.serviceWorker.ready`)
2. Service worker not handling `push` event (check `service-worker.js`)
3. Notification permission denied (check `Notification.permission`)

**Fix:**
- DevTools → Application → Service Workers → simulate push event
- DevTools → Application → Push Messaging → verify subscription exists

---

## Future Enhancements

1. **Notification preferences:** Let users opt-in/out per event type (e.g., disable game invites, keep new messages).
2. **Quiet hours:** Don't send push during user-specified hours (e.g., 23:00-07:00).
3. **Notification batching:** Combine multiple events ("3 new messages from Alice" instead of 3 separate pushes).
4. **Rich notifications:** Add images, action buttons beyond Open/Dismiss.
5. **Subscription analytics:** Track permission grant rate, denial rate, active subscription rate.
6. **Auto-resubscribe:** Handle `pushsubscriptionchange` event (re-subscribe when browser rotates endpoint).
7. **iOS/Android native push:** FCM/APNs for mobile apps (requires native wrappers).

---

## Related Documentation

- [README.md](./README.md) — Notification system overview
- [web-push.md](./web-push.md) — Web Push API, VAPID, encryption
- [triggers.md](./triggers.md) — Notification event types
- [service-worker.md](./service-worker.md) — Service worker implementation
- [frontend.md](./frontend.md) — Frontend push notification service

---

## References

- [Web Push Protocol (RFC 8030)](https://datatracker.ietf.org/doc/html/rfc8030)
- [VAPID (RFC 8292)](https://datatracker.ietf.org/doc/html/rfc8292)
- [WebPush C# Library](https://github.com/web-push-libs/web-push-csharp)
- [Push API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
