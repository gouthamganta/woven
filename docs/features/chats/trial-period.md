# Trial Period Mechanics

**Last Updated:** 2026-08-17

## What It Is

The Trial is a 3-minute window where both users can message freely, then decide whether to continue the match or end it gracefully.

**Purpose:** Reduces ghosting, encourages intentional engagement, gives ECHO clear preference signals.

**Duration:** 3 minutes (180 seconds)  
**Outcome:** Both users choose CONTINUE / END / BLOCK  
**Unlocks:** Find Love stage (if both choose CONTINUE)

---

## Lifecycle

### 1. Start Trial (Balloon Pop)

**Trigger:** Either user pops the balloon.

**Endpoint:** `POST /matches/{matchId}/pop`

**What happens:**
1. `Match.IsTrial = true`
2. `Match.TrialStartedAt = now`
3. Timer does NOT start yet — waits for both users to open the thread
4. User who popped has `TrialUserAOpenedAt` or `TrialUserBOpenedAt` set immediately

**Code reference:** `MatchesEndpoints.cs:292-345`

```csharp
match.IsTrial = true;
match.TrialStartedAt = now;

if (isUserA)
    match.TrialUserAOpenedAt = now;
else
    match.TrialUserBOpenedAt = now;
```

**Frontend response:**
```json
{
  "status": "TRIAL_STARTED",
  "matchId": "...",
  "trialStartedAt": "2026-08-17T12:00:00Z",
  "waitingForOtherToOpen": true
}
```

---

### 2. Timer Starts (Both Users Open Thread)

**Trigger:** Second user opens the chat thread (`GET /chats/{threadId}`)

**What happens:**
1. Chat GET endpoint detects `IsTrial == true` and user hasn't opened yet
2. Sets `TrialUserAOpenedAt` or `TrialUserBOpenedAt` for the second user
3. **When both timestamps are non-null:** `TrialEndsAt = now + 3 minutes`

**Code reference:** `ChatEndpoints.cs:212-236`

```csharp
if (match.IsTrial)
{
    var meIsUserA = match.UserAId == me;
    var changed = false;

    if (meIsUserA && match.TrialUserAOpenedAt == null)
    {
        match.TrialUserAOpenedAt = now;
        changed = true;
    }
    else if (!meIsUserA && match.TrialUserBOpenedAt == null)
    {
        match.TrialUserBOpenedAt = now;
        changed = true;
    }

    // Both have opened — start the 3-minute window
    if (changed && match.TrialEndsAt == null
        && match.TrialUserAOpenedAt != null && match.TrialUserBOpenedAt != null)
    {
        match.TrialEndsAt = now.AddMinutes(3);
    }

    if (changed)
        await db.SaveChangesAsync(ct);
}
```

**Why this design?**  
Prevents timer from starting when only one person is active. Both users get the full 3 minutes to engage.

---

### 3. Countdown (Active Trial)

**Frontend timer:** Calculated from `trialSecondsLeft` returned by backend.

**Backend calculation:** `ChatEndpoints.cs:65-67`

```csharp
trialSecondsLeft = (m.IsTrial && m.TrialEndsAt != null && m.TrialEndsAt > now)
    ? (int)Math.Ceiling((m.TrialEndsAt.Value - now).TotalSeconds)
    : 0
```

**Frontend display:**

```typescript
// chats-list.component.ts:84-88
if (c.isTrial && (c.trialSecondsLeft ?? 0) > 0) {
  const mm = Math.floor((c.trialSecondsLeft ?? 0) / 60);
  const ss = (c.trialSecondsLeft ?? 0) % 60;
  return `Trial Mode · ${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')} left`;
}
```

**What users see:**
- Chat list: "Trial Mode · 02:47 left"
- Chat header: "Trial · 02:47"

---

### 4. Trial Ends (Timer Reaches Zero)

**Trigger:** `Match.TrialEndsAt <= now`

**Two scenarios:**

#### A. Both users made decisions → outcome determined

- **Both CONTINUE:** Match continues, `FindLoveAt = now` (immediate unlock)
- **One or both END:** Match closes, `BalloonState = CLOSED`, `ClosedReason = UNMATCH`
- **One BLOCK:** Match closes, `BalloonState = CLOSED`, `ClosedReason = BLOCK`, creates `Block` record

**Code reference:** `ChatEndpoints.cs:717-759`

#### B. No decisions yet → auto-close check

**When:** User opens thread after trial ended but before decisions submitted.

**Check:** `ChatEndpoints.cs:240-268`

```csharp
if (match.IsTrial && match.TrialEndsAt != null && match.TrialEndsAt <= now)
{
    var bothDecided = !string.IsNullOrEmpty(match.UserADecision) && !string.IsNullOrEmpty(match.UserBDecision);
    if (!bothDecided)
    {
        // Check for messages during trial
        var hasTrialMessages = await db.ChatMessages.AsNoTracking()
            .AnyAsync(m => m.ThreadId == threadId && m.CreatedAt >= match.TrialStartedAt, ct);

        if (!hasTrialMessages)
        {
            // Auto-close as UNMATCH
            match.BalloonState = BalloonState.CLOSED;
            match.ClosedReason = ClosedReason.UNMATCH;
            match.ClosedAt = now;
            match.IsTrial = false;
            await db.SaveChangesAsync(ct);

            // Ghost refund: no messages were exchanged
            var uA = match.UserAId;
            var uB = match.UserBId;
            _ = Task.Run(async () =>
            {
                try { await sparks.GhostRefundAsync(uA); } catch { }
                try { await sparks.GhostRefundAsync(uB); } catch { }
            });
        }
    }
}
```

**Auto-close rule:**  
If trial ended + no messages exchanged + users haven't decided → close immediately as UNMATCH + ghost refund.

---

### 5. Decision Modal

**Frontend:** `trial-decision.component.ts`

**When shown:** `chat-thread.component.ts:422-429`

```typescript
if (this.data.canMakeDecision && !this.showTrialDecision) {
  const myDecision = this.data.isUserA ? this.data.userADecision : this.data.userBDecision;

  // Only show if user hasn't decided yet
  if (!myDecision) {
    this.showTrialDecision = true;
  }
}
```

**Backend flag:** `canMakeDecision = isTrial && trialEndsAt != null && trialEndsAt <= now`

**Decision options:**
1. **CONTINUE** — wants to keep talking
2. **END** — not interested, pick reason:
   - `no_spark` — chemistry isn't there
   - `wrong_timing` — bad timing in life
   - `not_my_type` — preference mismatch
3. **BLOCK** — safety concern, immediately closes + blocks

---

## Database Fields

**Table:** `matches`

```sql
-- Trial state
is_trial BOOLEAN NOT NULL DEFAULT FALSE,
trial_started_at TIMESTAMPTZ,
trial_ends_at TIMESTAMPTZ,

