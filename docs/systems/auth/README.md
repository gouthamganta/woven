# Authentication System

**Last Updated:** 2026-10-07  
**Status:** Production-ready (dual-mode: Bearer + httpOnly cookies)

---

## Overview

Woven uses **Google OAuth 2.0** as the sole authentication provider. Users sign in with their Google account, and the backend issues a JWT (JSON Web Token) for subsequent API calls.

**No passwords.** No email/password registration. No SMS OTP. Google-only.

### Architecture

```
Frontend                Backend                    Google
────────               ─────────                   ──────
 Login    ──1──>   Verify idToken     ──2──>   Public Keys (JWKS)
  Page              (GoogleTokenVerifier)        accounts.google.com
                           │
                           │ 3. Create/link user
                           ↓
                       WovenDbContext
                    (User + AuthIdentity)
                           │
                           │ 4. Issue JWT
                           ↓
                      JwtTokenService
                           │
                           │ 5. Set httpOnly cookie + return JSON
                           ↓
 Store in  <──────     Response:
localStorage         { accessToken, user }
  (legacy)            Set-Cookie: woven_access_token
```

### Key Components

| Component | File | Purpose |
|---|---|---|
| GoogleTokenVerifier | `Auth/GoogleTokenVerifier.cs` | Verifies Google ID tokens using JWKS |
| JwtTokenService | `Auth/JwtTokenService.cs` | Issues JWT access tokens |
| CookieAuthHelper | `Auth/CookieAuthHelper.cs` | Manages httpOnly cookies |
| AuthEndpoints | `Endpoints/AuthEndpoints.cs` | `POST /auth/google`, `POST /auth/logout` |
| EndpointHelper | `Endpoints/EndpointHelper.cs` | Extracts user ID from JWT claims |
| JwtBearer Middleware | `Program.cs:266-315` | Validates JWT from header/cookie |

### Data Model

**Users table** — core identity  
- `Id` (PK), `Email`, `FullName`, `ProfilePhoto`, `ProfileStatus`, `CreatedAt`, `UpdatedAt`

