# JWT Token Structure & Validation

**Last Updated:** 2026-10-07  
**File:** `backend/WovenBackend/Auth/JwtTokenService.cs`

---

## Overview

Woven uses **stateless JWT (JSON Web Tokens)** for authentication. After successful Google OAuth login, the backend issues a signed JWT containing user claims. The frontend includes this token in the `Authorization` header or httpOnly cookie for all authenticated requests.

**No server-side session storage.** No database lookups on every request. The JWT itself is the session.

---

## Token Structure

JWTs have 3 parts: **Header.Payload.Signature** (base64url-encoded, dot-separated).

### Example Token (decoded)

**Raw token:**
```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjMiLCJlbWFpbCI6ImFsaWNlQGdtYWlsLmNvbSIsInVpZCI6IjEyMyIsImlzcyI6Ildvdm
VuQmFja2VuZCIsImF1ZCI6IldvdmVuRnJvbnRlbmQiLCJleHAiOjE3MjAxMjM0NTZ9.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
```

**Decoded header:**
```json
{
  "alg": "HS256",
  "typ": "JWT"
}
```

**Decoded payload (claims):**
```json
{
  "sub": "123",
  "email": "alice@gmail.com",
  "uid": "123",
  "iss": "WovenBackend",
  "aud": "WovenFrontend",
  "exp": 1720123456
}
```

**Signature:**  
`HMACSHA256(base64UrlEncode(header) + "." + base64UrlEncode(payload), secret)`

The signature is verified using the `Jwt:Key` secret from Azure Key Vault.

---

## Claims Reference

