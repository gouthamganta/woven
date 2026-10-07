# Anti-Ghosting System

**Last Updated:** 2026-10-07  
**Component:** Ghost Detection & Spark Refunds  
**Status:** Production

---

## Overview

The Anti-Ghosting system protects users from one-sided conversations by:
1. **Detecting silent threads** (24h no reply → 0.5 spark refund)
2. **Nudging expiring balloons** (24h before balloon expires)
3. **Calculating GhostScore** (reply rate, 90-day lookback)
4. **Penalizing delivery** (low GhostScore → fewer moments sent)

**Key Insight:** Ghosting is about **responsiveness**, not account validity. We don't filter low GhostScore users from candidate pools — we reduce their **delivery rate** instead.

---

## Components

### 1. Silent Thread Detection
**What:** Refund spark when user waits 24h+ for reply  
**Trigger:** `GhostDetectionWorker` (every 6h)  
**Effect:** 0.5 spark returned + push notification

### 2. Balloon Expiry Nudges
**What:** Warn users 24h before balloon expires  
**Trigger:** `GhostDetectionWorker` (every 6h)  
**Effect:** Push notification to both users

### 3. GhostScore Calculation
**What:** Reply rate over 90 days (0.0–1.0)  
**Trigger:** `GhostDetectionWorker` (nightly at 03:30 UTC)  
**Effect:** Stored in `User.GhostScore`, used by `DeliveryBoostService`

### 4. 0.5 Spark Refund (Match Close Paths)
**What:** Refund on unmatch with zero messages  
**Trigger:** 3 close paths: BLOCK, END (no_spark), balloon EXPIRE  
**Effect:** 0.5 spark returned to both users

---

## Silent Thread Detection

### Algorithm

**Step 1:** Load all active balloon threads
```sql
SELECT t.id AS thread_id, m.user_a_id, m.user_b_id
FROM chat_threads t
JOIN matches m ON t.match_id = m.id
WHERE m.balloon_state = 'ACTIVE';
```

**Step 2:** Check last message timestamp
```sql
SELECT sender_user_id, created_at
FROM chat_messages
WHERE thread_id = @threadId
ORDER BY created_at DESC
LIMIT 1;
```

**Step 3:** If last message > 24h old → refund waiting user
```csharp
var cutoff = DateTimeOffset.UtcNow.AddHours(-24);

if (lastMsg.CreatedAt > cutoff) 
    continue; // Thread is still active, no refund needed

var waitingUserId = lastMsg.SenderUserId; // User who sent last message (waiting for reply)

// Deduplication: check if already refunded
var refundKey = $"ghost:refunded:{threadId}:{waitingUserId}";
var alreadyRefunded = await _cache.GetAsync<string>(refundKey, ct);
if (alreadyRefunded != null) 
    continue;

// Refund 0.5 spark (decrement Redis + DB daily_interactions.total_used)
await _budget.RefundSparkAsync(waitingUserId, ct);

// Send push notification
await _notify.SendPushAsync(waitingUserId, 
    "You haven't heard back — we've refunded your spark.", ct);

// Cache refund (TTL: 3 days, prevent duplicate refunds)
await _cache.SetAsync(refundKey, "1", TimeSpan.FromDays(3), ct);
```

**Cache Deduplication:**
- Key: `ghost:refunded:{threadId}:{waitingUserId}`
- Value: `"1"` (flag)
- TTL: 3 days (prevent duplicate refunds if worker runs multiple times)

---

### Edge Cases

#### Case 1: Both users ghost each other
**Scenario:** User A sends message → 24h pass → User B sends message → 24h pass  
**Effect:**
- Day 1: User A refunded (sent last, no reply)
- Day 2: User B refunded (sent last, no reply)
- Total: Both users get 0.5 spark back

**Acceptable?** Yes. Each user waited 24h without reply.

