# Close Paths (All Ways a Match Can End)

**Last Updated:** 2026-08-17

## Overview

A match can close through 6 distinct paths. Once `BalloonState = CLOSED`, the match is permanently ended — no reopening.

All paths set:
- `Match.BalloonState = CLOSED`
- `Match.ClosedReason` (enum)
- `Match.ClosedAt` (timestamp)

---

## Path 1: Trial Auto-Close (Timeout + No Messages)

**Trigger:** Trial timer expires, no messages exchanged, user opens thread.

**Where:** `GET /chats/{threadId}` (on thread load)

**Code reference:** `ChatEndpoints.cs:240-268`

```csharp
if (match.IsTrial && match.TrialEndsAt != null && match.TrialEndsAt <= now)
{
    var bothDecided = !string.IsNullOrEmpty(match.UserADecision) && !string.IsNullOrEmpty(match.UserBDecision);
    if (!bothDecided)
    {
        var hasTrialMessages = await db.ChatMessages.AsNoTracking()
            .AnyAsync(m => m.ThreadId == threadId && m.CreatedAt >= match.TrialStartedAt, ct);

        if (!hasTrialMessages)
        {
            match.BalloonState = BalloonState.CLOSED;
            match.ClosedReason = ClosedReason.UNMATCH;
            match.ClosedAt = now;
            match.IsTrial = false;
            await db.SaveChangesAsync(ct);

            // Ghost refund
            _ = Task.Run(async () =>
            {
                try { await sparks.GhostRefundAsync(match.UserAId); } catch { }
                try { await sparks.GhostRefundAsync(match.UserBId); } catch { }
            });
        }
    }
}
```

**Conditions (all must be true):**
1. `Match.IsTrial == true`
2. `Match.TrialEndsAt <= now`
3. Not both users decided yet
4. Zero messages exchanged during trial

**Result:**
- `BalloonState = CLOSED`
- `ClosedReason = UNMATCH`
- Ghost refund: 0.5 sparks to both users

**User experience:**  
Opens thread → sees "Match ended" → redirected to chat list.

---

## Path 2: Trial Decision (One or Both Choose END)

**Trigger:** Trial decision submitted, at least one user chose END.

**Endpoint:** `POST /chats/{threadId}/trial-decision`

**Code reference:** `ChatEndpoints.cs:732-758`

```csharp
else  // One or both chose END
{
    var noMessages = match.BothMessagedAt == null;

    match.BalloonState = BalloonState.CLOSED;
    match.ClosedReason = ClosedReason.UNMATCH;
    match.ClosedAt = now;
    match.IsTrial = false;
    await db.SaveChangesAsync(ct);

    if (noMessages)
    {
        var uA = match.UserAId;
        var uB = match.UserBId;
        _ = Task.Run(async () =>
        {
            try { await sparks.GhostRefundAsync(uA); } catch { }
            try { await sparks.GhostRefundAsync(uB); } catch { }
        });
    }

    return Results.Ok(new { status = "MATCH_ENDED", closedAt: match.ClosedAt });
}
```

**Scenarios:**
1. Both chose END
2. User A chose CONTINUE, User B chose END
3. User A chose END, User B chose CONTINUE

**Result:**
- `BalloonState = CLOSED`
- `ClosedReason = UNMATCH`
- `TrialEndReason` set (e.g., `"no_spark"`)
- Ghost refund if no messages exchanged

**Response:**
```json
{
  "status": "MATCH_ENDED",
  "closedAt": "2026-08-17T12:34:56Z"
}
```

**Frontend:** Toast "Match ended." → redirect to `/chats`.

---

## Path 3: Trial Decision (BLOCK)

**Trigger:** One user chooses BLOCK in trial decision.

**Endpoint:** `POST /chats/{threadId}/trial-decision`

**Code reference:** `ChatEndpoints.cs:696-714`

```csharp
if (decision == "BLOCK")
{
    var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == otherUserId, ct);
    if (!alreadyBlocked)
        db.Blocks.Add(new Block { BlockerId = me, BlockedId = otherUserId, CreatedAt = now });

    match.BalloonState = BalloonState.CLOSED;
    match.ClosedReason = ClosedReason.BLOCK;
    match.ClosedAt = now;
    match.IsTrial = false;
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { status = "MATCH_BLOCKED", closedAt: match.ClosedAt });
}
```

