# Analytics Implementation Guide

How to integrate analytics tracking into Woven's backend and frontend.

## Quick Start

### Backend Tracking

```csharp
// Inject services
public class MomentsEndpoints
{
    private readonly IInteractionLogService _interactionLog;
    private readonly IMatchSignalService _matchSignal;
    private readonly IAnalyticsService _analytics;

    // Use in endpoints...
}
```

**Option 1: User Interaction Log** (product analytics)

```csharp
await _interactionLog.LogAsync(
    userId: userId,
    eventType: "moments_deck_viewed",
    context: new { deckId, itemCount }
);
```

**Option 2: Analytics Event** (privacy-safe, cohort tracking)

```csharp
await _analytics.TrackAsync(
    userId: userId,
    sessionId: null, // auto-generated
    eventType: AnalyticsEvents.MomentResponded,
    properties: new { action = "magical", sparkCost = 1 }
);
```

**Option 3: Match Signal** (ECHO learning)

```csharp
await _matchSignal.RecordAsync(
    viewerId: userId,
    candidateId: matchId,
    eventType: MatchSignalEventTypes.TrialAccepted,
    eventValue: 1.0f
);
```

### Frontend Tracking

**Not implemented yet** — frontend currently does not track analytics directly.

Planned: HTTP interceptor to track navigation, feature usage.

---

## Service Interfaces

### IInteractionLogService

**Path:** `backend/WovenBackend/Services/IInteractionLogService.cs`

```csharp
public interface IInteractionLogService
{
    Task LogAsync(
        int userId, 
        string eventType, 
        object? context = null, 
        CancellationToken ct = default
    );
}
```

**Parameters:**
- `userId` — Direct user FK (integer)
- `eventType` — Free-form string (recommend constants)
- `context` — Optional JSON payload (any object)
- `ct` — Cancellation token

**Storage:** `user_interaction_logs` table

**Privacy:** Medium (direct user ID)

**Use when:** Product analytics, feature usage, debugging

### IAnalyticsService

**Path:** `backend/WovenBackend/Services/Analytics/IAnalyticsService.cs`

```csharp
public interface IAnalyticsService
{
    Task TrackAsync(
        int? userId, 
        string? sessionId, 
        string eventType, 
        object? properties = null, 
        CancellationToken ct = default
    );

    Task<string> GetOrAssignVariantAsync(
        int userId, 
        string experimentId, 
        CancellationToken ct = default
    );

    Task TrackAbConversionAsync(
        int userId, 
        string experimentId, 
        string conversionType, 
        CancellationToken ct = default
    );
}
```

**TrackAsync parameters:**
- `userId` — Hashed before storage (SHA-256)
- `sessionId` — Optional (auto-generated if null)
- `eventType` — Constant from `AnalyticsEvents`
- `properties` — Optional JSON payload (any object)

**Storage:** `analytics_events` table

**Privacy:** High (hashed user ID, 12mo retention)

**Use when:** Retention analysis, cohort tracking, A/B testing

### IMatchSignalService

**Path:** `backend/WovenBackend/Services/IMatchSignalService.cs`

```csharp
public interface IMatchSignalService
{
    Task RecordAsync(
        int viewerId,
        int candidateId,
        string eventType,
        float eventValue,
        string? metadataJson = null,
        CancellationToken ct = default
    );
}
```

**Parameters:**
- `viewerId` — User performing action
- `candidateId` — User being acted upon (directional)
- `eventType` — Constant from `MatchSignalEventTypes`
- `eventValue` — Numeric payload (interpretation varies)
- `metadataJson` — Optional extra context (JSON string)

**Storage:** `match_signal_logs` table

**Privacy:** Low (direct user IDs, indefinite retention)

**Use when:** ECHO weight learning, preference extraction

---

## Event Catalog Reference

**See:** [`events.md`](./events.md) for full catalog

### AnalyticsEvents Constants

**Path:** `backend/WovenBackend/Services/Analytics/AnalyticsEvents.cs`

```csharp
public static class AnalyticsEvents
{
    // User lifecycle
    public const string UserRegistered              = "user_registered";
    public const string OnboardingStepCompleted     = "onboarding_step_completed";
    public const string OnboardingAbandoned         = "onboarding_abandoned";
    
    // Discovery
    public const string MomentsDeckViewed           = "moments_deck_viewed";
    public const string MomentResponded             = "moment_responded";
    public const string TileViewed                  = "tile_viewed";
    
    // Matching
    public const string MatchCreated                = "match_created";
    public const string BalloonTimerStarted         = "balloon_timer_started";
    
    // ... 75+ more events
}
```

