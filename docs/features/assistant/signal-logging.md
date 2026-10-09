# Interaction Signal Logging

**Last Updated:** 2026-08-17

---

## Overview

Every user interaction with the Unified Woven Assistant is logged to the `user_interaction_logs` table for ECHO training. This document catalogs:
- All logged event types
- Context data captured
- ECHO use cases
- Implementation details

---

## Architecture

### Backend

**Endpoint:** `POST /me/interaction-log`  
**Service:** `IInteractionLogService.LogAsync()`  
**Entity:** `UserInteractionLog`

File: `backend/WovenBackend/Endpoints/InteractionEndpoints.cs`

```csharp
private static async Task<IResult> LogInteraction(
    [FromBody] LogInteractionRequest req,
    HttpContext http,
    IInteractionLogService interactionLog,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);
    await interactionLog.LogAsync(userId, req.EventType, req.Context, ct);
    return Results.Ok(new { logged = true });
}
```

### Frontend

**Service:** `PendingTaskService.logInteraction()`

File: `frontend/woven-frontend/src/app/services/pending-task.service.ts`

```typescript
logInteraction(eventType: string, context?: Record<string, any>): Observable<{logged: boolean}> {
  return this.http.post<{logged: boolean}>(`${this.api}/me/interaction-log`, {
    eventType,
    context
  });
}
```

**Usage in component:**

```typescript
await this.pendingTask.logInteraction('DailyPulseCompleted', {
  answers: { energy: 'medium', mood: 'good' }
}).toPromise();
```

---

## Event Catalog

### 1. AssistantChatOpened

**When:** User taps orb and chat sheet opens (no pending tasks, or after completing all pending tasks)

**Context:**
```json
{
  "hasPendingTasks": false
}
```

**Logged by:** `woven-assistant.component.ts:open()`

**Code:**
```typescript
this.pendingTask.logInteraction('AssistantChatOpened', {
  hasPendingTasks: this.pendingTasks.length > 0
}).subscribe();
```

**ECHO Use Cases:**
- Measure assistant engagement rate
- Correlate chat opens with user lifecycle stage
- Identify users who never use chat (potential UX issue)

---

### 2. DailyPulseCompleted

**When:** User fills out pulse + clicks "Save"

**Context:**
```json
{
  "answers": {
    "energy": "medium",
    "mood": "good",
    "connection": "seeking"
  }
}
```

**Logged by:** `woven-assistant.component.ts:onPulseSaved()`

**Code:**
```typescript
await this.pendingTask.logInteraction('DailyPulseCompleted', {
  answers
}).toPromise();
```

**ECHO Use Cases:**
- Track pulse completion rate (compliance metric)
- Analyze mood/energy patterns over time
- Correlate pulse states with match outcomes
- Adjust pulse frequency based on completion rates

**Note:** Answers are also stored in `UserDynamicIntakeSets` table. This log provides:
- Timestamp (`OccurredAt`)
- Context breadcrumb for session flow analysis

---

### 3. DailyPulseSkipped

**When:** User clicks "Skip" on pulse modal

**Context:**
```json
{
  "nextAction": "opened_next_pending"  // or "opened_assistant"
}
```

**Logged by:** `woven-assistant.component.ts:onPulseSkipped()`

**Code:**
```typescript
await this.pendingTask.logInteraction('DailyPulseSkipped', {
  nextAction: this.pendingTasks.length > 1 
    ? 'opened_next_pending' 
    : 'opened_assistant'
}).toPromise();
```

**ECHO Use Cases:**
- Survey fatigue analysis (how often users skip)
- A/B test pulse frequency (daily vs 2x/week)
- Identify chronic skippers → reduce pulse frequency
- Correlate skip rate with question types

**Why `nextAction` matters:**
- If user had more tasks pending → skip was mid-flow interruption
- If user had no more tasks → skip was final action before chat
- Different intents → different interventions

---

### 4. WeeklyCoachingDismissed

**When:** User clicks "Got it" on coaching card

**Context:**
```json
{
  "summaryId": 42
}
```

**Logged by:** `woven-assistant.component.ts:onCoachingDismissed()`

**Code:**
```typescript
await this.pendingTask.logInteraction('WeeklyCoachingDismissed', {
  summaryId: this.coachingSummary?.id
}).toPromise();
```

**ECHO Use Cases:**
- Coaching acceptance rate (dismissed = read & accepted)
- Correlate summary content with dismissal speed
- Identify high-value coaching topics (longer time-to-dismiss = more engagement)

**Note:** `CoachingSummary.DismissedAt` is also updated. This log provides:
- Cross-reference to summary content
- Session flow context (was it first or last pending task?)

---

### 5. WeeklyCoachingSkipped

**When:** User clicks "Skip" on coaching card (dismisses + disables future coaching)

