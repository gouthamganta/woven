# Authentication Security

**Consolidated from:** `docs/technical/SECURITY.md`, `docs/technical/ENCRYPTION_SECURITY_DESIGN.md`

---

## Overview

Woven uses Google OAuth 2.0 for authentication with dual-mode JWT delivery (HttpOnly cookies + Bearer tokens). No passwords are stored.

---

## Authentication Flow

**Sequence:**
```
1. User clicks "Sign in with Google"
2. Frontend receives Google ID token
3. POST /auth/google with idToken
4. Backend validates token with Google
5. Backend creates/updates user + AuthIdentity
6. Backend issues Woven JWT (30-day expiry)
7. Backend sets HttpOnly cookie + returns JSON with token
8. Client uses cookie (web) or Bearer token (mobile)
```

**Source:** [`backend/WovenBackend/Endpoints/AuthEndpoints.cs:17-143`](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs)

---

## Google OAuth Validation

**Server-side token validation:**
```csharp
var googleUser = await googleVerifier.VerifyAsync(req.IdToken, ct);
```

**Verified fields:**
- Token signature (RSA with Google's public keys)
- Audience (`aud` claim matches our client ID)
- Expiry (`exp` claim)
- Issuer (`iss` is `accounts.google.com`)

**Config:**
```json
{
  "GoogleAuth": {
    "ClientId": "211033152902-umjjk9n5mqd02s97skerf9sn383m0v00.apps.googleusercontent.com"
  }
}
```

**Source:** `backend/WovenBackend/Services/GoogleTokenVerifier.cs`

---

## JWT Structure

**Algorithm:** HS256 (HMAC with SHA-256)  
**Expiry:** 30 days (`Jwt:ExpiryMinutes = 43200`)  
**Issuer:** `woven-api`

**Claims:**
```json
{
  "uid": "123",
  "email": "user@example.com",
  "exp": 1730000000,
  "iat": 1727408000,
  "iss": "woven-api"
}
```

**Signing Key:**
- Stored in Azure Key Vault (production)
- User Secrets (local development)
- Minimum 32 characters required

---

## Dual-Mode Delivery

### HttpOnly Cookie (Recommended)

**Advantages:**
- XSS-resistant (JavaScript cannot access)
- CSRF protection via SameSite=Strict
- Automatic inclusion in requests

**Cookie configuration:**
```csharp
response.Cookies.Append("woven_access_token", token, new CookieOptions
{
    HttpOnly = true,        // XSS protection
    Secure = true,          // HTTPS only
    SameSite = SameSiteMode.Strict,  // CSRF protection
    MaxAge = TimeSpan.FromDays(30),
    Path = "/"
});
```

**Source:** [`backend/WovenBackend/Auth/CookieAuthHelper.cs`](../../backend/WovenBackend/Auth/CookieAuthHelper.cs)

---

### Bearer Token (Backward Compatible)

**Usage:**
```http
GET /moments HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

**When to use:**
- Mobile apps (cookies not supported)
- Third-party API clients
- Legacy integrations

---

## JWT Middleware

**Token extraction order:**
1. Check `woven_access_token` cookie
2. Check `Authorization: Bearer` header
3. Reject if neither present

**Implementation:**
```csharp
// JWT Bearer middleware configuration
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "woven-api",
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
        };
        
        // Check cookie first, then header
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Cookies["woven_access_token"];
                if (!string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });
```

**Source:** `backend/WovenBackend/Program.cs` (JWT configuration)

---

## User ID Extraction

**Always use EndpointHelper.GetUserId():**
```csharp
var userId = EndpointHelper.GetUserId(http.User);
```

**Never parse claims directly:**
```csharp
// ❌ Bad
var uid = http.User.FindFirstValue("uid");
var userId = int.Parse(uid);

// ✅ Good
var userId = EndpointHelper.GetUserId(http.User);
```

**Implementation:**
```csharp
public static int GetUserId(ClaimsPrincipal user)
{
    var uid = user.FindFirstValue("uid");
    if (int.TryParse(uid, out var id)) return id;

    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (int.TryParse(sub, out id)) return id;

    throw new UnauthorizedAccessException("Missing user id claim");
}
```

**Throws UnauthorizedAccessException** if claim invalid/missing → mapped to HTTP 401 by `AuthExceptionHandler`.

**Source:** [`backend/WovenBackend/Endpoints/EndpointHelper.cs:13-21`](../../backend/WovenBackend/Endpoints/EndpointHelper.cs)

---

## Endpoint Authorization

**All endpoints require authorization by default:**
```csharp
group.RequireAuthorization();
```

**Public endpoints (exceptions):**
- `GET /health` — Load balancer health check
- `POST /auth/google` — Authentication initiation
- `POST /auth/logout` — Cookie cleanup
- `GET /push-notifications/vapid-public-key` — VAPID key for Web Push

**Source:** All files in `backend/WovenBackend/Endpoints/`

---

## Rate Limiting

**Auth endpoint limit:** 20 requests per IP per calendar day

**Implementation:**
```csharp
var ipHash = PiiSanitizer.HashForAudit(ip, "rl-auth-v1");
var rlKey = $"rl:auth:{ipHash}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);

if (!allowed)
{
    http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

**Source:** [`backend/WovenBackend/Endpoints/AuthEndpoints.cs:30-38`](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs)

---

## Trust Signals

**Background trust checks (fire-and-forget):**
```csharp
_ = Task.Run(async () =>
{
    try
    {
        await trust.CheckDeviceFingerprintAsync(user.Id, req.DeviceFingerprint);
        await trust.CheckVelocityAsync(user.Id);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "[Auth] Trust check failed for user {UserId}", user.Id);
    }
});
```

**Signals tracked:**
- Device fingerprint (detects account sharing)
- Login velocity (detects suspicious patterns)
- IP geolocation (planned)

**Source:** [`backend/WovenBackend/Endpoints/AuthEndpoints.cs:108-119`](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs)

---

## Migration Path

**Current state:** Dual-mode authentication (cookies + Bearer tokens)

**Migration steps:**
1. ✅ Deploy cookie support (done June 2026)
2. ✅ Update frontend to use cookies
3. ⏳ Monitor adoption
4. ⏳ Deprecate Bearer token support
5. ⏳ Remove Bearer middleware

**See:** [`backend/WovenBackend/COOKIE_AUTH_MIGRATION.md`](../../backend/WovenBackend/COOKIE_AUTH_MIGRATION.md)

---

## Security Best Practices

1. **Never trust client-provided user IDs** — Always extract from JWT
2. **Never log JWT tokens** — Use correlation IDs for tracing
3. **Rotate signing keys periodically** — Planned via Azure Key Vault
4. **Use HTTPS only** — Cookies marked `Secure`
5. **Set appropriate expiry** — 30 days balances UX and security
6. **Monitor failed auth attempts** — Alert on > 10/minute from same IP

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/AuthEndpoints.cs`
- `backend/WovenBackend/Auth/CookieAuthHelper.cs`
- `backend/WovenBackend/Endpoints/EndpointHelper.cs`
- `backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs`
