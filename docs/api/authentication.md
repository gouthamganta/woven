# Authentication API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Woven uses JWT-based authentication with dual support for Bearer tokens and HttpOnly cookies. After successful login, clients receive a JWT that must be included in subsequent requests.

**Security Features:**
- Google OAuth 2.0 sign-in (primary method)
- HttpOnly cookies (XSS-resistant, recommended)
- Bearer tokens (backward compatible)
- Device fingerprinting and trust signals
- Rate limiting on auth endpoints (20 attempts/IP/day)

---

## Endpoints

### POST /auth/google

**Description:** Authenticate using Google OAuth ID token. Creates new user account if first login.

**Authentication:** None (public endpoint)

**Rate Limit:** 20 requests per IP per day

**Request:**
```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6...",
  "deviceFingerprint": "optional-device-id"
}
```

**Response (200 OK):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": 123,
    "email": "user@example.com",
    "fullName": "Jane Doe",
    "profilePhoto": "https://..."
  }
}
```

**Response Cookies:**
```http
Set-Cookie: woven_access_token=<jwt>; HttpOnly; Secure; SameSite=Strict; Max-Age=2592000
```

**Errors:**
- `400`: Bad Request — `idToken` missing
- `401`: Unauthorized — Google token verification failed
- `429`: Rate limit exceeded (see `Retry-After` header)

**JWT Claims:**
```json
{
  "uid": "123",
  "email": "user@example.com",
  "exp": 1730000000,
  "iat": 1727408000
}
```

**Source:** [`backend/WovenBackend/Endpoints/AuthEndpoints.cs:17-143`](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs)

**Implementation Details:**
```csharp
// JWT token creation
var accessToken = jwt.CreateAccessToken(user.Id, user.Email);

// HttpOnly cookie (XSS protection)
CookieAuthHelper.SetAccessTokenCookie(http.Response, accessToken);
```

**Trust Signals (fire-and-forget):**
- Device fingerprint tracking
- Login velocity checks
- Non-blocking background validation

---

### POST /auth/logout

**Description:** Clears authentication cookies (client-side token cleanup still required for Bearer auth).

**Authentication:** None

**Request:** Empty body

**Response (200 OK):**
```json
{
  "status": "logged_out"
}
```

**Response Cookies:**
```http
Set-Cookie: woven_access_token=; HttpOnly; Secure; SameSite=Strict; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT
```

**Source:** [`backend/WovenBackend/Endpoints/AuthEndpoints.cs:146-150`](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs)

---

## Authentication Methods

### Method 1: HttpOnly Cookie (Recommended)

**Advantages:**
- XSS-resistant (JavaScript cannot access)
- Automatic inclusion in requests
- CSRF protection via SameSite=Strict

**Usage:**
```bash
# Login sets cookie automatically
curl -X POST https://api.wooven.me/auth/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"..."}'

# Subsequent requests include cookie automatically
curl https://api.wooven.me/moments \
  --cookie-jar cookies.txt \
  --cookie cookies.txt
```

**Browser:** Cookies are handled automatically after login.

---

### Method 2: Bearer Token (Backward Compatible)

**Usage:**
```bash
# Extract token from login response
TOKEN=$(curl -X POST ... | jq -r '.accessToken')

# Include in Authorization header
curl https://api.wooven.me/moments \
  -H "Authorization: Bearer $TOKEN"
```

**Browser (localStorage):**
```javascript
// After login
localStorage.setItem('accessToken', response.accessToken);

// On requests
fetch('/api/moments', {
  headers: {
    'Authorization': `Bearer ${localStorage.getItem('accessToken')}`
  }
});
```

---

## JWT Token Details

**Algorithm:** HS256 (HMAC with SHA-256)  
**Expiration:** 30 days from issue  
**Issuer:** `woven-api`

**Claim Extraction:**
```csharp
// Server-side (EndpointHelper.GetUserId)
var uid = user.FindFirstValue("uid");
if (int.TryParse(uid, out var id)) return id;

// Fallback to sub / NameIdentifier
var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
if (int.TryParse(sub, out id)) return id;

throw new UnauthorizedAccessException("Missing user id claim");
```

**Source:** [`backend/WovenBackend/Endpoints/EndpointHelper.cs:13-21`](../../backend/WovenBackend/Endpoints/EndpointHelper.cs)

---

## Security Implementation

### Cookie Configuration

**Source:** [`backend/WovenBackend/Auth/CookieAuthHelper.cs`](../../backend/WovenBackend/Auth/CookieAuthHelper.cs)

```csharp
public static void SetAccessTokenCookie(HttpResponse response, string token)
{
    response.Cookies.Append("woven_access_token", token, new CookieOptions
    {
        HttpOnly = true,        // XSS protection
        Secure = true,          // HTTPS only
        SameSite = SameSiteMode.Strict,  // CSRF protection
        MaxAge = TimeSpan.FromDays(30),
        Path = "/"
    });
}
```

### JWT Middleware

**Reads from both sources:**
1. `Authorization: Bearer <token>` header
2. `woven_access_token` HttpOnly cookie

**Source:** [`backend/WovenBackend/Program.cs`](../../backend/WovenBackend/Program.cs) (JWT Bearer configuration)

---

## Rate Limiting

**Auth endpoint limit:** 20 requests per IP per calendar day

**Implementation:**
```csharp
var ipHash = PiiSanitizer.HashForAudit(ip, "rl-auth-v1");
var rlKey = $"rl:auth:{ipHash}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);

if (!allowed) {
    http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

**Source:** [`backend/WovenBackend/Endpoints/AuthEndpoints.cs:30-38`](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs)

---

## Error Responses

### 401 Unauthorized

Returned when JWT is missing, expired, or invalid.

```json
{
  "error": "Unauthorized",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

**Handled by:** [`backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs`](../../backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs)

### 429 Rate Limit Exceeded

```json
{
  "error": "Rate limit exceeded",
  "retryAfter": 3600,
  "correlationId": "a1b2c3d4e5f67890"
}
```

**Headers:**
```http
Retry-After: 3600
X-Correlation-ID: a1b2c3d4e5f67890
```

---

## Migration Notes

**Dual-mode authentication** allows gradual migration from Bearer tokens to HttpOnly cookies:
- Old clients continue using Bearer tokens
- New clients benefit from cookie security
- Both methods work simultaneously

**Migration Path:**
1. Deploy cookie support (done in June 2026)
2. Update frontend to use cookies
3. Eventually deprecate Bearer token support

See [`COOKIE_AUTH_MIGRATION.md`](../../backend/WovenBackend/COOKIE_AUTH_MIGRATION.md) for full migration details.

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/AuthEndpoints.cs`
- `backend/WovenBackend/Auth/CookieAuthHelper.cs`
- `backend/WovenBackend/Endpoints/EndpointHelper.cs`
- `backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs`