### MatchSignalEventTypes Constants

**Path:** `backend/WovenBackend/Data/Entities/MatchSignalLog.cs`

```csharp
public static class MatchSignalEventTypes
{
    public const string TileDwell              = "TileDwell";
    public const string BalloonPop            = "BalloonPop";
    public const string TrialAccepted         = "TrialAccepted";
    public const string TimeToFirstMessageMs  = "TimeToFirstMessageMs";
    public const string VoiceNoteListenComplete = "VoiceNoteListenComplete";
    
    // ... 30+ more signals
}
```

---

## Common Patterns

### Pattern 1: Track User Action

**When:** User performs discrete action (swap, send message, etc.)

```csharp
// POST /moments/respond
private static async Task<IResult> RespondToMoment(
    HttpContext http,
    WovenDbContext db,
    IInteractionLogService interactionLog,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);
    
    // ... perform action ...
    
    // Track interaction
    await interactionLog.LogAsync(userId, "moment_responded", new
    {
        momentId,
        action = "magical",
        sparkCost = 1
    });
    
    return Results.Ok();
}
```

### Pattern 2: Track Privacy-Safe Event

**When:** Cohort tracking, A/B testing

```csharp
// POST /auth/register
private static async Task<IResult> Register(
    WovenDbContext db,
    IAnalyticsService analytics,
    CancellationToken ct)
{
    // ... create user ...
    
    // Track registration (privacy-safe)
    await analytics.TrackAsync(
        userId: newUser.Id,
        sessionId: null,
        eventType: AnalyticsEvents.UserRegistered,
        properties: new { method = "email", source = "organic" }
    );
    
    return Results.Ok();
}
```

### Pattern 3: Track Match Signal (ECHO)

**When:** Behavioral signal between matched users

```csharp
// POST /matches/{matchId}/pop
private static async Task<IResult> PopBalloon(
    int matchId,
    WovenDbContext db,
    IMatchSignalService matchSignal,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);
    var match = await db.Matches.FindAsync(matchId);
    
    var partnerId = match.UserAId == userId ? match.UserBId : match.UserAId;
    
    // Track balloon pop signal
    await matchSignal.RecordAsync(
        viewerId: userId,
        candidateId: partnerId,
        eventType: MatchSignalEventTypes.BalloonPop,
        eventValue: 1.0f
    );
    
    // ... proceed with trial start ...
}
```

### Pattern 4: Track Trial Decision

**When:** User accepts/rejects trial

```csharp
// POST /chats/{threadId}/trial-decision
private static async Task<IResult> SubmitTrialDecision(
    string threadId,
    WovenDbContext db,
    IMatchSignalService matchSignal,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);
    var match = await db.Matches.Where(m => m.ThreadId == threadId).FirstAsync();
    var partnerId = match.UserAId == userId ? match.UserBId : match.UserAId;
    
    if (request.Decision == "CONTINUE")
    {
        await matchSignal.RecordAsync(userId, partnerId, 
            MatchSignalEventTypes.TrialAccepted, 1.0f);
    }
    else if (request.Decision == "END")
    {
        // Log rejection
        await matchSignal.RecordAsync(userId, partnerId, 
            MatchSignalEventTypes.TrialRejected, 0.0f);
        
        // Log specific reason
        var reasonEvent = request.Reason switch
        {
            "no_spark" => MatchSignalEventTypes.TrialEndedNoSpark,
            "wrong_timing" => MatchSignalEventTypes.TrialEndedWrongTiming,
            "not_my_type" => MatchSignalEventTypes.TrialEndedNotMyType,
            _ => null
        };
        
        if (reasonEvent != null)
        {
            await matchSignal.RecordAsync(userId, partnerId, reasonEvent, 1.0f);
        }
    }
    
    return Results.Ok();
}
```

### Pattern 5: Track Voice Note Engagement

**When:** User plays voice note to completion

