# Match Creation — When & How Matches Are Created

**Service:** `MomentsMatchService`  
**Table:** `matches`  
**Endpoints:** `POST /moments/respond`, `POST /moments/choose`

## Overview

A match is created when both users choose each other with a positive choice (Magical ◈, Resonant ◇, or legacy YES). The match creates a 36-hour "balloon" window for users to start a conversation.

## Match types

### PURE match

**Definition:** Both users chose the same type.

**Examples:**
- Both chose Magical ◈
- Both chose Resonant ◇
- Both chose legacy YES (treated as Magical)
- Both chose legacy NO (treated as Resonant)

**Properties:**
- `match_type = MatchType.PURE`
- `edge_owner_id = NULL`
- No asymmetry — equal footing

**UI copy:** "You both felt it. ◈"

### EDGE match

**Definition:** Users chose different types.

**Examples:**
- User A chose Magical ◈, User B chose Resonant ◇
- User A chose Resonant ◇, User B chose Magical ◈

**Properties:**
- `match_type = MatchType.EDGE`
- `edge_owner_id` = randomly assigned to A or B
- Asymmetric — one user "owns" the edge (used for notification routing)

**UI copy:** "Different angles, same pull. ◇◈"

**Code** ([MomentsEndpoints.cs:22-29](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L22-L29)):
```csharp
private static bool IsPure(MomentChoice a, MomentChoice b)
{
    var aMag = a is MomentChoice.MAGICAL or MomentChoice.YES;
    var bMag = b is MomentChoice.MAGICAL or MomentChoice.YES;
    var aLog = a is MomentChoice.LOGICAL or MomentChoice.NO;
    var bLog = b is MomentChoice.LOGICAL or MomentChoice.NO;
    return (aMag && bMag) || (aLog && bLog);
}
```

## Creation flow (with ChatNote)

