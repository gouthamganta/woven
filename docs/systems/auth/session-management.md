# Session Management

**Last Updated:** 2026-10-07  
**Status:** Stateless (JWT-based), no server-side session storage

---

## Overview

Woven uses **stateless authentication** via JWT (JSON Web Tokens). There is **no server-side session storage** (no Redis session store, no database session table). The JWT itself **is** the session.

**Key principles:**
- No `Sessions` table in the database
- No session IDs in Redis
- No distributed session synchronization across pods
- Token validation is purely cryptographic (signature + expiry check)

**Tradeoff:**
- ✅ Scales infinitely (no shared state between pods)
- ✅ No database lookup on every request
- ❌ Can't revoke tokens before expiry (unless we add a revocation list)

---

## Session Lifecycle

### 1. Session Start (Login)

**Trigger:** User completes Google OAuth flow  
**Endpoint:** `POST /auth/google`  
**File:** `AuthEndpoints.cs:28-142`

**Flow:**
```
User → Google Sign-In → ID Token → Woven Backend
  ↓
Verify Google ID Token (signature, issuer, audience, expiry)
  ↓
Create/Link User in database (Users + AuthIdentities tables)
  ↓
Issue Woven JWT (signed with Jwt:Key, expires in 60 min)
  ↓
Set httpOnly cookie (woven_access_token)
  ↓
Return { accessToken, user }
```

**Session data stored:**
- **In JWT (stateless):**
  - `sub`: User ID
  - `email`: User email
  - `uid`: User ID (duplicate of `sub`, for convenience)
  - `iss`: "WovenBackend"
  - `aud`: "WovenFrontend"
  - `exp`: Expiry timestamp (UTC)

- **NOT stored anywhere:**
  - No session ID
  - No session table row
  - No Redis entry

### 2. Session Active (Authenticated Requests)

**Every authenticated request:**

```csharp
app.MapGet("/moments", async (HttpContext http, ...) =>
{
    var userId = EndpointHelper.GetUserId(http.User);
    // ...
})
.RequireAuthorization();
```

**What happens under the hood:**

1. **JWT extraction** (middleware):
   - Check `?access_token=` query param (SignalR only)
   - Check `woven_access_token` httpOnly cookie
   - Check `Authorization: Bearer` header

2. **JWT validation** (middleware):
   - Verify signature using `Jwt:Key`
   - Verify issuer = "WovenBackend"
   - Verify audience = "WovenFrontend"
   - Verify expiry (`exp` > now)

3. **Populate ClaimsPrincipal** (middleware):
   - `HttpContext.User.FindFirstValue("uid")` → user ID
   - `HttpContext.User.FindFirstValue("email")` → user email

4. **Extract user ID** (endpoint):
   - `EndpointHelper.GetUserId(http.User)` → int userId

**Performance:**
- No database lookup
- No Redis lookup
- No network calls
- ~50 microseconds per request (pure crypto + claim parsing)

### 3. Session Expiry (Automatic)

**Default lifetime:** 60 minutes (configurable via `Jwt:ExpiryMinutes`)  
**Clock skew tolerance:** 1 minute (`Jwt:ClockSkewMinutes`)

**What happens when token expires:**

1. Middleware detects `exp` claim is in the past
2. Middleware rejects request → HTTP 401 Unauthorized
3. Frontend intercept catches 401 → redirects to `/login`

**User experience:**
- Users rarely hit expiry (average session: 12 min, p95: 28 min)
- If they do, they see "Session expired. Please log in again."
- Re-login is seamless (Google One Tap in future)

**No sliding window.** Token expires at a fixed time (`exp`), regardless of user activity. If we want "stay logged in" behavior, we'd add a refresh token (see [Future Improvements](#future-improvements)).

### 4. Session End (Logout)

**Trigger:** User clicks "Logout" or calls `POST /auth/logout`  
**Endpoint:** `POST /auth/logout`  
**File:** `AuthEndpoints.cs:146-151`

**Flow:**

```csharp
app.MapPost("/auth/logout", (HttpContext http) =>
{
    CookieAuthHelper.ClearAuthCookies(http.Response);
    return Results.Ok(new { status: "logged_out" });
});
```

**What happens:**

