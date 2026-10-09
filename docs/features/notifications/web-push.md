# Web Push API

**Last Updated:** 2026-08-17  
**Component:** WebPushService  
**Dependencies:** `WebPush 1.0.13` NuGet package

---

## What is Web Push?

Web Push is a browser-native API that allows web apps to send notifications to users even when the app is closed. It works across all major browsers (Chrome, Firefox, Safari, Edge) and is built on open web standards.

**Key benefits:**
- Works when browser/app is closed
- No app installation required (just permission grant)
- Cross-platform (desktop + mobile web)
- End-to-end encrypted by design

---

## VAPID (Voluntary Application Server Identification)

VAPID is an authentication protocol for Web Push that proves the server sending notifications is the same server the user subscribed to.

### Why VAPID?

Before VAPID, push services (FCM, APNs, etc.) couldn't verify who was sending notifications. VAPID adds:
- **Server identity verification** (prevents push spam from unauthorized servers)
- **Contact information** (mailto: or https: for push service to contact server owner)
- **Cryptographic proof** (JWT signed with server's private key)

### VAPID Keys

Woven uses a **single VAPID keypair** for all users:

```csharp
// One-time generation (already done in production)
var keys = VapidHelper.GenerateVapidKeys();
// PublicKey: "BG8x..." (base64url-encoded, 65 bytes uncompressed)
// PrivateKey: "..." (base64url-encoded, 32 bytes)
```

**Storage:**
- **Production:** Azure Key Vault (`Vapid:PublicKey`, `Vapid:PrivateKey`, `Vapid:Subject`)
- **Local dev:** User Secrets (never commit to git)

**Public key distribution:** Frontend fetches via `GET /push-notifications/vapid-public-key` (no auth required).

---

## Subscription Flow

### 1. User Grants Permission

```typescript
// Frontend: Request notification permission
const permission = await Notification.requestPermission();
// Returns: "granted" | "denied" | "default"
```

**Triggers:**
- On first app load after login
- When user clicks "Enable Notifications" in settings

**UX consideration:** Only ask once. If denied, provide UI to re-enable in settings (browser controls permission after first denial).

### 2. Service Worker Registration

```typescript
// Frontend: Register service worker
const registration = await navigator.serviceWorker.register('/service-worker.js', {
  scope: '/'
});
await navigator.serviceWorker.ready;
```

**Requirements:**
- Service worker must be served from **root path** (`/service-worker.js`)
- HTTPS required (or localhost for dev)
- Scope `/` means service worker controls all app routes

### 3. Create Push Subscription

```typescript
// Frontend: Subscribe to push
const subscription = await registration.pushManager.subscribe({
  userVisibleOnly: true,  // Required: all pushes must show notification
  applicationServerKey: vapidPublicKey  // Base64url → Uint8Array
});
```

**Browser generates:**
- `endpoint` — URL to browser's push service (e.g., `https://fcm.googleapis.com/fcm/send/...`)
- `p256dh` — Public key for encrypting push payloads (ECDH P-256 curve)
- `auth` — Secret for authenticating encrypted payloads

### 4. Send Subscription to Backend

```typescript
// Frontend: POST /push-notifications/subscribe
await this.http.post(`${apiUrl}/push-notifications/subscribe`, {
  endpoint: subscription.endpoint,
  p256dh: base64(subscription.getKey('p256dh')),
  auth: base64(subscription.getKey('auth'))
});
```

**Backend stores:**

```csharp
var sub = new UserPushSubscription
{
    UserId = userId,
    Endpoint = req.Endpoint,
    P256dh = req.P256dh,
    Auth = req.Auth,
    UserAgent = http.Request.Headers.UserAgent.ToString()
};
db.PushSubscriptions.Add(sub);
await db.SaveChangesAsync();
```

**Deduplication:** If `(userId, endpoint)` already exists, return existing subscription (idempotent).

---

## Sending Notifications

### Backend Code

```csharp
// WebPushService.SendToUserAsync
public async Task SendToUserAsync(
    int userId,
    string title,
    string body,
    string? icon = null,
    string? url = null,
    string? data = null,
    CancellationToken ct = default)
{
    // 1. Fetch all subscriptions for user
    var subscriptions = await _db.PushSubscriptions
        .Where(s => s.UserId == userId)
        .ToListAsync(ct);

    // 2. Build JSON payload
    var payload = JsonSerializer.Serialize(new {
        title,
        body,
        icon = icon ?? "/assets/icon-192.png",
        url = url ?? "/",
        data
    });

    // 3. Send to all subscriptions in parallel
    var tasks = subscriptions.Select(async sub =>
    {
        var subscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var vapidDetails = new VapidDetails(_subject, _publicKey, _privateKey);

        await _client.SendNotificationAsync(subscription, payload, vapidDetails, ct);
    });

    await Task.WhenAll(tasks);
}
```

### What Happens Next

1. **WebPush library** encrypts payload with `p256dh` + `auth` (AES-128-GCM)
2. **HTTP/2 POST** to browser's push service endpoint (e.g., FCM, APNs)
3. **Push service** queues notification and delivers to device
4. **Service worker** wakes up, receives `push` event, displays notification

---

## Payload Encryption

Web Push uses **end-to-end encryption** (backend → browser). The push service (FCM, APNs) cannot read the payload.

### Encryption Flow

1. **Backend** generates random salt (16 bytes)
2. **ECDH key agreement** between server ephemeral key + subscription `p256dh`
3. **HKDF key derivation** using shared secret + salt + `auth`
4. **AES-128-GCM encryption** of JSON payload
5. **HTTP headers** include `Encryption`, `Crypto-Key`, `Content-Encoding: aes128gcm`

**Handled by:** `WebPush` library (you don't write this manually).

**Decryption:** Browser's push service passes encrypted payload to service worker, which auto-decrypts using subscription's private key.

---

## Error Handling

### HTTP Status Codes

| Code | Meaning | Action |
|---|---|---|
| 201 Created | Success | None |
| 410 Gone | Subscription expired | Remove from DB |
| 404 Not Found | Subscription not found | Remove from DB |
| 429 Too Many Requests | Rate limit hit | Retry with backoff |
| 401 Unauthorized | VAPID authentication failed | Check keys |
| 400 Bad Request | Invalid payload/subscription | Log error |

### Auto-Cleanup

```csharp
catch (WebPushException ex)
{
    if (ex.StatusCode == HttpStatusCode.Gone ||
        ex.StatusCode == HttpStatusCode.NotFound)
    {
        _db.PushSubscriptions.Remove(sub);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[WebPush] Removed expired subscription");
    }
}
```

**Why auto-cleanup?** Subscriptions expire when:
- User uninstalls browser
- User clears browser data
- Browser rotates subscription endpoint (rare)

---

## Subscription Lifecycle

```
1. User visits app
   ↓
2. Permission requested
   ↓ (granted)
3. Service worker registered
   ↓
4. PushManager.subscribe() → browser push service
   ↓
5. Browser returns subscription (endpoint + keys)
   ↓
6. POST /push-notifications/subscribe → backend
   ↓
7. Backend stores in user_push_subscriptions
   ↓
   [subscription active]
   ↓
8. Backend sends push → browser push service
   ↓
9. Service worker receives push event
   ↓
10. Notification displayed
   ↓
   [subscription expires after 30-90 days of inactivity]
   ↓
11. 410 Gone returned on next push attempt
   ↓
12. Backend removes subscription from DB
```

### Re-subscription

If subscription expires:
1. Service worker `pushsubscriptionchange` event fires
2. Frontend calls `subscribe()` again → new endpoint
3. POST new subscription to backend
4. Old subscription auto-removed on next 410 response

**Woven's current implementation:** Does NOT handle `pushsubscriptionchange` (manual re-login required). **TODO:** Add auto-resubscribe handler.

---

## Multi-Device Support

Users can have **multiple subscriptions** (desktop Chrome + mobile Safari + Firefox, etc.):

```sql
SELECT user_id, COUNT(*) 
FROM user_push_subscriptions 
GROUP BY user_id;

-- Example:
-- user_id | count
-- --------+------
--      42 |     3  (desktop + mobile + tablet)
```

**Delivery:** `SendToUserAsync` sends to **all subscriptions in parallel** (user gets notification on all devices).

**Deduplication:** Browser handles deduplication (if user already saw notification on one device, others won't show duplicate).

---

## VAPID JWT Structure

When `WebPush` library sends a notification, it includes a VAPID JWT in the `Authorization` header:

```
Authorization: vapid t=<JWT>, k=<PublicKey>
```

**JWT payload:**

```json
{
  "sub": "mailto:support@wooven.me",  // Contact email
  "aud": "https://fcm.googleapis.com",  // Push service origin
  "exp": 1672531200  // Expires in 12 hours
}
```

**Signature:** ECDSA P-256 using VAPID private key.

**Verification:** Push service verifies signature with VAPID public key (proves server identity).

---

## Browser Compatibility

| Browser | Support | Notes |
|---|---|---|
| Chrome 42+ | ✅ | Uses FCM |
| Firefox 44+ | ✅ | Uses Mozilla push service |
| Safari 16+ | ✅ | Uses APNs (requires HTTPS) |
| Edge 17+ | ✅ | Uses WNS → migrated to FCM |
| Mobile Chrome | ✅ | Works on Android |
| Mobile Safari | ✅ | iOS 16.4+ (requires Add to Home Screen) |

**iOS caveat:** Safari on iOS requires app to be **installed to home screen** for push to work (PWA mode).

---

## Testing

### Local Dev (Chrome DevTools)

1. **Check permission:**
   - DevTools → Console: `Notification.permission`
   - Should be `"granted"`

2. **Check service worker:**
   - DevTools → Application → Service Workers
   - Should show `service-worker.js` (activated)

3. **Check subscription:**
   - DevTools → Application → Push Messaging
   - Should show endpoint + keys

4. **Trigger test push:**
   ```csharp
   await _webPush.SendToUserAsync(
       userId: 1,
       title: "Test",
       body: "This is a test notification",
       url: "/",
       ct: default
   );
   ```

5. **Verify delivery:**
   - Notification should appear in OS notification center
   - Click notification → app opens to specified URL

### Production Testing

1. **Check VAPID keys loaded:**
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

---

## Security Considerations

### VAPID Private Key

- **NEVER commit to git** (use User Secrets / Key Vault)
- **Rotate every 1-2 years** (requires re-subscription for all users)
- **Backup securely** (losing key = all subscriptions invalid)

### Subscription Keys (`p256dh`, `auth`)

- **User-specific** (each subscription has unique keys)
- **Stored in plaintext in DB** (required for encryption, not sensitive)
- **No PII in payload** (notification content is ephemeral, not sensitive)

### Endpoint URLs

- **Opaque tokens** (look like `https://fcm.googleapis.com/fcm/send/abc123...`)
- **User-specific** (one user cannot send push to another user's endpoint)
- **Time-limited** (expire after 30-90 days of inactivity)

---

## Troubleshooting

### Issue: Notifications not received

**Check:**
1. Permission granted? (`Notification.permission === "granted"`)
2. Service worker registered? (DevTools → Application)
3. Subscription exists in DB? (`SELECT * FROM user_push_subscriptions WHERE user_id = ?`)
4. Backend logs show send attempt? (`[WebPush] Sent notification | UserId=...`)
5. 410/404 errors? (subscription expired, needs re-subscription)

### Issue: 401 Unauthorized

**Cause:** VAPID authentication failure.

**Fix:**
1. Check VAPID keys match (public key in frontend === backend public key)
2. Check `Vapid:Subject` is valid (must be `mailto:` or `https:`)
3. Check VAPID private key is correct (matches public key)

### Issue: Service worker not registered

**Cause:** Path mismatch or HTTPS requirement.

**Fix:**
1. Service worker must be at `/service-worker.js` (served from public root)
2. HTTPS required (or localhost for dev)
3. Check browser console for registration errors

---

## Performance

### Batch Sending

`SendToUserAsync` sends to **all user subscriptions in parallel** (not sequential):

```csharp
var tasks = subscriptions.Select(async sub => {
    await _client.SendNotificationAsync(...);
});
await Task.WhenAll(tasks);  // Parallel, not sequential
```

**Typical latency:**
- 1 subscription: 100-200ms
- 3 subscriptions: 150-250ms (parallelized)

### Rate Limits

Push services impose rate limits:
- **FCM:** 1 million requests/min (per project)
- **APNs:** No public limit (throttles on abuse)

**Woven's scale:** <1000 active users → no risk of hitting limits.

---

## References

- [Web Push Protocol (RFC 8030)](https://datatracker.ietf.org/doc/html/rfc8030)
- [VAPID (RFC 8292)](https://datatracker.ietf.org/doc/html/rfc8292)
- [Push API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
- [WebPush C# Library](https://github.com/web-push-libs/web-push-csharp)
