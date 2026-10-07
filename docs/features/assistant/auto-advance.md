# Auto-Advance Logic

**Last Updated:** 2026-08-17

---

## Overview

When a user completes or skips a pending task, the assistant automatically:
1. Removes the task from local queue
2. Shows the next pending task (if any)
3. Opens the chat interface (if no more tasks)

**No manual navigation.** The assistant guides users through all pending work before allowing free-form chat.

---

## The Core Function

File: `frontend/woven-frontend/src/app/components/woven-assistant/woven-assistant.component.ts`

```typescript
async advanceToNextPendingOrChat() {
  // Remove the completed/skipped task
  this.pendingTasks.shift();  // ← Pop first element from array
  this.badgeCount = this.pendingTasks.length;
  this.updateGlowState();

  if (this.pendingTasks.length > 0) {
    // Show next pending task
    const next = this.pendingTasks[0];
    await this.showPendingTask(next);
  } else {
    // No more pending - show chat
    if (this.isBrowser) {
      setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
    }
  }
  this.cdr.markForCheck();
}
```

### Step-by-Step Breakdown

#### 1. Remove Completed Task

```typescript
this.pendingTasks.shift();
```

**What this does:**
- Removes **first element** from `pendingTasks` array
- Returns the removed element (not used)
- Modifies array in-place

**Why `.shift()` (not `.splice()` or `.filter()`):**
- Tasks are **already sorted by priority** (backend does this)
- First task in array = highest priority = the one we just showed
- `.shift()` is O(1) for first element removal

**Example:**

Before:
```typescript
pendingTasks = [
  { type: 'weekly_coaching', priority: 2, data: {...} },
  { type: 'daily_pulse', priority: 3, data: {...} }
]
```

After `.shift()`:
```typescript
pendingTasks = [
  { type: 'daily_pulse', priority: 3, data: {...} }
]
```

#### 2. Update Badge Count

```typescript
this.badgeCount = this.pendingTasks.length;
```

**Before:** Badge shows "2"  
**After:** Badge shows "1"

If array becomes empty, `badgeCount = 0` → badge hidden via `*ngIf`.

#### 3. Update Glow State

```typescript
this.updateGlowState();
```

Recalculates glow color based on **new first task** in array.

```typescript
private updateGlowState() {
  if (this.pendingTasks.length === 0) {
    this.glowState = 'none';
    return;
  }

  const highest = this.pendingTasks[0];
  switch (highest.type) {
    case 'date_feedback': this.glowState = 'red'; break;
    case 'weekly_coaching': this.glowState = 'gold'; break;
    case 'daily_pulse': this.glowState = 'white'; break;
    default: this.glowState = 'none';
  }
}
```

**Example (continuing from above):**

Before: `glowState = 'gold'` (coaching was first)  
After: `glowState = 'white'` (pulse is now first)

#### 4. Branch: Next Task or Chat?

```typescript
if (this.pendingTasks.length > 0) {
  const next = this.pendingTasks[0];
  await this.showPendingTask(next);
} else {
  // No more pending - show chat
  setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
}
```

**Path A:** More tasks remain  
→ Call `showPendingTask(next)` with new first element

**Path B:** Array is empty  
→ Chat interface is already visible (modals closed)  
→ Focus input field after 350ms (allows slide-in animation to finish)

#### 5. Trigger Change Detection

```typescript
this.cdr.markForCheck();
```

Component uses `OnPush` change detection → manual trigger required after async state changes.

---

## Show Pending Task Logic

```typescript
async showPendingTask(task: PendingTask) {
  if (task.type === 'daily_pulse') {
    this.showingPulse = true;
    this.pulseState = { cycleId: task.data.cycleId };
  } else if (task.type === 'weekly_coaching') {
    this.showingCoaching = true;
    this.coachingSummary = {
      id: task.data.summaryId,
      summaryText: task.data.summaryText
    };
  }
  // date_feedback not yet implemented in assistant flow
}
```

**Sets modal state:**
- `showingPulse = true` → `<app-pulse-sheet>` renders
- `showingCoaching = true` → `<woven-coaching-card>` renders

**No chat sheet visible** while modal is open (controlled by template logic):

```html
<div class="sheet" [class.visible]="isOpen && !showingPulse && !showingCoaching">
```

---

## Call Sites

`advanceToNextPendingOrChat()` is called from **4 event handlers**:

### 1. Pulse Saved

