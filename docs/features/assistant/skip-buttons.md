# Skip Buttons

**Last Updated:** 2026-08-17

---

## Overview

Two task types support **Skip** functionality:
1. **Daily Pulse** — User can skip without answering
2. **Weekly Coaching** — User can skip (dismisses + disables future coaching)

**Date Feedback** has no skip option (intentional — ECHO needs post-match data).

---

## Daily Pulse Skip

### UI

File: `frontend/woven-frontend/src/app/pages/home/pulse-sheet.component.ts`

```html
<div class="actions">
  <button class="btn ghost" (click)="skip()">Skip</button>
  <button class="btn primary" [disabled]="readonly || !complete" (click)="save()">
    Save
  </button>
</div>
```

**Button style:**
- `ghost` class = transparent background, border, dim text
- Left-aligned (secondary action)
- Always enabled (even if no answers selected)

### Behavior

```typescript
// In pulse-sheet.component.ts
skip() {
  this.skipped.emit();
}
```

```typescript
// In woven-assistant.component.ts
async onPulseSkipped() {
  this.showingPulse = false;
  await this.pendingTask.logInteraction('DailyPulseSkipped', {
    nextAction: this.pendingTasks.length > 1 
      ? 'opened_next_pending' 
      : 'opened_assistant'
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

### What Happens

1. Pulse modal closes (`showingPulse = false`)
2. **No backend state change** — Pulse cycle remains open, unanswered
3. Event logged: `DailyPulseSkipped` with context
4. Auto-advance to next pending task or chat

### Backend State

**Before skip:**
```sql
SELECT * FROM user_dynamic_intake_sets WHERE user_id = 123;
```
```
| id | user_id | cycle_id | answered_at_utc |
|----|---------|----------|-----------------|
| 1  | 123     | xyz      | NULL            |
```

**After skip:**
```
No change. Row remains.
```

User can still answer the pulse later via home page or next assistant open (pulse will reappear as pending).

### UX Rationale

**Why allow skip?**
- Pulse is routine, low-priority
- Forced completion = survey fatigue
- Better to capture willing responses than coerced ones

**Signal value:**
- Skip events logged → ECHO learns which users skip frequently
- Can adjust pulse frequency or question types for chronic skippers

---

## Weekly Coaching Skip

### UI

File: `frontend/woven-frontend/src/app/components/coaching-card/coaching-card.component.ts`

```html
<div class="actions">
  <button class="gotIt" (click)="dismiss()" [disabled]="busy">
    {{ busy ? '…' : 'Got it' }}
  </button>
  <button class="skip" (click)="skip()" [disabled]="busy">
    Skip
  </button>
</div>

<div class="confirming" *ngIf="confirming">
  Weekly reflections turned off. You can re-enable in Settings.
