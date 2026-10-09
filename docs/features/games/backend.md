# Games — Backend Implementation

**Status:** SHIPPED  
**Last Updated:** 2026-08-17

---

## Architecture

**Layers:**
1. **Endpoints** (API routes) → `GameEndpoints.cs`
2. **Service** (orchestration) → `GameService.cs`
3. **Agents** (AI content generation) → `KnowMeAgent.cs`, `RedGreenFlagAgent.cs`
4. **Outcome tracking** (ECHO signals) → `GameOutcomeService.cs`
5. **Data** (entities) → `GameSession`, `GameRound`, `GameResult`, `GameOutcome`

---

## GameEndpoints.cs

**Path:** `backend/WovenBackend/Endpoints/GameEndpoints.cs`

**Registration:**
```csharp
public static void MapGameEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/games");
    group.RequireAuthorization();  // All endpoints require JWT
    
    // ... endpoint mappings
}
```

---

### GET /games/matches/{matchId}/availability

**Purpose:** Check if a user can initiate a game in this match.

**Handler:**
```csharp
group.MapGet("/matches/{matchId:guid}/availability", async (
    Guid matchId,
    IGameService gameService,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);
    var availability = await gameService.CheckAvailabilityAsync(matchId, userId, ct);

    return Results.Ok(new
    {
        available = availability.Available,
        gamesRemaining = availability.GamesRemaining,
        reason = availability.Reason,
        games = new[]
        {
            new { type = "KNOW_ME", name = "Know Me", description = "Guess what they picked", duration = "2 min", icon = "🎯" },
            new { type = "RED_GREEN_FLAG", name = "Red / Green Flag", description = "React to scenarios and learn each other fast", duration = "2 min", icon = "🚦" }
        }
    });
});
```

**Response:**
```json
{
  "available": true,
  "gamesRemaining": 1,
  "reason": null,
  "games": [...]
}
```

**Availability checks:**
- Daily limit: user initiated < 2 games today
- No pending game in this match

---

### POST /games/matches/{matchId}/sessions

**Purpose:** Create a game invite (initiator sends to partner).

**Handler:**
```csharp
group.MapPost("/matches/{matchId:guid}/sessions", async (
    Guid matchId,
    CreateGameRequest req,
    IGameService gameService,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);

    if (!Enum.TryParse<GameSessionType>(req.GameType, true, out var gameType))
        return Results.BadRequest(new { error = "Invalid game type" });

    try
    {
        var session = await gameService.CreateSessionAsync(matchId, userId, gameType, ct);
        return Results.Ok(session);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
});
```

**Request:**
```json
{
  "gameType": "KNOW_ME"
}
```

**Response:**
```json
{
  "sessionId": "abc-123",
  "matchId": "def-456",
  "gameType": "KNOW_ME",
  "status": "PENDING",
  "expiresAt": "2026-08-17T12:30:00Z"
}
```

**Side effects:**
- Creates `GameSession` entity (status=PENDING, expires in 10min)
- Increments `DailyInteraction.GamesInitiated`
- Adds system message to chat thread (type=GAME)
- Sends push notification to partner (Phase 1C)

---

### POST /games/sessions/{sessionId}/accept

**Purpose:** Partner accepts the game invite.

**Handler:**
```csharp
group.MapPost("/sessions/{sessionId:guid}/accept", async (
    Guid sessionId,
    IGameService gameService,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);
    var accepted = await gameService.AcceptSessionAsync(sessionId, userId, ct);

    return accepted
        ? Results.Ok(new { status = "ACTIVE", message = "Game started!" })
        : Results.BadRequest(new { error = "Cannot accept game" });
});
```

**Checks:**
- Session exists and status=PENDING
- Not expired (10min + 30sec grace period)
- User is part of the match

**Side effects:**
- Session status → ACTIVE
- Expiry extended to 30 minutes
- Generates Round 1 (AI call)
- Creates `GameRound` entity
- Creates `GameAnalytic` entity
- Adds system message ("✅ Game started")
- Sends push notifications (Phase 1C)

