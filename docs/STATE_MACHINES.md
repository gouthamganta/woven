# Woven State Machines

> Complete reference for all state transitions in Woven.
> AI must respect these transitions — invalid transitions will corrupt data.

---

## 1. Balloon State Machine

Balloons are the core matching mechanism. A balloon represents a potential connection between two users after both make choices on the same Moment theme.

### States

| State | Description |
|-------|-------------|
| `ACTIVE` | Balloon exists, users can interact |
| `CLOSED` | Balloon terminated, no further interaction |

### Transitions

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│    ┌────────┐                                               │
│    │ ACTIVE │                                               │
│    └───┬────┘                                               │
│        │                                                    │
│        ├──── POP ────────┐                                  │
│        │                 │                                  │
│        ├──── EXPIRE ─────┼───────▶ ┌────────┐               │
│        │                 │         │ CLOSED │               │
│        ├──── UNMATCH ────┤         └────────┘               │
│        │                 │                                  │
│        └──── BLOCK ──────┘                                  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Transition Rules

| From | To | Trigger | Side Effects |
|------|-----|---------|--------------|
| ACTIVE | CLOSED | POP | Trial period starts (1-minute window) |
| ACTIVE | CLOSED | EXPIRE | 7-day window passed; BalloonExpiryWorker closes silently |
| ACTIVE | CLOSED | UNMATCH | User unmatches; no trial period |
| ACTIVE | CLOSED | BLOCK | User blocks; other party added to block list |

### Close Reasons (Enum)

```csharp
public enum BalloonCloseReason
{
    POP,      // User popped balloon intentionally
    EXPIRE,   // 7-day TTL expired
    UNMATCH,  // User chose to unmatch
    BLOCK     // User blocked the other party
}
```

### Code Example

```csharp
// ✅ CORRECT — Proper state transition
public async Task<bool> PopBalloon(Guid balloonId, Guid userId)
{
    var balloon = await _db.Balloons.FindAsync(balloonId);

    if (balloon == null || balloon.Status != BalloonStatus.ACTIVE)
        return false; // Cannot pop non-active balloon

    balloon.Status = BalloonStatus.CLOSED;
    balloon.CloseReason = BalloonCloseReason.POP;
    balloon.ClosedAt = DateTime.UtcNow;

    // Start 1-minute trial period
    await _trialService.StartTrial(balloon);

    await _db.SaveChangesAsync();
    return true;
}

// ❌ WRONG — Invalid transition
balloon.Status = BalloonStatus.ACTIVE; // Cannot reopen closed balloon
```

---

## 2. Match State Machine

Matches track the relationship between two users across the entire lifecycle.

### States

| State | Description |
|-------|-------------|
| `PENDING` | One user made a choice, waiting for other |
| `MATCHED` | Both users made choices, balloon is ACTIVE |
| `TRIAL` | Balloon popped; 1-minute trial decision window |
| `CONNECTED` | Trial successful (both CONTINUE); full connection |
| `ENDED` | Match terminated (unmatch, block, expire, trial fail) |

### Transitions

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  ┌─────────┐     both choose     ┌─────────┐                        │
│  │ PENDING │ ──────────────────▶ │ MATCHED │                        │
│  └────┬────┘                     └────┬────┘                        │
│       │                               │                             │
│       │ expire/                       │ pop balloon                 │
│       │ unmatch                       ▼                             │
│       │                         ┌─────────┐     both CONTINUE       │
│       │                         │  TRIAL  │ ─────────────────────┐  │
│       │                         └────┬────┘  (FindLoveAt = now)  │  │
│       │                              │                           │  │
│       │                              │ timeout/END/              ▼  │
│       │                              │ either END           ┌─────────┐
│       │                              │                      │CONNECTED│
│       │                              ▼                      └────┬────┘
│       │                         ┌─────────┐                      │  │
│       └───────────────────────▶ │  ENDED  │ ◀────────────────────┘  │
│                                 └─────────┘   unmatch/block         │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Match Access Level by Type

| Phase | PURE Match | EDGE Match (owner) | EDGE Match (non-owner) |
|-------|------------|-------------------|----------------------|
| MATCHED (balloon active) | Full profile | Full profile | 1 photo, no bio |
| After BothMessagedAt | Full profile | Full profile | Full profile unlocked |
| CONNECTED | Full profile | Full profile | Full profile |

---

## 3. Moment Choice State Machine

Tracks individual user choices on Moment themes.

### States

| State | Description |
|-------|-------------|
| `PENDING` | Theme shown, no choice made |
| `YES` | User chose Magical (◈) — heart leads; API value "MAGICAL" |
| `NO` | User chose Logical (◇) — head leads; API value "LOGICAL" |
| `PENDING` | User chose Save (⏳ Hold); API value "PENDING" — goes to pending queue |
| `SKIPPED` | User explicitly skipped |
| `EXPIRED` | Time window passed without choice |

