# PII Sanitization

**System:** Security / Privacy  
**Related:** [Encryption](../encryption/pii-sanitizer.md) | [Analytics Privacy](../analytics/privacy.md)

---

## Overview

PII (Personally Identifiable Information) is sanitized in logs, analytics events, and error reporting to prevent leakage.

---

## What Gets Sanitized

**Always removed:**
- Email addresses
- Phone numbers
- Full names (in logs)
- IP addresses (hashed in analytics)
- OAuth tokens
- JWT tokens

**Hashed (irreversible):**
- User IDs in analytics events
- Device identifiers

**Never logged:**
- Passwords (plain or hashed)
- Encryption keys
- Session tokens

---

## Implementation

**Log sanitization:** Serilog enrichers strip PII patterns before writing

**Analytics events:** `AnalyticsEvent.UserIdHash` stores SHA-256(userId + salt), not raw userId

**Evidence:** [AnalyticsEvent.cs](../../../backend/WovenBackend/data/Entities/AnalyticsEvent.cs)

**Error reporting:** Correlation IDs used instead of user context in external error trackers

---

## PII in Database

**Encrypted at rest:**
- `User.FullName` — AES-256-GCM
- `User.Email` — AES-256-GCM
- `UserIntent.EncryptedIntent` — end-to-end encrypted, server-blind

**Evidence:** [EncryptedStringConverter.cs](../../../backend/WovenBackend/data/Converters/EncryptedStringConverter.cs)

---

## Related

- [Encryption Implementation](../encryption/implementation.md)
- [Analytics Privacy](../analytics/privacy.md)
- [Security Audit](../../security/security-audit.md)
- [PII Data Protection](../../security/pii.md)