**Endpoint:** `POST /moments/choose` ([MomentsEndpoints.cs:664-871](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Endpoints\MomentsEndpoints.cs#L664-L871))

### Step 1: Record MomentResponse + ChatNote

```csharp
// Upsert MomentResponse (may be upgrading from PASS)
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

// Record ChatNote (new entry, never upsert)
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

### Step 2: Check for counterpart response

**Deck tab (`source = "TODAY"`):**
```csharp
otherResponse = await db.MomentResponses.AsNoTracking()
    .FirstOrDefaultAsync(r =>
        r.DateUtc == today &&
        r.FromUserId == req.TargetUserId &&
        r.ToUserId == me &&
        IsPositiveExpression(r.Choice), ct);
```

**Drawn tab (`source = "LIKED_YOU"`):**
```csharp
otherResponse = await db.MomentResponses.AsNoTracking()
    .Where(r => r.FromUserId == req.TargetUserId && r.ToUserId == me && IsPositiveExpression(r.Choice))
    .OrderByDescending(r => r.CreatedAt)
    .FirstOrDefaultAsync(ct);
```

**Difference:** Drawn tab doesn't filter by `DateUtc` (allows matching across days within 7-day window).

### Step 3: Check for counterpart ChatNote

```csharp
if (otherResponse is null)
    return Results.Ok(new { status = "RECORDED_WAITING" });

var otherNote = await db.ChatNotes.AsNoTracking()
    .FirstOrDefaultAsync(n => n.FromUserId == req.TargetUserId && n.ToUserId == me, ct);

if (otherNote is null)
    return Results.Ok(new { status = "WAITING_FOR_OTHER_NOTE" });
```

**Both must exist** — response alone is not enough (legacy `/respond` endpoint), note alone is not enough (can't exist without response).

### Step 4: Create match

```csharp
var (a, b) = MomentsRules.NormalizePair(me, req.TargetUserId);
var matchType = IsPure(choiceEnum.Value, otherResponse.Choice)
    ? MatchType.PURE
    : MatchType.EDGE;

var edgeOwner = matchType == MatchType.EDGE
    ? (int?)(Random.Shared.Next(0, 2) == 0 ? a : b)
    : null;

var created = await matchService.CreateActiveMatchAsync(a, b, matchType, edgeOwner, ct);

if (created.Created && created.Match != null)
{
    // Link both ChatNotes to the match
    note.MatchId = created.Match.Id;
    otherNote = await db.ChatNotes
        .FirstOrDefaultAsync(n => n.FromUserId == req.TargetUserId && n.ToUserId == me, ct);
    if (otherNote != null) otherNote.MatchId = created.Match.Id;
    await db.SaveChangesAsync(ct);
}
```

### Step 5: Return match result

```csharp
var statusStr = created.Created
    ? (matchType == MatchType.PURE ? "PURE_MATCH_CREATED" : "EDGE_MATCH_CREATED")
    : "MATCH_NOT_CREATED";

return Results.Ok(new
{
    status = statusStr,
    reason = created.Reason,
    matchId = created.Match?.Id,
    matchType = matchType.ToString(),
    edgeOwnerId = created.Match?.EdgeOwnerId
});
```

## MomentsMatchService implementation

**Service:** ([MomentsMatchService.cs:28-108](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\Services\Moments\MomentsMatchService.cs#L28-L108))

### Input validation

```csharp
if (user1Id == user2Id)
    return new CreateMatchResult(false, null, "CANNOT_MATCH_SELF");

// Validate EDGE/PURE input consistency
if (matchType == MatchType.PURE && edgeOwnerId != null)
    return new CreateMatchResult(false, null, "PURE_CANNOT_HAVE_EDGE_OWNER");

if (matchType == MatchType.EDGE && edgeOwnerId == null)
    return new CreateMatchResult(false, null, "EDGE_REQUIRES_EDGE_OWNER");

if (edgeOwnerId != null && edgeOwnerId != a && edgeOwnerId != b)
    return new CreateMatchResult(false, null, "EDGE_OWNER_NOT_IN_PAIR");
```

### Pair normalization

**Always store pairs in (A, B) order where A < B** — prevents `(123, 456)` and `(456, 123)` from both existing.

```csharp
var (a, b) = MomentsRules.NormalizePair(user1Id, user2Id);

public static (int A, int B) NormalizePair(int user1, int user2)
    => user1 < user2 ? (user1, user2) : (user2, user1);
```

### Duplicate check (SERIALIZABLE transaction)

```csharp
await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

var exists = await _db.Matches.AnyAsync(m =>
    m.UserAId == a &&
    m.UserBId == b &&
    m.BalloonState == BalloonState.ACTIVE, ct);

if (exists)
{
    await tx.RollbackAsync(ct);
    return new CreateMatchResult(false, null, "ACTIVE_MATCH_ALREADY_EXISTS");
}
```

**SERIALIZABLE prevents race conditions:** Two concurrent `POST /moments/choose` calls from A→B and B→A can't both pass the `exists` check and create duplicate matches.

### Create match entity

```csharp
var now = MomentsRules.NowUtc();
var expires = MomentsRules.ComputeExpiresAt(now);  // now + 36 hours

var match = new Match
{
    UserAId = a,
    UserBId = b,
    MatchType = matchType,
    EdgeOwnerId = edgeOwnerId,
    BalloonState = BalloonState.ACTIVE,
    ClosedReason = null,
    CreatedAt = now,
    ExpiresAt = expires,
    ClosedAt = null
};

_db.Matches.Add(match);
await _db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

### Send notifications

**EDGE match (one-sided notification):**
```csharp
if (matchType == MatchType.EDGE && edgeOwnerId.HasValue)
{
    var recipientId = edgeOwnerId.Value == a ? b : a;
    await _notifications.MomentReceivedAsync(recipientId, match.Id, edgeOwnerId.Value, ct);
}
```

**PURE match (both-sided notification):**
```csharp
else if (matchType == MatchType.PURE)
{
    await Task.WhenAll(
        _notifications.MomentReceivedAsync(a, match.Id, b, ct),
        _notifications.MomentReceivedAsync(b, match.Id, a, ct)
    );
}
```

**Notification implementation:** See `docs/features/notifications/` for details. Uses SignalR + Web Push.

## Match entity structure

**Table:** `matches` ([Match.cs:16-98](file://C:\Users\gauta\Desktop\Woven\backend\WovenBackend\data\Entities\Moments\Match.cs#L16-L98))

### Core fields

```csharp
public Guid Id { get; set; } = Guid.NewGuid();
public int UserAId { get; set; }
public int UserBId { get; set; }
public MatchType MatchType { get; set; }        // PURE or EDGE
public int? EdgeOwnerId { get; set; }           // NULL for PURE, A or B for EDGE
public BalloonState BalloonState { get; set; }  // ACTIVE or CLOSED
public ClosedReason? ClosedReason { get; set; } // POP, EXPIRE, UNMATCH, BLOCK
public DateTimeOffset CreatedAt { get; set; }
public DateTimeOffset ExpiresAt { get; set; }   // CreatedAt + 36 hours
public DateTimeOffset? ClosedAt { get; set; }
```

### Trial fields (used after balloon pop)

```csharp
public bool IsTrial { get; set; } = false;
public DateTimeOffset? TrialStartedAt { get; set; }
public DateTimeOffset? TrialEndsAt { get; set; }
public string? UserADecision { get; set; }  // "CONTINUE", "END", "BLOCK"
public string? UserBDecision { get; set; }
public DateTimeOffset? TrialUserAOpenedAt { get; set; }
public DateTimeOffset? TrialUserBOpenedAt { get; set; }
public string? TrialEndReason { get; set; }  // "no_spark", "wrong_timing", "not_my_type"
```

### Messaging signals

```csharp
public DateTimeOffset? BothMessagedAt { get; set; }  // When both users sent >= 1 message
public DateTimeOffset? FindLoveAt { get; set; }      // Final unlock stage (future feature)
```

## Balloon lifetime

**Constant:** `MomentsRules.BalloonLifetime = TimeSpan.FromHours(36)`

**Calculation:**
```csharp
public static DateTimeOffset ComputeExpiresAt(DateTimeOffset createdAtUtc)
    => createdAtUtc.Add(BalloonLifetime);
```

**Expiry worker:** `BalloonExpiryWorker` runs every 5 minutes, closes balloons where `ExpiresAt <= DateTimeOffset.UtcNow` and `BalloonState == ACTIVE`.

**On expiry:**
- `BalloonState = CLOSED`
- `ClosedReason = EXPIRE`
- `ClosedAt = DateTimeOffset.UtcNow`
- If `BothMessagedAt == null` → ghost refund (0.5 sparks to both users)

## Frontend navigation after match

**Frontend** ([moments.page.ts:373-379](file://C:\Users\gauta\Desktop\Woven\frontend\woven-frontend\src\app\pages\moments\moments.page.ts#L373-L379)):
```typescript
if (res?.matchId && (res.status === 'PURE_MATCH_CREATED' || res.status === 'EDGE_MATCH_CREATED')) {
  const copy = this.matchRevealCopy(res.matchType ?? '', res.status);
  this.showToast(copy);
  const started = await firstValueFrom(this.chat.start(res.matchId));
  this.router.navigateByUrl(`/chats/${started.threadId}`);
  return;
}
```

**Flow:**
1. Match created → show toast ("You both felt it. ◈" or "Different angles, same pull. ◇◈")
2. Call `POST /chats/start` to create chat thread (if not exists)
3. Navigate to `/chats/{threadId}`
4. Balloon countdown starts (36h from match creation)

## Edge cases

### Both users submit simultaneously

**Prevented by SERIALIZABLE transaction** — one wins, other gets `ACTIVE_MATCH_ALREADY_EXISTS` error.

**Frontend retry:** If backend returns 500 or network error, frontend shows "Something went wrong. Try again." toast and re-adds card to UI (optimistic rollback).

### User A passes, then upgrades to Magical from Drawn tab

**Allowed** — `POST /moments/choose` upserts `MomentResponse` if existing `choice = PASS`.

**Flow:**
1. Day 1: A passes B (Deck tab)
2. Day 2: B chooses A Magical (now in A's Drawn tab)
3. Day 3: A chooses B Magical from Drawn tab → upgrades pass → match created

### User blocks other user before match created

**Blocked check** runs before match creation:
```csharp
var blocked = await db.Blocks.AnyAsync(b =>
    (b.BlockerId == me && b.BlockedId == req.TargetUserId) ||
    (b.BlockerId == req.TargetUserId && b.BlockedId == me), ct);
if (blocked) return Results.BadRequest(new { error = "BLOCKED" });
```

No match can be created if either user blocked the other.

## Related files

**Backend:**
- `backend/WovenBackend/Services/Moments/MomentsMatchService.cs` — Match creation service
- `backend/WovenBackend/Endpoints/MomentsEndpoints.cs:664-871` — `POST /moments/choose`
- `backend/WovenBackend/data/Entities/Moments/Match.cs` — Match entity
- `backend/WovenBackend/Services/Moments/MomentsRules.cs` — Constants + pair normalization
- `backend/WovenBackend/Services/Moments/BalloonExpiryWorker.cs` — Expiry worker

**Frontend:**
- `frontend/woven-frontend/src/app/pages/moments/moments.page.ts:347-393` — Submit handler
- `frontend/woven-frontend/src/app/services/chat.service.ts` — `start()` method (creates thread)
