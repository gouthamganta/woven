# Seasons Configuration & Lifecycle

---

## Season Lifecycle

### Phase 1: Creation (Automated)

**Trigger:** SeasonTransitionWorker runs daily at **01:00 UTC**

**Logic:**
```
1. Check if active season exists (today within StartDate..EndDate)
2. If yes → exit (no transition needed)
3. If no → create new season:
   a. Generate prompt via OpenAI (gpt-4.1-mini)
   b. Set SeasonNumber = last season + 1 (or 1 if first)
   c. Set StartDate = today, EndDate = today + 21 days
   d. Save to database
4. Notify all COMPLETE users via SignalR (NewSeasonStarted)
```

**See:** `Services/Seasons/SeasonTransitionWorker.cs`

---

## SeasonTransitionWorker

**File:** `Services/Seasons/SeasonTransitionWorker.cs`

### Scheduling

| Property | Value |
|---|---|
| **Type** | Hosted Service (BackgroundService) |
| **Run Time** | 01:00 UTC daily |
| **Disabled When** | `WOVEN_DISABLE_BATCH_WORKERS=true` (env flag) |
| **Registered In** | Program.cs:559 (conditional) |

### Worker Logic

```csharp
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        // Sleep until next 01:00 UTC
        await SleepUntil1AmUtcAsync(ct);
        if (ct.IsCancellationRequested) break;

        try
        {
            await CheckAndTransitionSeasonAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SeasonWorker] Season transition check failed");
        }
    }
}
```

**Sleep Logic:**
```csharp
private static async Task SleepUntil1AmUtcAsync(CancellationToken ct)
{
    var now = DateTime.UtcNow;
    var next1Am = now.Date.AddDays(now.Hour >= 1 ? 1 : 0).AddHours(1);
    var delay = next1Am - now;
    if (delay > TimeSpan.Zero)
        await Task.Delay(delay, ct);
}
```

**Result:** Worker checks once per day, always at 01:00 UTC.

---

## CheckAndTransitionSeasonAsync

**Purpose:** Determine if new season is needed, create it if so

### Step-by-Step

#### 1. Check Last Season

```csharp
var lastSeason = await db.Seasons.AsNoTracking()
    .OrderByDescending(s => s.SeasonNumber)
    .FirstOrDefaultAsync(ct);
```

#### 2. Evaluate Transition Need

```csharp
var today = DateOnly.FromDateTime(DateTime.UtcNow);

// No transition needed if current season is still active
if (lastSeason != null && lastSeason.EndDate >= today)
    return;
```

**Exit early if:**
- Last season's `EndDate >= today` (season still active)

**Proceed if:**
- No seasons exist yet (first run)
- Last season expired (EndDate < today)

#### 3. Generate Prompt

```csharp
var promptText = await GenerateSeasonPromptAsync(openAi, ct);
```

**AI Prompt:**
```
"Generate a short introspective question for a dating app season. Max 200 chars. No clichés. Return only the question text, nothing else."
```

**Fallback (if OpenAI returns empty/whitespace):**
```
"What matters most to you in a connection right now?"
```

**Post-processing:**
- Trim whitespace
- Strip surrounding quotes
- Truncate to 200 chars if exceeded

#### 4. Create New Season

```csharp
var nextNumber = (lastSeason?.SeasonNumber ?? 0) + 1;
var newSeason = new Season
{
    SeasonNumber = nextNumber,
    StartDate = today,
    EndDate = today.AddDays(21),  // 21-day season
    PromptText = promptText,
    CreatedAt = DateTimeOffset.UtcNow
};

db.Seasons.Add(newSeason);
await db.SaveChangesAsync(ct);
```

#### 5. Notify All Active Users

```csharp
// Fetch all COMPLETE users
var activeUserIds = await db.Users.AsNoTracking()
    .Where(u => u.ProfileStatus == ProfileStatus.COMPLETE)
    .Select(u => u.Id)
    .ToListAsync(ct);

// Send SignalR notification to each
foreach (var userId in activeUserIds)
{
    try
    {
        await _notify.NewSeasonStartedAsync(userId, nextNumber, promptText, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[SeasonWorker] NewSeasonStarted notification failed for user {UserId}", userId);
    }
}
```

