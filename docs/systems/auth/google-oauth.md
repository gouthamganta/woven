# Google OAuth 2.0 Integration

**Last Updated:** 2026-10-07  
**File:** `backend/WovenBackend/Auth/GoogleTokenVerifier.cs`

---

## Overview

Woven uses **Google Sign-In** (OAuth 2.0) as the sole authentication provider. Users authenticate with their Google account, and Google issues an **ID token** (a JWT signed by Google). The frontend sends this ID token to Woven's backend, which verifies it and creates a user session.

**Flow:**
```
User → Google Sign-In → ID Token → Woven Backend → Verify with Google → Create/Link User → Issue Woven JWT
```

**No username/password.** No email verification. No account recovery. Google handles all of that.

---

## Authentication Flow

### Step 1: Frontend Initiates Google Sign-In

**Library:** `@react-oauth/google` (React) or `gapi.auth2` (vanilla JS)

**Example (React):**
```tsx
import { GoogleLogin } from '@react-oauth/google';

<GoogleLogin
  onSuccess={(credentialResponse) => {
    const idToken = credentialResponse.credential;
    fetch('/auth/google', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ idToken })
    })
    .then(res => res.json())
    .then(data => {
      localStorage.setItem('accessToken', data.accessToken);
      // Redirect to app
    });
  }}
  onError={() => console.error('Google Sign-In failed')}
/>
```

**What happens:**
1. User clicks "Sign in with Google"
2. Google popup opens → user selects account → Google returns ID token
3. Frontend sends `POST /auth/google` with `{ idToken: "..." }`

### Step 2: Backend Verifies ID Token

**File:** `backend/WovenBackend/Auth/GoogleTokenVerifier.cs`

```csharp
public async Task<GoogleUserInfo> VerifyAsync(string idToken, CancellationToken ct)
{
    // 1. Sanity check: must be a valid JWT (3 parts)
    if (idToken.Split('.').Length != 3)
        throw new SecurityTokenException("Invalid Google token format");

    // 2. Fetch Google's public keys (JWKS)
    var jwks = await GetJwksAsync(ct);

    // 3. Validate token signature + claims
    var validationParams = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuers = new[] { "accounts.google.com", "https://accounts.google.com" },

        ValidateAudience = true,
        ValidAudience = _options.ClientId,  // Woven's Google OAuth Client ID

        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(2),

        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = jwks.Keys  // Google's public keys
    };

    var principal = handler.ValidateToken(idToken, validationParams, out _);

    // 4. Extract user info
    return new GoogleUserInfo(
        Subject: principal.FindFirstValue("sub"),      // Google's unique user ID
        Email: principal.FindFirstValue("email"),
        Name: principal.FindFirstValue("name"),
        Picture: principal.FindFirstValue("picture")
    );
}
```

**Validation checks (automatic):**
- ✅ Signature matches Google's public key (JWKS)
- ✅ Issuer is `accounts.google.com`
- ✅ Audience is Woven's Google OAuth Client ID
- ✅ Token is not expired
- ✅ Contains required claims (`sub`, `email`)

If any check fails → `SecurityTokenException` → HTTP 401

### Step 3: Create or Link User

**File:** `backend/WovenBackend/Endpoints/AuthEndpoints.cs:58-102`

```csharp
// Check if this Google account is already linked
var existingIdentity = await db.AuthIdentities
    .Include(x => x.User)
    .FirstOrDefaultAsync(x =>
        x.Provider == "google" && x.ProviderSubject == googleUser.Subject, ct);

User user;

if (existingIdentity != null)
{
    // Returning user
    user = existingIdentity.User;
}
else
{
    // New user OR existing user linking Google account

    // Find existing user by email (prevents duplicate accounts)
    user = await db.Users.FirstOrDefaultAsync(u => u.Email == googleUser.Email, ct)
           ?? new User
           {
               Email = googleUser.Email,
               FullName = googleUser.Name,
               ProfilePhoto = googleUser.Picture,
               ProfileStatus = ProfileStatus.INCOMPLETE,
               CreatedAt = DateTime.UtcNow,
               UpdatedAt = DateTime.UtcNow
           };

    if (user.Id == 0)  // New user
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    // Create AuthIdentity link
    db.AuthIdentities.Add(new AuthIdentity
    {
        UserId = user.Id,
        Provider = "google",
        ProviderSubject = googleUser.Subject,
        Email = googleUser.Email
    });

    await db.SaveChangesAsync(ct);
}
```