**AuthIdentities table** — links users to OAuth providers  
- `Id` (PK), `UserId` (FK), `Provider` ("google"), `ProviderSubject` (Google's `sub` claim), `Email` (copy), `CreatedAt`

A user can have multiple `AuthIdentity` records if we later add Apple/Microsoft providers. Currently, only Google exists.

### Dual-Mode Authentication

The backend accepts JWT tokens from **two sources** (evaluated in order):

1. **Query string** (SignalR only): `?access_token=<jwt>` — WebSocket limitation
2. **httpOnly cookie**: `woven_access_token` — XSS-resistant (preferred)
3. **Authorization header**: `Bearer <jwt>` — legacy, still supported

Frontend currently uses `localStorage` + `Authorization: Bearer` header. Migration to httpOnly cookies is planned (see [cookie-auth.md](./cookie-auth.md)).

---

## Quick Reference

### Login Flow

1. Frontend obtains Google ID token via `gapi.auth2` or `@react-oauth/google`
2. Frontend sends `POST /auth/google` with `{ idToken }`
3. Backend verifies token against Google's JWKS
4. Backend creates/links user in database
5. Backend issues JWT, sets httpOnly cookie, returns JSON
6. Frontend stores token in `localStorage` (legacy) and uses `Authorization: Bearer` header

### JWT Claims

| Claim | Type | Example | Usage |
|---|---|---|---|
| `sub` | string | `"123"` | Standard JWT subject (user ID) |
| `email` | string | `"alice@gmail.com"` | User's email |
| `uid` | string | `"123"` | Custom claim (preferred for Woven) |
| `iss` | string | `"WovenBackend"` | Issuer |
| `aud` | string | `"WovenFrontend"` | Audience |
| `exp` | unix timestamp | `1720123456` | Expiry (60 min default) |

**Claim resolution order** (see `EndpointHelper.GetUserId`):  
`uid` → `sub` → `ClaimTypes.NameIdentifier`

If none exist or parse fails → `UnauthorizedAccessException` → HTTP 401

### Configuration

**appsettings.json** (non-sensitive values):
```json
{
  "GoogleAuth": {
    "ClientId": "211033152902-umjjk9n5mqd02s97skerf9sn383m0v00.apps.googleusercontent.com"
  },
  "Jwt": {
    "Issuer": "WovenBackend",
    "Audience": "WovenFrontend",
    "Key": "OVERRIDE_IN_USER_SECRETS_OR_KEYVAULT",
    "ExpiryMinutes": 60,
    "ClockSkewMinutes": 1
  }
}
```

**User Secrets** (local dev):
```bash
dotnet user-secrets set "Jwt:Key" "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_MIN_32_CHARS"
```

**Azure Key Vault** (production):
```bash
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "Jwt--Key" \
  --value "GENERATE_A_STRONG_RANDOM_KEY_HERE"
```

---

## Security Posture

| Feature | Status | Notes |
|---|---|---|
| Password storage | N/A | No passwords (Google OAuth only) |
| JWT signing | ✅ HS256 | Symmetric key in Key Vault |
| httpOnly cookies | ✅ Available | Frontend not yet migrated |
| HTTPS-only cookies | ✅ Secure=true | Production only |
| SameSite cookies | ✅ Strict | CSRF protection |
| Token expiry | ✅ 60 min | Configurable via `Jwt:ExpiryMinutes` |
| Clock skew tolerance | ✅ 1 min | Prevents time-sync issues |
| Rate limiting | ✅ 20 auth/day/IP | Prevents brute-force |
| Trust signals | ✅ Device fingerprint | Fire-and-forget, non-blocking |

---

## File Structure

```
backend/WovenBackend/
├── Auth/
│   ├── CookieAuthHelper.cs         # httpOnly cookie utilities
│   ├── GoogleTokenVerifier.cs      # Google ID token verification (JWKS)
│   ├── JwtTokenService.cs          # JWT issuance
├── Endpoints/
│   ├── AuthEndpoints.cs            # POST /auth/google, POST /auth/logout
│   ├── EndpointHelper.cs           # GetUserId (claim extraction)
├── data/Entities/
│   ├── User.cs                     # User entity
│   ├── AuthIdentity.cs             # OAuth provider links
├── Program.cs                      # JWT middleware config (lines 266-315)
├── appsettings.json                # Public config (Google ClientId, JWT Issuer/Audience)
└── COOKIE_AUTH_MIGRATION.md        # Migration guide (localStorage → httpOnly)
```

---

## Related Documentation

- [jwt.md](./jwt.md) — JWT structure, claims, validation
- [google-oauth.md](./google-oauth.md) — Google Sign-In integration
- [cookie-auth.md](./cookie-auth.md) — httpOnly cookie implementation
- [session-management.md](./session-management.md) — Session persistence, expiry
- [security.md](./security.md) — Security measures
- [api.md](./api.md) — Auth endpoints (request/response schemas)
- [SECRETS_SETUP.md](../../backend/WovenBackend/SECRETS_SETUP.md) — Key Vault configuration

---

## Common Operations

### Test Authentication Flow (curl)

```bash
# 1. Obtain Google ID token (use browser console on login page)
# const token = gapi.auth2.getAuthInstance().currentUser.get().getAuthResponse().id_token

# 2. Login to Woven
curl -X POST http://localhost:5135/auth/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"GOOGLE_ID_TOKEN_HERE"}' \
  -c cookies.txt

# 3. Use cookie for authenticated request
curl http://localhost:5135/moments \
  -b cookies.txt

# 4. Logout (clears cookies)
curl -X POST http://localhost:5135/auth/logout \
  -b cookies.txt \
  -c cookies.txt
```

### Extract User ID in Endpoint

```csharp
app.MapGet("/me", (HttpContext http) =>
{
    var userId = EndpointHelper.GetUserId(http.User);
    return Results.Ok(new { userId });
})
.RequireAuthorization();
```

**Never call `GetUserId` without `.RequireAuthorization()`** — it assumes the user is authenticated.

---

## Roadmap

- [x] Google OAuth integration (2025-12)
- [x] JWT issuance (2025-12)
- [x] httpOnly cookie support (2026-06)
- [x] Azure Key Vault secrets (2026-06)
- [x] Rate limiting (2026-06)
- [ ] Frontend migration to httpOnly cookies
- [ ] Token refresh mechanism (currently stateless, no refresh tokens)
- [ ] Add Apple Sign-In provider (if needed for iOS)
- [ ] Add Microsoft provider (if enterprise customers request it)

---

## FAQ

**Q: Why Google-only?**  
A: Simplicity. No password storage, no email verification, no account recovery flows. Google's OAuth is bulletproof.

**Q: What if a user doesn't have a Google account?**  
A: 95%+ of our target market (India, 18-35) has one. Edge cases can create a free Gmail.

**Q: Why not add Facebook/Apple?**  
A: We will if data shows signup drop-off. Google covers >90% of users.

**Q: Why is localStorage still used?**  
A: Legacy. httpOnly cookies are implemented on backend. Frontend migration is planned.

**Q: How do I test auth locally?**  
A: Use the dev login endpoint (`POST /dev-auth/login-dev`) or a real Google ID token from the frontend.

**Q: Why 60-minute expiry instead of 30 days?**  
A: Dating app = high-value sessions. Shorter lifetime reduces token theft impact. Frontend doesn't yet have refresh logic, but users rarely hit expiry (sessions < 30 min average).

**Q: Can I use the JWT from another device?**  
A: Yes. JWTs are stateless. Same token works anywhere until expiry. We track device fingerprints for trust signals but don't enforce device binding.

---

**Next:** Read [jwt.md](./jwt.md) for JWT internals, or [google-oauth.md](./google-oauth.md) for Google integration details.
