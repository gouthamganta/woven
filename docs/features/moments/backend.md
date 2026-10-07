# Backend Implementation — .NET Moments System

**Directory:** `backend/WovenBackend/`  
**Main Files:**
- `Endpoints/MomentsEndpoints.cs` — HTTP endpoints
- `Services/Moments/` — Business logic
- `data/Entities/Moments/` — EF Core entities

## Endpoints

**File:** `backend/WovenBackend/Endpoints/MomentsEndpoints.cs`

### Endpoint registration

```csharp
public static void MapMomentsEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/moments");
    group.RequireAuthorization();

    group.MapGet("", async (...) => { ... });                  // GET /moments
    group.MapGet("/liked-you", async (...) => { ... });        // GET /moments/liked-you
    group.MapPost("/respond", async (...) => { ... });         // POST /moments/respond
    group.MapPost("/choose", async (...) => { ... });          // POST /moments/choose
}
```

All endpoints require JWT auth (`RequireAuthorization()`).

### GET /moments (Deck tab)

**Purpose:** Load daily 5 candidates + budget + spark balance

**Dependencies:**
- `WovenDbContext db`
- `IDailyDeckOrchestrator deckOrchestrator` — Builds/retrieves daily deck
- `SparkWalletService sparks` — Spark balance
- `IVisualPreferenceService visualPreference` — Best photo selection (CLIP)
- `IAnalyticsService analytics` — Track deck views

**Flow:**
1. Get today's `DailyDeck` (or create if first call of day)
2. Filter out already-responded candidates (`moment_responses` where `date_utc = today AND from_user_id = me`)
3. Load candidate data in parallel:
   - User profiles (name, location, verified badge)
   - Photos (all photos for gallery, best photo via CLIP for card display)
   - Active tiles (live in Commons, not expired, moderated)
   - Highlighted tiles (pinned fallback if active < 3)
   - ECHO explanations (headline, bullets, bridge question)
   - Community ratings (red/green flag bar, shown if count >= 5)
   - "Already chose you" check (`moment_responses` where `from_user_id = candidate AND to_user_id = me`)
4. Build `cards` array with merged tile data (active first, highlights fill to 3)
5. Return `{ dateUtc, budget, sparkBalance, moodLine, count, cards }`

**Photo selection logic:**
```csharp
var bestPhotoTasks = candidateIds.Select(async cid =>
    (cid, url: await visualPreference.GetBestPhotoUrlAsync(userId, cid, ct)));
var bestPhotoResults = await Task.WhenAll(bestPhotoTasks);
var bestPhotoMap = bestPhotoResults.ToDictionary(x => x.cid, x => x.url);
```

Each viewer sees the candidate photo that best matches their visual preferences (CLIP similarity), not a global "primary photo."

**Tile merging logic:**
```csharp
var tilesByUser = candidateIds.ToDictionary(
    cid => cid,
    cid =>
    {
        var active    = activeByUser.GetValueOrDefault(cid) ?? new();
        var highlight = highlightByUser.GetValueOrDefault(cid) ?? new();
        var merged    = active.Take(3).ToList();
        if (merged.Count < 3)
            merged.AddRange(highlight.Take(3 - merged.Count));
        return merged.Select(t => new { id, contentType, contentText, mediaUrl, isActive }).ToList();
    });
```

Active tiles (`!IsExpired && IsModerated`) shown first (newest first), highlighted tiles fill remaining slots (up to 3 total).

### GET /moments/liked-you (Drawn tab)

**Purpose:** Load users who chose viewer in last 7 days

**Dependencies:**
- `WovenDbContext db`

**Flow:**
1. Calculate cutoff: `DateTimeOffset.UtcNow.AddDays(-7)`
2. Build exclusion filters:
   - Blocked users (both directions)
   - Already-matched users (`BalloonState.ACTIVE`)
   - Today's Deck candidates (no overlap)
3. Query `moment_responses`:
   - `to_user_id = me`
   - `IsPositiveExpression(choice)` (MAGICAL, LOGICAL, or legacy YES)
   - `created_at >= cutoff`
   - Exclude filtered users