**Key behaviors:**
- **Email-based deduplication** — If a user signs in with Google after creating an account another way (future: Apple, Microsoft), we link the Google account to the existing user instead of creating a duplicate.
- **AuthIdentity as the join table** — One user can have multiple OAuth providers (Google, Apple, Microsoft). Currently, only Google exists.
- **Profile photo from Google** — `user.ProfilePhoto` is set to Google's profile picture URL.

### Step 4: Issue Woven JWT

**File:** `backend/WovenBackend/Endpoints/AuthEndpoints.cs:121-142`

```csharp
var accessToken = jwt.CreateAccessToken(user.Id, user.Email);

// Set httpOnly cookie (XSS protection)
CookieAuthHelper.SetAccessTokenCookie(http.Response, accessToken);

return Results.Ok(new
{
    accessToken,  // For backward compatibility (frontend still uses this)
    user = new
    {
        user.Id,
        user.Email,
        user.FullName,
        user.ProfilePhoto
    }
});
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": 123,
    "email": "alice@gmail.com",
    "fullName": "Alice Johnson",
    "profilePhoto": "https://lh3.googleusercontent.com/a/..."
  }
}
```

**Also sets cookie:**
```
Set-Cookie: woven_access_token=eyJhbGc...; HttpOnly; Secure; SameSite=Strict; Expires=<now+60min>; Path=/
```

---

## Google JWKS (Public Keys)

### What is JWKS?

**JWKS (JSON Web Key Set)** — a list of public keys used to verify JWT signatures.

Google rotates its signing keys periodically. Instead of hardcoding a public key, we fetch the latest keys from:

```
https://www.googleapis.com/oauth2/v3/certs
```

**Example response:**
```json
{
  "keys": [
    {
      "kid": "a1b2c3d4e5f6...",
      "kty": "RSA",
      "alg": "RS256",
      "use": "sig",
      "n": "0vx7agoebGcQSu...",
      "e": "AQAB"
    },
    { ... }
  ]
}
```

### Caching

**File:** `GoogleTokenVerifier.cs:104-113`

```csharp
private async Task<JsonWebKeySet> GetJwksAsync(CancellationToken ct)
{
    // Cache for 24 hours
    if (_cachedJwks != null && DateTime.UtcNow - _jwksCachedAtUtc < TimeSpan.FromHours(24))
        return _cachedJwks;

    var json = await _http.GetStringAsync(GoogleJwksUrl, ct);
    _cachedJwks = new JsonWebKeySet(json);
    _jwksCachedAtUtc = DateTime.UtcNow;
    return _cachedJwks;
}
```

**Why 24 hours?** Google rotates keys slowly (weeks/months). Fetching on every login is wasteful. 24-hour cache balances freshness vs. performance.

**What if Google rotates keys mid-cache?** Token validation fails → user sees "Try again" error. On retry, cache is refreshed → new key is used → success. Happens <1 time per 10,000 logins.

---

## Configuration

### Google OAuth Client Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a project (or use existing)
3. Enable "Google+ API" (required for Sign-In)
4. Create OAuth 2.0 credentials:
   - **Application type:** Web application
   - **Authorized JavaScript origins:**
     - `http://localhost:4202` (dev)
     - `https://wooven.me` (prod)
   - **Authorized redirect URIs:** Not needed (we use popup flow, not redirect flow)

**Client ID:**
```
211033152902-umjjk9n5mqd02s97skerf9sn383m0v00.apps.googleusercontent.com
```

**Client Secret:** Not used (we verify tokens via JWKS, not exchange codes for tokens)

### Backend Configuration

