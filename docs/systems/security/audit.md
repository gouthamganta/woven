# Security Audit

**System:** Security / Compliance  
**Related:** [Security README](../../security/README.md) | [Security Audit](../../security/security-audit.md)

---

## Overview

Security audit logging tracks sensitive operations, access patterns, and anomalies for compliance and incident response.

---

## What Gets Audited

**Access events:**
- Admin actions (user suspensions, moderation overrides)
- Encryption key access
- PII field reads (full name, email)
- Authentication failures

**Data changes:**
- Profile updates
- Photo uploads/deletions
- Account closures
- Trust score adjustments

**Security events:**
- Failed login attempts
- Blocked IPs
- Reported users
- Prompt injection attempts

---

## Audit Log Storage

**Table:** `security_audit_logs`

**Retention:** 90 days (configurable)

**Fields:**
- `event_type` — action category
- `user_id` — who performed action
- `target_user_id` — affected user (if applicable)
- `details_json` — structured event data
- `ip_address_hash` — hashed IP
- `occurred_at` — timestamp

**Evidence:** [SecurityAuditLog.cs](../../../backend/WovenBackend/data/Entities/SecurityAuditLog.cs)

---

## Related

- [Security Audit Report](../../security/security-audit.md)
- [Incident Response](../../security/incident-response.md)
- [PII Protection](../../security/pii.md)