4. Deduplicate by `from_user_id` (keep most recent choice)
5. Load user data: profiles, photos, tiles, ratings
6. Calculate `expiresInHours` for each: `(created_at + 7 days - now).TotalHours`
7. Return `{ count, cards }`

**Exclusion filters (SQL):**
```csharp
var blockedIds = await db.Blocks
    .Where(b => b.BlockerId == me || b.BlockedId == me)
    .Select(b => b.BlockerId == me ? b.BlockedId : b.BlockerId)
    .ToListAsync(ct);

var matchedIds = await db.Matches.AsNoTracking()
    .Where(m => m.BalloonState == BalloonState.ACTIVE && (m.UserAId == me || m.UserBId == me))
    .Select(m => m.UserAId == me ? m.UserBId : m.UserAId)
    .ToListAsync(ct);

var todayDeckIds = await db.MomentResponses.AsNoTracking()
    .Where(r => r.DateUtc == today && r.FromUserId == me)
    .Select(r => r.ToUserId)
    .ToListAsync(ct);
```

### POST /moments/respond (Pass or legacy simple choice)

**Purpose:** Record PASS, MAGICAL, or LOGICAL choice without ChatNote (legacy endpoint)

**Request:**
```csharp
public record RespondRequest(int TargetUserId, string Choice, string? Source, int? TimeOnCardMs);
```

**Dependencies:**
- `WovenDbContext db`
- `InteractionBudgetService budget` — Daily cap enforcement
- `SparkWalletService sparks` — Spark spend (Drawn tab only)
- `MomentsMatchService matchService` — Match creation
- `IAnalyticsService analytics`
- `IMatchSignalService signals`
- `IIdempotencyService idempotency` — Prevent double-spend on Drawn tab

**Flow:**

**1. Validate input**
```csharp
if (req.TargetUserId <= 0 || req.TargetUserId == me)
    return Results.BadRequest(new { error = "INVALID_TARGET" });

var targetExists = await db.Users.AnyAsync(u => u.Id == req.TargetUserId, ct);
if (!targetExists) return Results.BadRequest(new { error = "TARGET_NOT_FOUND" });

var blocked = await db.Blocks.AnyAsync(b =>
    (b.BlockerId == me && b.BlockedId == req.TargetUserId) ||
    (b.BlockerId == req.TargetUserId && b.BlockedId == me), ct);
if (blocked) return Results.BadRequest(new { error = "BLOCKED" });
```

**2. Handle PASS**
```csharp
if (choiceEnum == MomentChoice.PASS)
{
    db.MomentResponses.Add(new MomentResponse { DateUtc = today, FromUserId = me, ToUserId = req.TargetUserId, Choice = MomentChoice.PASS, ... });
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { status = "RECORDED_PASS" });
}
```

**Pass is free** — no budget spend, no match check.

**3. Spend budget/sparks**
```csharp
if (isFromLikedYou)
{
    var sparkSpend = await sparks.TrySpendAsync(me, ct);
    if (!sparkSpend.Allowed)
        return Results.BadRequest(new { error = sparkSpend.DenyReason, sparkBalance = sparkSpend.Balance });
    newSparkBalance = sparkSpend.Balance;
}
else
{
    var spend = await budget.TrySpendAsync(me, InteractionBudgetService.SpendType.Moment, ct);
    if (!spend.Allowed)
        return Results.BadRequest(new { error = spend.DenyReason, spend.TotalUsed });
}
```

**4. Record response (upsert from PASS)**
```csharp
if (existingToday == null)
{
    db.MomentResponses.Add(new MomentResponse { ... });
}
else
{
    existingToday.Choice = choiceEnum.Value;  // Upgrade from PASS
    existingToday.TimeOnCardMs = req.TimeOnCardMs;
    existingToday.Source = source;
}
await db.SaveChangesAsync(ct);
```

**5. Fire-and-forget visual decision recording**
```csharp
_ = Task.Run(async () =>
{
    using var scope = scopeFactory.CreateScope();
    var innerDb = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
    var primaryPhoto = await innerDb.PhotoEmbeddings.AsNoTracking()
        .Where(p => p.UserId == capturedTarget)
        .OrderBy(p => p.EmbeddedAt)
        .Select(p => (int?)p.Id)
        .FirstOrDefaultAsync();
    innerDb.UserVisualDecisions.Add(new UserVisualDecision { ... });
    await innerDb.SaveChangesAsync();
});
```