---

### POST /games/sessions/{sessionId}/reject

**Purpose:** Partner rejects the game invite.

**Handler:**
```csharp
group.MapPost("/sessions/{sessionId:guid}/reject", async (
    Guid sessionId,
    IGameService gameService,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);
    var rejected = await gameService.RejectSessionAsync(sessionId, userId, ct);

    return rejected
        ? Results.Ok(new { status = "REJECTED" })
        : Results.BadRequest(new { error = "Cannot reject game" });
});
```

**Side effects:**
- Session status → REJECTED
- No ECHO signals logged (game never started)

---

### GET /games/sessions/{sessionId}/round

**Purpose:** Fetch current round data (questions, role, state).

**Handler:**
```csharp
group.MapGet("/sessions/{sessionId:guid}/round", async (
    Guid sessionId,
    IGameService gameService,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);
    var round = await gameService.GetCurrentRoundAsync(sessionId, userId, ct);

    return round == null
        ? Results.NotFound(new { error = "Round not found or game not active" })
        : Results.Ok(round);
});
```

**Response:**
```json
{
  "roundNumber": 1,
  "totalRounds": 2,
  "questions": [
    {
      "id": "q1",
      "text": "What's their go-to trail snack?",
      "options": [
        { "id": "a", "text": "Energy bars", "isCorrect": false },
        { "id": "b", "text": "Fresh fruit", "isCorrect": true },
        { "id": "c", "text": "Trail mix", "isCorrect": false },
        { "id": "d", "text": "Chocolate", "isCorrect": false }
      ],
      "difficulty": "EASY",
      "category": "lifestyle"
    }
  ],
  "timeLimit": 90,
  "isGuesser": true,
  "hasAnswered": false,
  "waitingForOther": false
}
```

---

### POST /games/sessions/{sessionId}/answers

**Purpose:** Guesser submits their predictions.

**Rate limit:** 5 requests/min (via `CheckRateLimitAsync`)

**Handler:**
```csharp
group.MapPost("/sessions/{sessionId:guid}/answers", async (
    Guid sessionId,
    SubmitAnswersRequest req,
    IGameService gameService,
    ICacheService cache,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);

    var rateLimitKey = $"ratelimit:game:answers:{userId}";
    var allowed = await cache.CheckRateLimitAsync(rateLimitKey, 5, TimeSpan.FromMinutes(1), ct);
    if (!allowed)
        return Results.Json(new { error = "RATE_LIMIT_EXCEEDED", retryAfter = 60 }, statusCode: 429);

    try
    {
        var result = await gameService.SubmitAnswersAsync(sessionId, userId, req.Answers, ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
```

**Request:**
```json
{
  "answers": {
    "q1": "b",
    "q2": "a",
    "q3": "d"
  }
}
```

**Response:**
```json
{
  "roundNumber": 1,
  "status": "WAITING_FOR_TARGET",
  "message": "Waiting for them to reveal their answers..."
}
```

**Side effects:**
- Stores `GameRound.AnswersJson`
- No scoring yet (waiting for target)

---

### POST /games/sessions/{sessionId}/target-answers

**Purpose:** Target submits their actual answers.

**Rate limit:** 5 requests/min (triggers AI calls)

**Handler:**
```csharp
group.MapPost("/sessions/{sessionId:guid}/target-answers", async (
    Guid sessionId,
    SubmitAnswersRequest req,
    IGameService gameService,
    ICacheService cache,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);

    var rateLimitKey = $"ratelimit:game:target-answers:{userId}";
    var allowed = await cache.CheckRateLimitAsync(rateLimitKey, 5, TimeSpan.FromMinutes(1), ct);
    if (!allowed)
        return Results.Json(new { error = "RATE_LIMIT_EXCEEDED", retryAfter = 60 }, statusCode: 429);

    try
    {
        var result = await gameService.SubmitTargetAnswersAsync(sessionId, userId, req.Answers, ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
```