-- Timer start tracking
trial_user_a_opened_at TIMESTAMPTZ,
trial_user_b_opened_at TIMESTAMPTZ,

-- Decisions
user_a_decision VARCHAR(20),  -- 'CONTINUE' or 'END'
user_b_decision VARCHAR(20),

-- Preference learning
trial_end_reason VARCHAR(30)  -- 'no_spark' | 'wrong_timing' | 'not_my_type'
```

---

## ECHO Signals Recorded

**During trial:**
- `MessageSent` (1.0 per message)
- `MessageResponseLatencyMs` (float ms)
- `TimeToFirstMessageMs` (float ms) — if user sends first message ever

**At decision time:**
- `TrialAccepted` (1.0) or `TrialRejected` (0.0)
- `TrialMessageCount` (float count of messages exchanged)
- `TrialEndedNoSpark` / `TrialEndedWrongTiming` / `TrialEndedNotMyType` (1.0, type encodes reason)

**Code reference:** `ChatEndpoints.cs:673-692`

```csharp
var trialEventType = decision == "CONTINUE"
    ? MatchSignalEventTypes.TrialAccepted
    : MatchSignalEventTypes.TrialRejected;
await signals.RecordAsync(me, otherUserId, trialEventType, decision == "CONTINUE" ? 1f : 0f, ct: ct);

await signals.RecordAsync(me, otherUserId, MatchSignalEventTypes.TrialMessageCount, trialMessageCount, ct: ct);

var reasonSignal = endReason switch
{
    "no_spark"     => MatchSignalEventTypes.TrialEndedNoSpark,
    "wrong_timing" => MatchSignalEventTypes.TrialEndedWrongTiming,
    "not_my_type"  => MatchSignalEventTypes.TrialEndedNotMyType,
    _              => null
};
if (reasonSignal != null)
    await signals.RecordAsync(me, otherUserId, reasonSignal, 1f, ct: ct);
```

**Why this matters:**  
ECHO learns which candidate features correlate with `TrialAccepted` vs `TrialRejected`. End reasons help refine pillar weights.

---

## Idempotency

**Header:** `X-Idempotency-Key`

**Purpose:** Prevent duplicate trial decisions if user double-taps submit button or network retries.

**Storage:** `IdempotencyRecords` table (24-hour TTL)

**Code reference:** `ChatEndpoints.cs:616-626`

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

**Result:** Second request with same key returns cached response immediately, no double-processing.

---

## Edge Cases

### What if only one user opens the thread?

Timer never starts. `TrialEndsAt` remains `null`. No decisions forced. Match can stay in this limbo state until:
1. Second user opens → timer starts
2. Either user unmatches manually
3. Balloon expires (if expiration worker is running)

### What if one user decides, the other ghosts?

**Status:** `DECISION_RECORDED`, `waitingForOther = true`

Match remains `ACTIVE` until:
1. Second user decides → outcome computed
2. Second user opens thread after timeout + no messages → auto-close UNMATCH + ghost refund

### What if both choose END with different reasons?

**Stored:** `Match.TrialEndReason` contains the last decision's reason (second user overwrites).

**ECHO:** Each user's signal log contains their own `TrialEndedXxx` event separately. Both contribute to preference learning.

---

## Related Documentation

- [Trial Decision Flow](./trial-decision.md)
- [Balloon State Machine](./balloon-state.md)
- [Close Paths](./close-paths.md)
- [Backend Implementation](./backend.md)