**Why only COMPLETE users?**
- Onboarding users haven't finished initial profile setup
- Seasonal prompts assume baseline pillar answers exist
- Avoids notification spam for inactive/incomplete accounts

---

## Season Duration

**Fixed:** 21 days (3 weeks)

**Rationale:**
- Long enough: users have time to respond (not rushed)
- Short enough: maintains freshness (not stale)
- Aligns with typical dating app engagement cycles
- 17 seasons per year → 17 re-engagement touchpoints

**Future Consideration:** Dynamic duration based on response rate (e.g., extend if <30% answered)

---

## Prompt Generation Strategy

### Current Approach (GPT-4.1-mini)

**Input:**
```
"Generate a short introspective question for a dating app season. Max 200 chars. No clichés. Return only the question text, nothing else."
```

**Output Examples:**
- "What's changing in how you think about partnership right now?"
- "What kind of energy do you want more of in your life this month?"
- "How has your relationship with vulnerability shifted recently?"

**Advantages:**
- Zero editorial work (fully automated)
- Variety (AI generates unique prompts each cycle)
- Introspective tone (better than generic "What are you looking for?")

**Disadvantages:**
- Quality variance (occasionally bland)
- No theme planning (random topics, no narrative arc)
- No cultural sensitivity review (AI may miss nuance)

### Alternative Approaches (Not Implemented)

| Approach | Pro | Con |
|---|---|---|
| **Pre-written prompt bank** | Curated quality, thematic arcs | Requires editorial work, finite pool |
| **User-submitted prompts** | Community engagement, diversity | Moderation burden, quality variance |
| **Seasonal themes** | Coherent narrative (e.g., "June: Adventure") | Constrains AI, requires planning |

**Current Decision:** Stick with AI generation until quality becomes a user complaint.

---

## Edge Cases & Failure Modes

### No Active Season on First Day

**Scenario:** App launches, no seasons exist yet, user hits `/seasons/current`

**Behavior:**
- `GetCurrentSeasonAsync` returns `Season = null, UserStatus = "not_started"`
- Frontend shows "No active season yet — check back soon"

**Resolution:** SeasonTransitionWorker runs at next 01:00 UTC, creates Season 1

### OpenAI API Failure

**Scenario:** Worker calls `GenerateSeasonPromptAsync`, API returns 500 or times out

**Behavior:**
- `IOpenAiResilientClient` retries 3x with exponential backoff
- If all retries fail → exception bubbles
- Worker catches exception, logs error, exits without creating season
- Next day's run retries

**Mitigation:** Fallback prompt hardcoded in `GenerateSeasonPromptAsync`:
```csharp
if (string.IsNullOrWhiteSpace(result))
{
    _logger.LogWarning("[SeasonWorker] OpenAI returned empty prompt — using fallback");
    return "What matters most to you in a connection right now?";
}
```

### Season Expires Mid-Day

**Scenario:** Season 5 ends on `2026-10-06`. User tries to submit response on `2026-10-07 00:30 UTC` (before worker runs at 01:00).

**Behavior:**
- `SubmitSeasonResponsesAsync` queries active season → none found (EndDate < today)
- Throws `InvalidOperationException("NO_ACTIVE_SEASON")`
- Endpoint returns `400 Bad Request: { error: "NO_ACTIVE_SEASON" }`

**User Experience:** Frontend shows "Season ended — new season coming soon"

**Resolution:** Worker runs at 01:00 UTC, creates Season 6, user can answer again

### Duplicate Season Creation (Race Condition)

**Scenario:** Two instances of SeasonTransitionWorker run simultaneously (e.g., Azure scale-out)

**Mitigation:**
- `WOVEN_DISABLE_BATCH_WORKERS` env flag prevents worker registration on web pods
- Only dedicated worker pods run background services
- Single-instance deployment (no concurrent workers)

**Future-Proofing:** Add distributed lock (Redis) around `CheckAndTransitionSeasonAsync` if multi-worker needed

---

## Database Schema

### Table: `seasons`

| Column | Type | Constraints |
|---|---|---|
| `id` | int | PK, identity |
| `season_number` | int | NOT NULL, unique |
| `start_date` | date | NOT NULL |
| `end_date` | date | NOT NULL |
| `prompt_text` | varchar(200) | NOT NULL |
| `created_at` | timestamptz | NOT NULL, default now() |

