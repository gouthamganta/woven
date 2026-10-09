# Security Audit Log

**Consolidated from:** `docs/technical/ENCRYPTION_SECURITY_DESIGN.md`

---

## Overview

`SecurityAuditService` writes structured audit records to `SecurityAuditLog` for security-relevant events. Records include timestamp, user ID, event type, and metadata.

---

## Audit Event Types

**7 security event types tracked:**

| Event Type | Trigger | Metadata |
|---|---|---|
| `LoginSuccess` | User logs in via Google OAuth | IP, device fingerprint |
| `LoginFailure` | Google token validation fails | Reason, IP |
| `PasswordReset` | N/A (no passwords) | — |
| `AccountDeletion` | User deletes account | Reason (if provided) |
| `SuspiciousActivity` | Trust scoring flags behavior | Trust score, pattern |
| `DataExport` | User exports their data | Export size, format |
| `PermissionChange` | Admin modifies user permissions | Old role, new role |

**Source:** `backend/WovenBackend/Services/Security/SecurityAuditService.cs`

---

## Audit Log Schema

**Table:** `security_audit_logs`

**Columns:**
- `id` — Primary key (bigint)
- `user_id` — Foreign key to users (nullable for system events)
- `event_type` — Enum (one of 7 types)
- `metadata_json` — JSON metadata (structured per event type)
- `ip_address_hash` — Hashed IP (PII-sanitized)
- `created_at` — Event timestamp (UTC)
- `correlation_id` — Request tracking ID

**Indexes:**
- `idx_audit_user_time` on `(user_id, created_at DESC)`
- `idx_audit_event_time` on `(event_type, created_at DESC)`

**Source:** `backend/WovenBackend/Data/Entities/SecurityAuditLog.cs`

---

## Recording Audit Events

**Interface:**
```csharp
public interface ISecurityAuditService
{
    Task RecordAsync(
        int? userId,
        SecurityEventType eventType,
        string? metadataJson,
        string? ipAddress,
        CancellationToken ct = default);
}
```

**Example usage:**
```csharp
await _securityAudit.RecordAsync(
    userId: user.Id,
    eventType: SecurityEventType.LoginSuccess,
    metadataJson: JsonSerializer.Serialize(new
    {
        provider = "google",
        deviceFingerprint = req.DeviceFingerprint
    }),
    ipAddress: http.Connection.RemoteIpAddress?.ToString(),
    ct: ct
);
```

**Source:** `backend/WovenBackend/Endpoints/AuthEndpoints.cs` (login events)

---

## Metadata Examples

### LoginSuccess

```json
{
  "provider": "google",
  "deviceFingerprint": "abc123",
  "trustScore": 0.85
}
```

---

### SuspiciousActivity

```json
{
  "pattern": "velocity",
  "trustScore": 0.45,
  "reason": "5 logins from different IPs in 10 minutes"
}
```

---

### DataExport

```json
{
  "exportSize": 1024576,
  "format": "json",
  "includeMessages": true
}
```

---

### AccountDeletion

```json
{
  "reason": "user_initiated",
  "retentionPeriod": 30
}
```

---

## IP Address Hashing

**PII protection:**
```csharp
var ipHash = PiiSanitizer.HashForAudit(ipAddress, "audit-v1");
```

**Implementation:**
- SHA-256 with salt
- Irreversible (cannot recover IP)
- Allows correlation (same IP → same hash)

**Use case:** Detect login patterns without storing raw IPs.

**Source:** `backend/WovenBackend/Services/Security/PiiSanitizer.cs`

---

## Audit Log Retention

**Worker:** `SecurityAuditCleanupWorker`

**Schedule:** Daily at 04:00 UTC

**Retention policy:**
- Login events: 90 days
- Suspicious activity: 180 days
- Account deletion: 365 days
- Data export: 90 days
- Permission changes: 365 days

**Implementation:**
```csharp
var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

await db.SecurityAuditLogs
    .Where(log => log.EventType == eventType && log.CreatedAt < cutoff)
    .ExecuteDeleteAsync(ct);
```

**Source:** `backend/WovenBackend/Services/Security/SecurityAuditCleanupWorker.cs`

---

## Querying Audit Logs

### Recent logins for user

```sql
SELECT *
FROM security_audit_logs
WHERE user_id = 123
  AND event_type = 'LoginSuccess'
  AND created_at > NOW() - INTERVAL '30 days'
ORDER BY created_at DESC;
```

---

### Failed logins by IP (hashed)

```sql
SELECT ip_address_hash, COUNT(*) as attempts
FROM security_audit_logs
WHERE event_type = 'LoginFailure'
  AND created_at > NOW() - INTERVAL '1 hour'
GROUP BY ip_address_hash
HAVING COUNT(*) > 5
ORDER BY attempts DESC;
```

---

### Suspicious activity alerts

```sql
SELECT user_id, created_at, metadata_json
FROM security_audit_logs
WHERE event_type = 'SuspiciousActivity'
  AND created_at > NOW() - INTERVAL '24 hours'
ORDER BY created_at DESC;
```

---

## Monitoring

### Alerts

**Configured in Azure Application Insights:**

| Alert | Condition | Action |
|---|---|---|
| Multiple failed logins | >10 LoginFailure from one IP in 1 hour | Email admin |
| Suspicious activity spike | >20 SuspiciousActivity events in 1 hour | Email admin |
| Mass data export | >100 DataExport events in 1 day | Email admin |
| Account deletion spike | >50 AccountDeletion events in 1 day | Email admin |

---

### Dashboards

**Azure Log Analytics query:**
```kusto
customEvents
| where name == "SecurityAuditLog"
| extend eventType = tostring(customDimensions.EventType)
| summarize count() by eventType, bin(timestamp, 1h)
| render timechart
```

---

## Access Controls

**Who can read audit logs:**
- **Admins only** (via Azure portal or admin UI)
- **Users can see their own** (via `/me/audit-log` endpoint — planned)
- **Automated alerts** (Application Insights)

**Implementation:**
```csharp
// Admin endpoint (planned)
app.MapGet("/admin/audit-logs", async (WovenDbContext db) =>
{
    var logs = await db.SecurityAuditLogs
        .OrderByDescending(log => log.CreatedAt)
        .Take(100)
        .ToListAsync();
    
    return Results.Ok(logs);
})
.RequireAuthorization(policy => policy.RequireRole("Admin"));
```

---

## Compliance

**Supports:**
- **GDPR Article 30** (Records of processing activities)
- **GDPR Article 32** (Security of processing)
- **CCPA § 1798.150** (Security audits)
- **SOC 2** (Audit trail requirement)

**Benefits:**
- Detect security incidents
- Investigate user complaints
- Demonstrate compliance
- Forensic analysis

---

## Best Practices

1. **Record all security events** — Even failed attempts
2. **Include correlation IDs** — Trace requests end-to-end
3. **Hash PII** — Never log raw IPs or emails
4. **Structured metadata** — Use JSON for queryability
5. **Retention policies** — Balance compliance vs. storage
6. **Access controls** — Audit logs are sensitive
7. **Automated cleanup** — Prevent unbounded growth
8. **Monitor anomalies** — Alert on suspicious patterns

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Services/Security/SecurityAuditService.cs`
- `backend/WovenBackend/Services/Security/SecurityAuditCleanupWorker.cs`
- `backend/WovenBackend/Data/Entities/SecurityAuditLog.cs`