#### Case 2: Thread reactivates after refund
**Scenario:** User A refunded on Day 1 → User B replies on Day 2  
**Effect:** Refund stands. No clawback.  
**Rationale:** User A earned the refund (waited 24h). Conversation resuming doesn't negate that.

#### Case 3: Balloon expires before 24h
**Scenario:** Balloon expires at 48h total TTL. Last message sent at 40h.  
**Effect:** No refund (balloon closed before 24h silent threshold).  
**Rationale:** Balloon expiry has its own refund path (see "Match Close Refunds").

---

## Balloon Expiry Nudges

### Algorithm

**Step 1:** Load all balloons expiring in next 24h
```sql
SELECT id, user_a_id, user_b_id, expires_at
FROM matches
WHERE balloon_state = 'ACTIVE'
  AND expires_at > NOW()
  AND expires_at <= NOW() + INTERVAL '24 hours';
```

**Step 2:** Check if already notified (deduplication)
```csharp
var notifyKey = $"ghost:expiry-notified:{matchId}";
var alreadyNotified = await _cache.GetAsync<string>(notifyKey, ct);
if (alreadyNotified != null) 
    continue;
```

**Step 3:** Send push notification to both users
```csharp
var hoursLeft = (int)Math.Ceiling((match.ExpiresAt - now).TotalHours);
var msg = $"Your balloon expires in ~{hoursLeft}h. Don't let the moment slip away.";

await Task.WhenAll(
    _notify.SendPushAsync(match.UserAId, msg, ct),
    _notify.SendPushAsync(match.UserBId, msg, ct)
);

// Cache notification (TTL: 25h, prevent duplicate nudges)
await _cache.SetAsync(notifyKey, "1", TimeSpan.FromHours(25), ct);
```

**Cache Deduplication:**
- Key: `ghost:expiry-notified:{matchId}`
- Value: `"1"` (flag)
- TTL: 25h (worker runs every 6h, nudge window is 24h)

---

### Timing Example

```
Match Created: 2026-10-07 00:00 UTC
Balloon Expires: 2026-10-09 00:00 UTC (48h TTL)

Worker Run #1: 2026-10-07 06:00 UTC → ExpiresAt > now + 24h → no nudge
Worker Run #2: 2026-10-07 12:00 UTC → ExpiresAt > now + 24h → no nudge
Worker Run #3: 2026-10-07 18:00 UTC → ExpiresAt > now + 24h → no nudge
Worker Run #4: 2026-10-08 00:00 UTC → ExpiresAt = now + 24h → NUDGE SENT
Worker Run #5: 2026-10-08 06:00 UTC → Already notified (cache hit) → skip
```

---

## GhostScore Calculation

### Formula
```
replied = count(threads where user replied to first message)
received = count(threads where user received first message)

GhostScore = (replied + 1) / (received + 2)
```

**Laplace Smoothing:** `+1` numerator, `+2` denominator (prevents 0/0, gives benefit of doubt to new users)

**Range:** `0.1 – 1.0` (clamped)

---

### Algorithm

**Step 1:** Load all users
```sql
SELECT id FROM users;
```

**Step 2:** For each user, find threads in last 90 days
```sql
SELECT t.id AS thread_id
FROM chat_threads t
JOIN matches m ON t.match_id = m.id
WHERE (m.user_a_id = @userId OR m.user_b_id = @userId)
  AND m.created_at >= NOW() - INTERVAL '90 days';
```

**Step 3:** For each thread, check if user received first message
```sql
SELECT sender_user_id
FROM chat_messages
WHERE thread_id = @threadId
ORDER BY created_at ASC
LIMIT 1;
```

**Step 4:** If first message sender ≠ userId → user received first message
```csharp
if (firstMsg.SenderUserId == userId) 
    continue; // User sent first message, skip this thread

totalReceived++;

// Check if user ever replied
var userReplied = await _db.ChatMessages
    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == userId, ct);

if (userReplied) 
    replied++;
```

