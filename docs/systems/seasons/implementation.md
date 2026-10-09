# Seasons Implementation

**File:** `Services/Seasons/SeasonService.cs`  
**Interface:** `Services/Seasons/ISeasonService.cs`

---

## Service Contract

```csharp
public interface ISeasonService
{
    Task<CurrentSeasonResult> GetCurrentSeasonAsync(int userId, CancellationToken ct = default);
    Task SubmitSeasonResponsesAsync(int userId, List<SeasonResponseRequest> responses, CancellationToken ct = default);
    Task<string?> GetSignaturePromptAsync(int userId, CancellationToken ct = default);
}
```

### DTOs

```csharp
// Season metadata
public record SeasonInfo(
    int Id,
    int SeasonNumber,
    DateOnly StartDate,
    DateOnly EndDate,
    string PromptText
);

// Current season + user state
public record CurrentSeasonResult(
    SeasonInfo? Season,     // null if no active season
    string UserStatus,      // "not_started" | "unanswered" | "answered"
    int ResponseCount       // how many responses user submitted (0-8)
);

// User's answer to one pillar
public record SeasonResponseRequest(
    string PillarId,        // e.g., "lifestyle"
    string QuestionId,      // which foundational question was shown
    string Response         // free-text answer
);
```

---

## Method: GetCurrentSeasonAsync

**Purpose:** Fetch active season + user's response status

### Logic

```csharp
public async Task<CurrentSeasonResult> GetCurrentSeasonAsync(int userId, CancellationToken ct = default)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    // Find active season (today falls within StartDate..EndDate)
    var season = await _db.Seasons.AsNoTracking()
        .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today, ct);

    if (season == null)
        return new CurrentSeasonResult(null, "not_started", 0);

    // Count user's responses for this season
    var responseCount = await _db.UserSeasonResponses.AsNoTracking()
        .CountAsync(r => r.UserId == userId && r.SeasonId == season.Id, ct);

    var status = responseCount > 0 ? "answered" : "unanswered";

    var info = new SeasonInfo(season.Id, season.SeasonNumber, season.StartDate, season.EndDate, season.PromptText);
    return new CurrentSeasonResult(info, status, responseCount);
}
```

### Return States

| State | Meaning |
|---|---|
| `Season = null, UserStatus = "not_started"` | No active season exists yet |
| `Season != null, UserStatus = "unanswered", ResponseCount = 0` | Season active, user hasn't responded |
| `Season != null, UserStatus = "answered", ResponseCount > 0` | User submitted at least one response |

**Note:** `ResponseCount` can be partial (e.g., 3/8 pillars answered). Frontend handles this.

---

## Method: SubmitSeasonResponsesAsync

**Purpose:** Save user's season responses, trigger re-embedding

### Logic Flow

```
1. Validate active season exists → throw if none
2. Load existing responses (UserId + SeasonId) into dictionary
3. For each incoming response:
   - If PillarId exists → UPDATE (Response, QuestionId, RespondedAt)
   - Else → INSERT new UserSeasonResponse
4. SaveChangesAsync (upsert batch)
5. Fire-and-forget: analytics event
6. Fire-and-forget: invalidate embedding cache + re-embed
7. Fire-and-forget: SignalR notification
```

### Code

```csharp
public async Task SubmitSeasonResponsesAsync(int userId, List<SeasonResponseRequest> responses, CancellationToken ct = default)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    var season = await _db.Seasons.AsNoTracking()
        .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today, ct)
        ?? throw new InvalidOperationException("NO_ACTIVE_SEASON");

    // Load existing responses for this user+season in one query
    var existingMap = await _db.UserSeasonResponses
        .Where(r => r.UserId == userId && r.SeasonId == season.Id)
        .ToDictionaryAsync(r => r.PillarId, ct);

    foreach (var req in responses)
    {
        if (existingMap.TryGetValue(req.PillarId, out var existing))
        {
            // UPDATE existing response
            existing.Response = req.Response;
            existing.QuestionId = req.QuestionId;
            existing.RespondedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // INSERT new response
            _db.UserSeasonResponses.Add(new UserSeasonResponse
            {
                UserId = userId,
                SeasonId = season.Id,
                PillarId = req.PillarId,
                QuestionId = req.QuestionId,
                Response = req.Response,
                RespondedAt = DateTimeOffset.UtcNow
            });
        }
    }

    await _db.SaveChangesAsync(ct);

    // Fire-and-forget: analytics
    _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.SeasonResponseSubmitted,
        new { seasonNumber = season.SeasonNumber });

    // Fire-and-forget: invalidate pillar embedding cache + re-embed
    _ = Task.Run(async () =>
    {
        using var scope = _scopeFactory.CreateScope();
        try
        {
            var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
            await cache.DeleteAsync(CacheKeys.PillarEmbedding(userId));

            var vectorBuilder = scope.ServiceProvider.GetRequiredService<IUserVectorBuilder>();
            await vectorBuilder.BuildAndSaveV1Async(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Seasons] Post-submit re-embed failed for user {UserId}", userId);
        }
    });

    // Fire-and-forget: SignalR notification
    _ = Task.Run(async () =>
    {
        try { await _notify.SeasonResponseSubmittedAsync(userId); }
        catch (Exception ex) { _logger.LogWarning(ex, "[Seasons] SeasonResponseSubmitted notification failed for user {UserId}", userId); }
    });
}
```

