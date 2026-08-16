# Woven — Encryption & Security Design

---

## 1. PII Encryption at Rest

Woven uses AES-256-GCM for all PII fields. Encryption and decryption are handled exclusively by `EncryptionService` / `IEncryptionService`. Fields are encrypted at write time and decrypted at read time — no plaintext PII touches the database.

### Encrypted Fields

| Table | Column | Notes |
|---|---|---|
| `users` | `email` | Encrypted on registration and every update |
| `users` | `full_name` | Encrypted on registration and every update |
| `user_profiles` | `city` | Encrypted at profile creation/update |
| `user_profiles` | `state` | Encrypted at profile creation/update |
| `user_intents` | `reflection_sentence` | Encrypted at write |
| `user_optional_fields` | `value` | Encrypted at write (covers all optional freetext fields) |

### Fields Intentionally NOT Encrypted

| Table | Column | Reason |
|---|---|---|
| `chat_messages` | `body` | A `CHECK` constraint enforces 1–1000 character length. AES-256-GCM ciphertext is longer than 1000 chars for any non-trivial message — applying encryption would violate the constraint. This is documented in `WovenDbContext` comments as a known gap. See `TECHNICAL_DEBT_AND_IMPROVEMENTS.md`. |

---

## 2. Key Management

- **Algorithm**: AES-256-GCM. Each encryption operation uses a fresh random nonce; the nonce is stored alongside the ciphertext.
- **Master key**: stored as the Terraform variable `encryption_master_key`, injected into Container Apps as a secret. Never checked into source control.
- **`KeyRotationWorker`**: a scheduled background worker that performs periodic encryption key rotation. Existing ciphertext is re-encrypted with the new key during rotation.
- **`EncryptionService`** / **`IEncryptionService`**: the single point of entry for all encrypt/decrypt calls throughout the application. No service calls `AesGcm` directly.

---

## 3. Prompt Injection Protection

Two layers prevent malicious profile text from influencing OpenAI prompts:

### Layer 1 — PiiSanitizer (all OpenAI calls)

Applied before every call to OpenAI across all services:

- **Email pattern stripping**: regex removes anything matching an email address format.
- **Phone pattern stripping**: regex removes phone number patterns.
- **Truncation**: input is truncated to 200 characters regardless of length.

`PiiSanitizer` is called by `AiProfileService`, `KnowMeAgent`, `RedGreenFlagAgent`, `OpenAiTaggingService`, and `MatchExplanationService` before constructing any prompt.

### Layer 2 — AiProfileService injection detection (profile scoring)

`AiProfileService` applies 8 regex patterns specifically designed to detect prompt injection attempts in user profile text (e.g., text containing "ignore previous instructions", role-play directives, or system prompt overrides). Profiles that trigger these patterns are handled before any text reaches the OpenAI API.

---

## 4. Security Audit Logging

`SecurityAuditService` / `ISecurityAuditService` writes structured records to `SecurityAuditLog` for 7 security event types (exact event type names are defined in the service). All audit records include a timestamp, user ID, event type, and relevant metadata.

`SecurityAuditCleanupWorker` runs on a scheduled basis and prunes `SecurityAuditLog` records older than the configured retention window, preventing unbounded table growth.

---

## 5. Authentication

### Google OAuth

- Users authenticate via Google Sign-In. The frontend receives a Google ID token and sends it to the backend.
- The backend validates the ID token server-side (signature, audience, expiry) before issuing a Woven JWT.
- Config key: `GoogleAuth:ClientId = 211033152902-umjjk9n5mqd02s97skerf9sn383m0v00.apps.googleusercontent.com`

### JWT Storage

- Woven JWTs are stored in **localStorage** on the client.
- This is a documented dev convenience — localStorage is accessible to JavaScript and is vulnerable to XSS.
- **Known gap**: before production, JWTs should be moved to httpOnly cookies. See `TECHNICAL_DEBT_AND_IMPROVEMENTS.md`.

### Endpoint Authorization

All Minimal API endpoints call `.RequireAuthorization()` unless they are explicitly public. User ID is extracted via the `GetUserId(http.User)` helper at the top of each endpoint file — never from request body parameters.

---

## 6. Container Security

| Container | Non-root user |
|---|---|
| Backend (.NET 10) | `appuser` |
| Frontend (nginx) | `nginxuser` |

Both Dockerfiles create and switch to these non-root users. The application process never runs as root inside the container.

---

## 7. CI/CD Security

- **OIDC authentication**: GitHub Actions uses `azure/login@v2` with OpenID Connect. No service principal client secrets are stored in the repository. Azure credentials are obtained via the OIDC token exchange at runtime.
- **No secrets in source**: all secrets (`OpenAI:ApiKey`, `encryption_master_key`, `replicate_api_token`, `google_places_api_key`, etc.) are Terraform variables injected as Azure Container Apps secrets at deployment time. They do not appear in `appsettings.json` or any checked-in file.

---

## 8. Content Moderation

`ModerationService` provides content moderation for user-generated content (profile text, tile captions, etc.) using the OpenAI moderation endpoint.

`ModerationWorker` processes the moderation queue asynchronously.

**Current state**: `IsModerationEnabled = false` in the development environment. Moderation is wired but disabled locally to avoid spurious blocks during development. It must be enabled before production deployment.

---

## Security Checklist Summary

| Control | Status |
|---|---|
| AES-256-GCM for PII fields | Implemented |
| Encryption key rotation | Implemented (`KeyRotationWorker`) |
| Prompt injection protection (PiiSanitizer) | Implemented — all OpenAI calls |
| Prompt injection detection (AiProfileService) | Implemented — 8 regex patterns |
| Security audit logging | Implemented — 7 event types |
| Google OAuth ID token validation | Implemented |
| JWT auth on all endpoints | Implemented (`.RequireAuthorization()`) |
| Non-root container users | Implemented |
| OIDC CI/CD (no stored secrets) | Implemented |
| Content moderation | Wired, disabled in dev |
| chat_messages.body encryption | **Not implemented** — CHECK constraint conflict |
| JWT in httpOnly cookie | **Not implemented** — using localStorage (dev gap) |
| Service worker + Web Push | **Not implemented** — VAPID configured, worker not deployed |