Background task records visual preference for CLIP learning.

**6. Check for counterpart response**
```csharp
MomentResponse? other;
if (isFromLikedYou)
{
    other = await db.MomentResponses.AsNoTracking()
        .Where(r => r.FromUserId == req.TargetUserId && r.ToUserId == me && IsPositiveExpression(r.Choice))
        .OrderByDescending(r => r.CreatedAt)
        .FirstOrDefaultAsync(ct);
}
else
{
    other = await db.MomentResponses.AsNoTracking()
        .FirstOrDefaultAsync(r =>
            r.DateUtc == today &&
            r.FromUserId == req.TargetUserId &&
            r.ToUserId == me &&
            IsPositiveExpression(r.Choice), ct);
}

if (other is null)
    return Results.Ok(new { status = "RECORDED_WAITING", sparkBalance = newSparkBalance });
```

**7. Create match**
```csharp
var (a, b) = MomentsRules.NormalizePair(me, req.TargetUserId);
var matchType = IsPure(choiceEnum.Value, other.Choice) ? MatchType.PURE : MatchType.EDGE;
var edgeOwner = matchType == MatchType.EDGE ? (int?)(Random.Shared.Next(0, 2) == 0 ? a : b) : null;
var created = await matchService.CreateActiveMatchAsync(a, b, matchType, edgeOwner, ct);

var statusStr = created.Created
    ? (matchType == MatchType.PURE ? "PURE_MATCH_CREATED" : "EDGE_MATCH_CREATED")
    : "MATCH_NOT_CREATED";

return Results.Ok(new { status = statusStr, reason = created.Reason, matchId = created.Match?.Id, matchType, edgeOwnerId, sparkBalance = newSparkBalance });
```

### POST /moments/choose (Choice + ChatNote)

**Purpose:** Record MAGICAL/LOGICAL choice with 20-150 char note (atomic commitment point)

**Request:**
```csharp
public record ChooseRequest(int TargetUserId, string Choice, string NoteText, string? Source, int? TimeOnCardMs);
```

**Dependencies:** Same as `/respond`, plus:
- Validates `NoteText` length (20-150)
- Checks for duplicate `ChatNote` (one note per target per user)

**Flow:**

**1-3. Validate + spend** (same as `/respond`)

**4. Record response + note**
```csharp
// Upsert MomentResponse
if (existingResponse == null)
{
    db.MomentResponses.Add(new MomentResponse { ... });
}
else
{
    existingResponse.Choice = choiceEnum.Value;
    existingResponse.TimeOnCardMs = req.TimeOnCardMs;
    existingResponse.Source = source;
}

// Record ChatNote (never upsert — one per target)
var note = new ChatNote
{
    FromUserId = me,
    ToUserId = req.TargetUserId,
    Choice = choiceEnum.Value,
    NoteText = noteText,
    Source = source,
    CreatedAt = MomentsRules.NowUtc()
};
db.ChatNotes.Add(note);
await db.SaveChangesAsync(ct);
```

**5. Check counterpart response + note**
```csharp
var otherResponse = await db.MomentResponses.AsNoTracking().FirstOrDefaultAsync(...);
if (otherResponse is null)
    return Results.Ok(new { status = "RECORDED_WAITING" });

var otherNote = await db.ChatNotes.AsNoTracking()
    .FirstOrDefaultAsync(n => n.FromUserId == req.TargetUserId && n.ToUserId == me, ct);
if (otherNote is null)
    return Results.Ok(new { status = "WAITING_FOR_OTHER_NOTE" });
```

**Both response AND note must exist** to create match.

**6. Create match + link notes**
```csharp
var created = await matchService.CreateActiveMatchAsync(a, b, matchType, edgeOwner, ct);

if (created.Created && created.Match != null)
{
    note.MatchId = created.Match.Id;
    otherNote = await db.ChatNotes.FirstOrDefaultAsync(n => n.FromUserId == req.TargetUserId && n.ToUserId == me, ct);
    if (otherNote != null) otherNote.MatchId = created.Match.Id;
    await db.SaveChangesAsync(ct);
}
```

