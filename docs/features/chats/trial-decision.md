# Trial Decision Flow

**Last Updated:** 2026-08-17

## Overview

When a trial period ends, both users must decide: **CONTINUE**, **END**, or **BLOCK**.

**Endpoint:** `POST /chats/{threadId}/trial-decision`  
**Component:** `trial-decision.component.ts`  
**Code reference:** `ChatEndpoints.cs:602-768`

---

## Decision Options

### CONTINUE

**Meaning:** "I want to keep talking."

**What happens:**
- Decision stored as `"CONTINUE"` in `Match.UserADecision` or `Match.UserBDecision`
- If **both users choose CONTINUE:**
  - `Match.IsTrial = false`
  - `Match.FindLoveAt = now` (immediate unlock, no 5-minute wait)
  - Response: `{ status: "MATCH_CONTINUES", findLoveAt: "..." }`
- If **waiting for other user:**
  - Response: `{ status: "DECISION_RECORDED", waitingForOther: true }`

**ECHO signal:** `TrialAccepted` (EventValue = 1.0)

**Code reference:** `ChatEndpoints.cs:719-730`

```csharp
if (match.UserADecision == "CONTINUE" && match.UserBDecision == "CONTINUE")
{
    match.IsTrial = false;
    match.FindLoveAt = now;
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { status = "MATCH_CONTINUES", findLoveAt = match.FindLoveAt });
}
```

---

### END

**Meaning:** "Not interested in continuing."

**Required:** Must provide `endReason` (one of):
- `no_spark` — chemistry isn't there
- `wrong_timing` — bad timing in life
- `not_my_type` — preference mismatch

**What happens:**
- Decision stored as `"END"` in `Match.UserADecision` or `Match.UserBDecision`
- `Match.TrialEndReason = endReason` (last decision overwrites)
- If **one or both choose END:**
  - `Match.BalloonState = CLOSED`
  - `Match.ClosedReason = UNMATCH`
  - `Match.IsTrial = false`
  - Ghost refund if no messages exchanged
  - Response: `{ status: "MATCH_ENDED", closedAt: "..." }`

**ECHO signals:**
1. `TrialRejected` (EventValue = 0.0)
2. `TrialMessageCount` (EventValue = float message count)
3. Reason-specific signal (EventValue = 1.0):
   - `TrialEndedNoSpark`
   - `TrialEndedWrongTiming`
   - `TrialEndedNotMyType`

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

---

### BLOCK

**Meaning:** "Safety concern. I don't want to see this person again."

**What happens:**
1. Decision **stored as `"END"`** in database (not `"BLOCK"` — treated as END for mutual outcome logic)
2. `Block` record created immediately (`BlockerId = me`, `BlockedId = otherUser`)
3. `Match.BalloonState = CLOSED`
4. `Match.ClosedReason = BLOCK`
5. `Match.IsTrial = false`
6. Does **not wait** for other user's decision
7. Response: `{ status: "MATCH_BLOCKED", closedAt: "..." }`

**ECHO signal:** `TrialRejected` (same as END)

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

**Why stored as END in decision fields?**  
So both-decided logic doesn't need special BLOCK case. `UserADecision = "END"` + `UserBDecision = "END"` → match ends, regardless of whether one was a BLOCK.

**Blocked user perspective:**  
Sees match disappear from chat list. Never knows they were blocked (no notification). Will not see blocker in future decks.

---

## Request Format

**Endpoint:** `POST /chats/{threadId}/trial-decision`

**Headers:**
- `Authorization: Bearer <token>`
- `X-Idempotency-Key: <random-uuid>` (recommended)

**Body:**

```json
{
  "decision": "CONTINUE",  // or "END" or "BLOCK"
  "endReason": "no_spark"  // required if decision == "END"
}
```

**Validation:**
- `decision` must be one of: `"CONTINUE"`, `"END"`, `"BLOCK"` (case-insensitive, normalized to uppercase)
- `endReason` optional for CONTINUE, required for END, ignored for BLOCK
- `endReason` must be one of: `"no_spark"`, `"wrong_timing"`, `"not_my_type"`

**Code reference:** `ChatEndpoints.cs:647-648`

```csharp
var decision = (req.Decision ?? "").Trim().ToUpperInvariant();
if (decision != "CONTINUE" && decision != "END" && decision != "BLOCK")
    return Results.BadRequest(new { error = "INVALID_DECISION" });
```

---

## Response Scenarios

### 1. First User Decides

**Request:**
```http
POST /chats/{threadId}/trial-decision
{ "decision": "CONTINUE" }
```

**Response:**
```json
{
  "status": "DECISION_RECORDED",
  "waitingForOther": true
}
```

**Match state:**
- `UserADecision = "CONTINUE"` (or `UserBDecision`)
- `IsTrial = true` (still active)
- `BalloonState = ACTIVE`

---

### 2. Both Decide CONTINUE

**Second user request:**
```http
POST /chats/{threadId}/trial-decision
{ "decision": "CONTINUE" }
```

**Response:**
```json
{
  "status": "MATCH_CONTINUES",
  "findLoveAt": "2026-08-17T12:34:56Z"
}
```

**Match state:**
- `UserADecision = "CONTINUE"`
- `UserBDecision = "CONTINUE"`
- `IsTrial = false`
- `FindLoveAt = now` (immediate unlock)
- `BalloonState = ACTIVE`