```csharp
// POST /chats/{threadId}/messages/{messageId}/voice-listened
private static async Task<IResult> TrackVoiceListened(
    string threadId,
    int messageId,
    WovenDbContext db,
    IMatchSignalService matchSignal,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);
    var message = await db.ChatMessages.FindAsync(messageId);
    
    // Track listen complete signal
    await matchSignal.RecordAsync(
        viewerId: userId,
        candidateId: message.SenderId,
        eventType: MatchSignalEventTypes.VoiceNoteListenComplete,
        eventValue: 1.0f
    );
    
    // Check for mutual voice exchange
    var sentVoice = await db.ChatMessages
        .AnyAsync(m => m.ThreadId == threadId 
                    && m.SenderId == userId 
                    && m.MessageType == "VOICE");
    
    if (sentVoice)
    {
        await matchSignal.RecordAsync(userId, message.SenderId,
            MatchSignalEventTypes.MutualVoiceExchange, 1.0f);
    }
    
    return Results.Ok();
}
```

---

## A/B Testing

### Creating an Experiment

**Step 1: Add to database**

```sql
INSERT INTO ab_experiments (id, variants, is_active)
VALUES ('new_deck_algorithm', '["control","treatment"]', true);
```

**Step 2: Assign users**

```csharp
var variant = await _analytics.GetOrAssignVariantAsync(userId, "new_deck_algorithm");
// Returns: "control" | "treatment"
```

**Properties:**
- Deterministic (same user → same variant)
- Permanent (assignment persists)
- Idempotent (safe to call multiple times)

**Step 3: Implement variant logic**

```csharp
if (variant == "treatment")
{
    // Use new algorithm
    deck = await _deckService.BuildDeckV2(userId);
}
else
{
    // Use control algorithm
    deck = await _deckService.BuildDeckV1(userId);
}
```

**Step 4: Track conversions**

```csharp
// When user gets first match
await _analytics.TrackAbConversionAsync(userId, "new_deck_algorithm", "first_match");
```

### Analyzing Results

**Query:**

```sql
-- Experiment results
SELECT 
  a.variant,
  COUNT(DISTINCT a.user_id) AS assigned_ct,
  COUNT(DISTINCT c.user_id) AS converted_ct,
  ROUND(100.0 * COUNT(DISTINCT c.user_id) / COUNT(DISTINCT a.user_id), 2) AS conversion_pct
FROM ab_assignments a
LEFT JOIN ab_conversions c ON a.user_id = c.user_id 
                            AND a.experiment_id = c.experiment_id
WHERE a.experiment_id = 'new_deck_algorithm'
GROUP BY a.variant;
```

**Expected output:**

| variant | assigned_ct | converted_ct | conversion_pct |
|---------|-------------|--------------|----------------|
| control | 1,250 | 320 | 25.60 |
| treatment | 1,280 | 410 | 32.03 |

**Decision:** Treatment wins (32% vs 26% conversion)

### Ending an Experiment

**Step 1: Deactivate**

```sql
UPDATE ab_experiments
SET is_active = false
WHERE id = 'new_deck_algorithm';
```

**Step 2: Remove variant logic**

```csharp
// Before (A/B test)
var variant = await _analytics.GetOrAssignVariantAsync(userId, "new_deck_algorithm");
if (variant == "treatment") { ... }

// After (winner deployed)
deck = await _deckService.BuildDeckV2(userId); // Treatment becomes default
```

**Step 3: Archive results**

```sql
-- Export to analytics_events for historical record
INSERT INTO analytics_events (user_id_hash, event_type, properties)
SELECT 
  PiiSanitizer.HashForAudit(user_id),
  'ab_experiment_completed',
  jsonb_build_object(
    'experimentId', 'new_deck_algorithm',
    'variant', variant,
    'result', 'treatment_won'
  )
FROM ab_assignments
WHERE experiment_id = 'new_deck_algorithm';
```

---

## Error Handling

### Pattern: Fail-Silent Tracking

**Never block user actions on analytics failures:**

```csharp
try
{
    await _interactionLog.LogAsync(userId, eventType, context);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "[Analytics] Failed to log event {EventType}", eventType);
    // Continue with user action
}
```

**Better:** Use fire-and-forget pattern (built into services)

```csharp
// AnalyticsService.TrackAsync already wraps in Task.Run
await _analytics.TrackAsync(userId, sessionId, eventType, properties);
// Returns immediately, tracks in background
```

### Common Errors

**1. Database timeout**

```
[Analytics] TrackAsync failed for event moment_responded
Npgsql.NpgsqlException: Exception while reading from stream
```

**Solution:** Fire-and-forget pattern already handles this (logged but not thrown)

**2. Invalid event type**

```
[Analytics] Unknown event type: moments_deck_view
```

**Solution:** Use constants from `AnalyticsEvents` or `MatchSignalEventTypes`

**3. Properties too large**

```
[Analytics] Properties exceed 1KB limit
```