**ChatNote.MatchId** links notes to the match they created (for future ECHO analysis).

## Services

### MomentsRules (static utilities)

**File:** `backend/WovenBackend/Services/Moments/MomentsRules.cs`

```csharp
public static class MomentsRules
{
    public const int DailyTotalCap = 5;
    public const int DailyPendingCap = 2;  // legacy Save budget
    public static readonly TimeSpan BalloonLifetime = TimeSpan.FromHours(36);

    public static DateOnly UtcToday() => DateOnly.FromDateTime(DateTime.UtcNow);
    public static DateTimeOffset NowUtc() => DateTimeOffset.UtcNow;
    public static DateTimeOffset ComputeExpiresAt(DateTimeOffset createdAtUtc)
        => createdAtUtc.Add(BalloonLifetime);

    public static (int A, int B) NormalizePair(int user1, int user2)
        => user1 < user2 ? (user1, user2) : (user2, user1);
}
```

**Pair normalization** — always store (A, B) where A < B to prevent duplicate matches `(123, 456)` and `(456, 123)`.

### InteractionBudgetService (daily cap)

**File:** `backend/WovenBackend/Services/Moments/InteractionBudgetService.cs`

**Purpose:** Enforce 5 actions/day limit on Deck tab (Today source)

**Database:** `daily_interactions` table
```sql
CREATE TABLE daily_interactions (
  user_id INT NOT NULL,
  date_utc DATE NOT NULL,
  total_used SMALLINT DEFAULT 0,
  pending_used SMALLINT DEFAULT 0,  -- legacy Save budget
  updated_at TIMESTAMPTZ,
  PRIMARY KEY (user_id, date_utc)
);
```

**Redis fast-gate:** Checks `spark:{userId}:{dateUtc}` counter before DB query. If cached count >= 5, rejects immediately (no DB hit).

**TrySpendAsync flow:**
1. Check Redis counter (fast-gate)
2. Begin SERIALIZABLE transaction (prevents double-spend race)
3. Load or create `DailyInteraction` row
4. Check `total_used >= 5` → reject if over cap
5. Increment `total_used`
6. Commit transaction
7. Increment Redis counter (best-effort)

**RefundSparkAsync:** Called when match ends as ghost (no messages). Decrements both Redis and DB counters (floor 0).

### SparkWalletService (spark economy)

**File:** `backend/WovenBackend/Services/Moments/SparkWalletService.cs`

**Purpose:** Enforce spark spend (1 spark per Drawn tab action), daily earn (5 sparks), ghost refund (0.5 sparks)

**Database:** `spark_wallets` table
```sql
CREATE TABLE spark_wallets (
  user_id INT PRIMARY KEY,
  balance_tenths INT DEFAULT 50,  -- 5.0 sparks
  last_earned_date DATE,
  updated_at TIMESTAMPTZ
);
```

**Stored as tenths** (50 = 5.0 sparks) to avoid floating-point precision issues.

**GetBalanceAsync:**
1. Ensure wallet exists (create with 50 tenths if not)
2. Earn daily sparks if `last_earned_date < today`
3. Return `balance_tenths / 10m`

**TrySpendAsync:**
1. Begin SERIALIZABLE transaction
2. Earn daily sparks inside transaction (prevent double-earn)
3. Check `balance_tenths >= 10` (1.0 spark)
4. Deduct 10 tenths
5. Commit transaction
6. Return `{ allowed: true, balance }`

**GhostRefundAsync:**
1. Ensure wallet exists
2. Add 5 tenths (0.5 sparks), cap at 100 tenths (10.0 sparks)
3. Save

### MomentsMatchService (match creation)

**File:** `backend/WovenBackend/Services/Moments/MomentsMatchService.cs`

**Purpose:** Create match entity, send notifications

