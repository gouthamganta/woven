# HttpOnly Cookie Authentication Migration

## Current State ✅

The backend now supports **dual-mode authentication**:
1. **Traditional**: JWT in `Authorization: Bearer <token>` header (from localStorage)
2. **Secure**: JWT in `woven_access_token` httpOnly cookie

Both methods work simultaneously, allowing gradual frontend migration with zero downtime.

## What Changed (Backend)

### Files Created
- `Auth/CookieAuthHelper.cs` - Cookie management utilities
- `COOKIE_AUTH_MIGRATION.md` - This file

### Files Modified
- `Program.cs` - JWT middleware now reads from cookies (fallback after SignalR query param)
- `Endpoints/AuthEndpoints.cs` - Sets httpOnly cookie on login + new `/auth/logout` endpoint
- `Endpoints/DevAuthEndpoints.cs` - Sets httpOnly cookie on dev login

### Cookie Configuration
```csharp
HttpOnly = true          // Prevents JavaScript access (XSS protection)
Secure = true            // HTTPS only
SameSite = Strict        // CSRF protection
Path = "/"               // Available to all routes
Domain = null            // Same domain only
Expires = 60 min         // Matches JWT expiry
```

## Security Benefits

### ❌ **localStorage (Current Frontend)**
- Accessible via JavaScript (`localStorage.getItem`)
- Vulnerable to XSS attacks (malicious scripts can steal tokens)
- Persists across tabs/windows
- No automatic expiry

### ✅ **httpOnly Cookies (New Backend Support)**
- **NOT** accessible via JavaScript
- XSS-resistant (stolen script cannot read the token)
- Automatically sent with every request
- Browser-managed expiry
- SameSite protection prevents CSRF

## Frontend Migration Path

### Phase 1: ✅ DONE - Dual Mode (Current)
Backend supports both methods. No frontend changes yet.

**Frontend still uses localStorage:**
```typescript
// login.component.ts
localStorage.setItem('accessToken', response.accessToken);
```

**Backend accepts both:**
- Bearer token from Authorization header
- Cookie from `woven_access_token`

### Phase 2: Remove localStorage (Future)

When ready to migrate frontend:

1. **Remove manual token storage**
```typescript
// ❌ DELETE THIS:
localStorage.setItem('accessToken', response.accessToken);
localStorage.getItem('accessToken');
localStorage.removeItem('accessToken');
```

2. **Update HTTP interceptor**
```typescript
// auth.interceptor.ts
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // ❌ DELETE: Authorization header injection
  // The cookie is automatically sent by the browser
  
  // ✅ ADD: Ensure credentials are sent
  const clonedReq = req.clone({
    withCredentials: true  // Send cookies with cross-origin requests
  });
  
  return next(clonedReq);
};
```

3. **Update HTTP service calls**
```typescript
// Ensure all HTTP calls include credentials
this.http.get('/api/...', { withCredentials: true })
this.http.post('/api/...', body, { withCredentials: true })
```

4. **Update logout**
```typescript
// logout() method
async logout() {
  await firstValueFrom(this.http.post('/auth/logout', {}, { withCredentials: true }));
  this.router.navigate(['/login']);
}
```

5. **Update CORS configuration** (if frontend is on different domain)
```csharp
// backend/Program.cs
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4202")
              .AllowCredentials()        // Required for cookies
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
```

### Phase 3: Remove Backward Compatibility (Future)

Once frontend migration is complete and tested:

1. **Remove JSON token from response**
```csharp
// Endpoints/AuthEndpoints.cs
return Results.Ok(new
{
    // ❌ DELETE: accessToken,
    user = new { ... }
});
```

2. **Remove localStorage comments from frontend**

3. **Remove Bearer token support from middleware** (optional - can keep for API clients)

## Testing

### Test Cookie Auth Works
```bash
# 1. Login and capture cookie
curl -X POST http://localhost:5135/auth/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"..."}' \
  -c cookies.txt

# 2. Use cookie for authenticated request
curl http://localhost:5135/moments \
  -b cookies.txt

# 3. Logout
curl -X POST http://localhost:5135/auth/logout \
  -b cookies.txt \
  -c cookies.txt
```

### Test localStorage Still Works
```typescript
// Should still work without any changes
localStorage.setItem('accessToken', token);
// Authorization header is still accepted
```

## Security Considerations

### Current Security Posture
- ✅ Secrets moved to Key Vault / User Secrets
- ✅ HttpOnly cookies available (XSS protection)
- ⚠️ Frontend still uses localStorage (XSS vulnerable)
- ✅ HTTPS enforced in production (Secure cookie)
- ✅ SameSite=Strict (CSRF protection)

### Recommended Timeline
1. **Immediate**: Backend changes deployed ✅
2. **Next Sprint**: Migrate frontend to cookies
3. **Following Sprint**: Remove localStorage + backward compat

### Known Limitations
- Cookie-based auth doesn't work well with native mobile apps
- If building iOS/Android app, keep Bearer token support
- Web app should use cookies exclusively

## Rollback Plan

If issues arise:
1. Backend still supports Bearer tokens - frontend can keep using localStorage
2. Cookies are additive, not breaking
3. No migration forced - both methods work indefinitely

## References
- [OWASP JWT Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html)
- [httpOnly Cookie Best Practices](https://owasp.org/www-community/HttpOnly)
- [SameSite Cookie Attribute](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Set-Cookie/SameSite)