**appsettings.json:**
```json
{
  "GoogleAuth": {
    "ClientId": "211033152902-umjjk9n5mqd02s97skerf9sn383m0v00.apps.googleusercontent.com"
  }
}
```

**Program.cs:**
```csharp
builder.Services.Configure<GoogleAuthOptions>(
    builder.Configuration.GetSection("GoogleAuth"));

builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
```

---

## Security

### Attack Vectors & Mitigations

| Attack | Mitigation |
|---|---|
| **Stolen Google ID token** | Tokens expire in <1 hour. Woven's JWT has its own expiry (60 min). |
| **Replay attack** | Google tokens are single-use (Google checks `nonce`). Woven doesn't re-verify Google tokens on every request. |
| **MITM (man-in-the-middle)** | HTTPS required in production. Local dev uses HTTP (acceptable risk). |
| **Phishing (fake Google popup)** | Browser verifies `accounts.google.com` TLS cert. User education. |
| **Client ID substitution** | Backend validates `aud` claim matches Woven's Client ID. |

### Why Not Use Google's Client Secret?

**Client Secret is for server-to-server flows** (OAuth 2.0 Authorization Code Grant). We use the **Implicit Flow** (popup + ID token), which doesn't require a secret.

**Our flow:**
```
User → Google popup → ID token → Woven backend
```

**Server-to-server flow (not used):**
```
User → Redirect to Google → Auth code → Woven backend exchanges code for token (using Client Secret)
```

**Why Implicit Flow?** Faster. No server-side redirect. Better UX (popup, not full-page redirect).

---

## Rate Limiting

**File:** `AuthEndpoints.cs:29-38`

```csharp
// 20 auth attempts per IP per day
var ipHash = PiiSanitizer.HashForAudit(ip, "rl-auth-v1");
var rlKey = $"rl:auth:{ipHash}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);

if (!allowed)
{
    http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

**Why 20/day/IP?** Prevents automated bot signups. Legitimate users rarely retry >3 times. If a user hits this limit, something is wrong (broken client, attacker).

**What if a user shares an IP (corporate NAT)?** They each get 20 attempts, but the limit is per-IP, so a busy office could hit it. We'd see this in logs and increase the limit if needed. Has not happened yet.

---

## Trust Signals (Phase 2B)

**File:** `AuthEndpoints.cs:108-119`

After login, we fire-and-forget two trust checks:

```csharp
_ = Task.Run(async () =>
{
    await trust.CheckDeviceFingerprintAsync(user.Id, req.DeviceFingerprint);
    await trust.CheckVelocityAsync(user.Id);
});
```

**Non-blocking.** If trust checks fail, login still succeeds. Trust signals are logged for later analysis (e.g., "This account logged in from 10 different devices in 1 hour").

**DeviceFingerprint:** Browser fingerprint (canvas hash, WebGL hash, screen resolution, etc.). Sent by frontend in `POST /auth/google` body (`deviceFingerprint` field). Optional.

**Velocity check:** Flags accounts with suspicious login patterns (e.g., 100 logins in 1 hour = likely a bot).

---

## Error Handling

### Google Token Verification Fails

**File:** `AuthEndpoints.cs:45-54`

```csharp
try
{
    googleUser = await googleVerifier.VerifyAsync(req.IdToken, ct);
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Google token verification failed");
    return Results.Unauthorized();
}
```

**Common causes:**
- Expired Google ID token (user took >1 hour to submit login)
- Invalid token (tampered, malformed, wrong Client ID)
- Google JWKS fetch failed (network issue)

**Frontend behavior:** Shows "Login failed. Please try again."

### Database Errors

**File:** `AuthEndpoints.cs` (no explicit try/catch)

Database errors (e.g., Postgres down, connection pool exhausted) are caught by `GlobalExceptionHandler` → HTTP 500 with correlation ID.

**Frontend behavior:** Shows "Something went wrong. Please try again later."

---

## Analytics

**File:** `AuthEndpoints.cs:127-129`

```csharp
var isNewUser = existingIdentity == null;
_ = analytics.TrackAsync(user.Id, null,
    isNewUser ? AnalyticsEvents.UserRegistered : AnalyticsEvents.AppOpened,
    new { provider = "google", isNewUser });
