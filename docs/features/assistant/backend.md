# Assistant Backend Architecture

**Last Updated:** 2026-08-17

---

## Overview

This document covers the backend implementation of the Unified Woven Assistant:
- REST endpoints for pending tasks and interaction logging
- Database entities and migrations
- Service layer implementation
- SQL query patterns

---

## Endpoints

### File Location

`backend/WovenBackend/Endpoints/InteractionEndpoints.cs`

### Endpoint Registration

```csharp
public static void MapInteractionEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/me").RequireAuthorization();

    group.MapGet("/pending-tasks", GetPendingTasks);
    group.MapPost("/interaction-log", LogInteraction);
}
```

**Location:** Lines 11-20

**Authorization:** All endpoints require JWT authentication (`.RequireAuthorization()`).

**Route Group:** `/me` prefix indicates user-scoped endpoints (returns data for authenticated user only).

---

## GET /me/pending-tasks

### Endpoint Signature

```csharp
private static async Task<IResult> GetPendingTasks(
    HttpContext http,
    WovenDbContext db,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);
    // ...
}
```

**Location:** Lines 25-29

**Dependency Injection:**
- `HttpContext` — For JWT claims extraction
- `WovenDbContext` — EF Core database context
- `CancellationToken` — Cancellation token for async operations

**User ID Extraction:**

```csharp
var userId = EndpointHelper.GetUserId(http.User);
```

**Location:** Line 30

**Implementation:** `EndpointHelper.GetUserId()` reads JWT claim `"uid"`, falls back to `"sub"` and `ClaimTypes.NameIdentifier`. Throws `UnauthorizedAccessException` if all claims missing or invalid (mapped to HTTP 401 by `AuthExceptionHandler`).

### Task Detection Logic

Endpoint runs **3 sequential queries** to detect pending tasks:

#### 1. Date Feedback Detection

```csharp
// Check for Date Feedback pending (highest priority)
var hasFeedbackPending = await db.Matches
    .Where(m => (m.UserAId == userId || m.UserBId == userId)
        && m.BalloonState == BalloonState.CLOSED
        && !db.Set<DateFeedback>()
            .Any(f => f.MatchId == m.Id && f.UserId == userId))
    .AnyAsync(ct);
```

**Location:** Lines 34-40

**Condition:** User has at least one match where:
- User is either `UserA` or `UserB`
- Balloon state is `CLOSED` (match connection window ended)
- No `DateFeedback` record exists for this match + user combination

**If true, fetch match details:**

```csharp
if (hasFeedbackPending)
{
    var match = await db.Matches
        .Where(m => (m.UserAId == userId || m.UserBId == userId)
            && m.BalloonState == BalloonState.CLOSED
            && !db.Set<DateFeedback>()
                .Any(f => f.MatchId == m.Id && f.UserId == userId))
        .OrderByDescending(m => m.ClosedAt)
        .FirstAsync(ct);

    // Get partner name
    var partnerId = match.UserAId == userId ? match.UserBId : match.UserAId;
    var partner = await db.Users
        .Where(u => u.Id == partnerId)
        .Select(u => u.FullName)
        .FirstOrDefaultAsync(ct);

    tasks.Add(new
    {
        type = "date_feedback",
        priority = 1,
        data = new
        {
            matchId = match.Id,
            partnerName = partner ?? "Someone"
        }
    });
}
```

**Location:** Lines 42-69

**Sorting:** Most recently closed match first (`.OrderByDescending(m => m.ClosedAt)`).

**Partner Name Fetch:** Separate query to `Users` table to get partner's full name.

**Fallback:** If partner not found, defaults to `"Someone"`.

#### 2. Weekly Coaching Detection

```csharp
// Check for Weekly Coaching pending
var coachingSummary = await db.Set<WovenBackend.Data.Entities.CoachingSummary>()
    .Where(c => c.UserId == userId 
        && !c.DismissedAt.HasValue 
        && c.DeliveredAt.HasValue)
    .OrderByDescending(c => c.CreatedAt)
    .FirstOrDefaultAsync(ct);
```

