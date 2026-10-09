# FeedbackTriggerWorker

**Purpose:** Daily batch job to queue, send, and reschedule date feedback prompts.

**File:** `backend/WovenBackend/Services/Feedback/FeedbackTriggerWorker.cs`  
**Type:** `BackgroundService` (ASP.NET Core hosted service)

---

## Execution Schedule

### Daily Target Time
```csharp
private static readonly TimeSpan DailyTarget = new(8, 0, 0); // 08:00 UTC
```

**Rationale:**
- 08:00 UTC = early morning for US (12am-3am PT/ET)
- Late morning/afternoon for India (1:30pm IST)
- Avoids overlap with ECHO batch workers:
  - `ConnectionScoreBatchWorker`: 03:50 UTC
  - `WeightLearningBatchWorker`: 04:00 UTC (Sunday)
  - `CfScoreBatchWorker`: 05:00 UTC

---

## Worker Lifecycle

### Startup
```csharp
public FeedbackTriggerWorker(
    IServiceScopeFactory scopeFactory, 
    ILogger<FeedbackTriggerWorker> logger)
{
    _scopeFactory = scopeFactory;
    _logger = logger;
}
```

**Registered in Program.cs:**
```csharp
builder.Services.AddHostedService<FeedbackTriggerWorker>();
```

### Main Loop
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var lastRunDate = DateOnly.MinValue;
    
    while (!stoppingToken.IsCancellationRequested)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        
        if (nowUtc.TimeOfDay >= DailyTarget && today > lastRunDate)
        {
            lastRunDate = today;
            await RunPassAsync(stoppingToken);
        }
        
        await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
    }
}
```

**Behavior:**
- Wakes every 30 minutes
- Checks if current time ≥ 08:00 UTC
- Runs once per day (tracked via `lastRunDate`)
- Never runs twice on same date

**Why 30-minute interval?**
- Balance between responsiveness and CPU waste
- If server restarts at 07:45, next check at 08:15 → still runs same day
- Low overhead (just time comparison, no DB queries)

---

## Daily Pass

### Three-Phase Execution
```csharp
private async Task RunPassAsync(CancellationToken ct)
{
    _logger.LogInformation("[FeedbackTrigger] Starting daily pass");
    
    await using var scope = _scopeFactory.CreateAsyncScope();
    var svc = scope.ServiceProvider.GetRequiredService<IDateFeedbackService>();
    
    try { await svc.QueueFeedbackPromptsAsync(ct); }
    catch (Exception ex) { _logger.LogWarning(ex, "[FeedbackTrigger] QueueFeedbackPromptsAsync failed"); }
    
    try { await svc.SendDuePromptsAsync(ct); }
    catch (Exception ex) { _logger.LogWarning(ex, "[FeedbackTrigger] SendDuePromptsAsync failed"); }
    
    try { await svc.ReschedulePendingAsync(ct); }
    catch (Exception ex) { _logger.LogWarning(ex, "[FeedbackTrigger] ReschedulePendingAsync failed"); }
    
    _logger.LogInformation("[FeedbackTrigger] Daily pass complete");
}
```

**Design:**
- Each phase wrapped in separate `try-catch`
- One failure doesn't block subsequent phases
- Scoped service ensures clean DB context per run

---

## Phase 1: Queue Feedback Prompts

**Method:** `IDateFeedbackService.QueueFeedbackPromptsAsync()`

### What It Does
1. Scan `Matches` table for three trigger conditions:
   - **Primary:** `interested_both` (mutual date interest, 5+ days ago)
   - **Secondary:** `deep_chat` (25+ messages, fast responses, 10+ days post-unlock)
   - **Tertiary:** `silent_thread` (active thread, no message in 5+ days)

2. Create `DateFeedbackPrompt` records (two per match: one for each user)

3. Set `ScheduledFor = now` (immediate delivery)

**SQL Queries:** See [triggers.md](./triggers.md) for full query logic.

**Output Log:**
```
[FeedbackQueue] Queued 15 primary + 8 secondary + 22 silent-thread match prompts
```

**Expected Volume:**
- **Primary:** 10-20 matches/day
- **Secondary:** 5-10 matches/day
- **Tertiary:** 20-30 matches/day
- **Total prompts:** 70-120 prompts/day (2x matches, both users)

---

## Phase 2: Send Due Prompts

**Method:** `IDateFeedbackService.SendDuePromptsAsync()`

### What It Does
1. Fetch prompts where:
   - `ScheduledFor <= now`
   - `SentAt == null` (not sent yet)
   - `RescheduleCount <= 2` (max 2 retries)

2. For each prompt:
   - Get partner name
   - Send push notification: `"How did it go with {FirstName}? We'd love to know 💙"`
   - Mark `SentAt = now`

**Push Notification Example:**
```
How did it go with Sarah? We'd love to know 💙
```

**Error Handling:**
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "[FeedbackSend] Failed for prompt {PromptId}", prompt.Id);
}
```

