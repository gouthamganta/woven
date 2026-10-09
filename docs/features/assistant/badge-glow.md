# Badge Count & Glow States

**Last Updated:** 2026-08-17

---

## Overview

The floating orb uses two visual signals to communicate pending tasks:
1. **Badge count** — Numeric indicator (1, 2, 3…)
2. **Glow color** — Pulsing box-shadow animation (red, white, gold)

Both are derived from the `pendingTasks` array returned by `/me/pending-tasks`.

---

## Badge Count

### Logic

```typescript
this.badgeCount = this.pendingTasks.length;
```

**Simple.** Count of items in array.

### Display

```html
<span class="orbBadge" *ngIf="badgeCount > 0">{{ badgeCount }}</span>
```

**Position:** Top-right corner of orb  
**Style:**
- Red background (`#E74C3C`)
- White text
- 2px white border (visually separates from orb)
- Min-width 20px, height 20px
- Padding: 0 6px (accommodates double digits)

### Behavior

| Scenario | Badge Count |
|----------|-------------|
| 0 pending tasks | Badge hidden (`*ngIf` removes from DOM) |
| 1 pending task | Badge shows "1" |
| 3 pending tasks | Badge shows "3" |

**No upper limit.** If backend returns 10 tasks, badge shows "10". In practice, max is 3 (Date Feedback + Coaching + Pulse).

---

## Glow States

### Purpose

Badge count tells users **how many** tasks are pending. Glow color tells them **what type** (and implicitly, urgency).

### Color Mapping

| Glow State | Color Hex | Pulse Animation | Task Type |
|------------|-----------|-----------------|-----------|
| `'red'` | `#E74C3C` | `pulse-red` | `date_feedback` |
| `'gold'` | `var(--gold-400)` | `pulse-gold` | `weekly_coaching` |
| `'white'` | `#E8E4F3` | `pulse-white` | `daily_pulse` |
| `'none'` | N/A | Default `orbBreath` | No pending tasks |

### Logic

```typescript
private updateGlowState() {
  if (this.pendingTasks.length === 0) {
    this.glowState = 'none';
    return;
  }

  const highest = this.pendingTasks[0];
  switch (highest.type) {
    case 'date_feedback':
      this.glowState = 'red';
      break;
    case 'weekly_coaching':
      this.glowState = 'gold';
      break;
    case 'daily_pulse':
      this.glowState = 'white';
      break;
    default:
      this.glowState = 'none';
  }
}
```

**Key insight:** Glow is determined by **first task** in array (highest priority), not all tasks.

**Example:**
- User has Coaching (priority 2) + Pulse (priority 3) pending
- Backend returns `tasks = [coaching, pulse]` (sorted)
- Glow = **gold** (coaching color)
- Badge = **2**

---

## Animations

### Default Breathing (No Pending Tasks)

```css
@keyframes orbBreath {
  0%, 100% { 
    box-shadow: 
      0 4px 20px rgba(212,160,23,0.35), 
      0 0 16px rgba(212,160,23,0.25); 
  }
  50% { 
    box-shadow: 
      0 6px 28px rgba(212,160,23,0.45), 
      0 0 22px rgba(212,160,23,0.35); 
  }
}

.orbCore {
  animation: orbBreath 3s ease-in-out infinite;
}
```

**Effect:** Gentle gold pulsing. Subtle, ambient. "I'm here if you need me."

---

### Red Pulse (Date Feedback Pending)

```css
@keyframes pulse-red {
  0%, 100% { 
    box-shadow: 
      0 0 24px #E74C3C, 
      0 4px 20px rgba(231,76,60,0.5); 
  }
  50% { 
    box-shadow: 
      0 0 32px #E74C3C, 
      0 6px 28px rgba(231,76,60,0.7); 
  }
}

.orb.glow-red .orbCore {
  animation: pulse-red 2s ease-in-out infinite;
}
```

**Effect:** Urgent red glow. Faster cycle (2s vs 3s). Higher intensity. "Answer this now."

**Why red?**
- Date Feedback is time-sensitive (memory fades)
- Red = attention, urgency (universal color psychology)
- Highest priority task

---

### Gold Pulse (Weekly Coaching Pending)

```css
@keyframes pulse-gold {
  0%, 100% { 
    box-shadow: 
      0 0 28px var(--gold-400), 
      0 4px 20px rgba(212,160,23,0.6); 
  }
  50% { 
    box-shadow: 
      0 0 36px var(--gold-400), 
      0 6px 28px rgba(212,160,23,0.8); 
  }
}

.orb.glow-gold .orbCore {
  animation: pulse-gold 2s ease-in-out infinite;
}
```

**Effect:** Enhanced gold glow. Slightly brighter than default breathing. "Check this out."

**Why gold?**
- Coaching is premium/valuable content
- Gold = brand color, positive signal
- Not urgent, but important

---

### White Pulse (Daily Pulse Pending)

