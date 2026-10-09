# Analytics Workers

Background workers for analytics data processing and privacy compliance.

## Overview

| Worker | Schedule | Purpose |
|--------|----------|---------|
| `AnalyticsRetentionWorker` | Monthly (1st @ 02:15 UTC) | Anonymize events older than 12 months |

---

## AnalyticsRetentionWorker

**File:** `backend/WovenBackend/Services/Analytics/AnalyticsRetentionWorker.cs`  
**Type:** Hosted service (BackgroundService)  
**Schedule:** 1st of month, 02:15 UTC  
**Purpose:** Privacy compliance — auto-anonymize old analytics events

### Schedule Strategy

```csharp
var now = DateTime.UtcNow;
var nextRun = new DateTime(now.Year, now.Month, 1)
    .AddMonths(1)
    .AddHours(2).AddMinutes(15); // Offset from TrustBatchWorker

var delay = nextRun - now;
if (delay < TimeSpan.Zero) delay = TimeSpan.FromDays(28);
```

**Timing notes:**
- Runs **after** TrustBatchWorker (02:00 UTC)
- Low-traffic window (early AM UTC)
- Allows previous month's events to stabilize

### Anonymization Process

**What it does:**

1. Calculate cutoff: 12 months before current run
2. Find all `analytics_events` with `created_at < cutoff`
3. Strip PII fields (`user_id_hash`, `session_id`) via bulk update
4. Log anonymization count
5. Audit trail via `ISecurityAuditService`

**Implementation:**

```csharp
private async Task AnonymizeOldEventsAsync(CancellationToken ct)
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
    var audit = scope.ServiceProvider.GetRequiredService<ISecurityAuditService>();

    var cutoff = DateTimeOffset.UtcNow.AddMonths(-12);

    var rowsAffected = await db.AnalyticsEvents
        .Where(e => e.CreatedAt < cutoff && e.UserIdHash != null)
        .ExecuteUpdateAsync(s => s
            .SetProperty(e => e.UserIdHash, (string?)null)
            .SetProperty(e => e.SessionId, (string?)null), ct);

    _logger.LogInformation(
        "[AnalyticsRetention] Anonymized {Count} events older than 12 months", 
        rowsAffected);

    audit.Log("pii_access",
        service: "AnalyticsRetentionWorker",
        resourceType: "analytics_events",
        piiStripped: true);
}
```

### What Gets Anonymized

**Before anonymization:**

```json
{
  "id": 123456,
  "user_id_hash": "a3f8c9d2e1b4...",
  "session_id": "8f3c9a2e1d5b...",
  "event_type": "moment_responded",
  "properties": "{\"action\":\"magical\"}",
  "created_at": "2025-09-15T10:30:00Z"
}
```

**After anonymization:**

```json
{
  "id": 123456,
  "user_id_hash": null,
  "session_id": null,
  "event_type": "moment_responded",
  "properties": "{\"action\":\"magical\"}",
  "created_at": "2025-09-15T10:30:00Z"
}
```

**Still usable for:**
- ✅ Aggregate metrics (event type distribution)
- ✅ A/B test results (variant performance)
- ✅ Time-series analysis (daily event volumes)

**No longer usable for:**
- ❌ Cohort retention analysis (no user hash)
- ❌ Session funnels (no session ID)
- ❌ User-level queries

### Why 12 Months?

**Rationale:**
- Long enough for YoY retention analysis
- Short enough to respect privacy (GDPR-inspired)
- Balances business needs vs user trust

**GDPR note:** While Woven may not be GDPR-regulated (India-focused), 12-month retention sets a privacy-first precedent.

### Error Handling

```csharp
catch (OperationCanceledException)
{
    // Graceful shutdown
    break;
}
catch (Exception ex)
{
    _logger.LogError(ex, "[AnalyticsRetention] Anonymization run failed");
    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
    // Retry in 1 hour
}
```