**Step 5:** Compute score
```csharp
if (totalReceived == 0) 
    continue; // No received messages → skip update (keep default 0.5)

var newScore = (float)((replied + 1.0) / (totalReceived + 2.0));
newScore = Math.Clamp(newScore, 0.1f, 1.0f);

await _db.Users
    .Where(u => u.Id == userId)
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.GhostScore, newScore), ct);
```

---

### Examples

#### Example 1: New User (No Match History)
```
received = 0
replied = 0

GhostScore = (0 + 1) / (0 + 2) = 0.50
```
**Interpretation:** Default score. No penalty, no bonus.

---

#### Example 2: Responsive User
```
received = 10 first messages
replied = 9 (replied to 9/10)

GhostScore = (9 + 1) / (10 + 2) = 10 / 12 = 0.83
```
**Interpretation:** High responsiveness. No delivery penalty.

---

#### Example 3: Frequent Ghoster
```
received = 20 first messages
replied = 2 (replied to 2/20)

GhostScore = (2 + 1) / (20 + 2) = 3 / 22 = 0.14 → clamped to 0.1
```
**Interpretation:** Low responsiveness. Delivery penalty applied.

---

#### Example 4: Perfect Responder
```
received = 100 first messages
replied = 100 (replied to every single one)

GhostScore = (100 + 1) / (100 + 2) = 101 / 102 = 0.99
```
**Interpretation:** Near-perfect responsiveness. Maximum delivery boost.

---

### Delivery Penalty (DeliveryBoostService)

**Implementation (simplified):**
```csharp
var recipientGhostScore = await _db.Users
    .Where(u => u.Id == recipientId)
    .Select(u => u.GhostScore)
    .FirstOrDefaultAsync(ct);

if (recipientGhostScore < 0.3f)
{
    // Low GhostScore → 50% chance moment is NOT delivered
    if (Random.Shared.NextDouble() < 0.5)
        return DeliveryDecision.Suppress;
}

return DeliveryDecision.Deliver;
```

**Effect:**
- GhostScore ≥ 0.3 → 100% delivery rate
- GhostScore < 0.3 → 50% delivery rate (moments sent by low-responders are suppressed)

**Rationale:** Don't waste other users' time on people who don't reply.

---

## Match Close Refunds (0.5 Spark)

### Trigger Paths

**Path 1: BLOCK (ChatEndpoints.cs)**
```csharp
// POST /chats/{threadId}/block
if (msgCount == 0)
{
    // No messages exchanged → refund both users 0.5 sparks
    await Task.WhenAll(
        _budget.RefundSparkAsync(match.UserAId, ct),
        _budget.RefundSparkAsync(match.UserBId, ct)
    );
}
```

**Path 2: END (Trial Decision, ChatEndpoints.cs)**
```csharp
// POST /chats/{threadId}/trial-decision
if (req.Decision == TrialDecision.END && msgCount == 0)
{
    // Trial ended with no messages → refund both users 0.5 sparks
    await Task.WhenAll(
        _budget.RefundSparkAsync(match.UserAId, ct),
        _budget.RefundSparkAsync(match.UserBId, ct)
    );
}
```

**Path 3: EXPIRE (Balloon Expiry, MatchExpiryWorker.cs — not yet built)**
```csharp
// Nightly worker: expire balloons where ExpiresAt < now
var expired = await _db.Matches
    .Where(m => m.BalloonState == BalloonState.ACTIVE && m.ExpiresAt < now)
    .ToListAsync(ct);

foreach (var match in expired)
{
    var msgCount = await _db.ChatMessages
        .CountAsync(m => m.ThreadId == match.ChatThreadId, ct);

    if (msgCount == 0)
    {
        // Balloon expired with no messages → refund both users 0.5 sparks
        await Task.WhenAll(
            _budget.RefundSparkAsync(match.UserAId, ct),
            _budget.RefundSparkAsync(match.UserBId, ct)
        );
    }

    match.BalloonState = BalloonState.CLOSED;
    match.BalloonClosedReason = "EXPIRED";
}

await _db.SaveChangesAsync(ct);
```