**Frontend:** Refreshes thread, shows "Find Love" stage immediately.

---

### 3. One or Both Choose END

**Request:**
```http
POST /chats/{threadId}/trial-decision
{ "decision": "END", "endReason": "no_spark" }
```

**Response (if both decided):**
```json
{
  "status": "MATCH_ENDED",
  "closedAt": "2026-08-17T12:34:56Z"
}
```

**Match state:**
- `BalloonState = CLOSED`
- `ClosedReason = UNMATCH`
- `TrialEndReason = "no_spark"` (last decider's reason)
- `ClosedAt = now`

**Frontend:** Shows toast "Match ended." → redirects to `/chats` (list)

---

### 4. One Chooses BLOCK

**Request:**
```http
POST /chats/{threadId}/trial-decision
{ "decision": "BLOCK" }
```

**Response (immediate):**
```json
{
  "status": "MATCH_BLOCKED",
  "closedAt": "2026-08-17T12:34:56Z"
}
```

**Match state:**
- `BalloonState = CLOSED`
- `ClosedReason = BLOCK`
- `ClosedAt = now`
- `Block` record created

**Frontend:** Shows toast "Blocked." → redirects to `/chats` (list)

**Other user experience:**  
If they reload, match disappears from list. No error, no notification.

---

## Idempotency

**Problem:** User double-taps "Continue" button → two POST requests → potential duplicate processing.

**Solution:** Idempotency keys.

**Frontend:**
```typescript
// chat-thread.component.ts:444-451
const result = await firstValueFrom(
  this.chatApi.trialDecision(this.data.threadId, event.decision, event.endReason)
);
```

**Service layer:** Should generate idempotency key (currently missing in frontend — improvement opportunity).

**Backend check:** `ChatEndpoints.cs:616-626`

```csharp
var idempotencyKey = http.Request.Headers["X-Idempotency-Key"].ToString();
if (!string.IsNullOrWhiteSpace(idempotencyKey))
{
    var cached = await idempotency.CheckAsync(idempotencyKey, me, $"/chats/{threadId}/trial-decision", ct);
    if (cached != null)
    {
        return Results.Json(
            System.Text.Json.JsonSerializer.Deserialize<object>(cached.ResponseBodyJson),
            statusCode: cached.StatusCode);
    }
}
```

**Storage:**
```csharp
if (!string.IsNullOrWhiteSpace(idempotencyKey))
{
    _ = idempotency.StoreAsync(idempotencyKey, me, $"/chats/{threadId}/trial-decision", 200, waitingResponse, ct);
}
```

**TTL:** 24 hours (from `IdempotencyRecord` entity)

---

## Ghost Refund Logic

**Trigger:** Match ends with `BothMessagedAt == null`

**Amount:** 0.5 sparks per user

**Why:** User spent 1 spark on Drawn action. If neither user messaged, they both get half back (no penalty for unresponsive match).

**Code reference:** `ChatEndpoints.cs:742-750`

```csharp
var noMessages = match.BothMessagedAt == null;

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
```

**Service:** `SparkWalletService.GhostRefundAsync(userId)`  
**Implementation:** Credits `UserSparkWallet.Balance += 0.5`, inserts `SparkTransaction` record

---

## Frontend UI

**Component:** `trial-decision.component.ts`

**Modal structure:**
```
┌─────────────────────────────────────┐
│  Trial Period Ended                 │
│                                     │
│  "How was your conversation?"       │
│                                     │
│  [✓] Continue                       │
│      Keep the conversation going    │
│                                     │
│  [ ] End Match                      │
│      Not the right fit              │
│                                     │
│  [ ] Block                          │
│      Safety concern                 │
│                                     │
│  (if END selected)                  │
│  Why are you ending?                │
│  ( ) No spark                       │
│  ( ) Wrong timing                   │
│  ( ) Not my type                    │
│                                     │
│            [Submit Decision]        │
└─────────────────────────────────────┘
```

**When shown:** `chat-thread.component.ts:415-430`

```typescript
checkTrialStatus() {
  if (!this.data) return;

  this.isInTrial = !!this.data.isTrial;
  this.trialSecondsLeft = this.data.trialSecondsLeft ?? 0;

  // Only show modal when trial has ended AND user hasn't decided yet
  if (this.data.canMakeDecision && !this.showTrialDecision) {
    const myDecision = this.data.isUserA ? this.data.userADecision : this.data.userBDecision;

    // Only show if user hasn't decided yet
    if (!myDecision) {
      this.showTrialDecision = true;
    }
  }
}
```

**Modal closes when:**
- User submits decision
- User clicks backdrop (dismissed without action — edge case, decision required eventually)

---

## Error Handling

**Not in trial:**
```json
{
  "error": "NOT_IN_TRIAL"
}
```
Status: 400

**Trial hasn't ended yet:**
```json
{
  "error": "TRIAL_NOT_ENDED"
}
```
Status: 400

**Invalid decision:**
```json
{
  "error": "INVALID_DECISION"
}
```
Status: 400

**Missing end reason:**  
(Not explicitly validated — backend accepts empty `endReason` even for END)

---

## Related Documentation

- [Trial Period Mechanics](./trial-period.md)
- [Balloon State Machine](./balloon-state.md)
- [Close Paths](./close-paths.md)
- [Find Love Stage](./find-love.md)