> Note: The DB enum reuses `PENDING` for both "not yet decided" and "Hold/Save". Context distinguishes them — a `MomentResponse` row with `PENDING` means Hold/Save; absence of a row means not yet decided.

### Transitions

```
┌───────────────────────────────────────────────────────┐
│                                                       │
│  ┌─────────────┐                                      │
│  │ (no choice) │                                      │
│  └──────┬──────┘                                      │
│         │                                             │
│         ├── Magical ◈ ──────▶ ┌─────┐                 │
│         │   (heart leads)     │ YES │ → creates match │
│         │                     └─────┘                 │
│         │                                             │
│         ├── Logical ◇ ──────▶ ┌────┐                  │
│         │   (head leads)      │ NO │ → creates match  │
│         │                     └────┘                  │
│         │                                             │
│         ├── Save ⏳ ─────────▶ ┌─────────┐             │
│         │   (can't decide)    │ PENDING │             │
│         │                     └─────────┘             │
│         │                                             │
│         ├── Skip ──────────▶ ┌─────────┐              │
│         │                    │ SKIPPED │              │
│         │                    └─────────┘              │
│         │                                             │
│         └── timeout ────────▶ ┌─────────┐             │
│                                │ EXPIRED │             │
│                                └─────────┘             │
│                                                       │
└───────────────────────────────────────────────────────┘
```

### Budget Impact

| Choice | Budget Cost | Notes |
|--------|-------------|-------|
| Magical (◈) | 1 | Standard; uses interaction budget |
| Logical (◇) | 1 | Standard; uses interaction budget |
| Save (⏳ Hold) | 1 | Uses Save cap (2/day) + total cap |
| Skip | 0 | No cost; won't see again |

---

## 4. Trial Period State Machine

After a balloon is popped, users enter a 1-minute trial decision window.

### States

| State | Description |
|-------|-------------|
| `IN_PROGRESS` | Trial running; waiting for both decisions |
| `PASSED` | Both chose CONTINUE; connection promoted |
| `FAILED` | At least one chose END or timeout |

### Transitions

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  ┌─────────────┐                                            │
│  │ IN_PROGRESS │  (1 minute window)                         │
│  └──────┬──────┘                                            │
│         │                                                   │
│         ├── both CONTINUE ────▶ ┌────────┐                  │
│         │                       │ PASSED │ → FindLoveAt=now │
│         │                       └────────┘                  │
│         │                                                   │
│         ├── either END ────────▶ ┌────────┐                 │
│         │                        │ FAILED │ → match ENDED   │
│         │                        └────────┘                 │
│         │                                                   │
│         └── timeout (60 s) ─────▶ ┌────────┐               │
│                                    │ FAILED │ → match ENDED │
│                                    └────────┘               │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Trial Rules

- **Duration**: 1 minute (60 seconds)
- **Both must decide**: CONTINUE or END within the window
- **Timeout behavior**: Treated as FAILED — match ends as UNMATCH
- **Rating**: User A must provide a rating (-100 to +100) when submitting their decision

---

## 5. User Status State Machine

Tracks overall user account status.

### States

| State | Description |
|-------|-------------|
| `ONBOARDING` | User creating profile; not yet in any deck |
| `ACTIVE` | Normal active user |
| `PAUSED` | User voluntarily paused account |
| `SUSPENDED` | Admin suspended for review |
| `BANNED` | Permanently banned |
| `DELETED` | Account deleted (soft delete) |

### Transitions

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  ┌────────────┐   complete   ┌────────┐                          │
│  │ ONBOARDING │ ───────────▶ │ ACTIVE │ ◀───┐                    │
│  └────────────┘              └───┬────┘     │                    │
│                                  │          │ resume             │
│                                  │ pause    │                    │
│                                  ▼          │                    │
│                             ┌────────┐      │                    │
│                             │ PAUSED │ ─────┘                    │
│                             └────────┘                           │
│                                                                  │
│  Any state ──── admin action ────▶ ┌───────────┐                 │
│                                    │ SUSPENDED │                 │
│                                    └─────┬─────┘                 │
│                                          │                       │
│                                          ├──── reinstate ──▶ ACTIVE
│                                          │                       │
│                                          └──── escalate ────▶ ┌────────┐
│                                                                │ BANNED │
│                                                                └────────┘
│                                                                  │
│  Any state ──── user request ────▶ ┌─────────┐                   │
│                                    │ DELETED │                   │
│                                    └─────────┘                   │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

---

## 6. Onboarding ProfileStatus State Machine

Tracks a user's progress through the multi-step onboarding flow.

### States (in order)

