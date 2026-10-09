# Matches — Backend

**Backend implementation** of the Matches feature: Match entity, match creation, access control, trial period, balloon lifecycle, and all match-related endpoints.

---

## File structure

```
backend/WovenBackend/
├── data/Entities/Moments/
│   ├── Match.cs                         # Core entity
│   └── PendingMatch.cs                  # Pre-match state (not used yet)
├── data/Entities/
│   ├── MatchExplanation.cs              # AI-generated "why you match" text
│   ├── MatchSignalLog.cs                # Behavioral signals for ECHO
│   ├── MatchOutcome.cs                  # Outcome tracking (unmatch/block)
│   ├── UserMatchingWeight.cs            # ECHO learned weights
│   └── MatchBucket.cs                   # Match bucket enum
├── Services/Matchmaking/
│   ├── MatchExplanationService.cs       # Generates explanations
│   ├── MatchNarratorService.cs          # Generates TTS narration
│   ├── MatchService.cs                  # Match creation logic
│   ├── MatchOutcomeService.cs           # Outcome tracking
│   └── MatchSignalService.cs            # Signal logging
└── Endpoints/
    └── MatchesEndpoints.cs              # API routes
```

---

## Match entity

**Path:** `backend/WovenBackend/data/Entities/Moments/Match.cs`

**Table:** `matches`

**Enums:**
```csharp
public enum MatchType { PURE = 1, EDGE = 2 }
public enum BalloonState { ACTIVE = 1, CLOSED = 2 }
public enum ClosedReason { POP = 1, EXPIRE = 2, UNMATCH = 3, BLOCK = 4 }
```

**Properties:**
```csharp
public Guid Id { get; set; } = Guid.NewGuid();
public int UserAId { get; set; }
public int UserBId { get; set; }
public MatchType MatchType { get; set; }
public int? EdgeOwnerId { get; set; }
public BalloonState BalloonState { get; set; }
public ClosedReason? ClosedReason { get; set; }

// Timestamps
public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
public DateTimeOffset ExpiresAt { get; set; }
public DateTimeOffset? ClosedAt { get; set; }
public DateTimeOffset? BothMessagedAt { get; set; }
public DateTimeOffset? FindLoveAt { get; set; }

// Trial period
public bool IsTrial { get; set; } = false;
public DateTimeOffset? TrialStartedAt { get; set; }
public DateTimeOffset? TrialEndsAt { get; set; }
public string? UserADecision { get; set; }        // "CONTINUE" | "END" | "BLOCK"
public string? UserBDecision { get; set; }
public DateTimeOffset? TrialUserAOpenedAt { get; set; }
public DateTimeOffset? TrialUserBOpenedAt { get; set; }
public string? TrialEndReason { get; set; }       // "no_spark" | "wrong_timing" | "not_my_type"

// Date coordination (Phase 4A)
public bool DateIdeaInterestedA { get; set; } = false;
public bool DateIdeaInterestedB { get; set; } = false;
public DateTimeOffset? DateIdeaInterestedAt { get; set; }
public DateTimeOffset? DateAgreedAt { get; set; }
```

