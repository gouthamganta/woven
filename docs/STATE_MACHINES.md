# Woven State Machines

> Complete reference for all state transitions in Woven.
> AI must respect these transitions - invalid transitions will corrupt data.

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
| ACTIVE | CLOSED | POP | User explicitly pops balloon, trial period starts |
| ACTIVE | CLOSED | EXPIRE | 72-hour window passed without action |
| ACTIVE | CLOSED | UNMATCH | User unmatches, no trial period |
| ACTIVE | CLOSED | BLOCK | User blocks, adds to block list |

### Close Reasons (Enum)

```csharp
public enum BalloonCloseReason
{
    POP,      // User popped balloon intentionally
    EXPIRE,   // Time window expired
    UNMATCH,  // User chose to unmatch
    BLOCK     // User blocked the other party
}
```

### Code Example

```csharp
// ✅ CORRECT - Proper state transition
public async Task<bool> PopBalloon(Guid balloonId, Guid userId)
{
    var balloon = await _db.Balloons.FindAsync(balloonId);

    if (balloon == null || balloon.Status != BalloonStatus.ACTIVE)
        return false; // Cannot pop non-active balloon

    balloon.Status = BalloonStatus.CLOSED;
    balloon.CloseReason = BalloonCloseReason.POP;
    balloon.ClosedAt = DateTime.UtcNow;

    // Start trial period
    await _trialService.StartTrial(balloon);

    await _db.SaveChangesAsync();
    return true;
}

// ❌ WRONG - Invalid transition
balloon.Status = BalloonStatus.ACTIVE; // Cannot reopen closed balloon
```

---

## 2. Match State Machine

Matches track the relationship between two users across the entire lifecycle.

### States

| State | Description |
|-------|-------------|
| `PENDING` | One user made a choice, waiting for other |
| `MATCHED` | Both users made choices, balloon created |
| `TRIAL` | Balloon popped, in trial communication period |
| `CONNECTED` | Trial successful, full connection established |
| `ENDED` | Match terminated (unmatch, block, expire) |

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
│       │                         ┌─────────┐     trial success       │
│       │                         │  TRIAL  │ ─────────────────────┐  │
│       │                         └────┬────┘                      │  │
│       │                              │                           │  │
│       │                              │ trial fail/               ▼  │
│       │                              │ unmatch              ┌─────────┐
│       │                              │                      │CONNECTED│
│       │                              ▼                      └────┬────┘
│       │                         ┌─────────┐                      │  │
│       └───────────────────────▶ │  ENDED  │ ◀────────────────────┘  │
│                                 └─────────┘   unmatch/block         │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Match Types

When both users choose on a Moment, the match type is determined:

| Type | Condition | Meaning |
|------|-----------|---------|
| `PURE` | Same choice (both YES or both NO) | "You both felt the same energy" |
| `EDGE` | Different choices (YES vs NO) | "Opposites attract" |

---

## 3. Moment Choice State Machine

Tracks individual user choices on Moment themes.

### States

| State | Description |
|-------|-------------|
| `PENDING` | Theme shown, no choice made |
| `YES` | User chose "Yes" (e.g., Brunch) |
| `NO` | User chose "No" (e.g., Dinner) |
| `HOLD` | User chose "Can't decide" / Hold |
| `SKIPPED` | User explicitly skipped |
| `EXPIRED` | Time window passed without choice |

### Transitions

```
┌───────────────────────────────────────────────────┐
│                                                   │
│  ┌─────────┐                                      │
│  │ PENDING │                                      │
│  └────┬────┘                                      │
│       │                                           │
│       ├──── user chooses YES ────▶ ┌─────┐        │
│       │                            │ YES │        │
│       │                            └─────┘        │
│       │                                           │
│       ├──── user chooses NO ─────▶ ┌─────┐        │
│       │                            │ NO  │        │
│       │                            └─────┘        │
│       │                                           │
│       ├──── user chooses HOLD ───▶ ┌──────┐       │
│       │                            │ HOLD │       │
│       │                            └──────┘       │
│       │                                           │
│       ├──── user skips ──────────▶ ┌─────────┐    │
│       │                            │ SKIPPED │    │
│       │                            └─────────┘    │
│       │                                           │
│       └──── timeout ─────────────▶ ┌─────────┐    │
│                                    │ EXPIRED │    │
│                                    └─────────┘    │
│                                                   │
└───────────────────────────────────────────────────┘
```

### Budget Impact

| Choice | Budget Cost | Notes |
|--------|-------------|-------|
| YES | 1 | Standard spend |
| NO | 1 | Standard spend |
| HOLD | 0 | No cost, saved for later |
| SKIPPED | 0 | No cost, won't see again |

---

## 4. Trial Period State Machine

After a balloon is popped, users enter a trial communication period.

### States

| State | Description |
|-------|-------------|
| `ACTIVE` | Trial in progress, limited messaging |
| `PASSED` | Both users engaged, convert to full connection |
| `FAILED` | Insufficient engagement, trial ends |
| `CANCELLED` | User manually ended trial |

### Transitions

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  ┌────────┐                                                 │
│  │ ACTIVE │                                                 │
│  └───┬────┘                                                 │
│      │                                                      │
│      ├──── both engaged ────────▶ ┌────────┐                │
│      │                            │ PASSED │ ──▶ Connection │
│      │                            └────────┘                │
│      │                                                      │
│      ├──── timeout/no engage ───▶ ┌────────┐                │
│      │                            │ FAILED │                │
│      │                            └────────┘                │
│      │                                                      │
│      └──── user cancels ────────▶ ┌───────────┐             │
│                                   │ CANCELLED │             │
│                                   └───────────┘             │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Trial Rules

- **Duration**: Configurable (e.g., 24-48 hours)
- **Message Limit**: Limited exchanges during trial
- **Engagement Threshold**: Both users must send at least N messages
- **Conversion**: Meeting threshold promotes to full connection

---

## 5. User Status State Machine

Tracks overall user account status.

### States

| State | Description |
|-------|-------------|
| `ONBOARDING` | User creating profile |
| `ACTIVE` | Normal active user |
| `PAUSED` | User voluntarily paused account |
| `SUSPENDED` | Admin suspended for review |
| `BANNED` | Permanently banned |
| `DELETED` | Account deleted |

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
│                                          └──── escalate ───▶ ┌────────┐
│                                                              │ BANNED │
│                                                              └────────┘
│                                                                  │
│  Any state ──── user request ────▶ ┌─────────┐                   │
│                                    │ DELETED │                   │
│                                    └─────────┘                   │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

---

## 6. Pulse (Daily Check-in) State Machine

Tracks user's daily pulse check-in status.

### States

| State | Description |
|-------|-------------|
| `UNANSWERED` | New cycle, user hasn't answered |
| `ANSWERED` | User completed pulse for this cycle |
| `LOCKED` | Already answered, cannot change until next cycle |

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

- **Cycle Duration**: Configurable (typically 24-48 hours)
- **Edit Window**: Can only edit after cycle ends
- **Impact**: Affects matching algorithm weighting

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

// Helper method
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
    public DateTime? ClosedAt { get; set; }
    public BalloonCloseReason? CloseReason { get; set; }
}
```

### 4. Frontend Must Reflect State Correctly

```typescript
// ✅ CORRECT - Check state before showing actions
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
