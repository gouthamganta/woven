# Coaching Workers

Background jobs that generate and deliver weekly coaching summaries.

---

## Overview

| Worker | Schedule | Lock Key | Lock Expiry | Purpose |
|---|---|---|---|---|
| `CoachingSummaryWorker` | Wednesday 18:00 UTC | `lock:coaching-summary-batch` | 4 hours | Generate weekly coaching summaries |

**Single worker.** No dependencies on other ECHO workers (reads from `DailyInteractions` + `MatchSignalLogs` which are populated by other systems).

---

## CoachingSummaryWorker

**Class:** `WovenBackend.Services.Coaching.CoachingSummaryWorker`  
**Base:** `BackgroundService` (hosted service, runs in web pods)  
**Schedule:** Wednesday 18:00 UTC  
**Lock:** Redis distributed lock (`lock:coaching-summary-batch`, 4h expiry)

### Execution Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Sleep until next Wednesday 18:00 UTC                        │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 2. Acquire distributed lock (skip if another pod holds it)     │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 3. Cleanup: DELETE coaching_summaries WHERE created_at < -90d  │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 4. Eligibility Check:                                           │
│    • Account ≥14 days                                           │
│    • ≥3 deck interactions in prior 7 days                       │
│    • Not opted out                                              │
│    • No existing summary for this week                          │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 5. For each eligible user (sequential):                        │
│    a. Aggregate signals (DailyInteractions + MatchSignalLogs)  │
│    b. Build C# narrative (deterministic)                        │
│    c. Call GPT-4.1-mini (temperature=0.8)                       │
│    d. Suppression check (SUPPRESS keyword, <50 chars)          │
│    e. Write to coaching_summaries table                         │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 6. Release lock, log summary (X written / Y eligible)          │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 7. Sleep until next Wednesday 18:00 UTC (repeat)               │
└─────────────────────────────────────────────────────────────────┘
```

---

## Schedule Logic

**Target:** Wednesday 18:00 UTC (weekly)

```csharp
private static async Task SleepUntilWednesdayAsync(CancellationToken ct)
{
    var now      = DateTime.UtcNow;
    var daysUntilWed = ((int)DayOfWeek.Wednesday - (int)now.DayOfWeek + 7) % 7;
    if (daysUntilWed == 0 && now.Hour >= 18) daysUntilWed = 7;
    var nextRun  = now.Date.AddDays(daysUntilWed).AddHours(18);
    var delay    = nextRun - now;
    if (delay > TimeSpan.Zero)
        await Task.Delay(delay, ct);
}
```

**Logic:**
1. Calculate days until next Wednesday (0-6)
2. If today is Wednesday and it's past 18:00, wait until next Wednesday
3. Sleep until target time (non-blocking)

**Edge cases:**
- Worker starts Monday → sleeps 2 days
- Worker starts Wednesday 17:59 → sleeps 1 minute
- Worker starts Wednesday 18:01 → sleeps 6 days 23h 59min

---

## Distributed Lock

**Why needed:**
- Multiple web pods run `CoachingSummaryWorker` (no dedicated worker pods)
- Only one pod should process the batch (avoid duplicate summaries)

**Lock mechanism:**

```csharp
if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
{
    _logger.LogInformation("[CoachingSummary] Skipping — another pod holds the lock");
    continue; // Sleep until next Wednesday
}
```

**Lock parameters:**
- **Key:** `"lock:coaching-summary-batch"` (Redis string key)
- **Expiry:** 4 hours (prevents deadlock if pod crashes mid-batch)
- **Release:** Explicit release in `finally` block

**Race condition handling:**
- Two pods wake up at 18:00:00.000
- Both try to acquire lock
- Redis `SET NX` ensures only one succeeds
- Loser pod skips this week (logs + sleeps)

---

## Batch Processing

### Eligibility Query

**Step 1: Account age + opt-out**
```csharp
var cutoffDate = now.AddDays(-MinAccountAgeDays); // -14 days
var eligibleUserIds = await db.Users.AsNoTracking()
    .Where(u => u.CreatedAt <= cutoffDate.DateTime && !u.CoachingOptedOut)
    .Select(u => u.Id)
    .ToListAsync(ct);
```

**Step 2: Deck engagement**
```csharp
var signalFrom = now.AddDays(-7);
var activeUserIds = await db.DailyInteractions.AsNoTracking()
    .Where(d => d.DateUtc >= DateOnly.FromDateTime(signalFrom.DateTime) &&
                eligibleUserIds.Contains(d.UserId))
    .GroupBy(d => d.UserId)
    .Where(g => g.Sum(d => d.TotalUsed) >= MinDeckInteractions) // ≥3
    .Select(g => g.Key)
    .ToListAsync(ct);