**Failure modes:**
- Database connection timeout → retry in 1h
- EF Core exception → logged, retry next month
- Service scope failure → logged, retry next month

### Monitoring

**Log signals:**

```
[AnalyticsRetention] Anonymized 45,821 events older than 12 months
```

**Audit trail:**

```csharp
audit.Log("pii_access",
    service: "AnalyticsRetentionWorker",
    resourceType: "analytics_events",
    piiStripped: true);
```

Appears in `security_audit_logs` table:

```sql
SELECT * FROM security_audit_logs
WHERE service = 'AnalyticsRetentionWorker'
ORDER BY created_at DESC
LIMIT 10;
```

### Performance Characteristics

**Bulk update strategy:**

```csharp
.ExecuteUpdateAsync(s => s
    .SetProperty(e => e.UserIdHash, (string?)null)
    .SetProperty(e => e.SessionId, (string?)null), ct);
```

**Benefits:**
- No load into memory (UPDATE in place)
- Single transaction
- No ORM overhead

**Estimated impact:**
- 100K events → ~2-5 seconds
- 1M events → ~30-60 seconds
- Index rebuild not required (nullable columns)

**Locks:**
- Row-level locks during update
- Minimal impact (02:15 UTC = low traffic)

### Testing

**Local test:**

```csharp
// Manually trigger worker for testing
var worker = new AnalyticsRetentionWorker(scopeFactory, logger);

// Run anonymization immediately (don't wait for schedule)
await worker.AnonymizeOldEventsAsync(CancellationToken.None);
```

**Test data setup:**

```sql
-- Insert old test events
INSERT INTO analytics_events (user_id_hash, session_id, event_type, created_at)
VALUES 
  ('test_hash_1', 'test_session_1', 'app_opened', NOW() - INTERVAL '13 months'),
  ('test_hash_2', 'test_session_2', 'moment_responded', NOW() - INTERVAL '13 months');

-- Run worker

-- Verify anonymization
SELECT * FROM analytics_events
WHERE created_at < NOW() - INTERVAL '12 months';
-- Should see: user_id_hash = null, session_id = null
```

### Deployment Configuration

**Environment variable:**

```bash
# Disable worker in development (optional)
WOVEN_DISABLE_ANALYTICS_RETENTION=true
```

**Program.cs registration:**

```csharp
if (!disableBatchWorkers)
{
    builder.Services.AddHostedService<AnalyticsRetentionWorker>();
}
```

**Azure Container Apps:**
- Run on backend pods (not web pods)
- Web pods have `WOVEN_DISABLE_BATCH_WORKERS=true`

### Future Enhancements

1. **Configurable retention window** — 6mo / 12mo / 24mo via app settings
2. **Partial anonymization** — Keep cohort ID, strip session ID
3. **Metrics export before anonymization** — Snapshot to cold storage
4. **Real-time anonymization triggers** — On account deletion requests

---

## Planned Workers (Not Implemented Yet)

### SessionRetentionWorker

**Schedule:** Daily @ 03:00 UTC  
**Purpose:** Clean up expired session cache entries (Redis cleanup)

```csharp
// Scan Redis keys: analytics:session:*
// Delete sessions older than 2h (already expired)
```

**Status:** 🔴 Not needed — Redis TTL handles this automatically

### EventAggregationWorker

**Schedule:** Hourly  
**Purpose:** Pre-aggregate common queries (DAU, funnel metrics)

```csharp
// Roll up hourly:
// - Event type counts
// - Session counts
// - Funnel conversions
// Write to: analytics_hourly_rollups table
```

**Status:** 🔴 Not implemented — query performance OK without rollups

---

## Related Docs

- [Analytics System Overview](./README.md)
- [Retention Tracking](./retention.md)
- [Privacy Approach](./privacy.md)
- [Events Catalog](./events.md)
- [Implementation Guide](./implementation.md)