**Solution:** Keep event properties small (<1KB recommended)

---

## Testing

### Unit Tests

**Example: Test tracking logic**

```csharp
[Fact]
public async Task TrackAsync_ValidEvent_Succeeds()
{
    // Arrange
    var db = GetInMemoryDb();
    var logger = new NullLogger<InteractionLogService>();
    var service = new InteractionLogService(db, logger);
    
    // Act
    await service.LogAsync(userId: 1, eventType: "test_event", context: new { foo = "bar" });
    
    // Assert
    var logged = await db.UserInteractionLogs.FirstAsync();
    Assert.Equal("test_event", logged.EventType);
    Assert.Contains("\"foo\":\"bar\"", logged.ContextJson);
}
```

**Example: Test A/B assignment**

```csharp
[Fact]
public async Task GetOrAssignVariantAsync_NewUser_AssignsVariant()
{
    // Arrange
    var db = GetInMemoryDb();
    db.AbExperiments.Add(new AbExperiment { Id = "test_exp", IsActive = true });
    await db.SaveChangesAsync();
    
    var service = new AnalyticsService(scopeFactory, cache, config, logger);
    
    // Act
    var variant = await service.GetOrAssignVariantAsync(userId: 1, "test_exp");
    
    // Assert
    Assert.Contains(variant, new[] { "control", "treatment" });
    
    // Act again (idempotent)
    var variant2 = await service.GetOrAssignVariantAsync(userId: 1, "test_exp");
    
    // Assert same variant
    Assert.Equal(variant, variant2);
}
```

### Integration Tests

**Example: Test signal recording**

```csharp
[Fact]
public async Task RecordAsync_TrialAccepted_CreatesSignal()
{
    // Arrange
    var db = GetRealDb();
    var service = new MatchSignalService(db, logger);
    
    // Act
    await service.RecordAsync(
        viewerId: 1,
        candidateId: 2,
        eventType: MatchSignalEventTypes.TrialAccepted,
        eventValue: 1.0f
    );
    
    // Assert
    var signal = await db.MatchSignalLogs.FirstAsync();
    Assert.Equal(1, signal.ViewerId);
    Assert.Equal(2, signal.CandidateId);
    Assert.Equal("TrialAccepted", signal.EventType);
    Assert.Equal(1.0f, signal.EventValue);
}
```

---

## Performance Considerations

### Background Execution

All analytics services use fire-and-forget pattern:

```csharp
_ = Task.Run(async () =>
{
    // Track event in background
});

return Task.CompletedTask; // Immediate return
```

**Benefits:**
- User actions not blocked
- Endpoint latency unaffected
- DB timeouts don't fail requests

**Trade-offs:**
- Some events may be lost (acceptable)
- No guarantees on delivery timing
- Cannot await tracking result

### Bulk Inserts

For high-volume tracking (games, voice notes):

```csharp
// Batch signals instead of one-by-one
var signals = new List<MatchSignalLog>();
foreach (var round in game.Rounds)
{
    signals.Add(new MatchSignalLog
    {
        ViewerId = userId,
        CandidateId = partnerId,
        EventType = MatchSignalEventTypes.FlagAgreementRate,
        EventValue = round.AgreementScore
    });
}

db.MatchSignalLogs.AddRange(signals);
await db.SaveChangesAsync();
```

### Index Optimization

**Current indexes:**

```sql
-- user_interaction_logs
CREATE INDEX idx_user_interaction_logs_user_occurred 
    ON user_interaction_logs(user_id, occurred_at DESC);

-- analytics_events
CREATE INDEX idx_analytics_events_created 
    ON analytics_events(created_at DESC);
CREATE INDEX idx_analytics_events_type_created 
    ON analytics_events(event_type, created_at DESC);

-- match_signal_logs
CREATE INDEX idx_match_signal_logs_viewer_candidate 
    ON match_signal_logs(viewer_id, candidate_id, occurred_at DESC);
CREATE INDEX idx_match_signal_logs_type_occurred 
    ON match_signal_logs(event_type, occurred_at DESC);
```

**Query patterns:**
- Most queries filter by user + time range
- ECHO queries filter by event type + time range
- Retention queries use created_at DESC

---

## Related Docs

- [Analytics System Overview](./README.md)
- [Event Catalog](./events.md)
- [Retention Tracking](./retention.md)
- [Workers](./workers.md)
- [Privacy Approach](./privacy.md)
- [ECHO System](../../systems/echo/README.md)