**CreateActiveMatchAsync:**
1. Validate input (`PURE` cannot have `edge_owner_id`, etc.)
2. Normalize pair: `(a, b)` where `a < b`
3. Begin SERIALIZABLE transaction
4. Check for existing active match (prevent duplicate)
5. Create `Match` entity: `BalloonState.ACTIVE`, `ExpiresAt = now + 36h`
6. Commit transaction
7. Send notifications:
   - **EDGE:** Notify recipient (other user)
   - **PURE:** Notify both users
8. Track analytics (match created event)
9. Return `{ created: true, match, reason: null }`

**Notification routing:**
```csharp
if (matchType == MatchType.EDGE && edgeOwnerId.HasValue)
{
    var recipientId = edgeOwnerId.Value == a ? b : a;
    await _notifications.MomentReceivedAsync(recipientId, match.Id, edgeOwnerId.Value, ct);
}
else if (matchType == MatchType.PURE)
{
    await Task.WhenAll(
        _notifications.MomentReceivedAsync(a, match.Id, b, ct),
        _notifications.MomentReceivedAsync(b, match.Id, a, ct)
    );
}
```

## Entities

### MomentResponse

**Table:** `moment_responses`  
**File:** `backend/WovenBackend/data/Entities/Moments/MomentResponse.cs`

```csharp
[Table("moment_responses")]
public class MomentResponse
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("date_utc")]
    public DateOnly DateUtc { get; set; }

    [Column("from_user_id")]
    public int FromUserId { get; set; }

    [Column("to_user_id")]
    public int ToUserId { get; set; }

    [Column("choice")]
    public MomentChoice Choice { get; set; }

    [Column("time_on_card_ms")]
    public int? TimeOnCardMs { get; set; }  // Dwell time (Deck tab only)

    [Column("source")]
    [MaxLength(20)]
    public string? Source { get; set; }  // "TODAY" or "LIKED_YOU"

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

**Unique index:** `(from_user_id, to_user_id, date_utc)` — one response per target per day (upgrades from PASS allowed).

### ChatNote

**Table:** `chat_notes`  
**File:** `backend/WovenBackend/data/Entities/Moments/ChatNote.cs`

```csharp
[Table("chat_notes")]
public class ChatNote
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("from_user_id")]
    public int FromUserId { get; set; }

    [Column("to_user_id")]
    public int ToUserId { get; set; }

    [Column("choice")]
    public MomentChoice Choice { get; set; }

    [Column("note_text")]
    [MaxLength(150)]
    public string NoteText { get; set; } = "";

    [Column("source")]
    [MaxLength(20)]
    public string? Source { get; set; }

    [Column("match_id")]
    public Guid? MatchId { get; set; }  // Linked after match created

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

**Unique index:** `(from_user_id, to_user_id)` — one note per target per user (no duplicates).

### Match

**Table:** `matches`  
**File:** `backend/WovenBackend/data/Entities/Moments/Match.cs`

See `match-creation.md` for full entity definition.

### DailyDeck

**Table:** `daily_decks`  
**File:** `backend/WovenBackend/data/Entities/DailyDeck.cs`

```csharp
public class DailyDeck
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly DateUtc { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string ItemsJson { get; set; } = "[]";  // [{ candidateId, score, bucket, explanationId }, ...]
    public string? MoodLine { get; set; }  // ECHO's one-line read on deck quality
}
```

**Unique index:** `(user_id, date_utc)` — one deck per user per day.

## Related files

**Endpoints:**
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs`

**Services:**
- `backend/WovenBackend/Services/Moments/MomentsRules.cs`
- `backend/WovenBackend/Services/Moments/InteractionBudgetService.cs`
- `backend/WovenBackend/Services/Moments/SparkWalletService.cs`
- `backend/WovenBackend/Services/Moments/MomentsMatchService.cs`
- `backend/WovenBackend/Services/Moments/BalloonExpiryWorker.cs`

**Entities:**
- `backend/WovenBackend/data/Entities/Moments/MomentResponse.cs`
- `backend/WovenBackend/data/Entities/Moments/ChatNote.cs`
- `backend/WovenBackend/data/Entities/Moments/Match.cs`
- `backend/WovenBackend/data/Entities/DailyDeck.cs`
- `backend/WovenBackend/data/Entities/DailyInteraction.cs`
- `backend/WovenBackend/data/Entities/SparkWallet.cs`
