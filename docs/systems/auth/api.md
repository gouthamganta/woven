# Authentication API Reference

**Last Updated:** 2026-10-07

---

## Endpoints

### POST /auth/google

Authenticate a user with a Google ID token.

**Request:**
```json
{
  "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6IjE4MmU..."
}
```

**Response (200 OK):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "userId": 42,
  "isNewUser": false
}
```

**Implementation:**
- Validates Google ID token with Google's public keys
- Creates user if first-time sign-in
- Issues Woven JWT token (60-minute expiry)
- Sets HttpOnly cookie (dual-mode auth)
- Returns both Bearer token (for mobile) and sets cookie (for web)

**Cookie:**
```
Set-Cookie: woven-auth=<jwt>; HttpOnly; Secure; SameSite=Strict; Max-Age=3600
```

**Source:** `backend/WovenBackend/Endpoints/AuthEndpoints.cs`

---

## Development Endpoints (Local Only)

These endpoints are registered ONLY in Development mode:

### POST /debug/dev-login

Issues a token for any user ID without Google sign-in.

**Request:**
```json
{
  "userId": 1
}
```

**Response:** Same as `/auth/google`

**Protection:**
- `#if DEBUG` compile-time guard
- `IsDevelopment()` runtime check
- Never registered in production builds

### GET /debug/admin-token

Issues an admin-role token.

**Response:** JWT with "Admin" role claim

**Source:** `backend/WovenBackend/Endpoints/DevAuthEndpoints.cs`

---

## Authentication Flow

**Web (Cookie-based):**
1. User clicks "Sign in with Google"
2. Frontend gets Google ID token
3. POST /auth/google with ID token
4. Backend validates, sets cookie, returns JWT
5. Browser stores cookie automatically
6. All requests include cookie (HttpOnly, XSS-resistant)

**Mobile (Bearer token):**
1. Same sign-in flow
2. App stores JWT in secure storage
3. Includes `Authorization: Bearer <token>` on requests

**Dual-mode support:**
- Middleware checks cookie first, then Authorization header
- Same JWT validation logic for both

---

## Security

**JWT Claims:**
```json
{
  "uid": "42",
  "sub": "user@example.com",
  "role": "User",
  "iat": 1696704000,
  "exp": 1696707600
}
```

**Token Lifetime:** 60 minutes

**Validation:**
- Signature verification (HMAC-SHA256)
- Expiry check (with 1-minute clock skew tolerance)
- Issuer/Audience validation

**Cookie Security:**
- HttpOnly (JavaScript cannot access)
- Secure (HTTPS only in production)
- SameSite=Strict (CSRF protection)

---

## Error Responses

**401 Unauthorized:**
```json
{
  "error": "Invalid Google ID token",
  "correlationId": "a1b2c3d4e5f6g7h8"
}
```

**500 Internal Server Error:**
```json
{
  "error": "Authentication failed",
  "correlationId": "a1b2c3d4e5f6g7h8"
}
```

---

## Related Documentation

- [JWT Token Structure](jwt.md)
- [Google OAuth Integration](google-oauth.md)
- [Cookie Auth Implementation](cookie-auth.md)
- [Session Management](session-management.md)
- [Security Considerations](security.md)