### Key Decisions

**Why upsert instead of fail-on-duplicate?**
- Users may want to revise answers before season ends
- Avoids "already answered" error UX
- Latest response is always canonical

**Why fire-and-forget for re-embedding?**
- Embedding generation can take 1-3 seconds (OpenAI API call)
- Don't block HTTP response on non-critical success path
- Failures logged but don't interrupt user flow

**Why fire-and-forget for notifications?**
- SignalR can fail if user disconnected
- Analytics can be delayed
- Both are best-effort, never block saves

---

## Method: GetSignaturePromptAsync

**Purpose:** Fetch current season's prompt text (used in profile signature generation)

### Logic

```csharp
public async Task<string?> GetSignaturePromptAsync(int userId, CancellationToken ct = default)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var season = await _db.Seasons.AsNoTracking()
        .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today, ct);
    return season?.PromptText;
}
```

**Returns:** `PromptText` if active season exists, else `null`

**Used By:** Profile signature generation (combines foundational + seasonal context)

---

## Dependencies

| Service | Purpose |
|---|---|
| `WovenDbContext` | Database access (Seasons, UserSeasonResponses) |
| `INotificationService` | SignalR push (`SeasonResponseSubmittedAsync`) |
| `ICacheService` | Invalidate `CacheKeys.PillarEmbedding(userId)` |
| `IServiceScopeFactory` | Create scope for background re-embedding |
| `IAnalyticsService` | Track `SeasonResponseSubmitted` event |
| `IUserVectorBuilder` | Regenerate pillar embeddings from updated responses |
| `ILogger<SeasonService>` | Log re-embedding failures |

---

## Error Handling

### Thrown Exceptions

| Exception | When | Caller Sees |
|---|---|---|
| `InvalidOperationException("NO_ACTIVE_SEASON")` | Submit when no active season | 400 Bad Request (caught in endpoint) |

### Silent Failures (Logged Only)

- Re-embedding fails → warning log, user not notified
- SignalR notification fails → warning log
- Analytics tracking fails → silent (fire-and-forget)

**Rationale:** Core operation (save responses) must succeed even if side effects fail.

---

## Performance Characteristics

### GetCurrentSeasonAsync
- **Queries:** 2
  1. Find active season (indexed on StartDate/EndDate)
  2. Count user responses (indexed on UserId + SeasonId)
- **Typical latency:** <50ms
- **Caching:** Not cached (season state changes daily, response count changes on submit)

### SubmitSeasonResponsesAsync
- **Queries:** 2 + N
  1. Find active season
  2. Load existing responses (dictionary)
  3. SaveChangesAsync (INSERT or UPDATE per pillar)
- **Typical latency:** 100-200ms (sync path only, excludes fire-and-forget)
- **Concurrency:** Upsert-safe (same user submitting twice → last write wins)

### GetSignaturePromptAsync
- **Queries:** 1 (find active season)
- **Typical latency:** <20ms

---

## Testing Considerations

### Unit Test Scenarios
- ✅ GetCurrent when no season exists → `not_started`
- ✅ GetCurrent when season active, user hasn't answered → `unanswered`
- ✅ GetCurrent when user partially answered (3/8) → `answered`, count=3
- ✅ Submit when no active season → throws `NO_ACTIVE_SEASON`
- ✅ Submit new responses (first time) → inserts 8 rows
- ✅ Submit updates (second time) → updates existing rows (upsert)
- ✅ Submit triggers re-embedding (verify cache invalidation call)

### Integration Test Scenarios
- ✅ End-to-end flow: create season → user submits → embeddings regenerated → match scores updated
- ✅ Concurrent submits (same user, same season) → last write wins, no duplicates

---

## Logging

All log lines prefixed with `[Seasons]`

### Info Level
- (None — service is silent on success path)

### Warning Level
- `[Seasons] Post-submit re-embed failed for user {UserId}` — re-embedding threw exception
- `[Seasons] SeasonResponseSubmitted notification failed for user {UserId}` — SignalR failed

### Error Level
- (None — errors bubble to caller or logged in background tasks)

---

## Related Files

- `SeasonTransitionWorker.cs` — creates new seasons
- `UserVectorBuilder.cs` — rebuilds embeddings from season responses
- `SeasonEndpoints.cs` — HTTP layer
- `NotificationService.cs` — SignalR events

---

**Last Updated:** 2026-10-07