**Context:**
```json
{
  "summaryId": 42,
  "nextAction": "opened_next_pending"  // or "opened_assistant"
}
```

**Logged by:** `woven-assistant.component.ts:onCoachingSkipped()`

**Code:**
```typescript
await this.pendingTask.logInteraction('WeeklyCoachingSkipped', {
  summaryId: this.coachingSummary?.id,
  nextAction: this.pendingTasks.length > 1 
    ? 'opened_next_pending' 
    : 'opened_assistant'
}).toPromise();
```

**ECHO Use Cases:**
- Coaching rejection rate (skip = don't want coaching)
- Correlate summary quality with skip rate
- Identify user segments that skip (e.g., power users vs casual users)
- Improve coaching content to reduce skips

**Why `summaryId` matters:**
- Can analyze which summary text/topics trigger skips
- A/B test summary tone (formal vs casual)
- Identify bad coaching batches (high skip rate for specific week)

---

### 6. DateFeedbackCompleted (Future)

**When:** User submits date feedback form

**Context (planned):**
```json
{
  "matchId": "abc-123",
  "metInPerson": true,
  "stars": 4,
  "timeToSubmit": 87  // seconds from open to submit
}
```

**Logged by:** `feedback.page.ts:submit()` (not yet wired to assistant)

**ECHO Use Cases:**
- Feedback completion rate (most critical signal)
- Time-to-submit analysis (rushed vs thoughtful feedback)
- Correlate feedback speed with feedback quality
- Identify users who never give feedback → intervention

**Not implemented in assistant yet.** Date Feedback is separate route (`/feedback/:matchId`).

---

### 7. DateFeedbackSkipped (Future — Controversial)

**Status:** Not implemented. No skip button exists for Date Feedback.

**If implemented:**

**Context:**
```json
{
  "matchId": "abc-123",
  "reason": "user_closed_modal"  // or "didnt_meet"
}
```

**ECHO Use Cases:**
- Loss of ground-truth outcome data (bad for ECHO)
- Could offer "Didn't meet" quick-dismiss to capture basic outcome

**Design debate:**
- **Pro:** Reduce user friction, increase completion rate for willing users
- **Con:** Loss of critical ECHO training data
- **Compromise:** Add "Didn't meet" button that auto-fills `MetInPerson = false`, still requires submit

---

## Database Schema

### Table: `user_interaction_logs`

File: `backend/WovenBackend/data/Entities/UserInteractionLog.cs`

```csharp
[Table("user_interaction_logs")]
public class UserInteractionLog
{
    [Key] public int Id { get; set; }
    public int UserId { get; set; }
    
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;
    
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    
    [Column(TypeName = "jsonb")]
    public string ContextJson { get; set; } = "{}";
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
```

### Indexes

**Migration:** `20260817051023_AddUserInteractionLogs.cs`

```csharp
migrationBuilder.CreateIndex(
    name: "ix_user_interaction_logs_user_id_occurred_at",
    table: "user_interaction_logs",
    columns: new[] { "user_id", "occurred_at" });

migrationBuilder.CreateIndex(
    name: "ix_user_interaction_logs_event_type_occurred_at",
    table: "user_interaction_logs",
    columns: new[] { "event_type", "occurred_at" });
```

**Why two indexes:**
1. `(user_id, occurred_at)` — For user timeline queries ("all events for user X")
2. `(event_type, occurred_at)` — For cohort analysis ("all DailyPulseSkipped events in last week")

---

## Service Implementation

File: `backend/WovenBackend/Services/IInteractionLogService.cs`

```csharp
public async Task LogAsync(int userId, string eventType, object? context = null, CancellationToken ct = default)
{
    try
    {
        var contextJson = context != null
            ? JsonSerializer.Serialize(context, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            : "{}";

        var log = new UserInteractionLog
        {
            UserId = userId,
            EventType = eventType,
            OccurredAt = DateTimeOffset.UtcNow,
            ContextJson = contextJson,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.UserInteractionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[InteractionLog] {EventType} | UserId={UserId}",
            eventType, userId);
    }
    catch (Exception ex)
    {
        // Non-critical — log but don't throw
        _logger.LogError(ex,
            "[InteractionLog] Failed to log event | EventType={EventType} UserId={UserId}",
            eventType, userId);
    }
}
```

**Design notes:**

### 1. Non-Throwing

Logging failure **never blocks user flow**. If DB write fails:
- Exception caught
- Error logged to Serilog
- Returns successfully (200 OK still sent to client)

**Why:** User action > logging accuracy.

### 2. JSON Serialization

Context object serialized with `camelCase` policy:

```csharp
{ PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
```

**C# input:**
```csharp
await LogAsync(userId, "DailyPulseCompleted", new {
    Answers = new { Energy = "medium", Mood = "good" }
});
```

**DB storage:**
```json
{
  "answers": {
    "energy": "medium",
    "mood": "good"
  }
}
```

**Why camelCase:** Matches frontend JSON conventions, easier to query in PostgreSQL.

### 3. Timestamps

Both `OccurredAt` and `CreatedAt` set to `UtcNow`.

**Why two fields:**
- `OccurredAt` — When event happened (semantically meaningful)
- `CreatedAt` — When row was inserted (audit trail)

In practice, they're identical. Separation allows for future scenarios where logs are queued/batched (OccurredAt ≠ CreatedAt).

---

## Context JSON Examples

### Minimal Context

```typescript
this.pendingTask.logInteraction('AssistantChatOpened', {
  hasPendingTasks: false
});
```

**DB:**
```json
{
  "hasPendingTasks": false
}
```

### Rich Context

```typescript
this.pendingTask.logInteraction('DailyPulseCompleted', {
  answers: {
    energy: 'high',
    mood: 'excited',
    connection: 'open'
  },
  timeToComplete: 12,  // seconds
  skippedQuestions: 0
});
```

**DB:**
```json
{
  "answers": {
    "energy": "high",
    "mood": "excited",
    "connection": "open"
  },
  "timeToComplete": 12,
  "skippedQuestions": 0
}
```

### Nested Context

```typescript
this.pendingTask.logInteraction('WeeklyCoachingSkipped', {
  summaryId: 42,
  nextAction: 'opened_next_pending',
  userState: {
    activeDaysThisWeek: 5,
    matchesThisWeek: 3,
    messagesThisWeek: 47
  }
});
```

**DB:**
```json
{
  "summaryId": 42,
  "nextAction": "opened_next_pending",
  "userState": {
    "activeDaysThisWeek": 5,
    "matchesThisWeek": 3,
    "messagesThisWeek": 47
  }
}
```

**Current limitation:** Frontend doesn't capture `userState` context yet. Would require additional API calls. Future enhancement.

---

## ECHO Analysis Queries

### Skip Rate by Event Type

```sql
WITH totals AS (
  SELECT
    CASE
      WHEN event_type IN ('DailyPulseCompleted', 'DailyPulseSkipped') THEN 'Pulse'
      WHEN event_type IN ('WeeklyCoachingDismissed', 'WeeklyCoachingSkipped') THEN 'Coaching'
      ELSE 'Other'
    END AS category,
    COUNT(*) FILTER (WHERE event_type LIKE '%Skipped') AS skipped,
    COUNT(*) AS total
  FROM user_interaction_logs
  WHERE occurred_at >= NOW() - INTERVAL '30 days'
  GROUP BY category
)
SELECT
  category,
  skipped,
  total,
  ROUND(100.0 * skipped / total, 2) AS skip_rate_pct
FROM totals;
```

**Output:**
```
 category  | skipped | total | skip_rate_pct 
-----------+---------+-------+---------------
 Pulse     |     142 |   523 |         27.15
 Coaching  |      18 |    89 |         20.22
```

### User Pulse Compliance

```sql
SELECT
  u.id,
  u.full_name,
  COUNT(*) FILTER (WHERE event_type = 'DailyPulseCompleted') AS completed,
  COUNT(*) FILTER (WHERE event_type = 'DailyPulseSkipped') AS skipped,
  ROUND(
    100.0 * COUNT(*) FILTER (WHERE event_type = 'DailyPulseCompleted') /
    NULLIF(COUNT(*), 0),
    2
  ) AS completion_rate_pct
FROM users u
LEFT JOIN user_interaction_logs uil ON uil.user_id = u.id
  AND uil.event_type IN ('DailyPulseCompleted', 'DailyPulseSkipped')
  AND uil.occurred_at >= NOW() - INTERVAL '30 days'
GROUP BY u.id, u.full_name
HAVING COUNT(*) > 0
ORDER BY completion_rate_pct DESC;
```

### Coaching Summary Effectiveness

```sql
SELECT
  cs.id AS summary_id,
  cs.summary_text,
  COUNT(*) FILTER (WHERE uil.event_type = 'WeeklyCoachingDismissed') AS dismissed,
  COUNT(*) FILTER (WHERE uil.event_type = 'WeeklyCoachingSkipped') AS skipped,
  ROUND(
    100.0 * COUNT(*) FILTER (WHERE uil.event_type = 'WeeklyCoachingDismissed') /
    NULLIF(COUNT(*), 0),
    2
  ) AS acceptance_rate_pct
FROM coaching_summaries cs
LEFT JOIN user_interaction_logs uil ON (uil.context_json->>'summaryId')::int = cs.id
WHERE cs.delivered_at >= NOW() - INTERVAL '30 days'
GROUP BY cs.id, cs.summary_text
ORDER BY acceptance_rate_pct ASC;
```

**Use case:** Identify low-acceptance summaries → analyze text patterns → improve future summaries.

---

## Privacy & Retention

### PII in Context JSON

**Rule:** No raw PII in `ContextJson`.

**Allowed:**
- IDs (`matchId`, `summaryId`, `cycleId`)
- Enumerated values (`energy: "medium"`, `nextAction: "opened_assistant"`)
- Aggregate counts (`timeToComplete: 12`)

**Forbidden:**
- Free text (`freeResponse: "I feel lonely today"`) — use separate table with encryption
- Partner names (`partnerName: "Sarah"`) — use partner ID
- Location data (`lat/lng`) — use city ID or geohash

**Current compliance:** All events use only IDs + enums. ✅

### Retention Policy

**Not implemented yet.** Consider:

```sql
DELETE FROM user_interaction_logs
WHERE occurred_at < NOW() - INTERVAL '2 years';
```

Run quarterly via cron job. Keep 2 years for ECHO trend analysis, discard older data.

---

## Error Handling

### Backend Logging Failure

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "[InteractionLog] Failed | EventType={EventType}", eventType);
    // Swallows exception — returns 200 OK to client
}
```

**Effect:** Frontend always receives `{ logged: true }`, even if DB write failed.

**Why:** Prevent user-facing errors for non-critical logging.

**Trade-off:** Silent data loss possible. Monitor Serilog error rate.

### Frontend HTTP Failure

```typescript
await this.pendingTask.logInteraction('DailyPulseSkipped', { ... }).toPromise();
```

**If network error:**
- `.toPromise()` throws
- No `try/catch` in component
- **Unhandled promise rejection** → logged to browser console
- **User flow continues** (auto-advance still happens)

**Why this is okay:**
- Logging failure doesn't block UX
- Error visible in console for debugging
- Production error tracking (Sentry) captures unhandled rejections

**Future improvement:**
```typescript
try {
  await this.pendingTask.logInteraction(...).toPromise();
} catch {
  // Silent fail — logging is best-effort
}
```

---

## Testing

### Unit Tests (Backend)

**Test cases:**
- ✅ Log with null context → stores `"{}"`
- ✅ Log with rich context → JSON serialized correctly
- ✅ DB write failure → exception swallowed, error logged
- ✅ Multiple logs for same user → all inserted

### Integration Tests (E2E)

**Test cases:**
- ✅ Complete pulse → `DailyPulseCompleted` row exists in DB
- ✅ Skip coaching → `WeeklyCoachingSkipped` row exists
- ✅ Open chat → `AssistantChatOpened` row exists
- ✅ Context JSON matches sent payload

### Manual Testing Checklist

- [ ] Open assistant → Check logs for `AssistantChatOpened`
- [ ] Complete pulse → Check logs for `DailyPulseCompleted` with answers
- [ ] Skip pulse → Check logs for `DailyPulseSkipped` with nextAction
- [ ] Dismiss coaching → Check logs for `WeeklyCoachingDismissed` with summaryId
- [ ] Skip coaching → Check logs for `WeeklyCoachingSkipped`
- [ ] Verify timestamps are UTC
- [ ] Verify context JSON is camelCase
- [ ] Verify no PII in context

---

## Future Enhancements

### 1. Frontend Queueing

**Problem:** Network offline → log events lost.

**Solution:** Queue events in `IndexedDB`, retry on reconnect.

```typescript
class OfflineLogQueue {
  async add(eventType: string, context: any) {
    await this.idb.logs.add({ eventType, context, timestamp: Date.now() });
  }