```typescript
async onPulseSaved(answers: any) {
  this.showingPulse = false;
  await this.pendingTask.logInteraction('DailyPulseCompleted', { answers }).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

**Trigger:** User fills out pulse + clicks "Save"  
**Before advance:** Log completion event  
**After advance:** Show next task or chat

### 2. Pulse Skipped

```typescript
async onPulseSkipped() {
  this.showingPulse = false;
  await this.pendingTask.logInteraction('DailyPulseSkipped', {
    nextAction: this.pendingTasks.length > 1 ? 'opened_next_pending' : 'opened_assistant'
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

**Trigger:** User clicks "Skip" on pulse  
**Before advance:** Log skip event with context (`nextAction`)  
**After advance:** Show next task or chat

### 3. Coaching Dismissed

```typescript
async onCoachingDismissed() {
  this.showingCoaching = false;
  await this.pendingTask.logInteraction('WeeklyCoachingDismissed', {
    summaryId: this.coachingSummary?.id
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

**Trigger:** User clicks "Got it" on coaching card  
**Before advance:** Log dismissal event  
**After advance:** Show next task or chat

### 4. Coaching Skipped

```typescript
async onCoachingSkipped() {
  this.showingCoaching = false;
  await this.pendingTask.logInteraction('WeeklyCoachingSkipped', {
    summaryId: this.coachingSummary?.id,
    nextAction: this.pendingTasks.length > 1 ? 'opened_next_pending' : 'opened_assistant'
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

**Trigger:** User clicks "Skip" on coaching card  
**Before advance:** Log skip event  
**After advance:** Show next task or chat

---

## Timing & Animations

### Modal Close → Next Modal Open

**Sequence:**
1. User clicks "Save" on Pulse
2. `showingPulse = false` → Pulse modal starts exit animation
3. `advanceToNextPendingOrChat()` called **immediately** (not after animation)
4. `showingCoaching = true` → Coaching modal starts enter animation

**Visual effect:**
- Pulse modal slides down (250ms CSS transition)
- Coaching modal slides up (300ms CSS transition)
- **Overlap period:** ~50ms where both are animating

**Why this works:**
- Modals have `position: fixed`, don't conflict spatially
- Exit animation keeps old modal visible briefly
- Enter animation starts from off-screen, smooth handoff

### Last Task → Chat Open

**Sequence:**
1. User completes/skips final task
2. Modal closes (`showingPulse/Coaching = false`)
3. `advanceToNextPendingOrChat()` sets `pendingTasks = []`
4. Chat sheet already visible (template condition met)
5. After 350ms, input field focused

**Why 350ms delay?**
- Allows chat sheet slide-in animation to finish
- Keyboard appears smoothly (no layout shift)
- Feels intentional, not jarring

---

## Edge Cases

### User Closes Modal Without Completing/Skipping

**Example:** User clicks backdrop on Pulse modal.

```typescript
// In pulse-sheet.component.ts
close() {
  this.closed.emit();
}

// In woven-assistant.component.ts
onPulseClosed() {
  this.showingPulse = false;
  this.close();  // ← Closes entire assistant
}
```

**Effect:**
- Pulse modal closes
- **Entire assistant closes** (orb minimizes)
- **Task NOT removed** from `pendingTasks`
- Next time user opens orb, same task appears

**Why this is correct:**
- Closing ≠ skipping
- User explicitly closed assistant (intent signal)
- Task remains pending until action taken

### Multiple Rapid Taps During Animation

**Scenario:** User spam-clicks "Skip" button.

**Protection:**
```typescript
@Output() skipped = new EventEmitter<void>();

skip() {
  this.skipped.emit();  // Only emits once per click
}
```

**In parent:**
```typescript
busy = false;  // Not used in assistant component (should be added)

async onPulseSkipped() {
  // No busy guard currently - potential issue
  this.showingPulse = false;
  await this.pendingTask.logInteraction(...).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

**Current issue:** No `busy` flag in assistant component → rapid clicks could cause:
- Multiple log events
- Race condition in `advanceToNextPendingOrChat()`

**Fix (not implemented):**
```typescript
private advanceBusy = false;

async advanceToNextPendingOrChat() {
  if (this.advanceBusy) return;
  this.advanceBusy = true;
  try {
    // ... existing logic
  } finally {
    this.advanceBusy = false;
  }
}
```

### Backend Creates New Task During Session

**Scenario:** User has Pulse pending. While completing it, `CoachingSummaryWorker` delivers new coaching summary.

**Current behavior:**
- Frontend `pendingTasks` array is static (fetched once on init)
- New task doesn't appear until page refresh

**Why this is okay:**
- Coaching worker runs once/week (Sunday 04:00 UTC)
- Unlikely to coincide with active assistant session
- Worst case: User sees new task on next orb tap (after refresh)

**Future improvement:**
- WebSocket listener pushes new tasks to client
- `PendingTaskService` exposes `Observable<PendingTask[]>`
- Assistant component subscribes, updates local state

---

## State Machine View

```
┌──────────────┐
│  Orb Tapped  │
└──────┬───────┘
       │
       ▼
┌─────────────────────┐
│ Pending tasks > 0?  │
└──────┬──────────────┘
       │
       ├─ YES ──→ Show first task modal
       │          │
       │          ▼
       │     ┌────────────────────┐
       │     │  User completes    │
       │     │  or skips task     │
       │     └────────┬───────────┘
       │              │
       │              ▼
       │     ┌────────────────────────┐
       │     │ advanceToNextPending   │
       │     │ OrChat()               │
       │     └────────┬───────────────┘
       │              │
       │              ▼
       │     Remove task from array
       │              │
       │              ▼
       │     Update badge + glow
       │              │
       │              ▼
       │     ┌────────────────────┐
       │     │ More tasks pending?│
       │     └────────┬───────────┘
       │              │
       │              ├─ YES ──→ Show next task modal
       │              │          (loop back up)
       │              │
       │              └─ NO ───→ Open chat sheet
       │
       └─ NO ──→ Open chat sheet directly
```

---

## Accessibility Notes

### Focus Management

**After final task completion:**
```typescript
setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
```

**Effect:**
- Screen reader announces: "Text input, Ask anything…"
- Keyboard users can immediately type
- No manual focus hunting

**Why 350ms:**
- Allows animation to finish
- Mobile keyboard appears smoothly
- Tested on iOS VoiceOver + Android TalkBack

### No Announcement on Task Advance

**Current behavior:**
- Badge count decrements (visual only)
- Glow changes (visual only)
- Next modal appears (announced by screen reader when rendered)

**Missing:** No `aria-live` announcement like "1 task remaining" or "Opening coaching summary."

**Future improvement:**
```html
<div aria-live="polite" aria-atomic="true" class="sr-only">
  {{ srAnnouncement }}
</div>
```

```typescript
private updateSrAnnouncement() {
  if (this.pendingTasks.length === 0) {
    this.srAnnouncement = 'All tasks complete. Opening chat.';
  } else {
    const next = this.pendingTasks[0];
    this.srAnnouncement = `${this.pendingTasks.length} task${this.pendingTasks.length > 1 ? 's' : ''} remaining. Opening ${next.type.replace('_', ' ')}.`;
  }
}
```

---

## Performance Notes

### Array Mutation

`.shift()` modifies array in-place → O(n) worst-case (reindexes all elements).

**In practice:**
- Max array length = 3 (Date Feedback + Coaching + Pulse)
- O(3) = effectively O(1)
- No performance concern

### Change Detection

`cdr.markForCheck()` triggers:
- Template re-evaluation
- Glow state class binding update
- Badge count text update

**Cost:** Negligible. Component tree is shallow.

### Async Logging

```typescript
await this.pendingTask.logInteraction(...).toPromise();
```

**Blocks auto-advance** until log completes (~50–100ms).

**Why blocking is fine:**
- Ensures log captures `nextAction` context correctly
- User doesn't notice 50ms delay (animation takes 300ms)
- If log fails (network error), caught silently (try/catch in service)

---

## Testing Checklist

- [ ] Completing Pulse with 1 more task pending → shows next task
- [ ] Completing Pulse with 0 more tasks → opens chat, focuses input
- [ ] Skipping Coaching with 1 more task → shows next task
- [ ] Skipping Coaching with 0 more tasks → opens chat, focuses input
- [ ] Badge count decrements correctly (2→1→0)
- [ ] Glow state updates correctly (gold→white→none)
- [ ] Closing modal (not skip/complete) keeps task pending
- [ ] Re-opening orb after close shows same task again
- [ ] Chat input auto-focuses after last task (desktop + mobile)
- [ ] No visual jank during modal transitions
- [ ] Screen reader announces new modal when it appears

---

## Related Docs

- [pending-tasks.md](./pending-tasks.md) — How `pendingTasks` array is populated
- [skip-buttons.md](./skip-buttons.md) — What triggers auto-advance
- [badge-glow.md](./badge-glow.md) — How badge/glow are updated
- [signal-logging.md](./signal-logging.md) — Events logged before advance
- [frontend.md](./frontend.md) — Full component implementation