</div>
```

**Button style:**
- Border, transparent background, dim text
- Bottom-aligned (secondary action)
- Disabled during API call (`busy = true`)

### Behavior

```typescript
// In coaching-card.component.ts
async skip() {
  this.busy = true;
  try {
    await firstValueFrom(this.coaching.skipCoaching(this.summary!.id));
    this.confirming = true;
    setTimeout(() => this.skipped.emit(), 1800);
  } finally {
    this.busy = false;
  }
}
```

```typescript
// In woven-assistant.component.ts
async onCoachingSkipped() {
  this.showingCoaching = false;
  await this.pendingTask.logInteraction('WeeklyCoachingSkipped', {
    summaryId: this.coachingSummary?.id,
    nextAction: this.pendingTasks.length > 1 
      ? 'opened_next_pending' 
      : 'opened_assistant'
  }).toPromise();
  await this.advanceToNextPendingOrChat();
}
```

### What Happens

1. **API call:** `POST /coaching/skip/{summaryId}`
2. Backend updates:
   - `CoachingSummary.DismissedAt = NOW()` (marks as dismissed)
   - `User.CoachingDisabled = true` (disables future coaching)
3. Confirmation message shows: "Weekly reflections turned off…"
4. After 1.8s delay, modal closes + auto-advance
5. Event logged: `WeeklyCoachingSkipped` with context

### Backend State Change

**Before skip:**
```sql
SELECT * FROM coaching_summaries WHERE id = 42;
```
```
| id | user_id | delivered_at        | dismissed_at | summary_text          |
|----|---------|---------------------|--------------|----------------------|
| 42 | 123     | 2026-08-17 08:00:00 | NULL         | "This week you..."   |
```

**After skip:**
```
| id | user_id | delivered_at        | dismissed_at        | summary_text          |
|----|---------|---------------------|---------------------|-----------------------|
| 42 | 123     | 2026-08-17 08:00:00 | 2026-08-17 14:32:15 | "This week you..."    |
```

```sql
SELECT coaching_disabled FROM users WHERE id = 123;
```
```
| coaching_disabled |
|-------------------|
| true              |
```

### Re-Enabling Coaching

User can turn coaching back on via:
1. **Settings page** → Toggle "Weekly Reflections"
2. Backend: `PATCH /me/settings` → `User.CoachingDisabled = false`

**Next coaching summary:**
- `CoachingSummaryWorker` runs every Sunday 04:00 UTC
- Checks `User.CoachingDisabled` before creating summary
- If `false`, new summary delivered

### UX Rationale

**Why permanent disable (not just dismiss)?**

Original design had 2 buttons:
- "Dismiss" — Hide this summary only
- "Turn off coaching" — Disable all future summaries

**Problem:** Two buttons = decision fatigue. Users hesitated.

**Solution:** Merge into one "Skip" button that does both:
- Dismisses current summary
- Disables future summaries
- Shows confirmation message with re-enable path

**Why this works:**
- Users who skip coaching likely don't want it
- Confirmation message explains what happened
- Re-enable path is clear ("Settings")
- Reduces UI clutter (1 button instead of 2)

**Signal value:**
- Skip events logged → ECHO learns coaching acceptance rate
- Can improve summary quality or targeting based on skip patterns

---

## Date Feedback — No Skip

### Why?

**Date Feedback is the most critical ECHO signal.**

After a match closes (balloon pops or expires), we need to know:
- Did they meet in person?
- How did the date go? (1–5 stars)
- What felt right? What felt off?
- Would they meet again?

This data trains:
- `WeightLearningService` — Adjusts match scoring weights
- `FeedbackInsightService` — Generates user-specific coaching
- `MatchExplanationService` — Improves future match narratives

**Skip would mean:**
- Loss of ground-truth outcome data
- ECHO can't learn from that match
- User's future matches suffer (less accurate)

### Enforcement

File: `frontend/woven-frontend/src/app/pages/feedback/feedback.page.ts` (separate route, not in assistant yet)

**No skip button in UI.** Only options:
- Fill out feedback + submit
- Close modal (but task remains pending)

**Backend enforcement:**
```csharp
var hasFeedbackPending = await db.Matches
    .Where(m => (m.UserAId == userId || m.UserBId == userId)
        && m.BalloonState == BalloonState.CLOSED
        && !db.Set<DateFeedback>()
            .Any(f => f.MatchId == m.Id && f.UserId == userId))
    .AnyAsync(ct);