**Indexes:**
- PK on `id`
- Unique on `season_number`
- Composite index on `(start_date, end_date)` (for active season query)

### Table: `user_season_responses`

| Column | Type | Constraints |
|---|---|---|
| `id` | uuid | PK, default gen_random_uuid() |
| `user_id` | int | NOT NULL, FK → users.id |
| `season_id` | int | NOT NULL, FK → seasons.id |
| `pillar_id` | varchar(50) | NOT NULL |
| `question_id` | varchar(100) | NOT NULL |
| `response` | text | NOT NULL |
| `responded_at` | timestamptz | NOT NULL, default now() |

**Indexes:**
- PK on `id`
- Unique on `(user_id, season_id, pillar_id)` — enforces one response per pillar per season
- Index on `user_id` (for user response lookup)
- Index on `season_id` (for season analytics)

---

## Monitoring & Observability

### Log Events

| Level | Message | When |
|---|---|---|
| INFO | `[SeasonWorker] Season transition triggered — creating next season` | New season creation started |
| INFO | `[SeasonWorker] Created Season {Number}: {Prompt}` | Season successfully created |
| WARNING | `[SeasonWorker] OpenAI returned empty prompt — using fallback` | AI generation failed |
| WARNING | `[SeasonWorker] NewSeasonStarted notification failed for user {UserId}` | SignalR failed for one user |
| ERROR | `[SeasonWorker] Season transition check failed` | Worker exception (entire run failed) |

### Metrics to Track

**Operational:**
- Season creation success rate (should be 100% except OpenAI outages)
- Worker execution time (should be <30 seconds)
- Notification delivery rate (SignalR success %)

**Product:**
- Season response rate (% of COMPLETE users who answer each season)
- Average response time (days from season start to user submit)
- Pillar coverage (how many of 8 pillars users typically answer)

**Query for Response Rate:**
```sql
SELECT
    s.season_number,
    COUNT(DISTINCT usr.user_id)::float / NULLIF(COUNT(DISTINCT u.id), 0) AS response_rate
FROM seasons s
CROSS JOIN users u
LEFT JOIN user_season_responses usr ON usr.season_id = s.id AND usr.user_id = u.id
WHERE u.profile_status = 'COMPLETE'
GROUP BY s.id, s.season_number
ORDER BY s.season_number DESC;
```

---

## Configuration Settings

### Environment Variables

| Var | Default | Purpose |
|---|---|---|
| `WOVEN_DISABLE_BATCH_WORKERS` | `false` | If `true`, disables SeasonTransitionWorker (web pods set this) |

### Hardcoded Constants

| Constant | Value | Location |
|---|---|---|
| Season Duration | `21 days` | `SeasonTransitionWorker.cs:77` |
| Worker Run Time | `01:00 UTC` | `SeasonTransitionWorker.cs:44` |
| Prompt Max Length | `200 chars` | `Season.cs:22`, `SeasonTransitionWorker.cs:120` |
| Fallback Prompt | "What matters most to you in a connection right now?" | `SeasonTransitionWorker.cs:115` |

**No appsettings.json config** — all season mechanics are code-driven.

---

## Deployment Considerations

### Azure Setup
- **Web Pods:** `WOVEN_DISABLE_BATCH_WORKERS=true` (no worker runs here)
- **Worker Pods:** Flag unset (worker enabled)
- **Time Zone:** Always UTC (Azure default)

### Local Development
- Worker runs on `dotnet run` (no flag needed)
- If testing without worker, set `WOVEN_DISABLE_BATCH_WORKERS=true` in `launchSettings.json`
- Manually insert seasons into DB for testing:
  ```sql
  INSERT INTO seasons (season_number, start_date, end_date, prompt_text)
  VALUES (1, '2026-10-01', '2026-10-22', 'What brings you joy in everyday moments?');
  ```

---

## Related Documentation

- [README.md](./README.md) — system overview
- [implementation.md](./implementation.md) — SeasonService internals
- [api.md](./api.md) — HTTP endpoint contracts
- [../background-workers/README.md](../background-workers/README.md) — all batch workers schedule

---

**Last Updated:** 2026-10-07