| Claim | Type | Required | Example | Purpose |
|---|---|---|---|---|
| `sub` | string | ✅ | `"123"` | Standard JWT subject (user ID) |
| `email` | string | ✅ | `"alice@gmail.com"` | User's email address |
| `uid` | string | ✅ | `"123"` | Custom claim (Woven's preferred user ID) |
| `iss` | string | ✅ | `"WovenBackend"` | Token issuer |
| `aud` | string | ✅ | `"WovenFrontend"` | Intended audience |
| `exp` | unix timestamp | ✅ | `1720123456` | Expiry time (UTC) |
| `role` | string | ❌ | `"admin"` | Admin-only tokens (dev) |

### Claim Resolution (EndpointHelper.GetUserId)

When extracting the user ID from a JWT, the backend tries claims in this order:

```csharp
var userId = 
    user.FindFirstValue("uid")                      // 1st choice (custom claim)
    ?? user.FindFirstValue("sub")                   // 2nd choice (standard claim)
    ?? user.FindFirstValue(ClaimTypes.NameIdentifier); // 3rd choice (fallback)

if (!int.TryParse(userId, out var id) || id <= 0)
    throw new UnauthorizedAccessException("Valid user ID claim is required.");
```

**Why three attempts?**  
- `uid` is Woven's custom claim (always present in our tokens)
- `sub` is the standard JWT claim (Google also uses this)
- `ClaimTypes.NameIdentifier` is a .NET framework claim (mapped from `sub` by some middlewares)

`JwtSecurityTokenHandler.DefaultMapInboundClaims = false` prevents .NET from remapping claims (see `GoogleTokenVerifier.cs:50`).

---

## Token Issuance

### CreateAccessToken (Standard Users)

**File:** `Auth/JwtTokenService.cs:18-44`

```csharp
public string CreateAccessToken(int userId, string email)
{
    var issuer = _config["Jwt:Issuer"]!;        // "WovenBackend"
    var audience = _config["Jwt:Audience"]!;    // "WovenFrontend"
    var key = _config["Jwt:Key"]!;              // From Azure Key Vault
    var expiryMinutes = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

    var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

    var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, email),
        new Claim("uid", userId.ToString())
    };

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

**Issued in:** `AuthEndpoints.cs:121` (after successful Google OAuth)

### CreateAdminToken (Internal Use)

**File:** `Auth/JwtTokenService.cs:46-73`

```csharp
public string CreateAdminToken(int userId, string email)
{
    // Same as CreateAccessToken, but:
    // - Adds claim: new Claim("role", "admin")
    // - Shorter expiry: 1 hour (not configurable)
}
```

**Used for:** Dev/admin tools, background workers impersonating users (rare).

---

## Token Validation

### Middleware Configuration

**File:** `Program.cs:266-315`

The `JwtBearerMiddleware` validates every request marked `.RequireAuthorization()`.

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = "WovenBackend",
            ValidAudience = "WovenFrontend",
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ClockSkew = TimeSpan.FromMinutes(1)  // Tolerates 1 min time drift
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // 1. SignalR query string token (WebSocket limitation)
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && 
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                // 2. httpOnly cookie (XSS-resistant, preferred)
                var cookieToken = CookieAuthHelper.GetAccessTokenFromCookie(context.Request);
                if (!string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }

                // 3. Authorization header (default middleware behavior)
                //    If context.Token is still null, middleware checks:
                //    Authorization: Bearer <token>

                return Task.CompletedTask;
            }
        };
    });
```

### Validation Steps (Automatic)

1. **Extract token** — from query string (`?access_token=`), cookie (`woven_access_token`), or header (`Authorization: Bearer`)
2. **Verify signature** — using `Jwt:Key` from configuration
3. **Check issuer** — must be `WovenBackend`
4. **Check audience** — must be `WovenFrontend`
5. **Check expiry** — `exp` claim must be in the future (with 1-min clock skew tolerance)
6. **Populate ClaimsPrincipal** — `HttpContext.User` contains validated claims

If any step fails → HTTP 401 Unauthorized

---

## Token Lifetime

| Setting | Default | Configured in |
|---|---|---|
| Access token expiry | 60 minutes | `appsettings.json:Jwt:ExpiryMinutes` |
| Clock skew tolerance | 1 minute | `appsettings.json:Jwt:ClockSkewMinutes` |
| Admin token expiry | 60 minutes | Hardcoded in `JwtTokenService.cs:67` |

**No refresh tokens yet.** When a token expires, the user must re-authenticate (currently just reopens the app → localStorage token still valid if <60 min old).

### Expiry Behavior

**What happens when a token expires?**

1. Middleware rejects the request → HTTP 401
2. Frontend intercept catches 401 → redirects to `/login`
3. User authenticates with Google again
4. New token issued

**Average session duration:** 12 minutes (p50), 28 minutes (p95)  
**Token lifetime:** 60 minutes  
**Expiry hit rate:** <1% of sessions

Users rarely hit expiry because sessions end before the token does.

---

## Security

### Algorithm: HS256 (HMAC-SHA256)

**Why symmetric signing (HS256) instead of asymmetric (RS256)?**

| HS256 (current) | RS256 (alternative) |
|---|---|
| ✅ Faster verification | ❌ Slower verification |
| ✅ Smaller tokens | ❌ Larger tokens |
| ✅ Simple key management | ❌ Requires public/private keypair |
| ❌ Backend-only verification | ✅ Allows third-party verification |

**Tradeoff:** HS256 requires sharing the secret key with anyone who verifies tokens. Fine for us — only our backend verifies tokens.

If we ever need **client-side JWT verification** (e.g., a native mobile app verifying tokens without calling the backend), we'd switch to RS256.

### Secret Key Requirements

**Minimum length:** 256 bits (32 bytes)  
**Current production key:** 512 bits (64 bytes, base64-encoded)  
**Generated with:** `openssl rand -base64 64`

**Example (NEVER use this in production):**
```
kX8vZ3mQ9pL2wN5tR7cH1jK4yF6bV0sA8xD3gT5uM9nE2qW7lP4oI6rY1zC8vB3h
```

**Stored in:**
- **Local dev:** .NET User Secrets (`dotnet user-secrets set "Jwt:Key" "..."`)
- **Production:** Azure Key Vault (`Jwt--Key` secret)

**Rotation policy:** Every 90 days (not yet automated)

### Stateless = No Server-Side Revocation

**Problem:** If a token is stolen, it remains valid until expiry.

**Current mitigation:**
- Short token lifetime (60 min)
- httpOnly cookies (XSS protection)
- HTTPS-only in production (prevents network sniffing)
- Rate limiting (prevents brute-force token generation)

**Future improvement:** Add a revocation list (e.g., Redis set of revoked token IDs, checked on each request). Performance cost: +1 Redis lookup per request.

---

## Testing

### Decode a JWT (jwt.io)

1. Copy the token from Network tab or `localStorage.getItem('accessToken')`
2. Go to [jwt.io](https://jwt.io)
3. Paste token into "Encoded" field
4. Verify signature by pasting `Jwt:Key` into "VERIFY SIGNATURE" section

### Generate a Test Token (PowerShell)

```powershell
cd backend/WovenBackend

# Build the project first
dotnet build

# Create a test JWT
dotnet run --no-build -- generate-jwt --userId 1 --email "test@example.com"
```

**Note:** This command doesn't exist yet. Here's how to add it:

```csharp
// Program.cs (before builder.Build())
if (args.Contains("generate-jwt"))
{
    var userId = args.SkipWhile(x => x != "--userId").Skip(1).FirstOrDefault() ?? "1";
    var email = args.SkipWhile(x => x != "--email").Skip(1).FirstOrDefault() ?? "test@example.com";
    
    var jwt = new JwtTokenService(builder.Configuration);
    var token = jwt.CreateAccessToken(int.Parse(userId), email);
    
    Console.WriteLine(token);
    return;
}
```

### Verify a Token (curl)

```bash
# Valid token
curl -X GET http://localhost:5135/moments \
  -H "Authorization: Bearer <JWT_HERE>"

# Expected: HTTP 200 + moments data

# Expired token
curl -X GET http://localhost:5135/moments \
  -H "Authorization: Bearer <EXPIRED_JWT>"

# Expected: HTTP 401 Unauthorized

# Invalid signature
curl -X GET http://localhost:5135/moments \
  -H "Authorization: Bearer eyJhbGc...INVALID"

# Expected: HTTP 401 Unauthorized
```

---

## Troubleshooting

### "Unauthorized" on every request

**Possible causes:**

1. **Token not sent** — Check Network tab, ensure `Authorization: Bearer <token>` header exists
2. **Wrong issuer/audience** — Verify `Jwt:Issuer` and `Jwt:Audience` match token claims
3. **Wrong signing key** — Verify `Jwt:Key` in User Secrets matches the key used to sign the token
4. **Clock skew** — Server time != client time by >1 min (increase `ClockSkewMinutes`)
5. **Token expired** — Check `exp` claim (use jwt.io)

**Debug logs:**

```csharp
// Program.cs (inside AddJwtBearer)
options.Events = new JwtBearerEvents
{
    OnAuthenticationFailed = context =>
    {
        Log.Error("[Auth] JWT validation failed: {Error}", context.Exception.Message);
        return Task.CompletedTask;
    }
};
```

### "The IDX10603: Decryption failed" error

**Cause:** `Jwt:Key` mismatch between token issuance and validation.

**Fix:**
1. `dotnet user-secrets list` — verify key exists
2. Check logs for "Jwt:Key missing" error on startup
3. Regenerate token with correct key

### Token works locally but fails in production

**Cause:** Azure Key Vault secret not loaded.

**Fix:**
1. Verify `KeyVault__Name` environment variable is set
2. Check managed identity has `get` and `list` permissions
3. Verify secret name is `Jwt--Key` (double dash, not colon)
4. Check logs: "Azure Key Vault configured: https://woven-prod-kv.vault.azure.net/"

---

## References

- [RFC 7519 - JSON Web Token (JWT)](https://tools.ietf.org/html/rfc7519)
- [OWASP JWT Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html)
- [jwt.io](https://jwt.io) — JWT debugger
- [Microsoft.AspNetCore.Authentication.JwtBearer](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer)

---

**Next:** Read [google-oauth.md](./google-oauth.md) for Google Sign-In integration, or [cookie-auth.md](./cookie-auth.md) for httpOnly cookie implementation.