**Key design notes:**
- `UserAId` and `UserBId` are NOT ordered by choice type (one isn't always MAGICAL)
- `EdgeOwnerId` is set ONLY for EDGE matches (null for PURE)
- Trial period mechanics use TWO "opened" timestamps (one per user)
- `TrialEndsAt` is set ONLY when both users have opened the thread
- Date coordination fields are placeholders for future feature

---

## Match creation flow

**Trigger:** `POST /moments/respond` with `action: "MAGICAL"` or `action: "RESONANT"`

**Logic in `MatchService.CreateMatchAsync()`:**

1. **Check for reciprocal choice:**
   ```csharp
   var reciprocalChoice = await db.DailyDeckItems
       .Where(i => i.CandidateId == viewerId 
                && i.UserId == candidateId 
                && i.Choice != null
                && i.Choice != "PASS")
       .OrderByDescending(i => i.CreatedAt)
       .FirstOrDefaultAsync();
   
   if (reciprocalChoice == null) return null;  // No match yet
   ```

2. **Determine match type:**
   ```csharp
   var viewerChoice = action;  // "MAGICAL" or "RESONANT"
   var candidateChoice = reciprocalChoice.Choice;
   
   MatchType matchType;
   int? edgeOwnerId = null;
   
   if (viewerChoice == candidateChoice)
   {
       matchType = MatchType.PURE;
   }
   else
   {
       matchType = MatchType.EDGE;
       edgeOwnerId = (viewerChoice == "MAGICAL") ? viewerId : candidateId;
   }
   ```

3. **Create match record:**
   ```csharp
   var match = new Match
   {
       Id = Guid.NewGuid(),
       UserAId = viewerId,
       UserBId = candidateId,
       MatchType = matchType,
       EdgeOwnerId = edgeOwnerId,
       BalloonState = BalloonState.ACTIVE,
       CreatedAt = now,
       ExpiresAt = now.AddHours(96),  // 96h balloon window
       IsTrial = false  // Trial starts on pop, not creation
   };
   
   db.Matches.Add(match);
   await db.SaveChangesAsync();
   ```

4. **Create chat thread:**
   ```csharp
   var thread = new ChatThread
   {
       Id = Guid.NewGuid(),
       MatchId = match.Id,
       UserAId = viewerId,
       UserBId = candidateId,
       CreatedAt = now
   };
   
   db.ChatThreads.Add(thread);
   await db.SaveChangesAsync();
   ```

5. **Pre-load bridge question as system message** (if available):
   ```csharp
   var explanation = await db.MatchExplanations
       .Where(e => e.UserId == viewerId && e.CandidateId == candidateId)
       .OrderByDescending(e => e.CreatedAt)
       .FirstOrDefaultAsync();
   
   if (explanation?.BridgeQuestion != null)
   {
       db.ChatMessages.Add(new ChatMessage
       {
           ThreadId = thread.Id,
           MessageType = "SYSTEM",
           Content = $"Suggested opener: {explanation.BridgeQuestion}",
           CreatedAt = now
       });
       await db.SaveChangesAsync();
   }
   ```

**Result:** Match record + chat thread created, balloon is ACTIVE.

---

## Access control logic

**Endpoint:** `GET /matches/{matchId}/profile`

**Access rules:**

| User type | Access level | Unlock trigger |
|---|---|---|
| Pure match (both users) | FULL | Immediate |
| Edge owner | FULL | Immediate |
| Edge non-owner | LIMITED | Until `BothMessagedAt != null` |

**Implementation:**
```csharp
string accessLevel;
string reason;

if (match.MatchType == MatchType.PURE)
{
    accessLevel = "FULL";
    reason = "PURE_MATCH";
}
else if (match.EdgeOwnerId == me)
{
    accessLevel = "FULL";
    reason = "EDGE_OWNER";
}
else
{
    var unlocked = match.BothMessagedAt != null;
    accessLevel = unlocked ? "FULL" : "LIMITED";
    reason = unlocked ? "TWO_WAY_MESSAGE" : "WAIT_TWO_WAY_MESSAGE";
}
```

**Data filtering based on access level:**

```csharp
// Bio: show only when FULL
var bio = "";
if (accessLevel == "FULL")
{
    bio = optional.FirstOrDefault(x => x.Key == "bio")?.Value ?? "";
}

// Optional fields: FULL shows public fields, LIMITED shows none
var optionalPublic = accessLevel == "FULL"
    ? detailOptional
        .Where(x => x.Visibility == VisibilityLevel.Public)
        .Select(x => (object)new { key = x.Key, value = x.Value })
        .ToList()
    : new List<object>();

// Photos: LIMITED => 1 photo, FULL => all
var photosQuery = db.UserPhotos.AsNoTracking()
    .Where(x => x.UserId == otherUserId)
    .OrderBy(x => x.SortOrder)
    .Select(p => new { url = p.Url, caption = p.Caption, sortOrder = p.SortOrder });

var photos = accessLevel == "FULL"
    ? await photosQuery.ToListAsync(ct)
    : await photosQuery.Take(1).ToListAsync(ct);
```

**Result:** Backend sends only the data the user is allowed to see. Frontend doesn't need to filter.

---

## Trial period mechanics

**Start trial:** `POST /matches/{matchId}/pop`

```csharp
var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
if (match.BalloonState != BalloonState.ACTIVE)
    return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

if (match.IsTrial || match.FindLoveAt != null)
    return Results.BadRequest(new { error = "CANNOT_POP_NOW" });

var now = MomentsRules.NowUtc();
var isUserA = match.UserAId == me;

match.IsTrial = true;
match.TrialStartedAt = now;

// Record that the popper has already "opened" the trial
if (isUserA)
    match.TrialUserAOpenedAt = now;
else
    match.TrialUserBOpenedAt = now;

await db.SaveChangesAsync();
```

**Important:** `TrialEndsAt` is NOT set here. It's set when the OTHER user opens the chat thread.

**Set trial timer:** When second user opens chat thread:

```csharp
// In ChatEndpoints.cs GET /chats/{threadId}
if (match.IsTrial && match.TrialEndsAt == null)
{
    var isUserA = match.UserAId == me;
    
    // Record that this user opened the thread
    if (isUserA && match.TrialUserAOpenedAt == null)
        match.TrialUserAOpenedAt = now;
    else if (!isUserA && match.TrialUserBOpenedAt == null)
        match.TrialUserBOpenedAt = now;
    
    // If both have now opened, set the timer
    if (match.TrialUserAOpenedAt != null && match.TrialUserBOpenedAt != null)
    {
        match.TrialEndsAt = now.AddMinutes(3);
        await db.SaveChangesAsync();
    }
}
```

**Trial decision:** `POST /chats/{threadId}/trial-decision`

```csharp
var req = await http.Request.ReadFromJsonAsync<TrialDecisionRequest>();
if (req?.Decision == null || !new[] { "CONTINUE", "END", "BLOCK" }.Contains(req.Decision))
    return Results.BadRequest(new { error = "INVALID_DECISION" });

var isUserA = match.UserAId == me;

// Save decision
if (isUserA)
    match.UserADecision = req.Decision;
else
    match.UserBDecision = req.Decision;

// Save end reason if provided
if (!string.IsNullOrWhiteSpace(req.Reason))
{
    match.TrialEndReason = req.Reason;  // "no_spark" | "wrong_timing" | "not_my_type"
}

// If BLOCK, close match and create Block record
if (req.Decision == "BLOCK")
{
    match.BalloonState = BalloonState.CLOSED;
    match.ClosedReason = ClosedReason.BLOCK;
    match.ClosedAt = now;
    
    var otherId = isUserA ? match.UserBId : match.UserAId;
    db.Blocks.Add(new Block
    {
        BlockerId = me,
        BlockedId = otherId,
        CreatedAt = now
    });
}

await db.SaveChangesAsync();
```

**Signal tracking:**
```csharp
if (req.Decision == "CONTINUE")
{
    await signals.RecordAsync(me, candidateId, "TrialContinued", 1f, null);
}
else if (req.Decision == "END")
{
    var meta = JsonSerializer.Serialize(new { reason = req.Reason ?? "no_spark" });
    await signals.RecordAsync(me, candidateId, "TrialEndedNoSpark", 1f, meta);
}
```

---

## Balloon expiration (background worker)

**Job:** `MatchExpirationWorker` (runs every 15 minutes)

**Logic:**
```csharp
var expired = await db.Matches
    .Where(m => m.BalloonState == BalloonState.ACTIVE && m.ExpiresAt <= now)
    .ToListAsync();

foreach (var match in expired)
{
    match.BalloonState = BalloonState.CLOSED;
    match.ClosedReason = ClosedReason.EXPIRE;
    match.ClosedAt = now;
}

await db.SaveChangesAsync();
```

**Expiration window:** 96 hours after `CreatedAt`.

---

## Find Love unlock

**Trigger:** Both users send at least 1 message each.

**Logic in `ChatEndpoints.cs POST /chats/{threadId}/messages`:**

```csharp
// Count messages per user
var userACount = await db.ChatMessages
    .CountAsync(m => m.ThreadId == threadId && m.SenderId == match.UserAId);

var userBCount = await db.ChatMessages
    .CountAsync(m => m.ThreadId == threadId && m.SenderId == match.UserBId);

// If both have messaged AND BothMessagedAt is not set, set it now
if (userACount > 0 && userBCount > 0 && match.BothMessagedAt == null)
{
    match.BothMessagedAt = now;
    match.FindLoveAt = now.AddHours(12);  // 12h reflection period
    await db.SaveChangesAsync();
}
```

**Unlocks:**
- Date ideas via `GET /chats/{threadId}/date-ideas`
- Full profile access for Edge non-owner (if they didn't already have it)

---

## MatchesEndpoints.cs

**Path:** `backend/WovenBackend/Endpoints/MatchesEndpoints.cs`

**Endpoints:**

### GET /matches
**Returns:** All ACTIVE balloons for current user.

**Response:**
```json
{
  "count": 2,
  "matches": [
    {
      "matchId": "abc123",
      "matchType": "PURE",
      "edgeOwnerId": null,
      "balloonState": "ACTIVE",
      "createdAt": "2026-06-04T10:00:00Z",
      "expiresAt": "2026-06-08T10:00:00Z",
      "bothMessagedAt": "2026-06-04T12:00:00Z",
      "findLoveAt": "2026-06-05T00:00:00Z",
      "showFindLove": true,
      "showBalloonTimer": false,
      "reflectionSecondsLeft": 0,
      "other": {
        "userId": 123,
        "fullName": "Alex",
        "isVerified": true,
        "displayPronouns": "she/her",
        "profilePhoto": "https://..."
      }
    }
  ]
}
```

**Key logic:**
```csharp
var matches = await db.Matches.AsNoTracking()
    .Where(m => m.BalloonState == BalloonState.ACTIVE && (m.UserAId == me || m.UserBId == me))
    .OrderByDescending(m => m.CreatedAt)
    .Select(m => new
    {
        matchId = m.Id,
        matchType = m.MatchType.ToString(),
        edgeOwnerId = m.EdgeOwnerId,
        balloonState = m.BalloonState.ToString(),
        createdAt = m.CreatedAt,
        expiresAt = m.ExpiresAt,
        bothMessagedAt = m.BothMessagedAt,
        findLoveAt = m.FindLoveAt,
        showFindLove = m.FindLoveAt != null && m.FindLoveAt <= now,
        showBalloonTimer = m.BothMessagedAt != null && m.FindLoveAt != null && m.FindLoveAt > now,
        reflectionSecondsLeft = (m.FindLoveAt != null && m.FindLoveAt > now)
            ? (int)Math.Ceiling((m.FindLoveAt.Value - now).TotalSeconds)
            : 0,
        otherUserId = (m.UserAId == me ? m.UserBId : m.UserAId)
    })
    .ToListAsync(ct);
```

---

### GET /matches/{matchId}/profile-access
**Returns:** Access level (FULL/LIMITED) without profile data.

**Response:**
```json
{
  "matchId": "abc123",
  "accessLevel": "FULL",
  "reason": "PURE_MATCH",
  "showBalloonTimer": false,
  "reflectionSecondsLeft": 0,
  "showFindLove": true
}
```

**Use case:** Lightweight check before rendering profile UI.

---

### GET /matches/{matchId}/profile
**Returns:** Full profile data with access control applied.

**Response:**
```json
{
  "matchId": "abc123",
  "accessLevel": "FULL",
  "reason": "TWO_WAY_MESSAGE",
  "showBalloonTimer": false,
  "reflectionSecondsLeft": 0,
  "showFindLove": true,
  "publicPreview": {
    "name": "Alex",
    "age": 28,
    "gender": "Woman",
    "location": "San Francisco, CA",
    "bio": "I believe in finding joy in the small things...",
    "intent": {
      "primaryIntent": "long-term, exclusive",
      "openness": ["marriage", "kids someday"]
    },
    "photos": [
      { "url": "https://...", "caption": "Hiking in Yosemite", "sortOrder": 0 },
      { "url": "https://...", "caption": null, "sortOrder": 1 }
    ],
    "optionalPublic": [
      { "key": "height", "value": "5'6\"" },
      { "key": "zodiac_sign", "value": "Virgo" }
    ]
  }
}
```

**If LIMITED access:**
```json
{
  "accessLevel": "LIMITED",
  "reason": "WAIT_TWO_WAY_MESSAGE",
  "publicPreview": {
    "name": "Alex",
    "age": 28,
    "gender": "Woman",
    "location": "San Francisco, CA",
    "bio": "",  // ← Empty
    "intent": {
      "primaryIntent": "long-term, exclusive",
      "openness": ["marriage", "kids someday"]
    },
    "photos": [
      { "url": "https://...", "caption": null, "sortOrder": 0 }  // ← 1 photo only
    ],
    "optionalPublic": []  // ← Empty
  }
}
```

---

### POST /matches/{matchId}/pop
**Action:** Pops balloon, starts trial period.

**Request:** Empty body, accepts `X-Idempotency-Key` header.

**Response:**
```json
{
  "status": "TRIAL_STARTED",
  "matchId": "abc123",
  "trialStartedAt": "2026-06-04T10:00:00Z",
  "waitingForOtherToOpen": true
}
```

**Idempotency:**
- If same `X-Idempotency-Key` is sent twice, returns cached response
- Prevents double-pop on network retry

---

### POST /matches/{matchId}/unmatch
**Action:** Soft close (ClosedReason = UNMATCH).

**Request:**
```json
{
  "rating": 3  // Optional, 1-5
}
```

**Response:**
```json
{
  "status": "UNMATCHED",
  "matchId": "abc123",
  "closedAt": "2026-06-04T10:00:00Z"
}
```

**Rating storage:**
```csharp
if (req?.Rating != null && req.Rating >= -100 && req.Rating <= 100)
{
    db.UserRatings.Add(new UserRating
    {
        RatedUserId = candidateId,
        RaterUserId = me,
        MatchId = matchId,
        RatingValue = req.Rating.Value,
        CreatedAt = now
    });
}
```

**Outcome tracking:**
```csharp
_ = Task.Run(async () =>
{
    await outcomeService.RecordUnmatchAsync(match.Id, me, candidateId, CancellationToken.None);
});
```

---

### POST /matches/{matchId}/block
**Action:** Hard close (ClosedReason = BLOCK), creates Block record.

**Request:** Empty body.

**Response:**
```json
{
  "status": "BLOCKED",
  "matchId": "abc123",
  "blockedUserId": 456,
  "closedAt": "2026-06-04T10:00:00Z"
}
```

**Block record creation:**
```csharp
var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == otherId);
if (!alreadyBlocked)
{
    db.Blocks.Add(new Block
    {
        BlockerId = me,
        BlockedId = otherId,
        CreatedAt = now
    });
}
```

**Effect:** Blocked user will NEVER appear in future decks for this user.

---

### POST /matches/{matchId}/flag
**Action:** Safety report (does NOT close match).

**Request:**
```json
{
  "reason": "uncomfortable"  // "no_issues" | "uncomfortable" | "inappropriate"
}
```

**Response:**
```json
{
  "status": "FLAGGED"
}
```

**Signal logging:**
```csharp
if (reason != "no_issues")
{
    var metaJson = JsonSerializer.Serialize(new { reason, matchId });
    await signals.RecordAsync(me, candidateId, MatchSignalEventTypes.UserFlagged, 1f, metaJson);
}
```

**Use case:** Post-trial feedback for safety/trust scoring. Does NOT affect match state.

---

## MatchExplanationService

**Path:** `backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs`

**Purpose:** Generates "What caught our eye" explanation for each match.

**See:** [explanations.md](./explanations.md) for full details.

**Key method:**
```csharp
public async Task<int> GenerateAndSaveExplanationAsync(
    int userId,
    int candidateId,
    MatchScore score,
    MatchBucket bucket,
    DateOnly dateUtc,
    CancellationToken ct = default)
{
    // 1. Load PairContext
    var pairContext = await _aiProfileService.GetPairContextAsync(userId, candidateId, ct);
    
    // 2. Load behavioral feedback
    var toneBias = await GetToneBiasAsync(userId, ct);
    var dateStyleHint = await GetDateStyleHintAsync(userId, ct);
    
    // 3. Determine tone from viewer's pulse
    var tone = pairContext?.UserProfile.ConversationTone ?? DetermineTone(userVector.VectorJson);
    
    // 4. Generate via OpenAI
    var (headline, bullets, dateIdeas, bridgeQuestion) = await GenerateExplanationAsync(
        reasons, bucket, tone, pairContext, toneBias, dateStyleHint, ct);
    
    // 5. Save to database
    var explanation = new MatchExplanation
    {
        UserId = userId,
        CandidateId = candidateId,
        DateUtc = dateUtc,
        Headline = headline,
        BulletsJson = JsonSerializer.Serialize(bullets),
        Tone = tone,
        DateIdea = dateIdeas.Count > 0 ? dateIdeas[0] : null,
        DateIdeasJson = JsonSerializer.Serialize(dateIdeas),
        BridgeQuestion = bridgeQuestion,
        CreatedAt = DateTime.UtcNow
    };
    
    db.MatchExplanations.Add(explanation);
    await db.SaveChangesAsync(ct);
    
    return explanation.Id;
}
```

---

## MatchNarratorService

**Path:** `backend/WovenBackend/Services/Matchmaking/MatchNarratorService.cs`

**Purpose:** Generates TTS narration + Ken Burns photos for Moments cards.

**See:** [narration.md](./narration.md) for full details.

**Key method:**
```csharp
public async Task<MatchNarratorResult> BuildNarratorFieldsAsync(
    int candidateId, DateOnly dateUtc, CancellationToken ct = default)
{
    // 1. Load up to 3 photos
    var photos = await db.UserPhotos
        .Where(p => p.UserId == candidateId)
        .OrderBy(p => p.SortOrder)
        .Take(3)
        .Select(p => p.Url)
        .ToListAsync(ct);
    
    // 2. Pick curated quote (shortest ≥20 chars)
    var curatedQuote = PickCuratedQuote(bio, answerTexts);
    
    // 3. Generate TTS (if quote exists)
    string? narrationUrl = null;
    if (curatedQuote != null)
    {
        narrationUrl = await GetOrGenerateTtsAsync(candidateId, dateUtc, curatedQuote, apiKey, ct);
    }
    
    return new MatchNarratorResult
    {
        KenBurnsPhotoUrls = photos.Count >= 1 ? photos.ToArray() : null,
        CuratedQuote = curatedQuote,
        NarrationUrl = narrationUrl,
        NarrationExposed = false  // Baseline period
    };
}
```

---

## Security notes

**GetUserId helper:**
```csharp
private static int GetUserId(ClaimsPrincipal user)
{
    var uid = user.FindFirstValue("uid");
    if (int.TryParse(uid, out var id)) return id;
    
    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (int.TryParse(sub, out id)) return id;
    
    throw new UnauthorizedAccessException("Missing user id claim");
}
```

**Why throw?** Forces 401 response via `AuthExceptionHandler`. Never returns 0 (which would silently break access control).

**Authorization:**
- All endpoints require `RequireAuthorization()`
- JWT token in `Authorization: Bearer <token>` header
- User ID extracted from `"uid"` or `"sub"` claim

**Access control:**
- Backend ALWAYS checks if user is participant in match
- Frontend cannot bypass access rules (data is filtered server-side)
- No client-side access checks needed

---

## Performance optimization

**Query optimization:**
```csharp
// AsNoTracking for read-only queries
var matches = await db.Matches.AsNoTracking()
    .Where(...)
    .ToListAsync();

// Select only needed fields
.Select(m => new { matchId = m.Id, ... })
```

**Parallel TTS generation:**
```csharp
var narratorTasks = selectedCandidates.Select(c =>
    _matchNarrator.BuildNarratorFieldsAsync(c.CandidateId, dateUtc, ct));

var narratorResults = await Task.WhenAll(narratorTasks);
```

**Caching:**
- TTS audio cached in Azure Blob (48h TTL)
- Explanations cached in database (per user-candidate-date)
- Match access checks use in-memory `BothMessagedAt` field (no joins)

---

## Related files

**Entities:**
- `backend/WovenBackend/data/Entities/Moments/Match.cs`
- `backend/WovenBackend/data/Entities/MatchExplanation.cs`
- `backend/WovenBackend/data/Entities/MatchSignalLog.cs`
- `backend/WovenBackend/data/Entities/MatchOutcome.cs`

**Services:**
- `backend/WovenBackend/Services/Matchmaking/MatchService.cs`
- `backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs`
- `backend/WovenBackend/Services/Matchmaking/MatchNarratorService.cs`
- `backend/WovenBackend/Services/Matchmaking/MatchSignalService.cs`
- `backend/WovenBackend/Services/Matchmaking/MatchOutcomeService.cs`

**Endpoints:**
- `backend/WovenBackend/Endpoints/MatchesEndpoints.cs`

**Migrations:**
- `20260603000002_AddBridgeQuestionToMatchExplanation.cs`

---

## Testing checklist

**Match creation:**
- [ ] PURE match created when both users choose same action
- [ ] EDGE match created when users choose different actions
- [ ] EdgeOwnerId set correctly (whoever chose MAGICAL)
- [ ] Chat thread created with match
- [ ] Bridge question pre-loaded as system message

**Access control:**
- [ ] PURE match grants FULL access immediately
- [ ] Edge owner gets FULL access immediately
- [ ] Edge non-owner gets LIMITED until BothMessagedAt
- [ ] LIMITED shows 1 photo, no bio, no optional fields
- [ ] FULL shows all photos, bio, all public fields

**Trial period:**
- [ ] Pop starts trial, sets TrialStartedAt
- [ ] Popper's "opened" timestamp set immediately
- [ ] TrialEndsAt set when other user opens thread
- [ ] Trial decision saves correctly (CONTINUE/END/BLOCK)
- [ ] BLOCK closes match and creates Block record
- [ ] Signal logged for trial outcome

**Balloon lifecycle:**
- [ ] Match expires after 96h (ClosedReason = EXPIRE)
- [ ] Unmatch closes match (ClosedReason = UNMATCH)
- [ ] Block closes match (ClosedReason = BLOCK)
- [ ] Closed matches cannot be reopened

**Find Love:**
- [ ] FindLoveAt set when both users message
- [ ] 12h reflection period enforced
- [ ] Date ideas unlocked after FindLoveAt
- [ ] Edge non-owner gets FULL access after FindLoveAt

**Idempotency:**
- [ ] Duplicate pop requests return cached response
- [ ] X-Idempotency-Key stored for 24h
- [ ] Cached response includes same matchId/timestamps
