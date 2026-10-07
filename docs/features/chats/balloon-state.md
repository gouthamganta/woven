# Balloon State Machine

**Last Updated:** 2026-08-17

## Overview

Every `Match` entity has a `BalloonState` enum that controls whether the match is still active or permanently closed.

**Entity:** `Match` (table: `matches`)  
**Field:** `balloon_state` (enum: `BalloonState`)  
**File:** `backend/WovenBackend/data/Entities/Moments/Match.cs`

---

## States

```csharp
public enum BalloonState 
{ 
    ACTIVE = 1,  // Match is live, users can message
    CLOSED = 2   // Match ended permanently (cannot reopen)
}
```

### ACTIVE

**What it means:** Match is live. Users can:
- Send/receive messages
- Send/listen to voice notes
- Play games
- React with ❤️
- Pop balloon (start trial)
- Unmatch gracefully

**Database value:** `balloon_state = 1`

**Frontend check:**  
```typescript
data.balloonState === 'ACTIVE'
```

**Backend check:**
```csharp
match.BalloonState == BalloonState.ACTIVE
```

---

### CLOSED

**What it means:** Match has permanently ended. No further messaging allowed. Chat thread is archived (removed from active chat list).

**Database value:** `balloon_state = 2`

**When set:** Any of the following close events:
- Balloon expires (timeout)
- User pops balloon (deprecated path, now starts trial instead)
- Trial ends with one or both users choosing END
- Trial ends with one user choosing BLOCK
- Either user unmatches
- Either user blocks

**Immutable:** Once `CLOSED`, cannot transition back to `ACTIVE`.

**Frontend behavior:** Chat threads with `CLOSED` state are excluded from the list (`GET /chats` filters by `ACTIVE` only).

**Code reference:** `ChatEndpoints.cs:43` — list query filters `WHERE m.BalloonState == BalloonState.ACTIVE`

---

## State Transitions

```
┌─────────┐
│ ACTIVE  │ ─────────┐
└─────────┘          │
     │               │  (any close event)
     │               │
     │               ▼
     │          ┌─────────┐
     └─────────▶│ CLOSED  │ ◀──── (immutable)
                └─────────┘
```

**All transitions:**

| From | Event | To | Reason Set |
|---|---|---|---|
| ACTIVE | Trial timeout (no messages) | CLOSED | `UNMATCH` |
| ACTIVE | Trial decision: both END | CLOSED | `UNMATCH` |
| ACTIVE | Trial decision: one BLOCK | CLOSED | `BLOCK` |
| ACTIVE | User unmatches (`POST /matches/{id}/unmatch`) | CLOSED | `UNMATCH` |
| ACTIVE | User blocks (`POST /matches/{id}/block`) | CLOSED | `BLOCK` |
| ACTIVE | Balloon expires (24h no interaction) | CLOSED | `EXPIRE` |
| ACTIVE | Balloon popped (legacy, now trial) | CLOSED | `POP` (deprecated) |

**Note:** Balloon pop (`POST /matches/{id}/pop`) no longer closes immediately — it starts a trial instead. The `POP` close reason is now deprecated but remains in the enum for historical data.

---

## Close Reasons

When a match transitions to `CLOSED`, a `ClosedReason` enum is set to explain why.

**Entity field:** `Match.ClosedReason`  
**Database field:** `closed_reason`

```csharp
public enum ClosedReason 
{
    POP = 1,      // Deprecated (balloon pop now starts trial)
    EXPIRE = 2,   // Match expired (timeout)
    UNMATCH = 3,  // User unmatched gracefully
    BLOCK = 4     // User blocked
}
```

### POP (Deprecated)

**Historical:** When balloon pop closed the match immediately (pre-trial feature).  
**Current behavior:** Balloon pop starts trial instead → no longer sets `ClosedReason = POP`.

**Legacy data:** Matches closed via POP before 2026-06-03 still have this reason in the database.

---

### EXPIRE

**When set:** Match balloon expires after 24 hours of no activity.

**Code location:** Background worker (not in current endpoints — feature exists but may not be wired).

**Example:**
```
Match.ExpiresAt = 2026-08-18 12:00:00 UTC
now             = 2026-08-18 12:01:00 UTC
→ Set BalloonState = CLOSED, ClosedReason = EXPIRE
```

---

### UNMATCH

**When set:**
1. User calls `POST /matches/{id}/unmatch`
2. Trial ends with both users choosing END
3. Trial times out with no messages exchanged (auto-close)

**Ghost refund:** If `Match.BothMessagedAt == null`, both users get 0.5 sparks back.

**Code references:**
- `ChatEndpoints.cs:252-257` — trial auto-close
- `ChatEndpoints.cs:733-740` — trial decision both END
- `MatchesEndpoints.cs:166-183` — manual unmatch

---

### BLOCK

**When set:**
1. User calls `POST /matches/{id}/block`
2. Trial decision with one user choosing BLOCK

**Side effects:**
- Creates `Block` record (`BlockerId`, `BlockedId`)
- Blocker cannot see blocked user in future decks
- Blocked user never knows they were blocked (remains hidden)

**Code references:**
- `ChatEndpoints.cs:696-713` — trial decision BLOCK
- `MatchesEndpoints.cs:185-204` — manual block

---

## Database Schema

**Table:** `matches`

```sql
CREATE TABLE matches (
    id UUID PRIMARY KEY,
    balloon_state INT NOT NULL DEFAULT 1,  -- BalloonState enum
    closed_reason INT,                      -- ClosedReason enum (nullable)
    closed_at TIMESTAMPTZ,
    -- ... other fields
);
```

**Indexes:**
- Primary: `id`
- Composite: `(user_a_id, user_b_id, balloon_state)` — for filtering active matches
- Single: `balloon_state` — for chat list query performance

---

## Frontend State Checks

**Active balloon:**
```typescript
const isBalloonStage = data.balloonState === 'ACTIVE' 
    && !data.findLoveAt 
    && !data.isTrial;
```

**Trial active:**
```typescript
const isTrialStage = data.balloonState === 'ACTIVE' 
    && data.isTrial;
```

**Find Love unlocked:**
```typescript
const isFindLoveStage = data.balloonState === 'ACTIVE' 
    && data.findLoveAt 
    && new Date(data.findLoveAt).getTime() <= Date.now();
```

**Closed (archived):**
```typescript
const isClosed = data.balloonState === 'CLOSED';
// Chat thread no longer appears in list
```

---

## Backend Guards

Every chat endpoint checks balloon state before allowing actions:

```csharp
if (match.BalloonState != BalloonState.ACTIVE)
    return Results.BadRequest(new { error = "BALLOON_NOT_ACTIVE" });
```

**Applies to:**
- `POST /chats/{threadId}/messages` (line 426)
- `POST /chats/{threadId}/voice-message` (line 805)
- `POST /chats/{threadId}/trial-decision` (implicit — trial only exists in ACTIVE)
- `POST /chats/{threadId}/date-interest` (line 1082)

**Does not apply to:**
- `GET /chats/{threadId}` — read-only, allowed for closed threads (for message history)
- `POST /chats/{threadId}/close-gracefully` — checked, then immediately closes (line 573)

---

## Related Documentation

- [Trial Period Mechanics](./trial-period.md)
- [Trial Decision Flow](./trial-decision.md)
- [Close Paths](./close-paths.md)
- [Backend Implementation](./backend.md)