**Response (Round 1 → Round 2):**
```json
{
  "roundNumber": 1,
  "score": 2,
  "totalQuestions": 3,
  "status": "NEXT_ROUND",
  "message": "Round 1 complete! Score: 2/3"
}
```

**Response (Round 2 → Game Complete):**
```json
{
  "roundNumber": 2,
  "score": 1,
  "totalQuestions": 3,
  "status": "GAME_COMPLETE",
  "message": "Game complete! See final results."
}
```

**Side effects:**
- Stores `GameRound.TargetAnswersJson`
- Calculates score (matches between guesser + target answers)
- Stores `GameRound.Score`
- If Round 1 → generates Round 2 (roles flip)
- If Round 2 → completes game (creates `GameResult`, `GameOutcome`, logs ECHO signals)

---

### GET /games/sessions/{sessionId}/result

**Purpose:** Fetch final game results.

**Handler:**
```csharp
group.MapGet("/sessions/{sessionId:guid}/result", async (
    Guid sessionId,
    IGameService gameService,
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);
    var result = await gameService.GetFinalResultAsync(sessionId, ct);
    if (result == null)
        return Results.NotFound(new { error = "Result not found" });

    var userA = await db.Users.FirstOrDefaultAsync(u => u.Id == result.UserAId, ct);
    var userB = await db.Users.FirstOrDefaultAsync(u => u.Id == result.UserBId, ct);

    return Results.Ok(new
    {
        sessionId = result.SessionId,
        gameType = result.GameType,
        yourScore = userId == result.UserAId ? result.UserAScore : result.UserBScore,
        theirScore = userId == result.UserAId ? result.UserBScore : result.UserAScore,
        youWon = result.WinnerUserId == userId,
        isTie = result.WinnerUserId == null,
        aiInsight = result.AiInsight,
        userAName = userA?.FullName,
        userBName = userB?.FullName
    });
});
```

**Response:**
```json
{
  "sessionId": "abc-123",
  "gameType": "KNOW_ME",
  "yourScore": 3,
  "theirScore": 5,
  "youWon": false,
  "isTie": false,
  "aiInsight": "One of you is reading the other better. Interesting dynamic.",
  "userAName": "Alex",
  "userBName": "Sam"
}
```

---

### GET /games/matches/{matchId}/active

**Purpose:** Check if there's an active or pending game in this match.

**Handler:**
```csharp
group.MapGet("/matches/{matchId:guid}/active", async (
    Guid matchId,
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);

    var activeSession = await db.GameSessions
        .Where(g => g.MatchId == matchId &&
                   (g.Status == GameSessionStatus.PENDING.ToString() ||
                    g.Status == GameSessionStatus.ACTIVE.ToString()))
        .OrderByDescending(g => g.CreatedAt)
        .FirstOrDefaultAsync(ct);

    if (activeSession == null)
        return Results.Ok(new { hasActive = false });

    return Results.Ok(new
    {
        hasActive = true,
        sessionId = activeSession.Id,
        gameType = activeSession.GameType,
        status = activeSession.Status,
        expiresAt = activeSession.ExpiresAt,
        isInitiator = activeSession.InitiatorUserId == userId
    });
});
```

**Used by frontend:** Poll this endpoint to detect if the other user started a game while you were chatting.

---

## GameService.cs

**Path:** `backend/WovenBackend/Services/Games/GameService.cs`

**Interface:** `IGameService`

---

### CreateSessionAsync

**Steps:**
1. Check daily limit (`DailyInteraction.GamesInitiated < 2`)
2. Load match, verify balloon=ACTIVE
3. Prevent duplicate pending sessions
4. Create `GameSession` (status=PENDING, expires in 10min)
5. Increment daily counter
6. Add GAME chat message
7. Add SYSTEM message
8. Send push notification (Phase 1C)
9. Track analytics event

