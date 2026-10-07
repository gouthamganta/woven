# Pending Tasks — Priority System

**Last Updated:** 2026-08-17

---

## Overview

The Unified Woven Assistant uses a **priority-driven task queue** to determine what to show users when they tap the orb. This document explains:
- How tasks are detected
- How priority is assigned
- How tasks are fetched and cached

---

## Priority Hierarchy

Tasks are ranked 1–3, with **1 being highest priority**:

| Priority | Task Type | What It Is | Why It's Prioritized |
|----------|-----------|------------|----------------------|
| **1** | `date_feedback` | Post-match feedback after balloon closes | Critical ECHO signal. Needs to be captured while memory is fresh. |
| **2** | `weekly_coaching` | Weekly coaching summary delivered by worker | High-value engagement. User explicitly requested coaching insights. |
| **3** | `daily_pulse` | Daily mood/state check-in | Routine engagement. Least urgent. Can be skipped without major loss. |

**Rule:** The frontend ALWAYS shows the task with the lowest priority number (highest importance).

---

## Task Detection Logic

### Backend: `GET /me/pending-tasks`

File: `backend/WovenBackend/Endpoints/InteractionEndpoints.cs`

The endpoint runs 3 separate queries in sequence:

```csharp
// 1. Check for Date Feedback pending
var hasFeedbackPending = await db.Matches
    .Where(m => (m.UserAId == userId || m.UserBId == userId)
        && m.BalloonState == BalloonState.CLOSED
        && !db.Set<DateFeedback>()
            .Any(f => f.MatchId == m.Id && f.UserId == userId))
    .AnyAsync(ct);
```

**Condition:** User has at least one match with `BalloonState = CLOSED` AND no `DateFeedback` record exists for that match + user.

**Data returned:**
```json
{
  "type": "date_feedback",
  "priority": 1,
  "data": {
    "matchId": "abc-123",
    "partnerName": "Sarah"
  }
}
```

---

```csharp
// 2. Check for Weekly Coaching pending
var coachingSummary = await db.Set<CoachingSummary>()
    .Where(c => c.UserId == userId 
        && !c.DismissedAt.HasValue 
        && c.DeliveredAt.HasValue)
    .OrderByDescending(c => c.CreatedAt)
    .FirstOrDefaultAsync(ct);
```