**Location:** Lines 72-76

**Condition:** User has a `CoachingSummary` where:
- `DeliveredAt` is non-null (worker delivered it)
- `DismissedAt` is null (user hasn't dismissed it)

**If found:**

```csharp
if (coachingSummary != null)
{
    tasks.Add(new
    {
        type = "weekly_coaching",
        priority = 2,
        data = new
        {
            summaryId = coachingSummary.Id,
            summaryText = coachingSummary.SummaryText
        }
    });
}
```

**Location:** Lines 78-89

**Note:** Only most recent undismissed summary returned (`.OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync()`).

#### 3. Daily Pulse Detection

```csharp
// Check for Daily Pulse pending
var currentCycle = await db.UserDynamicIntakeSets
    .Where(d => d.UserId == userId
        && d.CycleEndUtc > DateTimeOffset.UtcNow
        && !d.AnsweredAtUtc.HasValue)
    .OrderByDescending(d => d.CycleStartUtc)
    .FirstOrDefaultAsync(ct);
```

**Location:** Lines 92-98

**Condition:** User has a `UserDynamicIntakeSet` (pulse cycle) where:
- `CycleEndUtc` is in the future (cycle still active)
- `AnsweredAtUtc` is null (user hasn't answered yet)

**If found:**

```csharp
if (currentCycle != null)
{
    tasks.Add(new
    {
        type = "daily_pulse",
        priority = 3,
        data = new
        {
            cycleId = currentCycle.CycleId
        }
    });
}
```

**Location:** Lines 100-110

### Response Formatting

```csharp
// Sort by priority
var sorted = tasks.OrderBy(t => ((dynamic)t).priority).ToList();

return Results.Ok(new { tasks = sorted });
```

**Location:** Lines 113-115

**Sorting:** Ascending by `priority` field (1 = highest priority, 3 = lowest).

**Dynamic Cast:** `((dynamic)t).priority` required because `tasks` is `List<object>` (anonymous types). EF Core doesn't support strongly-typed projections across multiple unrelated entities.

### Response Example

```json
{
  "tasks": [
    {
      "type": "date_feedback",
      "priority": 1,
      "data": {
        "matchId": "550e8400-e29b-41d4-a716-446655440000",
        "partnerName": "Sarah"
      }
    },
    {
      "type": "daily_pulse",
      "priority": 3,
      "data": {
        "cycleId": "cycle_2026-08-17"
      }
    }
  ]
}
```

---

## POST /me/interaction-log

### Endpoint Signature

```csharp
private record LogInteractionRequest(
    string EventType,
    Dictionary<string, object>? Context
);

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

**Location:** Lines 121-137

**Request Body:**

```json
{
  "eventType": "DailyPulseCompleted",
  "context": {
    "answers": {
      "energy": "high",
      "mood": "good"
    }
  }
}
```

**Dependency Injection:**
- `HttpContext` — JWT claims
- `IInteractionLogService` — Service layer (registered in `Program.cs`)
- `CancellationToken` — Async cancellation

**Delegation:** Endpoint delegates all logic to `IInteractionLogService.LogAsync()`. No business logic in endpoint itself.

**Response:**

```json
{
  "logged": true
}
```

**Status Code:** Always `200 OK` (even if DB write fails — service swallows exceptions).

---

## Database Entities

### UserInteractionLog

**File:** `backend/WovenBackend/data/Entities/UserInteractionLog.cs`

```csharp
[Table("user_interaction_logs")]
public class UserInteractionLog
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("user_id")]
    public int UserId { get; set; }

    [Required]
    [Column("event_type")]
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    [Required]
    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    [Column("context", TypeName = "jsonb")]
    public string ContextJson { get; set; } = "{}";

    [Required]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
```

**Location:** Lines 8-38

### Column Details

| Column | Type | Constraints | Default | Notes |
|--------|------|-------------|---------|-------|
| `id` | `int` | Primary key, auto-increment | — | PostgreSQL `SERIAL` |
| `user_id` | `int` | Foreign key, NOT NULL | — | References `users.id` |
| `event_type` | `string` | NOT NULL, max 50 chars | `""` | Event name (e.g., `DailyPulseCompleted`) |
| `occurred_at` | `DateTimeOffset` | NOT NULL | `UtcNow` | When event happened (semantic timestamp) |
| `context` | `string` (jsonb) | NOT NULL | `"{}"` | JSON context data |
| `created_at` | `DateTimeOffset` | NOT NULL | `UtcNow` | Row insertion timestamp (audit trail) |

### JSONB Column

```csharp
[Column("context", TypeName = "jsonb")]
public string ContextJson { get; set; } = "{}";
```

**PostgreSQL Type:** `jsonb` (binary JSON, indexed, queryable)

**Why JSONB:**
- Flexible schema (context shape varies per event type)
- Queryable via PostgreSQL operators (`->`, `->>`, `@>`)
- Indexed for fast lookups (GIN index on migration)

**Example Query:**

```sql
SELECT * FROM user_interaction_logs
WHERE context_json @> '{"summaryId": 42}';
```

Returns all logs with `summaryId = 42` in context.

---

## Migration

**File:** `backend/WovenBackend/Migrations/20260817051023_AddUserInteractionLogs.cs`

### Table Creation

```csharp
migrationBuilder.CreateTable(
    name: "user_interaction_logs",
    columns: table => new
    {
        id = table.Column<int>(type: "integer", nullable: false)
            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
        user_id = table.Column<int>(type: "integer", nullable: false),
        event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
        occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
        context = table.Column<string>(type: "jsonb", nullable: false),
        created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_user_interaction_logs", x => x.id);
        table.ForeignKey(
            name: "FK_user_interaction_logs_users_user_id",
            column: x => x.user_id,
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    });
```

**Foreign Key Cascade:** If user deleted, all interaction logs deleted (privacy compliance).

### Indexes

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

**Index 1:** `(user_id, occurred_at)`  
**Use Case:** User timeline queries ("all events for user 123")

**Index 2:** `(event_type, occurred_at)`  
**Use Case:** Cohort analysis ("all `DailyPulseSkipped` events in last 7 days")

**Why Both:**
- Different query patterns
- Both include `occurred_at` for time-range filtering
- Composite indexes faster than single-column lookups

---

## Service Layer

### Interface

**File:** `backend/WovenBackend/Services/IInteractionLogService.cs`

```csharp
public interface IInteractionLogService
{
    Task LogAsync(int userId, string eventType, object? context = null, CancellationToken ct = default);
}
```

**Location:** Lines 6-9

**Parameters:**
- `userId` — User who triggered the event
- `eventType` — Event name (e.g., `"DailyPulseCompleted"`)
- `context` — Optional context object (will be JSON-serialized)
- `ct` — Cancellation token

**Return:** `Task` (no return value, fire-and-forget logging)

### Implementation

**File:** `backend/WovenBackend/Services/IInteractionLogService.cs`

```csharp
public class InteractionLogService : IInteractionLogService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<InteractionLogService> _logger;

    public InteractionLogService(WovenDbContext db, ILogger<InteractionLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(int userId, string eventType, object? context = null, CancellationToken ct = default)
    {
        try
        {
            var contextJson = context != null
                ? JsonSerializer.Serialize(context, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                : "{}";

            var log = new data.Entities.UserInteractionLog
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
}
```

**Location:** Lines 11-54

### JSON Serialization

```csharp
var contextJson = context != null
    ? JsonSerializer.Serialize(context, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    : "{}";
```

**Location:** Lines 26-28

**Naming Policy:** `JsonNamingPolicy.CamelCase` converts C# PascalCase to JSON camelCase.

**Example:**

**C# Input:**
```csharp
await LogAsync(userId, "DailyPulseCompleted", new {
    Answers = new { Energy = "high", Mood = "good" },
    TimeToComplete = 12
});
```

**DB Storage:**
```json
{
  "answers": {
    "energy": "high",
    "mood": "good"
  },
  "timeToComplete": 12
}
```

**Null Context:** Serializes to `"{}"` (not `null`). Ensures `ContextJson` column always contains valid JSON.

### Error Handling

```csharp
try
{
    // ...insert logic
}
catch (Exception ex)
{
    _logger.LogError(ex,
        "[InteractionLog] Failed to log event | EventType={EventType} UserId={UserId}",
        eventType, userId);
    // Swallows exception — does NOT rethrow
}
```

**Location:** Lines 24-52

**Philosophy:** Logging failure should **never block user flow**.

**Effect:**
- Exception logged to Serilog
- Method returns successfully
- Endpoint returns `200 OK` to client
- **Silent data loss** (trade-off for UX reliability)

**Monitoring:** Production should alert on `[InteractionLog] Failed` errors (indicates DB issues).

---

## Service Registration

**File:** `backend/WovenBackend/Program.cs`

```csharp
builder.Services.AddScoped<IInteractionLogService, InteractionLogService>();
```

**Lifetime:** Scoped (one instance per HTTP request).

**Why Scoped:**
- Service uses `WovenDbContext` (also scoped)
- Each request gets isolated DB context
- Prevents concurrency issues

---

## SQL Performance

### Query Execution Plans

#### Date Feedback Detection

```sql
SELECT EXISTS(
  SELECT 1 FROM matches m
  WHERE (m.user_a_id = $1 OR m.user_b_id = $1)
    AND m.balloon_state = 'CLOSED'
    AND NOT EXISTS(
      SELECT 1 FROM date_feedback df
      WHERE df.match_id = m.id AND df.user_id = $1
    )
);
```

**Index Used:** `ix_matches_user_a_id_user_b_id_balloon_state`

**Performance:** ~5-10ms (index scan + nested loop anti-join)

#### Coaching Summary Detection

```sql
SELECT * FROM coaching_summaries
WHERE user_id = $1
  AND delivered_at IS NOT NULL
  AND dismissed_at IS NULL
ORDER BY created_at DESC
LIMIT 1;
```

**Index Used:** `ix_coaching_summaries_user_id_delivered_at_dismissed_at`

**Performance:** ~2-5ms (index-only scan)

#### Daily Pulse Detection

```sql
SELECT * FROM user_dynamic_intake_sets
WHERE user_id = $1
  AND cycle_end_utc > NOW()
  AND answered_at_utc IS NULL
ORDER BY cycle_start_utc DESC
LIMIT 1;
```

**Index Used:** `ix_user_dynamic_intake_sets_user_id_cycle_end_utc_answered_at`

**Performance:** ~2-5ms (index-only scan)

### Total Endpoint Latency

**3 sequential queries:** ~10-20ms (DB time)  
**Partner name fetch:** +2-5ms (if date feedback pending)  
**HTTP overhead:** ~5-10ms  
**Total:** ~20-35ms

**Optimization Opportunity:** Run 3 queries in parallel via `Task.WhenAll`:

```csharp
var (hasFeedback, coaching, pulse) = await (
    CheckDateFeedbackAsync(userId, db, ct),
    CheckCoachingAsync(userId, db, ct),
    CheckPulseAsync(userId, db, ct)
);
```

**Benefit:** Reduces DB time to ~10-15ms (single round-trip).

**Status:** Not implemented (premature optimization — current latency acceptable).

---

## Data Retention

### Current State

**Retention:** Infinite (no automatic deletion)

**Risk:** Table grows unbounded (~100KB/user/year → 10GB at 100k users).

### Proposed Policy

```sql
DELETE FROM user_interaction_logs
WHERE occurred_at < NOW() - INTERVAL '2 years';
```

**Frequency:** Quarterly cron job (via batch worker)

**Rationale:**
- ECHO training needs 6-12 months of data
- 2-year retention covers trend analysis
- Older data has diminishing value

**Status:** Not implemented (table too small to matter yet — <1GB total).

---

## Privacy & Compliance

### PII Rules

**Forbidden in `ContextJson`:**
- Free-text user input (`{ freeResponse: "I feel lonely" }`)
- Partner names (`{ partnerName: "Sarah" }`)
- Location data (`{ lat: 40.7128, lng: -74.0060 }`)
- Phone numbers, emails, addresses

**Allowed in `ContextJson`:**
- Entity IDs (`{ matchId: "uuid", summaryId: 42 }`)
- Enumerated values (`{ energy: "high", mood: "good" }`)
- Aggregate counts (`{ timeToComplete: 12, activeDaysThisWeek: 5 }`)

**Current Compliance:** All logged events use only IDs + enums. ✅

### GDPR Compliance

**User Deletion:**

Foreign key cascade ensures all interaction logs deleted when user deleted:

```csharp
onDelete: ReferentialAction.Cascade
```

**Data Export:**

Users can request export via support ticket. Query:

```sql
SELECT * FROM user_interaction_logs
WHERE user_id = $1
ORDER BY occurred_at DESC;
```

**Anonymization:**

If user requests anonymization (not deletion), update `UserId` to sentinel value:

```sql
UPDATE user_interaction_logs
SET user_id = 0  -- Anonymous user ID
WHERE user_id = $1;
```

**Status:** Export/anonymization not yet implemented (on roadmap).

---

## Testing

### Unit Tests (Service Layer)

**Test Cases:**
- ✅ Log with null context → stores `"{}"`
- ✅ Log with nested object → JSON serialized correctly
- ✅ Log with PascalCase properties → stored as camelCase
- ✅ DB write failure → exception swallowed, error logged
- ✅ Multiple logs for same user → all inserted

**Example:**

```csharp
[Fact]
public async Task LogAsync_WithNullContext_StoresEmptyJson()
{
    var service = new InteractionLogService(_db, _logger);
    
    await service.LogAsync(userId: 1, eventType: "TestEvent", context: null);
    
    var log = await _db.UserInteractionLogs.FirstAsync();
    Assert.Equal("{}", log.ContextJson);
}
```

### Integration Tests (Endpoint)

**Test Cases:**
- ✅ GET /me/pending-tasks with no pending → returns `{ tasks: [] }`
- ✅ GET /me/pending-tasks with date feedback → returns priority 1 task
- ✅ GET /me/pending-tasks with multiple → sorted by priority
- ✅ POST /me/interaction-log → row inserted in DB
- ✅ POST /me/interaction-log with complex context → JSON matches
- ✅ Unauthenticated request → 401 Unauthorized

**Example:**

```csharp
[Fact]
public async Task GetPendingTasks_WithDateFeedbackPending_ReturnsPriority1Task()
{
    // Arrange
    var match = CreateClosedMatch(userId);
    _db.Matches.Add(match);
    await _db.SaveChangesAsync();
    
    // Act
    var response = await _client.GetAsync("/me/pending-tasks");
    var result = await response.Content.ReadFromJsonAsync<PendingTasksResponse>();
    
    // Assert
    Assert.Single(result.Tasks);
    Assert.Equal("date_feedback", result.Tasks[0].Type);
    Assert.Equal(1, result.Tasks[0].Priority);
}
```

### Manual Testing Checklist

- [ ] User with closed match (no feedback) → task returned
- [ ] User with undismissed coaching → task returned
- [ ] User with active pulse cycle (unanswered) → task returned
- [ ] User with multiple pending → sorted by priority
- [ ] User with no pending → empty array
- [ ] POST interaction log → row inserted with correct timestamp
- [ ] Context JSON stored as camelCase
- [ ] Error logged but 200 returned on DB failure

---

## Error Scenarios

### Invalid JWT

**Request:** Missing or invalid `Authorization` header

**Response:**
```
HTTP 401 Unauthorized
{
  "error": "Unauthorized access.",
  "correlationId": "a1b2c3d4e5f6g7h8",
  "timestamp": "2026-08-17T12:34:56Z"
}
```

**Handler:** `AuthExceptionHandler` catches `UnauthorizedAccessException` thrown by `EndpointHelper.GetUserId()`.

### Database Connection Failure

**Scenario:** PostgreSQL down during `/me/pending-tasks` call

**Response:**
```
HTTP 500 Internal Server Error
{
  "error": "An unexpected error occurred.",
  "correlationId": "a1b2c3d4e5f6g7h8",
  "timestamp": "2026-08-17T12:34:56Z"
}
```

**Handler:** `GlobalExceptionHandler` catches all unhandled exceptions.

**Logged:** Full stack trace to Serilog with `CorrelationId`.

### Logging Service Failure

**Scenario:** DB write fails during `POST /me/interaction-log`

**Response:**
```
HTTP 200 OK
{
  "logged": true
}
```

**Effect:** User sees success, but no DB row inserted.

**Logged:**
```
[InteractionLog] Failed to log event | EventType=DailyPulseCompleted UserId=123
Npgsql.PostgresException: 42P01: relation "user_interaction_logs" does not exist
```

**Why 200:** Prevent user-facing errors for non-critical logging.

---

## Future Enhancements

### 1. Parallel Task Detection

**Current:** 3 sequential queries (~20ms total)

**Proposed:**

```csharp
var (dateFeedback, coaching, pulse) = await Task.WhenAll(
    CheckDateFeedbackAsync(userId, db, ct),
    CheckCoachingAsync(userId, db, ct),
    CheckPulseAsync(userId, db, ct)
);
```

**Benefit:** Reduces latency to ~10ms (single round-trip).

### 2. Batch Logging Endpoint

**Current:** 1 HTTP request per event (5 events = 5 requests)

**Proposed:**

```csharp
POST /me/interaction-log/batch
{
  "events": [
    { "eventType": "DailyPulseCompleted", "context": {...}, "occurredAt": "..." },
    { "eventType": "AssistantChatOpened", "context": {...}, "occurredAt": "..." }
  ]
}
```

**Benefit:** Reduce HTTP overhead, especially on slow connections.

### 3. Real-Time ECHO Feedback

**Current:** Logs analyzed in batch workers (nightly).

**Proposed:** Stream logs to real-time ECHO service (Redis Streams or Azure Service Bus):

```csharp
await _bus.PublishAsync(new InteractionLogEvent {
    UserId = userId,
    EventType = eventType,
    Context = context,
    OccurredAt = DateTimeOffset.UtcNow
});
```

**Use Cases:**
- User skips pulse 3x in a row → reduce pulse frequency immediately
- User dismisses coaching <10s → mark summary as low-engagement

**Status:** Not planned (batch processing sufficient for v1).

### 4. Context Schema Validation

**Current:** No validation on `context` object (any JSON accepted).

**Proposed:** Define JSON schemas per event type:

```csharp
var schema = GetSchemaForEvent(eventType);
if (!schema.Validate(context)) {
    throw new ValidationException("Invalid context for event type");
}
```

**Benefit:** Catch frontend bugs (missing fields, wrong types).

**Trade-off:** Adds complexity, slows endpoint.

**Status:** Not planned (trust frontend to send correct data).

---

## Related Docs

- [README.md](./README.md) — Feature overview
- [pending-tasks.md](./pending-tasks.md) — Task priority system
- [signal-logging.md](./signal-logging.md) — Event catalog
- [frontend.md](./frontend.md) — Frontend implementation
