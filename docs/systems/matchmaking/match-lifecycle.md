# Match Lifecycle

**System:** Matchmaking  
**Related:** [Balloon Lifecycle](../../features/matches/balloon-lifecycle.md) | [Trial Period](../../features/matches/trial-period.md)

---

## Overview

A match progresses through states from creation to closure. Immutable once closed.

---

## State Flow

```
DECK RESPONSE → MATCH CREATED → BALLOON ACTIVE → (pop) → TRIAL → DECISION → ACTIVE or CLOSED
```

---

## States

### 1. Match Created
- Both users swiped right (MAGICAL or RESONANT)
- `Match` record created
- `BalloonState = ACTIVE`
- `BalloonExpiresAt = CreatedAt + 36h`

### 2. Balloon Active
- 36-hour window to pop and start trial
- Users see each other in DRAWN tab (costs 1 spark to pop)

### 3. Trial Period
- Starts when second user opens chat
- `TrialEndsAt = now + 3 minutes`
- Users test compatibility

### 4. Trial Decision
- Each user chooses: CONTINUE / END / BLOCK
- If both CONTINUE → match stays active
- Any END/BLOCK → match closes

### 5. Match Active
- Ongoing chat, no time limit
- Can close via: user unmatch, block, or Find Love unlock

### 6. Match Closed
- **Immutable** — cannot reopen
- `BalloonState = CLOSED`
- `CloseReason`: POP/EXPIRE/BLOCK/END/UNMATCH

---

## Evidence

- [Match.cs](../../../backend/WovenBackend/data/Entities/Moments/Match.cs)
- [MomentsRules.cs](../../../backend/WovenBackend/Services/Moments/MomentsRules.cs)
- [State Machines](../../business/state-machines.md)

---

## Related

- [Balloon Lifecycle](../../features/matches/balloon-lifecycle.md)
- [Trial Period](../../features/matches/trial-period.md)
- [Match Close Paths](../../features/chats/close-paths.md)