**Condition:** User has a `CoachingSummary` record where:
- `DeliveredAt` is non-null (worker delivered it)
- `DismissedAt` is null (user hasn't dismissed it yet)

**Data returned:**
```json
{
  "type": "weekly_coaching",
  "priority": 2,
  "data": {
    "summaryId": 42,
    "summaryText": "This week you showed curiosity..."
  }
}
```

---

```csharp
// 3. Check for Daily Pulse pending
var currentCycle = await db.UserDynamicIntakeSets
    .Where(d => d.UserId == userId
        && d.CycleEndUtc > DateTimeOffset.UtcNow
        && !d.AnsweredAtUtc.HasValue)
    .OrderByDescending(d => d.CycleStartUtc)
    .FirstOrDefaultAsync(ct);
```

**Condition:** User has a `UserDynamicIntakeSet` (pulse cycle) where:
- `CycleEndUtc` is in the future (cycle still active)
- `AnsweredAtUtc` is null (not yet answered)

**Data returned:**
```json
{
  "type": "daily_pulse",
  "priority": 3,
  "data": {
    "cycleId": "cycle_xyz"
  }
}
```

---

### Response Format

```json
{
  "tasks": [
    {
      "type": "date_feedback",
      "priority": 1,
      "data": { "matchId": "abc", "partnerName": "Sarah" }
    },
    {
      "type": "daily_pulse",
      "priority": 3,
      "data": { "cycleId": "xyz" }
    }
  ]
}
```

**Sorting:** Backend sorts by `priority` ascending before returning:

```csharp
var sorted = tasks.OrderBy(t => ((dynamic)t).priority).ToList();
return Results.Ok(new { tasks = sorted });
```

Frontend receives tasks **already sorted**. First task in array = highest priority.

---

## Frontend: Task Fetching & Caching

File: `frontend/woven-frontend/src/app/components/woven-assistant/woven-assistant.component.ts`

### Initial Fetch

```typescript
async ngOnInit() {
  if (this.isBrowser) {
    await this.checkPendingTasks();
  }
}

async checkPendingTasks() {
  try {
    const res = await firstValueFrom(this.pendingTask.getPendingTasks());
    this.pendingTasks = res.tasks;
    this.badgeCount = this.pendingTasks.length;
    this.updateGlowState();
    this.cdr.markForCheck();
  } catch {
    // Silent fail - just no badge/glow
  }
}
```

**Timing:** Fetched once on component init (`ngOnInit`). No polling. No WebSocket updates.

**State storage:**
- `pendingTasks: PendingTask[]` — Array of tasks (already sorted by backend)
- `badgeCount: number` — Length of array
- `glowState: 'none' | 'red' | 'white' | 'gold'` — Calculated from first task in array

---

### Task Consumption (Auto-Advance)

When a task is completed or skipped:

```typescript
async advanceToNextPendingOrChat() {
  // Remove the completed/skipped task
  this.pendingTasks.shift();  // ← Pop first element
  this.badgeCount = this.pendingTasks.length;
  this.updateGlowState();

  if (this.pendingTasks.length > 0) {
    // Show next pending task
    const next = this.pendingTasks[0];
    await this.showPendingTask(next);
  } else {
    // No more pending - show chat
    setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
  }
  this.cdr.markForCheck();
}
```

**No refetch.** Tasks are consumed from in-memory array. When array is empty, chat opens.

---

## Why No Refetch After Skip?

**Design choice:** Skipping a task doesn't change backend state immediately.

- **Daily Pulse Skip:** Pulse cycle remains open. User can still answer later via home page.
- **Weekly Coaching Skip:** `CoachingSummary.DismissedAt` is updated, but we don't refetch because the skip action removes it from local state anyway.

**Future improvement:** If we add "Remind me later" (4h snooze), we'd need to refetch or update local state with a delay timestamp.

---

## Edge Cases

### What if user completes a task outside the assistant?

Example: User dismisses coaching summary via a separate card on home page.

**Current behavior:** Orb badge remains until page refresh. Local state is stale.

**Fix (not implemented):** Add a shared state service (RxJS BehaviorSubject) that both components subscribe to. When coaching-card dismisses, it broadcasts event → assistant component updates local state.

### What if multiple users share a device?

**No issue.** `/me/pending-tasks` is user-scoped (JWT userId). Each user sees their own tasks.

### What if backend returns tasks out of order?

**Impossible.** Backend explicitly sorts by priority before returning. Frontend trusts the sort order.

---

## SQL Queries (Performance Notes)

All 3 task checks run **sequentially** (not parallel). This is fine because:
1. Each query is indexed:
   - `Matches` has index on `(UserAId, UserBId, BalloonState)`
   - `CoachingSummary` has index on `(UserId, DeliveredAt, DismissedAt)`
   - `UserDynamicIntakeSets` has index on `(UserId, CycleEndUtc, AnsweredAtUtc)`
2. Each query returns at most 1 row (or a boolean check)
3. Total latency: ~15–30ms

**Future optimization:** If we add more task types (e.g., `unread_date_ideas`, `profile_incomplete`), consider running checks in parallel via `Task.WhenAll`.

---

## Adding a New Task Type

To add a 4th task type (e.g., `profile_incomplete`):

1. **Backend (`InteractionEndpoints.cs`):**
   - Add detection query after Pulse check
   - Assign priority (1–4)
   - Add to tasks list

2. **Frontend (`woven-assistant.component.ts`):**
   - Add case to `showPendingTask()` switch
   - Add case to `updateGlowState()` switch (if new glow color needed)
   - Import modal component

3. **Service (`pending-task.service.ts`):**
   - Add type to `PendingTaskType` union
   - Add data shape to `PendingTask` interface

4. **Logging (`signal-logging.md`):**
   - Add new event types: `ProfileIncompleteShown`, `ProfileIncompleteCompleted`, etc.

---

## Testing Checklist

- [ ] User with Date Feedback pending sees red glow, badge "1"
- [ ] User with Coaching + Pulse pending sees gold glow, badge "2"
- [ ] User with no pending tasks sees no badge, no glow
- [ ] Skipping Pulse shows next task (if any) or opens chat
- [ ] Completing Pulse shows next task (if any) or opens chat
- [ ] Tapping orb with 0 pending tasks opens chat directly
- [ ] Backend sorts tasks by priority (Date Feedback > Coaching > Pulse)

---

## Related Docs

- [badge-glow.md](./badge-glow.md) — How glow state is calculated
- [auto-advance.md](./auto-advance.md) — How tasks are consumed
- [backend.md](./backend.md) — Backend endpoint details