```css
@keyframes pulse-white {
  0%, 100% { 
    box-shadow: 
      0 0 24px #E8E4F3, 
      0 4px 20px rgba(232,228,243,0.4); 
  }
  50% { 
    box-shadow: 
      0 0 32px #E8E4F3, 
      0 6px 28px rgba(232,228,243,0.6); 
  }
}

.orb.glow-white .orbCore {
  animation: pulse-white 2s ease-in-out infinite;
}
```

**Effect:** Soft white/lavender glow. Gentle reminder. "When you have a sec."

**Why white?**
- Pulse is routine, low-urgency
- White = neutral, non-intrusive
- Lowest priority task

---

## Visual Hierarchy

From most to least visually intense:

1. **Red glow + badge** — Date Feedback (screams "urgent")
2. **Gold glow + badge** — Coaching (bright, attention-grabbing)
3. **White glow + badge** — Pulse (subtle, ambient)
4. **Default breathing, no badge** — No tasks (calm, available)

Users learn the color language through repeated exposure. No need to explain in UI.

---

## State Transitions

### Scenario: User with 2 pending tasks (Coaching + Pulse)

**Initial state:**
- `pendingTasks = [coaching, pulse]`
- `badgeCount = 2`
- `glowState = 'gold'` (coaching is first)

**User dismisses coaching:**
1. `advanceToNextPendingOrChat()` is called
2. `pendingTasks.shift()` → `pendingTasks = [pulse]`
3. `badgeCount = 1`
4. `updateGlowState()` recalculates → `glowState = 'white'`

**Visual change:**
- Badge drops from "2" to "1"
- Glow shifts from gold → white
- Animation smoothly transitions (CSS handles)

---

## Edge Cases

### Multiple Date Feedbacks Pending

**Can this happen?**  
Yes. User could have 2 closed balloons without feedback.

**Backend behavior:**  
`GET /me/pending-tasks` returns only the **most recent** closed match:

```csharp
.OrderByDescending(m => m.ClosedAt)
.FirstAsync(ct);
```

**Frontend behavior:**  
Badge shows "1" (only 1 task returned). After completing feedback, if user refetches, next feedback task appears.

**Current limitation:** User can't see full queue of feedback tasks. Shows one at a time.

---

### User Completes Task Outside Assistant

Example: User dismisses coaching card on home page (separate component).

**Current behavior:**
- Assistant's local state is stale
- Badge/glow don't update until page refresh

**Why this is okay (for now):**
- Most users tap the orb when they see a badge
- Page refresh is common (navigating to/from matches, chats)
- Shared state sync adds complexity without major UX gain

**Future fix:**
- Use RxJS `BehaviorSubject` in `PendingTaskService`
- Both assistant and home page components subscribe
- Any task completion broadcasts to all subscribers

---

## Accessibility

### Badge Count
- Not announced by screen readers (purely visual indicator)
- `title` attribute on orb provides text: `"3 pending"` or `"Ask Woven"`

### Glow States
- Color alone doesn't convey meaning (text fallback via `title`)
- Animations are CSS-only (no JS-driven changes that skip screen readers)

**Improvement opportunity:** Add `aria-live` region that announces task count changes.

---

## Performance Notes

### Animation Performance
All glow animations use `box-shadow` only (no `transform`, no `opacity` on orb itself).

**Why this matters:**
- `box-shadow` triggers **paint** (not layout/reflow)
- Animates smoothly at 60fps on mobile
- Doesn't cause janky scrolling on low-end devices

**Tested on:**
- iPhone 11 (iOS 17) — smooth
- Pixel 5 (Android 13) — smooth
- Budget Android (<2GB RAM) — no jank reported

### State Update Cost
`updateGlowState()` runs after:
- Initial fetch (`ngOnInit`)
- Each task advance (`advanceToNextPendingOrChat`)

**Cost:** O(1) switch statement. Negligible.

---

## Design Rationale

### Why glow instead of badge color change?

**Option A:** Change badge color (red/gold/white)  
**Option B:** Keep badge red, change orb glow  

**Chose B because:**
- Red badge is universal "notification" signal (iOS/Android pattern)
- Changing badge color breaks learned pattern
- Glow adds dimension without replacing familiar UI

### Why not show task names in badge tooltip?

**Current:** `title="3 pending"`  
**Alternative:** `title="Date Feedback, Coaching, Pulse"`

**Why we don't:**
- Tooltip becomes long, cluttered
- User doesn't need to know specifics before tapping
- Glow color already hints at priority
- Task names shown after tap (in modal)

---

## Testing Checklist

- [ ] Badge hidden when `pendingTasks.length === 0`
- [ ] Badge shows correct count (1, 2, 3)
- [ ] Red glow when Date Feedback is first task
- [ ] Gold glow when Coaching is first task (no Date Feedback)
- [ ] White glow when Pulse is first task (no higher-priority tasks)
- [ ] Glow transitions smoothly when task is completed/skipped
- [ ] Badge count decrements when task is completed/skipped
- [ ] Default breathing animation when no tasks pending
- [ ] Orb `title` attribute reflects badge count

---

## Related Docs

- [pending-tasks.md](./pending-tasks.md) — How `pendingTasks` array is populated
- [auto-advance.md](./auto-advance.md) — How badge/glow update after task completion
- [frontend.md](./frontend.md) — Component implementation details