- Individual send failures logged
- Loop continues (one failure doesn't block others)
- No retries within same run (will retry next day if still unsent)

**Expected Volume:** ~70-120 notifications/day (same as queued prompts).

---

## Phase 3: Reschedule Pending

**Method:** `IDateFeedbackService.ReschedulePendingAsync()`

### What It Does
1. Find prompts sent 3+ days ago without response, < 2 reschedules:
   ```csharp
   var cutoff = DateTimeOffset.UtcNow.AddDays(-3);
   
   var toReschedule = await _db.DateFeedbackPrompts
       .Where(p => p.SentAt != null
                && p.RespondedAt == null
                && p.SentAt < cutoff
                && p.RescheduleCount < 2)
       .ToListAsync(ct);
   ```

2. Reschedule each:
   ```csharp
   p.RescheduleCount++;
   p.ScheduledFor = now.AddDays(5);  // Retry in 5 days
   p.SentAt = null;  // Reset so SendDuePromptsAsync picks it up
   ```

3. Expire exhausted prompts (2 reschedules already):
   ```csharp
   var toExpire = await _db.DateFeedbackPrompts
       .Where(p => p.SentAt != null
                && p.RespondedAt == null
                && p.RescheduleCount >= 2
                && p.SentAt < cutoff)
       .ToListAsync(ct);
   
   foreach (var p in toExpire)
       p.SentAt = now;  // Mark fresh so it won't reschedule again
   ```

**Timeline Example:**
```
Day 0:  Initial send (RescheduleCount = 0)
Day 3:  No response → reschedule for Day 8 (RescheduleCount = 1)
Day 8:  Second send
Day 11: No response → reschedule for Day 16 (RescheduleCount = 2)
Day 16: Third send
Day 19: No response → expire (SentAt updated, no more reschedules)
```

**Expected Volume:**
- **Rescheduled/day:** ~30-40 prompts
- **Expired/day:** ~10-15 prompts

---

## Error Recovery

### Phase Isolation
Each phase catches exceptions independently:

```csharp
try { await svc.QueueFeedbackPromptsAsync(ct); }
catch (Exception ex) { _logger.LogWarning(ex, "[FeedbackTrigger] QueueFeedbackPromptsAsync failed"); }
```

**Benefits:**
- Database deadlock in Phase 1 → Phases 2 & 3 still run
- OpenAI timeout in Phase 2 → Phase 3 still runs
- Critical: reschedules happen even if new prompts fail to queue

### Next-Day Retry
- Failed operations retry next day (08:00 UTC)
- No manual intervention needed
- Idempotent queries prevent duplicate prompts

---

## Monitoring

### Logs to Watch

#### Success Case
```
[FeedbackTrigger] Starting daily pass
[FeedbackQueue] Queued 15 primary + 8 secondary + 22 silent-thread match prompts
[FeedbackTrigger] Daily pass complete
```

#### Failure Case
```
[FeedbackTrigger] Starting daily pass
[FeedbackTrigger] QueueFeedbackPromptsAsync failed
System.InvalidOperationException: Connection timeout
[FeedbackTrigger] Daily pass complete
```

### Alerts

| Condition | Action |
|---|---|
| No prompts queued for 3+ days | Check trigger query logic (matches stopped?) |
| Phase 2 fails 2+ days | Push notification service down |
| Phase 3 fails | DB deadlock issue, check long-running transactions |
| Daily pass never runs | Worker crashed, check host service registration |

---

## Database Impact

### Queries Per Run
```sql
-- Phase 1: QueueFeedbackPromptsAsync
SELECT DISTINCT match_id FROM date_feedback_prompts;  -- Dedup list
SELECT * FROM matches WHERE ...;  -- Primary trigger (3 queries)
SELECT * FROM matches JOIN chat_threads ...;  -- Secondary/tertiary triggers (2 queries)
INSERT INTO date_feedback_prompts VALUES ...;  -- Batch insert

-- Phase 2: SendDuePromptsAsync
SELECT * FROM date_feedback_prompts WHERE scheduled_for <= NOW() AND ...;
SELECT full_name FROM users WHERE id = ? FOR EACH prompt;
UPDATE date_feedback_prompts SET sent_at = ? WHERE id = ?;

-- Phase 3: ReschedulePendingAsync
SELECT * FROM date_feedback_prompts WHERE sent_at < cutoff AND ...;  -- Reschedule list
SELECT * FROM date_feedback_prompts WHERE sent_at < cutoff AND ...;  -- Expire list
UPDATE date_feedback_prompts SET reschedule_count = ?, scheduled_for = ?, sent_at = NULL;
UPDATE date_feedback_prompts SET sent_at = ?;  -- Expire
```

**Index Requirements:**
```sql
CREATE INDEX idx_feedback_prompts_scheduled 
    ON date_feedback_prompts(scheduled_for, sent_at, reschedule_count);

CREATE INDEX idx_feedback_prompts_reschedule 
    ON date_feedback_prompts(sent_at, responded_at, reschedule_count);

CREATE INDEX idx_matches_date_interest 
    ON matches(date_idea_interested_at, closed_reason)
    WHERE date_idea_interested_a = true AND date_idea_interested_b = true;
```

**Estimated Load:**
- **Scan:** ~500-1000 match records
- **Insert:** ~70-120 prompt records
- **Update:** ~50-70 prompt records
- **Duration:** < 5 seconds

---

## Cancellation Handling

### Graceful Shutdown
```csharp
while (!stoppingToken.IsCancellationRequested)
{
    // ...
    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
}
```

**Behavior:**
- `stoppingToken` triggered on app shutdown
- Current `RunPassAsync()` completes (cancellation token passed to DB queries)
- Worker exits cleanly
- Next run happens on next startup (if within same day, skipped via `lastRunDate`)

---

## Testing Considerations

### Local Testing
```csharp
// Override DailyTarget for immediate execution
private static readonly TimeSpan DailyTarget = TimeSpan.Zero;
```

**Warning:** Runs immediately on startup, every 30 minutes. Set back to `new(8, 0, 0)` before deploy.

### Manual Trigger
```csharp
// Add to FeedbackEndpoints.cs (admin-only)
app.MapPost("/admin/trigger-feedback-worker", async (IDateFeedbackService svc) =>
{
    await svc.QueueFeedbackPromptsAsync();
    await svc.SendDuePromptsAsync();
    await svc.ReschedulePendingAsync();
    return Results.Ok(new { triggered = true });
}).RequireAuthorization("Admin");
```

---

## Deployment Considerations

### Multi-Instance Safety
**Problem:** If backend runs on 2+ instances, worker runs twice.

**Current State:** ❌ Not handled (single-instance deployment assumed)

**Future Solution:**
```csharp
// Use distributed lock (Redis)
var lockKey = $"feedback-worker:{today:yyyy-MM-dd}";
if (await _redis.LockAsync(lockKey, TimeSpan.FromMinutes(10)))
{
    try { await RunPassAsync(stoppingToken); }
    finally { await _redis.UnlockAsync(lockKey); }
}
```

### Environment Flag
```csharp
// Program.cs
var disableWorkers = Environment.GetEnvironmentVariable("WOVEN_DISABLE_BATCH_WORKERS") == "true";

if (!disableWorkers)
    builder.Services.AddHostedService<FeedbackTriggerWorker>();
```

**Set on web pods:** `WOVEN_DISABLE_BATCH_WORKERS=true`  
**Set on worker pod:** Omit flag (or `=false`)

---

**Last Updated:** 2026-10-07
**Related:** [triggers.md](./triggers.md), [README.md](./README.md)