| State | Description |
|-------|-------------|
| `STARTED` | Account created, onboarding begun |
| `PHOTOS_ADDED` | At least 1 photo uploaded |
| `BIO_ADDED` | Bio text entered |
| `INTENT_SET` | lookingFor + timeline answered |
| `PREFERENCES_SET` | Gender/age/distance preferences configured |
| `FOUNDATIONAL_DONE` | Foundational questions answered |
| `COMPLETE` | All required steps done; user enters deck |

### Transitions

```
STARTED
  └──▶ PHOTOS_ADDED
         └──▶ BIO_ADDED
                └──▶ INTENT_SET
                       └──▶ PREFERENCES_SET
                              └──▶ FOUNDATIONAL_DONE
                                     └──▶ COMPLETE
```

### Rules

- Steps can be completed in any order except COMPLETE (requires all prior steps)
- User is excluded from all candidate pools until status = `COMPLETE`
- Users can return and edit steps after COMPLETE; status stays COMPLETE

---

## 7. Game Session State Machine

Tracks the lifecycle of a game session (Know Me, Red/Green Flag) between two connected users.

### States

| State | Description |
|-------|-------------|
| `PENDING` | Session created; waiting for opponent to accept |
| `ACTIVE` | Both accepted; rounds in progress |
| `COMPLETED` | All rounds finished; result available |
| `REJECTED` | Opponent declined the game invitation |
| `ABANDONED` | Session expired or abandoned mid-game |

### Transitions

```
┌────────────────────────────────────────────────────────────────┐
│                                                                │
│  ┌─────────┐     accept      ┌────────┐                        │
│  │ PENDING │ ──────────────▶ │ ACTIVE │                        │
│  └────┬────┘                 └───┬────┘                        │
│       │                          │                             │
│       │ reject                   │ all rounds done             │
│       ▼                          ▼                             │
│  ┌──────────┐              ┌───────────┐                       │
│  │ REJECTED │              │ COMPLETED │                       │
│  └──────────┘              └───────────┘                       │
│       │                                                        │
│       │ timeout / no response                                  │
│       ▼                                                        │
│  ┌───────────┐                                                 │
│  │ ABANDONED │                                                 │
│  └───────────┘                                                 │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### Game Round Flow

```
GameSession (ACTIVE)
  └── Round 1: Initiator answers → Opponent guesses
  └── Round 2: Opponent answers → Initiator guesses
  └── ... (configurable rounds)
  └── All rounds done → status = COMPLETED
```

---

## 8. Verification Status State Machine

Tracks user photo/selfie verification.

### States

| State | Description |
|-------|-------------|
| `UNVERIFIED` | No verification submitted |
| `PENDING_REVIEW` | Selfie submitted; awaiting AI/manual review |
| `VERIFIED` | Verification passed; badge displayed |
| `REJECTED` | Verification failed; user can retry |

### Transitions

```
┌────────────────────────────────────────────────────────────┐
│                                                            │
│  ┌────────────┐   submit selfie   ┌────────────────┐       │
│  │ UNVERIFIED │ ────────────────▶ │ PENDING_REVIEW │       │
│  └────────────┘                   └───────┬────────┘       │
│       ▲                                   │                │
│       │                        ┌──────────┴──────────┐     │
│       │                        ▼                     ▼     │
│       │                  ┌──────────┐          ┌──────────┐ │
│       │                  │ VERIFIED │          │ REJECTED │ │
│       │                  └──────────┘          └────┬─────┘ │
│       │                                             │       │
│       └─────────────── retry ──────────────────────┘       │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

### Rules

- Verified badge visible on profile in all relationship tiers
- Verification uses selfie liveness check (SpeechBrain in prod, stub in dev)
- Verification expires after 180 days and must be renewed

---

## 9. Match Access Level State Machine

Tracks what profile information is visible between matched users (separate from Match State).

### States

| State | Description |
|-------|-------------|
| `LIMITED` | Restricted view (1 photo, no bio) — EDGE non-owner before both message |
| `STANDARD` | Full name + photos — most matched users |
| `FULL` | All details including last active — CONNECTED users |

### Transitions

```
EDGE non-owner:
  LIMITED ──── BothMessagedAt set ────▶ STANDARD ──── CONNECTED ────▶ FULL

PURE / EDGE owner:
  STANDARD (immediately) ──── CONNECTED ────▶ FULL
```

### Rules