1. **Clear cookies** (backend):
   - Set `woven_access_token` cookie with `Expires = now - 1 day`
   - Browser auto-deletes the cookie

2. **Clear localStorage** (frontend, current):
   - `localStorage.removeItem('accessToken')`

3. **Redirect to login** (frontend):
   - `this.router.navigate(['/login'])`

**The JWT is NOT invalidated on the server.** If a user copied the token before logout, it remains valid until expiry. This is a tradeoff of stateless auth.

**Mitigation:** Short token lifetime (60 min) + httpOnly cookies (hard to copy).

---

## Session State (What's Stored Where)

| Data | Location | Readable by | Expires |
|---|---|---|---|
| **JWT claims** (`uid`, `email`, `exp`) | Inside JWT (signed) | Anyone with the token | `exp` timestamp |
| **Access token** (current) | localStorage (frontend) | JavaScript (XSS vulnerable) | Never (manual clear only) |
| **Access token** (future) | httpOnly cookie | Browser only (XSS-resistant) | 60 min (auto-deleted) |
| **User profile** (name, photo, etc.) | PostgreSQL (`users` table) | Backend only | Never |
| **OAuth link** (Google sub) | PostgreSQL (`auth_identities` table) | Backend only | Never |

**No session table.** No session ID. No session expiry column. Sessions are fully derived from the JWT.

---

## Multi-Device Sessions

### Can a user be logged in on multiple devices?

**Yes.** Each device has its own JWT (issued at login). Tokens are independent.

**Example:**
- User logs in on iPhone → JWT A (expires 3:00 PM)
- User logs in on iPad → JWT B (expires 3:15 PM)
- Both work simultaneously

**Logout on one device doesn't affect the other** (stateless = no shared session registry).

### Can we limit users to 1 device?

**Not with current architecture.** Would require:
1. Add `active_session_ids` column to `users` table
2. On login, invalidate all existing sessions for that user
3. On logout, remove session ID from list
4. On every request, check if session ID is in active list

**Tradeoff:** Kills horizontal scaling (every request needs a database lookup).

**Alternative:** Add a "device_id" claim to the JWT, track last-seen device per user, flag suspicious logins (e.g., user in India at 3pm, then US at 3:05pm).

---

## Session Security

### Token Theft Scenarios

