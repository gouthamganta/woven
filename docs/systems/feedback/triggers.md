# Feedback Triggers

**Purpose:** Logic for determining when to request date feedback from users.

---

## Trigger Types

### 1. Primary: `interested_both`

**Condition:** Both users expressed interest in meeting up, 5+ days ago, no feedback collected yet.

**SQL Logic:**
```csharp
var primaryCutoff = DateTimeOffset.UtcNow.AddDays(-5);

var primaryMatches = await _db.Matches.AsNoTracking()
    .Where(m => m.DateIdeaInterestedA == true
             && m.DateIdeaInterestedB == true
             && m.DateIdeaInterestedAt != null
             && m.DateIdeaInterestedAt < primaryCutoff
             && m.ClosedReason != ClosedReason.BLOCK
             && !existingMatchIds.Contains(m.Id))
    .Select(m => new { m.Id, m.UserAId, m.UserBId })
    .ToListAsync(ct);
```

**Explanation:**
- Strongest signal: both users said "yes" to date suggestions
- 5-day delay ensures date likely happened
- Blocked matches excluded (user won't see feedback form)
- Deduped against existing prompts (any trigger type)

**Expected Volume:** ~10-20% of matches with date interest expressed.

---

### 2. Secondary: `deep_chat`

**Condition:** 25+ messages, fast responses (< 2 hours avg), 10+ days since unlock, no date scheduled.

**SQL Logic:**
```csharp
var deepChatCutoff = DateTimeOffset.UtcNow.AddDays(-10);

var secondaryMatches = await (
    from m in _db.Matches.AsNoTracking()
    join t in _db.ChatThreads.AsNoTracking() on m.Id equals t.MatchId
    where t.MessageCount > 25
       && t.AvgResponseTimeMs < 7200000  // 2 hours in ms
       && m.FindLoveAt != null
       && m.FindLoveAt < deepChatCutoff
       && m.ClosedReason != ClosedReason.BLOCK
       && !(m.ClosedReason == ClosedReason.EXPIRE && t.MessageCount < 5)
       && !existingMatchIds.Contains(m.Id)
    select new { m.Id, m.UserAId, m.UserBId }
).ToListAsync(ct);
```

**Explanation:**
- Engaged conversations without explicit date confirmation
- `FindLoveAt` = when match was unlocked (balloon popped)
- Fast response time indicates genuine interest
- Excludes expired matches with minimal engagement (< 5 messages)
- Likely met IRL but didn't log it via date interest button

**Expected Volume:** ~5-10% of deep matches.

---

### 3. Tertiary: `silent_thread`

**Condition:** Active chat that went silent 5+ days ago (conversation faded).

**SQL Logic:**
```csharp
var silentCutoff = DateTimeOffset.UtcNow.AddDays(-5);

var silentThreadMatches = await (
    from m in _db.Matches.AsNoTracking()
    join t in _db.ChatThreads.AsNoTracking() on m.Id equals t.MatchId
    where m.BalloonState == BalloonState.ACTIVE
       && m.BothMessagedAt != null
       && t.LastMessageAt != null
       && t.LastMessageAt < silentCutoff
       && m.ClosedReason != ClosedReason.BLOCK
       && !silentPromptedMatchIds.Contains(m.Id)
    select new { m.Id, m.UserAId, m.UserBId }
).ToListAsync(ct);
```

**Explanation:**
- Thread still open (`BalloonState.ACTIVE`)
- Both users sent at least one message (`BothMessagedAt` set)
- No messages in 5+ days → conversation faded
- **Separate dedup list** (`silentPromptedMatchIds`) — doesn't block future post-date prompts
- Useful for learning why conversations die (safety signal)

**Expected Volume:** ~20-30% of active matches go silent.

---

## Deduplication Strategy

### Primary/Secondary Share Dedup List
```csharp
var existingMatchIds = await _db.DateFeedbackPrompts.AsNoTracking()
    .Select(p => p.MatchId)
    .Distinct()
    .ToListAsync(ct);
```

- A match gets **at most one** `interested_both` or `deep_chat` prompt
- Once user submits feedback, no more prompts for that match

### Silent Thread Uses Separate List
```csharp
var silentPromptedMatchIds = await _db.DateFeedbackPrompts.AsNoTracking()
    .Where(p => p.TriggerType == "silent_thread")
    .Select(p => p.MatchId)
    .Distinct()
    .ToListAsync(ct);
```

**Why Separate?**
- User may get `silent_thread` prompt → conversation resumes → they meet IRL → get `interested_both` prompt
- Allows post-date feedback even if we already asked about silent thread
- Silent prompts only block future silent prompts (not date prompts)

---

## Execution Schedule

### Worker: `FeedbackTriggerWorker`
**Runs Daily:** 08:00 UTC  
**Check Interval:** Every 30 minutes

```csharp
private static readonly TimeSpan DailyTarget = new(8, 0, 0); // 08:00 UTC

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
- Runs once per day at/after 08:00 UTC
- `lastRunDate` prevents multiple runs on same day

---

## Prompt Creation

### Immediate Scheduling
```csharp
var now = DateTimeOffset.UtcNow;

foreach (var match in primaryMatches)
{
    _db.DateFeedbackPrompts.Add(new DateFeedbackPrompt
    {
        MatchId = match.Id, 
        UserId = match.UserAId,
        TriggerType = "interested_both", 
        ScheduledFor = now  // Send immediately
    });
    _db.DateFeedbackPrompts.Add(new DateFeedbackPrompt
    {
        MatchId = match.Id, 
        UserId = match.UserBId,
        TriggerType = "interested_both", 
        ScheduledFor = now
    });
}
```

**Why Two Prompts Per Match?**
- Feedback is per-user (both sides need to respond)
- Each user gets their own push notification
- Responses are independent (User A can respond without User B)

---

## Sending Logic

### Due Prompts Filter
```csharp
var due = await _db.DateFeedbackPrompts
    .Where(p => p.ScheduledFor <= now 
             && p.SentAt == null 
             && p.RescheduleCount <= 2)
    .ToListAsync(ct);
```

**Criteria:**
- `ScheduledFor <= now` — time to send
- `SentAt == null` — not sent yet (or rescheduled)
- `RescheduleCount <= 2` — max 2 retries

### Push Notification
```csharp
var partnerName = await _db.Users.AsNoTracking()
    .Where(u => u.Id == partnerId)
    .Select(u => u.FullName)
    .FirstOrDefaultAsync(ct) ?? "your match";

var firstName = partnerName.Split(' ')[0];

await _notify.SendPushAsync(prompt.UserId,
    $"How did it go with {firstName}? We'd love to know 💙", ct);

prompt.SentAt = now;
```

**Notification Text:**
```
How did it go with Sarah? We'd love to know 💙
```

---

## Reschedule Strategy

### 3-Day Silence Window
```csharp
var cutoff = DateTimeOffset.UtcNow.AddDays(-3);

var toReschedule = await _db.DateFeedbackPrompts
    .Where(p => p.SentAt != null
             && p.RespondedAt == null
             && p.SentAt < cutoff
             && p.RescheduleCount < 2)
    .ToListAsync(ct);

foreach (var p in toReschedule)
{
    p.RescheduleCount++;
    p.ScheduledFor = now.AddDays(5);  // Try again in 5 days
    p.SentAt = null;  // Reset so SendDuePromptsAsync picks it up
}
```

**Timeline:**
1. Day 0: Initial prompt sent
2. Day 3: No response → reschedule for Day 8
3. Day 8: Second reminder sent
4. Day 11: No response → reschedule for Day 16
5. Day 16: Third reminder sent
6. Day 19: No response → give up

**Max Attempts:** 3 notifications total (initial + 2 reschedules)

### Expiration
```csharp
var toExpire = await _db.DateFeedbackPrompts
    .Where(p => p.SentAt != null
             && p.RespondedAt == null
             && p.RescheduleCount >= 2
             && p.SentAt < cutoff)
    .ToListAsync(ct);

foreach (var p in toExpire)
    p.SentAt = now;  // Mark as sent so it won't reschedule again
```

**Behavior:**
- Exhausted prompts (2 reschedules) marked with fresh `SentAt`
- No longer appear in reschedule query
- Never deleted (useful for analytics: response rate by attempt)

---

## Edge Cases

### Match Closed Mid-Feedback
**Scenario:** User A closes/blocks match after prompt sent but before User B responds.

**Handling:**
- Prompt still exists in DB
- User B can still submit feedback (useful for safety signals)
- `CheckBadRatingPatternAsync()` still runs (may flag User A)

### Date Interest Retracted
**Scenario:** User changes `DateIdeaInterestedA` back to `false` after prompt queued.

**Handling:**
- Prompt already created, won't be deleted
- Next day's trigger run won't create duplicate (dedup list blocks it)
- User can still provide feedback (reflects changed circumstances)

### Both Users Respond on Same Day
**Scenario:** Prompt sent to both User A and User B → both respond within hours.

**Handling:**
- Two separate `DateFeedback` records (one per user)
- Two separate `MatchSignal` logs
- Both trigger `FeedbackInsightService` independently
- `CheckBadRatingPatternAsync()` runs for each user (cross-checks ratings)

---

## Performance Considerations

### Query Optimization
```sql
-- Primary trigger query uses indexes:
CREATE INDEX idx_matches_date_interest 
    ON matches(date_idea_interested_at, closed_reason)
    WHERE date_idea_interested_a = true 
      AND date_idea_interested_b = true;

-- Deep chat trigger uses join + indexes:
CREATE INDEX idx_chat_threads_message_stats 
    ON chat_threads(message_count, avg_response_time_ms);

-- Silent thread trigger:
CREATE INDEX idx_chat_threads_last_message 
    ON chat_threads(last_message_at);
```

### Expected Daily Volume
- **Active users:** ~10,000
- **New matches/day:** ~500
- **Prompts queued/day:** ~100-150 (10-15% of matches)
- **Push notifications sent:** ~100-150
- **Rescheduled prompts:** ~30-40
- **Responses collected:** ~40-60 (40% response rate)

**DB Impact:** Minimal. One batch insert, one batch update, no locking.

---

## Monitoring

### Key Metrics
```csharp
_logger.LogInformation(
    "[FeedbackQueue] Queued {P} primary + {S} secondary + {T} silent-thread match prompts",
    primaryMatches.Count, secondaryMatches.Count, silentThreadMatches.Count);
```

**Alerts:**
- No prompts queued for 3+ days → trigger logic broken
- Response rate < 20% → notification fatigue (reduce frequency)
- Reschedule rate > 70% → timing wrong (adjust cutoffs)

---

**Last Updated:** 2026-10-07
**Related:** [workers.md](./workers.md), [README.md](./README.md)