```

Feedback task reappears every time `/me/pending-tasks` is called until `DateFeedback` row exists.

### UX Trade-off

**Con:** Forced action = user friction.  
**Pro:** Data quality > convenience for critical signals.

**Mitigation:**
- Feedback form is short (~30 seconds)
- Only appears after match closes (not mid-conversation)
- Red glow = clear priority signal
- Can't be buried under other tasks (priority 1)

### Future Improvement

**Option:** Add "Didn't meet" quick-dismiss button.

If user selects:
- `MetInPerson = false`
- `Stars = null`
- `FeltRightText = null`
- `FeltOffText = null`
- `MeetAgain = null`

Still captures **outcome** (no date happened) without full feedback burden.

**Not implemented yet.** Needs product decision.

---

## Interaction Logging

All skip actions are logged for ECHO analysis.

### DailyPulseSkipped

```json
{
  "eventType": "DailyPulseSkipped",
  "context": {
    "nextAction": "opened_next_pending" // or "opened_assistant"
  }
}
```

**Logged to:** `user_interaction_logs` table  
**ECHO use:** Survey fatigue analysis, pulse frequency optimization

### WeeklyCoachingSkipped

```json
{
  "eventType": "WeeklyCoachingSkipped",
  "context": {
    "summaryId": 42,
    "nextAction": "opened_next_pending" // or "opened_assistant"
  }
}
```

**Logged to:** `user_interaction_logs` table  
**ECHO use:** Coaching acceptance rate, content quality analysis

See [signal-logging.md](./signal-logging.md) for full event catalog.

---

## Auto-Advance After Skip

Both Pulse and Coaching skip trigger the same auto-advance flow:

```typescript
async advanceToNextPendingOrChat() {
  this.pendingTasks.shift();  // Remove completed/skipped task
  this.badgeCount = this.pendingTasks.length;
  this.updateGlowState();

  if (this.pendingTasks.length > 0) {
    const next = this.pendingTasks[0];
    await this.showPendingTask(next);
  } else {
    // No more pending - show chat
    setTimeout(() => this.inputEl?.nativeElement.focus(), 350);
  }
  this.cdr.markForCheck();
}
```

See [auto-advance.md](./auto-advance.md) for full details.

---

## Skip vs Dismiss Terminology

### Skip
**Used for:** Pulse, Coaching  
**Meaning:** "I don't want to engage with this task right now (or ever)."  
**Effect:** Task removed from queue, may or may not affect backend state.

### Dismiss
**Used for:** Coaching (in code, but labeled "Got it" in UI)  
**Meaning:** "I read this, I'm done with it."  
**Effect:** Task removed from queue, backend marks as dismissed.

### Close
**Used for:** Chat, Modals  
**Meaning:** "Hide this UI, but don't change task state."  
**Effect:** Modal closes, task remains pending (unless explicitly completed/skipped).

**User-facing labels:**
- Pulse: "Skip" button
- Coaching: "Skip" button (even though it's also a dismiss)
- Chat: "×" close button

Avoid "Dismiss" in UI — too formal, not conversational.

---

## Testing Checklist

### Daily Pulse Skip
- [ ] Skip button always enabled (even if no answers selected)
- [ ] Skip logs `DailyPulseSkipped` event
- [ ] Skip does NOT update `UserDynamicIntakeSet.AnsweredAtUtc`
- [ ] After skip, pulse reappears on next assistant open (if cycle still active)
- [ ] Auto-advances to next pending task after skip
- [ ] Auto-opens chat if no more pending tasks

### Weekly Coaching Skip
- [ ] Skip button disabled during API call (`busy = true`)
- [ ] Skip logs `WeeklyCoachingSkipped` event
- [ ] Skip updates `CoachingSummary.DismissedAt`
- [ ] Skip sets `User.CoachingDisabled = true`
- [ ] Confirmation message shows after skip
- [ ] Modal closes after 1.8s
- [ ] Auto-advances to next pending task after skip
- [ ] User can re-enable coaching via Settings

### Date Feedback (No Skip)
- [ ] No skip button visible in feedback modal
- [ ] Closing modal without submitting leaves task pending
- [ ] Feedback task reappears on next assistant open
- [ ] Red glow persists until feedback submitted

---

## Related Docs

- [auto-advance.md](./auto-advance.md) — What happens after skip
- [signal-logging.md](./signal-logging.md) — Skip event logging
- [pending-tasks.md](./pending-tasks.md) — How tasks are detected
- [frontend.md](./frontend.md) — Component implementation