```

**Step 3: No existing summary**
```csharp
var weekStart = DateOnly.FromDateTime(now.DateTime).AddDays(-(int)now.DayOfWeek);
var alreadySummarized = await db.CoachingSummaries.AsNoTracking()
    .Where(c => c.WeekStartDate == weekStart && activeUserIds.Contains(c.UserId))
    .Select(c => c.UserId)
    .ToListAsync(ct);

var toProcess = activeUserIds.Except(alreadySummarized).ToList();
```

**Typical funnel (1000 users):**
- Eligible by age: 800 (80%)
- Active (≥3 deck interactions): 300 (30%)
- No existing summary: 300 (100% — first run of week)
- **To process:** 300 users

---

## Per-User Processing

**Sequential processing** (not parallel) to avoid OpenAI rate limits.

```csharp
int written = 0;
foreach (var userId in toProcess)
{
    if (ct.IsCancellationRequested) break;
    try
    {
        using var innerScope = _scopeFactory.CreateScope();
        var innerDb = innerScope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var saved = await ProcessUserAsync(innerDb, apiKey, userId, weekStart, signalFrom, now, ct);
        if (saved) written++;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[CoachingSummary] Failed for user {UserId}", userId);
    }
}
```

**Why sequential:**
- OpenAI API rate limits (3500 req/min for `gpt-4.1-mini`)
- 300 users × 1 req/user = 300 requests (~1 req/sec = safe)
- Parallel would risk 429 errors

**Error handling:**
- Per-user failures logged, batch continues
- Failed user gets no summary this week (retry next Wednesday)

---

## ProcessUserAsync

**Function:** `ProcessUserAsync(db, apiKey, userId, weekStart, signalFrom, now, ct)`  
**Returns:** `bool` (true if summary saved, false if suppressed)

### Steps

1. **Aggregate signals** (7-day window)
   ```csharp
   var deckInteractions = await db.DailyInteractions.AsNoTracking()
       .Where(d => d.UserId == userId && d.DateUtc >= DateOnly.FromDateTime(signalFrom.DateTime))
       .SumAsync(d => d.TotalUsed, ct);

   var signalCounts = await db.MatchSignalLogs.AsNoTracking()
       .Where(s => s.ViewerId == userId && s.OccurredAt >= signalFrom)
       .GroupBy(s => s.EventType)
       .Select(g => new CoachingSignalStat(g.Key, g.Count(), (double)g.Average(s => s.EventValue)))
       .ToListAsync(ct);
   ```

2. **Build C# narrative** (deterministic)
   ```csharp
   var narrative = BuildNarrative(userId, deckInteractions, signalMap);
   ```

3. **Call GPT-4.1-mini**
   ```csharp
   var summaryText = await GenerateSummaryAsync(apiKey, narrative, ct);
   ```

4. **Suppression check**
   ```csharp
   if (string.IsNullOrWhiteSpace(summaryText) ||
       summaryText.Trim().Equals("SUPPRESS", StringComparison.OrdinalIgnoreCase) ||
       summaryText.Trim().Length < MinSummaryChars)
   {
       _logger.LogInformation("[CoachingSummary] Suppressed for user {UserId}", userId);
       return false;
   }
   ```

5. **Write to DB**
   ```csharp
   db.CoachingSummaries.Add(new CoachingSummary
   {
       UserId               = userId,
       WeekStartDate        = weekStart,
       SummaryText          = summaryText.Trim(),
       InterpretedNarrative = narrative,
       DeliveredAt          = now
   });
   await db.SaveChangesAsync(ct);
   return true;
   ```

---

## Performance

### Typical Batch

**Scenario:** 300 eligible users

**Timing:**
- Eligibility check: ~500ms (3 queries)
- Per-user processing: ~1.5s/user (DB query + OpenAI call + DB write)
- **Total:** 300 × 1.5s = 450s (~7.5 minutes)

**Lock expiry:** 4 hours (ample buffer)

**Bottleneck:** OpenAI API latency (~800ms/request)

---

### Worst Case

**Scenario:** 1000 eligible users (large spike)

**Timing:**
- 1000 × 1.5s = 1500s (~25 minutes)
- Still well under 4h lock expiry

**Mitigation if needed:**
- Parallel OpenAI calls (batches of 10)
- Dedicated worker pods (set `WOVEN_DISABLE_BATCH_WORKERS=true` on web pods)

---

## Logging

**Key log lines:**

### Batch start
```
[CoachingSummary] Starting batch at 2026-10-07T18:00:03Z
```

### Lock skip
```
[CoachingSummary] Skipping — another pod holds the lock
```

### Eligibility summary
```
[CoachingSummary] Processing 287 eligible users
```

### Suppression
```
[CoachingSummary] Suppressed for user 1234
```

### Per-user failure
```
[CoachingSummary] Failed for user 5678
System.Net.Http.HttpRequestException: OpenAI API returned 429
```

### Batch completion
```
[CoachingSummary] Done — 243/287 summaries written
```

**CorrelationId:** All log lines include `{CorrelationId}` from `CorrelationIdMiddleware`.

---

## Cleanup (90-Day TTL)

**When:** Preamble of `RunAsync()` (before batch processing)

```csharp
await db.CoachingSummaries
    .Where(c => c.CreatedAt < now.AddDays(-90))
    .ExecuteDeleteAsync(ct);