**Result:**
- `BalloonState = CLOSED`
- `ClosedReason = BLOCK`
- `Block` record created
- Does NOT wait for other user's decision (immediate close)
- No ghost refund (blocking is intentional, not ghosting)

**Response:**
```json
{
  "status": "MATCH_BLOCKED",
  "closedAt": "2026-08-17T12:34:56Z"
}
```

**Frontend:** Toast "Blocked." → redirect to `/chats`.

**Blocked user perspective:**  
Match disappears from list. No notification. Will not see blocker in future decks.

---

## Path 4: Manual Unmatch

**Trigger:** User clicks "Unmatch" in chat thread menu.

**Endpoint:** `POST /matches/{matchId}/unmatch`

**Code reference:** `MatchesEndpoints.cs:166-183` (inferred from typical implementation)

```csharp
// Typical unmatch implementation
var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
if (match == null) return Results.NotFound();
if (!IsParticipant(match, me)) return Results.Forbid();

if (match.BalloonState != BalloonState.ACTIVE)
    return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

var noMessages = match.BothMessagedAt == null;

match.BalloonState = BalloonState.CLOSED;
match.ClosedReason = ClosedReason.UNMATCH;
match.ClosedAt = DateTimeOffset.UtcNow;
await db.SaveChangesAsync(ct);

if (noMessages)
{
    _ = sparks.GhostRefundAsync(match.UserAId);
    _ = sparks.GhostRefundAsync(match.UserBId);
}

return Results.Ok(new { status = "UNMATCHED" });
```

**Result:**
- `BalloonState = CLOSED`
- `ClosedReason = UNMATCH`
- Ghost refund if no messages exchanged

**Frontend flow:**
1. User clicks "Unmatch" → unmatch rating modal shown
2. User selects rating (optional) → `POST /matches/{id}/unmatch`
3. Toast "Unmatched." → redirect to `/chats`

**Code reference:** `chat-thread.component.ts:623-657`

---

## Path 5: Manual Block

**Trigger:** User clicks "Block" in chat thread menu.

**Endpoint:** `POST /matches/{matchId}/block`

**Code reference:** `MatchesEndpoints.cs:185-204` (inferred)

```csharp
var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
if (match == null) return Results.NotFound();
if (!IsParticipant(match, me)) return Results.Forbid();

if (match.BalloonState != BalloonState.ACTIVE)
    return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

var otherUserId = match.UserAId == me ? match.UserBId : match.UserAId;

var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == otherUserId, ct);
if (!alreadyBlocked)
    db.Blocks.Add(new Block { BlockerId = me, BlockedId = otherUserId, CreatedAt = DateTimeOffset.UtcNow });

match.BalloonState = BalloonState.CLOSED;
match.ClosedReason = ClosedReason.BLOCK;
match.ClosedAt = DateTimeOffset.UtcNow;
await db.SaveChangesAsync(ct);

return Results.Ok(new { status = "BLOCKED" });
```

**Result:**
- `BalloonState = CLOSED`
- `ClosedReason = BLOCK`
- `Block` record created
- No ghost refund

**Frontend flow:**
1. User clicks "Block" → confirmation (no modal, instant action)
2. Toast "Blocked." → redirect to `/chats`

**Code reference:** `chat-thread.component.ts:663-688`

---

## Path 6: Graceful Close (Mutual Walk-Away)

**Trigger:** User selects "Close gracefully" option.

**Endpoint:** `POST /chats/{threadId}/close-gracefully`

**Code reference:** `ChatEndpoints.cs:552-597`

```csharp
var me = GetUserId(http.User);
var now = MomentsRules.NowUtc();

var thread = await db.ChatThreads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
if (thread == null) return Results.NotFound(new { error = "THREAD_NOT_FOUND" });

var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);
if (match == null) return Results.NotFound(new { error = "MATCH_NOT_FOUND" });

var isParticipant = match.UserAId == me || match.UserBId == me;
if (!isParticipant) return Results.Forbid();

if (match.BalloonState != BalloonState.ACTIVE)
    return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });

var noMessages = match.BothMessagedAt == null;

match.BalloonState = BalloonState.CLOSED;
match.ClosedReason = ClosedReason.UNMATCH;
match.ClosedAt = now;
await db.SaveChangesAsync(ct);

await notify.MomentExpiredAsync(match.UserAId, match.UserBId, match.Id, ct);

// Ghost refund: if nobody messaged, give both users 0.5 sparks back
if (noMessages)
{
    var uA = match.UserAId;
    var uB = match.UserBId;
    _ = Task.Run(async () =>
    {
        try { await sparks.GhostRefundAsync(uA); } catch { }
        try { await sparks.GhostRefundAsync(uB); } catch { }
    });
}

return Results.Ok(new { status = "CLOSED", closedAt: now });
```