---

### Refund Implementation (InteractionBudgetService.cs)

```csharp
public async Task RefundSparkAsync(int userId, CancellationToken ct = default)
{
    var today = MomentsRules.UtcToday();
    var totalKey = CacheKeys.SparkCounter(userId, today);

    // Decrement Redis counter (floor 0)
    var current = await _cache.GetCounterAsync(totalKey, ct);
    if (current > 0)
        await _cache.SetAsync(totalKey, (current - 1).ToString(), CacheTtl.UntilMidnightUtc(), ct);

    // Decrement DB daily_interactions.total_used (floor 0)
    var row = await _db.DailyInteractions
        .FirstOrDefaultAsync(x => x.UserId == userId && x.DateUtc == today, ct);

    if (row != null && row.TotalUsed > 0)
    {
        row.TotalUsed = (short)(row.TotalUsed - 1);
        row.UpdatedAt = MomentsRules.NowUtc();
        await _db.SaveChangesAsync(ct);
    }
}
```

**Dual Storage:**
- Redis: `spark:{userId}:{DateOnly}` → counter (fast reads)
- DB: `daily_interactions.total_used` → persistent record

**Floor:** Refunds never go negative (if `TotalUsed = 0`, refund is no-op).

---

### Edge Cases

#### Case 1: Both users refunded, then balloon expires
**Scenario:** User A gets 24h silent refund → User B gets 24h silent refund → balloon expires → msgCount = 2  
**Effect:** No additional refund (msgCount > 0 on expiry).

#### Case 2: Match ends immediately after first message
**Scenario:** User A sends message → User B blocks → msgCount = 1  
**Effect:** No refund (msgCount > 0).

#### Case 3: User spent 5 sparks today, refunded 0.5
**Redis State:**
- Before: `spark:123:2026-10-07 = 5`
- After: `spark:123:2026-10-07 = 4`

**Wallet State:**
- Daily budget: 5/5 used → 4/5 used (1 spark available again)

---

## Background Worker

### GhostDetectionWorker.cs

**Execution Schedule:**
```
ProcessSilentThreadsAsync()      → Every 6 hours
ProcessExpiringBalloonsAsync()   → Every 6 hours
UpdateGhostScoresAsync()         → Nightly at 03:30 UTC
```

**Implementation:**
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var lastNightlyRun = DateOnly.MinValue;

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IGhostDetectionService>();

            // Run every 6h
            await svc.ProcessSilentThreadsAsync(stoppingToken);
            await svc.ProcessExpiringBalloonsAsync(stoppingToken);

            // Run nightly at 03:30 UTC
            var nowUtc = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
            if (today > lastNightlyRun && nowUtc.TimeOfDay >= new TimeSpan(3, 30, 0))
            {
                await svc.UpdateGhostScoresAsync(stoppingToken);
                lastNightlyRun = today;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GhostWorker] Pass failed");
        }

        await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
    }
}
```

**Deployment:**
- Web pods: `WOVEN_DISABLE_BATCH_WORKERS=true` (worker disabled)
- Worker pods: env flag unset (worker enabled)

---

## Database Schema

### Users Table (GhostScore Field)
```sql
ALTER TABLE users ADD COLUMN ghost_score FLOAT DEFAULT 0.5;
ALTER TABLE users ADD COLUMN last_active_at TIMESTAMPTZ;