- Access level is computed, not stored — derived from match type, edge ownership, and BothMessagedAt
- Reverting from FULL is not possible (connections don't downgrade)

---

## 10. Moderation Queue State Machine

Tracks the lifecycle of user reports.

### States

| State | Description |
|-------|-------------|
| `QUEUED` | Report submitted; awaiting review |
| `UNDER_REVIEW` | Moderator has opened the report |
| `DISMISSED` | Report reviewed; no action taken |
| `ACTION_TAKEN` | Moderation action applied (warn/suspend/ban) |
| `APPEALED` | User appealed the action |
| `APPEAL_RESOLVED` | Appeal reviewed and closed |

### Transitions

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│  ┌────────┐     assigned     ┌──────────────┐                 │
│  │ QUEUED │ ───────────────▶ │ UNDER_REVIEW │                 │
│  └────────┘                  └───────┬──────┘                 │
│                                      │                        │
│                         ┌────────────┴────────────┐           │
│                         ▼                         ▼           │
│                   ┌───────────┐           ┌──────────────┐    │
│                   │ DISMISSED │           │ ACTION_TAKEN │    │
│                   └───────────┘           └──────┬───────┘    │
│                                                  │            │
│                                           user appeals        │
│                                                  ▼            │
│                                          ┌──────────┐         │
│                                          │ APPEALED │         │
│                                          └────┬─────┘         │
│                                               │               │
│                                               ▼               │
│                                     ┌─────────────────┐       │
│                                     │ APPEAL_RESOLVED │       │
│                                     └─────────────────┘       │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

---

## 11. Pulse (Weekly Vibe) State Machine

Tracks user's weekly vibe check-in status.

### States

| State | Description |
|-------|-------------|
| `UNANSWERED` | New cycle started; user hasn't answered |
| `ANSWERED` | User completed pulse for this cycle |
| `LOCKED` | Cycle still active; cannot change until next cycle |

### Transitions

```
┌─────────────────────────────────────────────────────┐
│                                                     │
│  ┌────────────┐                                     │
│  │ UNANSWERED │                                     │
│  └─────┬──────┘                                     │
│        │                                            │
│        │ user submits                               │
│        ▼                                            │
│  ┌────────────┐                                     │
│  │  ANSWERED  │                                     │
│  └─────┬──────┘                                     │
│        │                                            │
│        │ cycle continues                            │
│        ▼                                            │
│  ┌────────────┐      cycle resets      ┌──────────┐ │
│  │   LOCKED   │ ─────────────────────▶ │UNANSWERED│ │
│  └────────────┘                        └──────────┘ │
│                                                     │
└─────────────────────────────────────────────────────┘
```

### Pulse Rules

- **Cycle Duration**: 7 days
- **Impact**: Pulse layer (15% weight) in matching algorithm updated on submit
- **Edit Window**: Can edit only during ANSWERED state; locks once confirmed

---

## Implementation Guidelines

### 1. Always Validate Current State Before Transition

```csharp
// ✅ CORRECT
public async Task<Result> TransitionBalloon(Guid id, BalloonAction action)
{
    var balloon = await _db.Balloons.FindAsync(id);

    if (!CanTransition(balloon.Status, action))
        return Result.Fail("Invalid transition");

    // Proceed with transition
}

private bool CanTransition(BalloonStatus current, BalloonAction action)
{
    return current switch
    {
        BalloonStatus.ACTIVE => action is BalloonAction.POP or BalloonAction.EXPIRE
                                or BalloonAction.UNMATCH or BalloonAction.BLOCK,
        _ => false // CLOSED cannot transition
    };
}
```

### 2. Log All State Transitions

```csharp
// ✅ CORRECT
balloon.Status = BalloonStatus.CLOSED;
balloon.CloseReason = reason;
balloon.ClosedAt = DateTime.UtcNow;

_logger.LogInformation(
    "Balloon {BalloonId} transitioned to CLOSED via {Reason}",
    balloon.Id, reason);
```

### 3. Use Timestamps for All Transitions

```csharp
public class Balloon
{
    public BalloonStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }  // CreatedAt + 7 days
    public DateTime? ClosedAt { get; set; }
    public BalloonCloseReason? CloseReason { get; set; }
}
```

### 4. Frontend Must Reflect State Correctly

```typescript
// ✅ CORRECT — Check state before showing actions
<button *ngIf="balloon.status === 'ACTIVE'" (click)="pop(balloon)">
  Pop Balloon
</button>

<div *ngIf="balloon.status === 'CLOSED'" class="closed-message">
  This balloon has ended
</div>
```

---

## State Machine Testing Checklist

- [ ] Test all valid transitions
- [ ] Test rejection of invalid transitions
- [ ] Test boundary conditions (timing, counts)
- [ ] Test concurrent transition attempts
- [ ] Test state persistence after restart
- [ ] Test state display in UI matches backend
- [ ] Test trial timeout behavior (60-second window)
- [ ] Test EDGE match access level before/after BothMessagedAt
- [ ] Test game session reject and abandon paths
- [ ] Test moderation appeal flow end-to-end