  async flush() {
    const pending = await this.idb.logs.toArray();
    for (const log of pending) {
      await this.http.post('/me/interaction-log', log).toPromise();
      await this.idb.logs.delete(log.id);
    }
  }
}
```

### 2. Batch Logging

**Problem:** 5 events in quick succession → 5 HTTP requests.

**Solution:** Batch events, send every 5s or on page unload.

```typescript
POST /me/interaction-log/batch
{
  "events": [
    { "eventType": "DailyPulseCompleted", "context": {...}, "occurredAt": "..." },
    { "eventType": "AssistantChatOpened", "context": {...}, "occurredAt": "..." }
  ]
}
```

### 3. Real-Time ECHO Feedback

**Current:** Logs analyzed in batch workers (nightly/weekly).

**Future:** Stream logs to real-time ECHO service:
- User skips pulse 3x in a row → reduce pulse frequency immediately
- User dismisses coaching quickly (<10s) → mark summary as low-engagement

Requires WebSocket or Server-Sent Events.

---

## Related Docs

- [pending-tasks.md](./pending-tasks.md) — What triggers log events
- [auto-advance.md](./auto-advance.md) — When logs are sent (before or after advance)
- [backend.md](./backend.md) — Backend endpoint details
- [frontend.md](./frontend.md) — Frontend service usage