**Expiry time:**
```csharp
ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)  // Extended from 3min (June fix)
```

---

### AcceptSessionAsync

**Steps:**
1. Load session, verify status=PENDING
2. Check expiry (with 30-second grace period)
3. Verify user is part of match
4. Update status → ACTIVE
5. Extend expiry to 30 minutes
6. **Build GameContext** (load ECHO profiles, pair context)
7. **Generate Round 1** via `IGameAgent`
8. Store session metadata (difficulty, tone, bucket)
9. Create `GameRound` entity
10. Create `GameAnalytic` entity (track messages before game)
11. Add SYSTEM message
12. Send push notifications (Phase 1C)
13. Track analytics event

**Grace period logic:**
```csharp
if (DateTimeOffset.UtcNow > session.ExpiresAt.AddSeconds(30))
{
    session.Status = GameSessionStatus.EXPIRED.ToString();
    await _db.SaveChangesAsync(ct);
    return false;
}
```

**Why grace period?** Network latency — user taps Accept right before expiry, request arrives 1-2 seconds late.

---

### SubmitTargetAnswersAsync

**Steps:**
1. Load session + current round
2. Store `GameRound.TargetAnswersJson`
3. **Calculate score:**
   ```csharp
   var score = questions.Count(q =>
       guesserAnswers.TryGetValue(q.Id, out var guessed) &&
       targetAnswers.TryGetValue(q.Id, out var actual) &&
       guessed == actual
   );
   ```
4. Store `GameRound.Score`
5. Mark `GameRound.CompletedAt`
6. **If Round 1:**
   - Increment `session.CurrentRound`
   - **Generate Round 2** (roles flipped)
   - Return `status: "NEXT_ROUND"`
7. **If Round 2:**
   - Call `CompleteGameAsync()`
   - Return `status: "GAME_COMPLETE"`

---

### CompleteGameAsync