```

**Why preamble:**
- Runs once per week (not per user)
- Reduces DB bloat before batch processing
- Ensures fresh start (no orphaned rows)

**Typical deletions:** 200-300 rows/week (previous week's summaries from 13 weeks ago)

---

## Error Handling

### OpenAI API Failure

**Trigger:** 500 error, timeout, rate limit (429)

**Behavior:**
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "[CoachingSummary] OpenAI call failed");
    return null; // No summary for this user
}
```

**Recovery:**
- Per-user failure (batch continues)
- Next Wednesday retries (signals still in DB)

---

### Redis Lock Failure

**Trigger:** Redis down, network partition

**Behavior:**
```csharp
if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
{
    _logger.LogInformation("[CoachingSummary] Skipping — another pod holds the lock");
    continue; // Sleep until next Wednesday
}
```

**Recovery:**
- Worker skips this week
- Next Wednesday retries (double summaries prevented by `WeekStartDate` check)

---

### Database Deadlock

**Trigger:** Concurrent writes to `coaching_summaries`

**Mitigation:**
- Sequential processing (not parallel)
- Per-user scoped `DbContext` (no shared connection)

**Unlikely scenario:** Only one pod processes batch (distributed lock).

---

## Monitoring

### Metrics to Track

**Batch-level:**
- Eligible users per week (trend)
- Suppression rate (target: 15-20%)
- Batch duration (target: <30 min for 1000 users)
- Lock acquisition failures (should be 0 unless Redis down)

**Per-user:**
- OpenAI API failure rate (target: <1%)
- Avg summary length (chars)
- Token usage per summary

### Alerts

**Critical:**
- Batch duration >1h → performance regression
- Lock acquisition failed 3 weeks in a row → Redis issue

**Warning:**
- Suppression rate >40% → GPT prompt degraded
- OpenAI API failure rate >10% → upstream issue

---

## Environment Flags

**`WOVEN_DISABLE_BATCH_WORKERS`**

**Effect:**
- Set on web pods → `CoachingSummaryWorker` sleeps forever (no-op)
- Unset on worker pods → normal execution

**Why:**
- Dedicate worker pods for batch jobs
- Web pods focus on serving traffic

**Current setup:**
- All pods run all workers (no dedicated worker pods yet)
- Lock ensures only one pod processes batch

---

## Testing

### Unit Test: Eligibility Logic

```csharp
[Test]
public async Task RunAsync_UserOptedOut_Skipped()
{
    // Arrange
    var user = CreateTestUser(createdAt: now.AddDays(-20), coachingOptedOut: true);
    SeedDailyInteractions(user.Id, totalUsed: 8, dateRange: last7Days);

    // Act
    await worker.RunAsync(ct);

    // Assert
    var summary = await db.CoachingSummaries.FirstOrDefaultAsync(c => c.UserId == user.Id);
    Assert.That(summary, Is.Null);
}
```

### Integration Test: Full Batch

```csharp
[Test]
public async Task RunAsync_QualifiedUsers_CreatesSummaries()
{
    // Arrange
    var users = CreateTestUsers(count: 10, createdAt: now.AddDays(-20));
    foreach (var user in users)
        SeedDailyInteractions(user.Id, totalUsed: 8, dateRange: last7Days);

    // Act
    await worker.RunAsync(ct);

    // Assert
    var summaries = await db.CoachingSummaries.CountAsync();
    Assert.That(summaries, Is.EqualTo(10));
}
```

---

## Future Enhancements

### Parallel Processing

**Why:**
- 1000+ users/week → 25+ min batch duration
- OpenAI API can handle 3500 req/min

**How:**
```csharp
var batches = toProcess.Chunk(10); // Process 10 users in parallel
foreach (var batch in batches)
{
    await Task.WhenAll(batch.Select(userId => ProcessUserAsync(...)));
}
```

**Trade-off:** More complex error handling, risk of rate limits.

---

### Dedicated Worker Pods

**Why:**
- Offload batch jobs from web pods
- Vertical scaling (worker pods can have more CPU/memory)

**How:**
- Deploy separate worker pod group (same image, different env vars)
- Set `WOVEN_DISABLE_BATCH_WORKERS=true` on web pods
- Worker pods run all batch jobs (coaching, connection scores, etc.)

---

## See Also

- [summary-generation.md](./summary-generation.md) — How summaries are generated
- [delivery.md](./delivery.md) — When summaries are delivered
- [api.md](./api.md) — Coaching endpoints
- [docs/systems/echo/workers.md](../echo/workers.md) — All ECHO batch workers