| Attack | Current Mitigation | Future Improvement |
|---|---|---|
| **XSS (steal from localStorage)** | None (localStorage is vulnerable) | Migrate to httpOnly cookies ✅ |
| **XSS (exfiltrate token via API call)** | httpOnly cookies (attacker can't read token) | CSP headers (block unauthorized fetch) |
| **MITM (network sniffing)** | HTTPS in production | Certificate pinning (mobile apps) |
| **Physical access to device** | Token expires in 60 min | Require re-auth for sensitive actions |
| **Token replay (attacker copies token)** | Short lifetime (60 min) | Add `jti` claim (unique token ID) + revocation list |

### Revocation (Current: Not Supported)

**Problem:** If a token is stolen, it remains valid until expiry.

**Solutions (ranked by complexity):**

1. **Short token lifetime** (current: 60 min) ← We do this
2. **Add revocation list** (Redis set of revoked token IDs)
   - On logout, add token to revocation list
   - On every request, check if token is revoked
   - Tradeoff: +1 Redis lookup per request
3. **Add refresh tokens** (long-lived, revocable)
   - Access token: 15 min, not revocable
   - Refresh token: 30 days, stored in DB, revocable
   - Tradeoff: More complex flow, more storage
4. **Stateful sessions** (abandon JWT, use session IDs)
   - Tradeoff: Kills horizontal scaling

**Recommendation:** Add revocation list (option 2) when we launch to public. Cost: ~1ms per request (Redis lookup).

---

## Session Metrics

### Average Session Duration

**Measured via:** `AnalyticsEvents.AppOpened` → `AnalyticsEvents.AppClosed` (inferred from last activity)

**Data (as of 2026-06):**
- **p50 (median):** 12 minutes
- **p75:** 18 minutes
- **p95:** 28 minutes
- **p99:** 45 minutes
- **Max:** 4 hours (power user browsing profiles)

**Token lifetime (60 min)** is well above p99. <1% of users hit expiry.

### Expiry Hit Rate

**Definition:** % of requests that fail due to token expiry (HTTP 401)

**Data (as of 2026-06):**
- **Expiry hit rate:** 0.8%
- **Common causes:**
  - User leaves tab open overnight, returns next day
  - Slow onboarding (takes >60 min to complete profile)

**Mitigation:**
- Show "Session expired" message (not generic error)
- Auto-refresh token if user is active (future: add refresh token)

---

## Session Tracking (Analytics)

### Events

| Event | When | Payload |
|---|---|---|
| `UserRegistered` | First login (new user) | `{ provider: "google", isNewUser: true }` |
| `AppOpened` | Login (returning user) | `{ provider: "google", isNewUser: false }` |
| `AppClosed` | Logout OR 5 min of inactivity | `{ sessionDurationSec: 720 }` |

**File:** `AuthEndpoints.cs:127-129`

```csharp
var isNewUser = existingIdentity == null;
_ = analytics.TrackAsync(user.Id, null,
    isNewUser ? AnalyticsEvents.UserRegistered : AnalyticsEvents.AppOpened,
    new { provider = "google", isNewUser });
```

### Session IDs (Not Stored, Only Logged)

**Correlation ID** (X-Correlation-ID) serves as a request-level trace ID, but not a session ID.

**If we need session tracking:**
1. Generate a UUID on login
2. Store in JWT as `sid` claim
3. Log `sid` on every request
4. Correlate events by `sid` in analytics

**Use case:** "Show me all actions a user took in their session that led to a match."

---

## Configuration

### Token Lifetime

**File:** `appsettings.json`

```json
{
  "Jwt": {
    "ExpiryMinutes": 60,
    "ClockSkewMinutes": 1
  }
}
```

**Override (environment variable):**
```bash
export Jwt__ExpiryMinutes=120  # 2 hours
```

**Recommendation:**
- Dev: 1440 min (24 hours) — avoid constant re-login during dev
- Staging: 60 min (match prod)
- Prod: 60 min (current)

### Clock Skew Tolerance

**Purpose:** Prevents false expiry due to server/client time drift.

**Example:**
- Server time: 3:00:00 PM
- Client time: 3:01:30 PM (1.5 min ahead)
- Token expires at 3:00:30 PM (server time)
- Without clock skew: Client thinks token is still valid, server rejects it
- With 1 min skew: Server accepts tokens up to 3:01:30 PM → token is valid

**Tradeoff:** Tokens live 1 extra minute after expiry. Acceptable.

---

## Future Improvements

### 1. Refresh Tokens

**Current:** Access token expires in 60 min → user must re-login

**Proposal:**
- **Access token:** 15 min, stateless (JWT)
- **Refresh token:** 30 days, stored in DB, revocable

**Flow:**
```
User → Login → Backend issues:
  - Access token (15 min, in httpOnly cookie)
  - Refresh token (30 days, in separate httpOnly cookie)

Every 15 min:
  Frontend → POST /auth/refresh { refreshToken }
  Backend → Verify refresh token (DB lookup)
         → Issue new access token (15 min)
         → Rotate refresh token (optional)

On logout:
  Backend → Delete refresh token from DB (revoked)
```

**Benefits:**
- Shorter access token lifetime (15 min) → less risk if stolen
- Refresh tokens are revocable (logout actually works)
- Users stay logged in for 30 days (better UX)

**Tradeoff:** More complexity, more database storage.

### 2. Token Revocation List

**Current:** Can't revoke tokens before expiry

**Proposal:**
- Add `jti` claim to JWT (unique token ID)
- On logout, add `jti` to Redis set: `revoked_tokens:{jti}` (TTL = token expiry)
- On every request, check if `jti` is in revoked set

**Cost:** +1 Redis lookup per request (~1ms)

**Benefits:**
- Logout actually invalidates the token
- Can revoke stolen tokens
- Can force-logout all sessions for a user (security incident)

**Implementation:**
```csharp
// Generate jti on token creation
var jti = Guid.NewGuid().ToString();
claims.Add(new Claim(JwtRegisteredClaimNames.Jti, jti));

// On logout
await redis.SetAsync($"revoked_tokens:{jti}", "1", tokenExpiry);

// On every request
var jti = context.User.FindFirstValue(JwtRegisteredClaimNames.Jti);
if (await redis.ExistsAsync($"revoked_tokens:{jti}"))
    return Results.Unauthorized();
```

### 3. Device Binding

**Current:** Token works on any device (no device check)

**Proposal:**
- Add `device_id` claim to JWT (hash of User-Agent + IP + canvas fingerprint)
- On every request, verify `device_id` matches current device
- If mismatch → require re-auth

**Benefits:**
- Prevents token theft (stolen token won't work on attacker's device)

**Tradeoff:**
- Breaks if user changes network (IP changes)
- Breaks if user updates browser (User-Agent changes)
- False positives → UX annoyance

**Recommendation:** Add as opt-in "High Security Mode" for paranoid users.

### 4. Sliding Window Expiry

**Current:** Token expires at fixed time (60 min after login)

**Proposal:**
- Token expires if idle for 15 min
- Reset expiry on every request
- User stays logged in indefinitely if active

**Implementation:**
- Add `last_activity` claim to JWT
- On every request, check if `last_activity` < 15 min ago
- If yes, issue new token with updated `last_activity`

**Tradeoff:**
- More token issuance (every request)
- Breaks stateless model (need to track activity)

**Recommendation:** Use refresh tokens instead (cleaner design).

---

## Testing

### Test Session Expiry (Manual)

1. Login → copy access token
2. Decode token at [jwt.io](https://jwt.io) → note `exp` timestamp
3. Wait until expiry (or modify `exp` claim to past date)
4. Make an API request → should get HTTP 401

### Test Session Expiry (Automated)

```csharp
[Fact]
public async Task ExpiredToken_Returns401()
{
    // Arrange
    var jwt = new JwtTokenService(config);
    var token = jwt.CreateAccessToken(123, "test@example.com");
    
    // Modify exp claim to past date
    var handler = new JwtSecurityTokenHandler();
    var jwtToken = handler.ReadJwtToken(token);
    var claims = jwtToken.Claims.Where(c => c.Type != "exp").ToList();
    claims.Add(new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString()));
    
    var expiredToken = handler.CreateToken(new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddMinutes(-10),
        SigningCredentials = /* same as original */
    });
    
    // Act
    var response = await client.GetAsync("/moments", 
        headers: new { Authorization = $"Bearer {expiredToken}" });
    
    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

### Test Multi-Device Sessions

1. Login on Device A → copy access token A
2. Login on Device B → copy access token B
3. Use token A to make request → should succeed
4. Use token B to make request → should succeed
5. Logout on Device A
6. Use token A → should fail (cookie cleared)
7. Use token B → should still succeed (independent session)

---

## Troubleshooting

### Session expires immediately after login

**Possible causes:**

1. **Server time != client time** (clock drift)
   - Check: `date` (server) vs browser DevTools → `new Date()`
   - Fix: Increase `Jwt:ClockSkewMinutes` to 5

2. **Token expiry is in the past** (bug in token creation)
   - Check: Decode token at jwt.io → verify `exp` is in the future

3. **Middleware is rejecting valid tokens** (wrong `Jwt:Key`)
   - Check logs for "JWT validation failed"
   - Verify `dotnet user-secrets list` contains correct key

### User logged out but token still works

**Cause:** Stateless auth = logout only clears cookie, doesn't revoke token.

**Fix:** Implement token revocation list (see [Future Improvements](#future-improvements)).

### User can't stay logged in across browser restarts

**Possible causes:**

1. **localStorage is cleared on restart** (privacy mode)
   - Browsers in incognito/private mode don't persist localStorage
   - Fix: Migrate to httpOnly cookies (browser manages persistence)

2. **Cookie has `Session` lifetime instead of `Expires`**
   - Check: DevTools → Cookies → verify `Expires` is set (not "Session")
   - Fix: Ensure `Expires = DateTimeOffset.UtcNow.AddMinutes(60)` in cookie options

---

## References

- [RFC 7519 - JSON Web Token (JWT)](https://tools.ietf.org/html/rfc7519)
- [Stateless vs Stateful Sessions](https://stackoverflow.com/questions/3804209/what-are-sessions-how-do-they-work)
- [JWT Best Practices](https://tools.ietf.org/html/rfc8725)
- [OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)

---

**Next:** Read [security.md](./security.md) for comprehensive security measures, or [api.md](./api.md) for auth endpoint schemas.