**Steps:**
1. Update session status → COMPLETED
2. Load all rounds
3. Calculate total scores (sum of each user's rounds as guesser)
4. **Generate AI insight** via `IGameAgent.GenerateInsightAsync()`
5. Create `GameResult` entity (scores, winner, insight)
6. Mark `GameAnalytic.Completed = true`
7. **Record outcome** via `IGameOutcomeService.RecordOutcomeAsync()` → **ECHO signals logged**
8. Add SYSTEM message ("🏁 Game completed")
9. Send push notifications (Phase 1C)
10. Track analytics events

**Winner determination:**
```csharp
WinnerUserId = userAScore > userBScore
    ? match.UserAId
    : (userBScore > userAScore ? match.UserBId : null)  // null = tie
```

---

### BuildGameContextAsync

**Loads ECHO profile data for AI personalization:**

```csharp
private async Task<GameContext> BuildGameContextAsync(
    GameSession session,
    Match match,
    int roundNumber,
    CancellationToken ct)
{
    var guesserUserId = roundNumber == 1
        ? session.InitiatorUserId
        : (match.UserAId == session.InitiatorUserId ? match.UserBId : match.UserAId);

    var targetUserId = guesserUserId == match.UserAId ? match.UserBId : match.UserAId;

    var userAVector = await LoadUserVectorAsync(match.UserAId, ct);
    var userBVector = await LoadUserVectorAsync(match.UserBId, ct);
    var pairContext = await _aiProfileService.GetPairContextAsync(match.UserAId, match.UserBId, ct);

    var difficulty = DetermineDifficulty(pairContext);
    var tone = DetermineTone(pairContext);
    var intentAlignment = pairContext?.IntentAlignment ?? 0.5;
    var bucket = DetermineMatchBucket(pairContext);

    return new GameContext { ... };
}
```

**LoadUserVectorAsync:**
```csharp
private async Task<UserVectorData?> LoadUserVectorAsync(int userId, CancellationToken ct)
{
    var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
    var vector = await _db.UserVectors
        .Where(v => v.UserId == userId)
        .OrderByDescending(v => v.Version)
        .FirstOrDefaultAsync(ct);
    var tags = await _db.UserVectorTags.Where(t => t.UserId == userId).Select(t => t.Tag).Take(10).ToListAsync(ct);
    var lifestyle = await _db.UserOptionalFields.Where(f => f.UserId == userId).ToDictionaryAsync(f => f.Key, f => f.Value ?? "", ct);

    var pillars = JsonSerializer.Deserialize<Dictionary<string, double>>(vector.PillarScoresJson) ?? new();
    var pulse = JsonSerializer.Deserialize<Dictionary<string, double>>(vectorData["pulse"].GetRawText()) ?? new();

    return new UserVectorData { ... };
}
```

---

## Data Entities

### GameSession

**Table:** `game_sessions`

```csharp
public class GameSession
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public string GameType { get; set; }           // "KNOW_ME" | "RED_GREEN_FLAG"
    public int InitiatorUserId { get; set; }
    public string Status { get; set; }             // "PENDING" | "ACTIVE" | "COMPLETED" | "REJECTED" | "EXPIRED"
    public int TotalRounds { get; set; } = 2;
    public int CurrentRound { get; set; } = 1;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? MetadataJson { get; set; }      // { difficulty, tone, bucket, ... }
}
```

---

### GameRound

**Table:** `game_rounds`

```csharp
public class GameRound
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int RoundNumber { get; set; }           // 1 or 2
    public int GuesserUserId { get; set; }         // Person guessing
    public int TargetUserId { get; set; }          // Person being guessed about
    public string QuestionsJson { get; set; }      // AI-generated questions
    public string? AnswersJson { get; set; }       // Guesser's answers
    public string? TargetAnswersJson { get; set; } // Target's actual answers
    public int? Score { get; set; }                // Matches count (0-3)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

---

### GameResult

**Table:** `game_results`

```csharp
public class GameResult
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid MatchId { get; set; }
    public string GameType { get; set; }
    public int? UserAScore { get; set; }
    public int? UserBScore { get; set; }
    public int? WinnerUserId { get; set; }  // null = tie
    public string? AiInsight { get; set; }   // "You're reading each other pretty well."
    public DateTimeOffset CreatedAt { get; set; }
}
```

---

### GameOutcome

**Table:** `game_outcomes`  
**See:** [outcomes.md](./outcomes.md#gameoutcome-entity)

---

## Logging

All game operations log with:
- **Correlation ID** (from middleware)
- **Session ID**
- **User IDs**
- **Status transitions**

**Example logs:**
```
[Game] Creating session for match {MatchId}, initiator {UserId}, type {GameType}
[Game] Created session {SessionId}, expires at {ExpiresAt}
[Game] User {UserId} accepting session {SessionId}
[Game] Session {SessionId} started, round 1 generated, expires at {ExpiresAt}
[Game] User {UserId} submitting answers for session {SessionId}
[Game] Completing session {SessionId}
[Game] Session {SessionId} completed, winner: {Winner}
[GameOutcome] Recording outcome for session {SessionId}, status={Status}
[GameOutcome] Recorded outcome {Id} for session {SessionId}
```

---

## Error Handling

**Common error responses:**

**400 Bad Request:**
- Invalid game type
- Session not active
- Daily limit reached
- Pending game already exists

**401 Unauthorized:**
- Missing/invalid JWT

**403 Forbidden:**
- User not part of this match

**404 Not Found:**
- Session not found
- Round not found
- Match not found

**429 Rate Limit:**
```json
{
  "error": "RATE_LIMIT_EXCEEDED",
  "retryAfter": 60
}
```

---

## Future Enhancements

- [ ] **Abandoned game tracking** (user exits mid-round)
- [ ] **Response time tracking** (measure answer speed)
- [ ] **Batch game cleanup** (expire old PENDING sessions)
- [ ] **Game recommendations** (suggest which game type to play)
- [ ] **Replay protection** (prevent same questions for same pair)