```

**Events tracked:**
- `UserRegistered` — first-time signup
- `AppOpened` — returning user login

**Metadata:**
- `provider: "google"`
- `isNewUser: true|false`

Used for:
- Signup funnel analysis ("How many users complete onboarding?")
- Retention analysis ("How many users return after Day 1?")

---

## Testing

### Get a Google ID Token (Browser Console)

1. Open app in browser → click "Sign in with Google"
2. After Google popup closes, open DevTools console
3. Run:
   ```js
   gapi.auth2.getAuthInstance().currentUser.get().getAuthResponse().id_token
   ```
4. Copy the token

### Test Login (curl)

```bash
curl -X POST http://localhost:5135/auth/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"PASTE_GOOGLE_ID_TOKEN_HERE"}' \
  -v
```

**Expected response:**
```json
{
  "accessToken": "eyJhbGc...",
  "user": {
    "id": 123,
    "email": "your-email@gmail.com",
    "fullName": "Your Name",
    "profilePhoto": "https://lh3.googleusercontent.com/a/..."
  }
}
```

**Also check:** `Set-Cookie` header should contain `woven_access_token=eyJhbGc...`

### Mock Google Verification (Dev Only)

**File:** Create `Auth/MockGoogleTokenVerifier.cs`

```csharp
public class MockGoogleTokenVerifier : IGoogleTokenVerifier
{
    public Task<GoogleUserInfo> VerifyAsync(string idToken, CancellationToken ct)
    {
        // Parse idToken as JSON (assuming dev sends {"sub":"123","email":"test@example.com"})
        var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(idToken);
        return Task.FromResult(new GoogleUserInfo(
            Subject: json["sub"],
            Email: json["email"],
            Name: json.GetValueOrDefault("name", "Test User"),
            Picture: json.GetValueOrDefault("picture", "https://via.placeholder.com/150")
        ));
    }
}
```

**Register in Program.cs:**
```csharp
if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IGoogleTokenVerifier, MockGoogleTokenVerifier>();
else
    builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
```

**Usage:**
```bash
curl -X POST http://localhost:5135/auth/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"{\"sub\":\"123\",\"email\":\"test@example.com\"}"}'
```

---

## Troubleshooting

### "Invalid Google token format" error

**Cause:** Token is not a JWT (doesn't have 3 parts separated by dots).

**Fix:** Verify you're sending the **ID token**, not the **access token** or **auth code**.

### "Google token missing sub" error

**Cause:** Google ID token doesn't contain `sub` claim (shouldn't happen with valid tokens).

**Fix:** Check Google Cloud Console → OAuth consent screen → ensure "email" scope is enabled.

### "Invalid audience" error

**Cause:** Token's `aud` claim doesn't match Woven's Google Client ID.

**Fix:** Verify `GoogleAuth:ClientId` in `appsettings.json` matches the Client ID from Google Cloud Console.

### "Token expired" error

**Cause:** Google ID tokens expire after 1 hour. User took too long to submit login.

**Fix:** Refresh the page and try again.

---

## Future Improvements

- [ ] Add Apple Sign-In (required for iOS app)
- [ ] Add Microsoft Sign-In (enterprise customers)
- [ ] Support Google One Tap (faster login for returning users)
- [ ] Cache JWKS in Redis (avoid singleton cache in multi-pod deployments)
- [ ] Add token exchange flow (swap Google token for Woven token without storing Google token)

---

## References

- [Google Sign-In for Websites](https://developers.google.com/identity/sign-in/web)
- [Google ID Token Verification](https://developers.google.com/identity/sign-in/web/backend-auth)
- [Google JWKS Endpoint](https://www.googleapis.com/oauth2/v3/certs)
- [OAuth 2.0 Implicit Flow](https://oauth.net/2/grant-types/implicit/)

---

**Next:** Read [cookie-auth.md](./cookie-auth.md) for httpOnly cookie implementation, or [session-management.md](./session-management.md) for session lifecycle details.