**Result:**
- `BalloonState = CLOSED`
- `ClosedReason = UNMATCH`
- Ghost refund if no messages exchanged
- Notification sent to both users

**Difference from manual unmatch:**  
Presented as a mutual, no-fault walk-away (UI language may differ). Backend behavior identical.

---

## Path 7 (Deprecated): Balloon Pop → Close

**Historical:** Before trial feature, popping balloon closed the match immediately.

**Current behavior:** Balloon pop starts trial (`POST /matches/{id}/pop` → `IsTrial = true`).

**Close reason:** `ClosedReason.POP` (deprecated, no longer used)

**Legacy data:** Matches closed via POP before 2026-06-03 still have this reason.

---

## Path 8: Expiration (Timer-Based)

**Trigger:** Match balloon expires after 24 hours of no activity.

**Field:** `Match.ExpiresAt`

**Worker:** Background job checks `ExpiresAt <= now`, closes matches.

**Code location:** Not visible in current `ChatEndpoints.cs` or `MatchesEndpoints.cs` — likely in a scheduled background worker.

**Result:**
- `BalloonState = CLOSED`
- `ClosedReason = EXPIRE`

**Note:** This path may not be fully wired in production (no worker code found in codebase review).

---

## Ghost Refund Summary

**When triggered:**
- Trial auto-close (no messages)
- Trial decision END (no messages)
- Manual unmatch (no messages)
- Graceful close (no messages)

**NOT triggered:**
- BLOCK (any path)
- Trial decision END (if messages exchanged)
- Manual unmatch (if messages exchanged)

**Check logic:**
```csharp
var noMessages = match.BothMessagedAt == null;
```

**Service:**
```csharp
await sparks.GhostRefundAsync(userId);
```

**Amount:** 0.5 sparks per user  
**Rationale:** User spent 1 spark on Drawn action. If no conversation happened, both get half back.

---

## Frontend Confirmation Flows

### Unmatch

**Component:** `unmatch-rating.component.ts`

**Flow:**
1. User clicks "Unmatch" → `showUnmatchRating = true`
2. Modal shows rating options (1-5 stars, optional)
3. User confirms → `POST /matches/{id}/unmatch` with rating
4. Modal closes → toast → redirect

**Code reference:** `chat-thread.component.ts:623-657`

---

### Block

**No modal.** Instant action.

**Flow:**
1. User clicks "Block" → `isBlocking = true`
2. `POST /matches/{id}/block`
3. Toast "Blocked." → redirect

**Code reference:** `chat-thread.component.ts:663-688`

---

## Database State After Close

**Table:** `matches`

```sql
-- Example closed match
balloon_state = 2  -- CLOSED
closed_reason = 3  -- UNMATCH
closed_at = '2026-08-17 12:34:56+00'
is_trial = false
```

**Immutable:** No transitions from `CLOSED` back to `ACTIVE`.

**Chat thread visibility:**  
- `GET /chats` excludes closed matches (filters by `BalloonState = ACTIVE`)
- `GET /chats/{threadId}` still works (for message history, read-only)

---

## Analytics Events

**None specific to close paths.**

All closing tracked via ECHO signals:
- `TrialRejected` (trial END decision)
- `TrialEndedNoSpark` / `WrongTiming` / `NotMyType` (trial end reasons)

**UserFlagged signal:**  
If user blocks, optionally tracked as `MatchSignalEventTypes.UserFlagged` (safety signal, not compatibility).

---

## Related Documentation

- [Balloon State Machine](./balloon-state.md)
- [Trial Period Mechanics](./trial-period.md)
- [Trial Decision Flow](./trial-decision.md)
- [Backend Implementation](./backend.md)