CREATE INDEX idx_users_ghost_score ON users(ghost_score);
```

---

## API (No Public Endpoints)

Anti-ghosting runs entirely in **background workers**. No user-facing API.

**Internal Service Methods (IGhostDetectionService):**
```csharp
Task ProcessSilentThreadsAsync(CancellationToken ct);
Task ProcessExpiringBalloonsAsync(CancellationToken ct);
Task UpdateGhostScoresAsync(CancellationToken ct);
```

---

## Monitoring

### Key Metrics
- `ghost_score_distribution` — histogram (0.1–1.0, bins=10)
- `spark_refunds_per_day` — total refunds issued (target: <5% daily active users)
- `silent_threads_detected_per_day` — 24h+ no-reply threads
- `balloon_expiry_nudges_sent_per_day` — expiry warnings
- `avg_ghost_score_active_users` — mean GhostScore (target: 0.60–0.80)
- `low_ghost_score_users` — count(GhostScore < 0.3) (target: <10% active users)

### Alerts
- `spark_refunds_per_day > 1000` → investigate silent thread threshold or balloon TTL
- `avg_ghost_score_active_users < 0.50` → investigate user engagement or matching quality
- `low_ghost_score_users > 15% active users` → investigate GhostScore formula or delivery penalty threshold

---

## Known Issues

### Issue 1: GhostScore Update is Slow (N+1 Queries)
**Current Behavior:** `UpdateGhostScoresAsync` loops over all users → N queries per user  
**Impact:** 10,000 users × 5 threads/user × 2 queries/thread = 100,000 DB queries (nightly)  
**Mitigation:** Worker runs at 03:30 UTC (low traffic)  
**Fix:** Batch SQL query (materialize thread reply counts in one pass)

### Issue 2: Silent Thread Refund Before Balloon Expires
**Scenario:** Balloon expires in 2h, but silent thread refund triggers (24h no reply)  
**Effect:** User gets 0.5 spark refund, then balloon expires → no additional refund (msgCount > 0)  
**Acceptable?** Yes. User earned the silent thread refund.

### Issue 3: Laplace Smoothing May Over-Reward New Users
**Scenario:** New user, 1 received message, 0 replied → GhostScore = 1/3 = 0.33  
**Effect:** No delivery penalty (0.33 > 0.3).  
**Acceptable?** Yes. Benefit of the doubt for new users.

---

## Future Enhancements

### Phase 4B: Adaptive Silent Threshold
- Vary 24h threshold by user behavior (responsive users → 12h threshold, ghosters → 48h threshold)
- Reduce false positives (users who take >24h to reply but still reply)

### Phase 4C: GhostScore Formula Tuning
- Add recency weighting (recent ghosting → higher penalty)
- Add severity weighting (ghost after 1 message vs 10 messages)

### Phase 4D: Delivery Penalty Calibration
- Current: GhostScore < 0.3 → 50% suppression
- Test: GhostScore < 0.3 → 75% suppression (stricter)

### Phase 4E: Match Close Refund Granularity
- Current: 0.5 spark refund (fixed)
- Future: Vary refund by balloon age (expire after 1h → 0.9 refund, expire after 47h → 0.1 refund)

---

## Related Documentation

- [README.md](./README.md) — Trust & Verification system overview
- [trust-score.md](./trust-score.md) — Trust scoring (separate from GhostScore)
- [verification.md](./verification.md) — Verified badge (not affected by GhostScore)
- [../matchmaking/delivery-boost.md](../matchmaking/delivery-boost.md) — GhostScore penalty implementation
- [../sparks/refunds.md](../sparks/refunds.md) — Spark refund logic

---

## Production Checklist

- [ ] `GhostDetectionWorker` disabled on web pods (`WOVEN_DISABLE_BATCH_WORKERS=true`)
- [ ] `GhostDetectionWorker` enabled on worker pods (env flag unset)
- [ ] Silent threshold: 24h
- [ ] Balloon expiry nudge window: 24h
- [ ] GhostScore update schedule: nightly at 03:30 UTC
- [ ] GhostScore lookback: 90 days
- [ ] Delivery penalty threshold: GhostScore < 0.3 → 50% suppression
- [ ] 0.5 spark refund wired to all 3 unmatch close paths (BLOCK, END, EXPIRE)
- [ ] Push notifications enabled: "You haven't heard back — we've refunded your spark."
- [ ] Redis cache deduplication keys: `ghost:refunded:{threadId}:{userId}`, `ghost:expiry-notified:{matchId}`
