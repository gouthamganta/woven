# Summary Delivery

How coaching summaries are delivered to users and when they disappear.

---

## Overview

Coaching summaries are **not push notifications**. They're discovered organically in the You tab.

**Delivery lifecycle:**
1. **Created** — Wednesday 18:00 UTC, `DeliveredAt` timestamp set
2. **Visible** — card appears in You tab (`DismissedAt == null`)
3. **Dismissed** — user taps "Got it", `DismissedAt` timestamp set
4. **Cleaned up** — deleted after 90 days (TTL)

---

## DeliveredAt

**Column:** `CoachingSummaries.DeliveredAt`  
**Type:** `timestamptz` (always UTC)  
**Set by:** `CoachingSummaryWorker` at creation time

### Naming Caveat

"DeliveredAt" is a misnomer — it's actually **"created at"**, not "when the user read it".

The column was named before delivery mechanics were finalized. It means:
- ✅ **When the summary was written to the database**
- ❌ **NOT when the user opened the You tab**
- ❌ **NOT when the user saw the card**

**Why it matters:**
- Frontend cannot infer "new" vs "seen" from this timestamp
- Card visibility is controlled by `DismissedAt` (null = visible)

**Possible future column:** `FirstSeenAt` (set on first GET request)

---

## Visibility Logic

**Backend:** `GET /coaching/current-summary`

```csharp
var summary = await db.CoachingSummaries
    .Where(c => c.UserId == userId && c.DismissedAt == null && c.OptedOutAt == null)
    .OrderByDescending(c => c.DeliveredAt)
    .FirstOrDefaultAsync(ct);

if (summary == null) return Results.NoContent();
```

**Frontend rendering (You tab):**

```typescript
async loadCoachingSummary() {
  try {
    const summary = await this.coachingService.getCurrent();
    this.coachingSummary = summary;
  } catch (err: any) {
    if (err.status === 204) {
      this.coachingSummary = null; // No summary available
    }
  }
}
```

**Visibility conditions:**
- `DismissedAt == null` (user hasn't dismissed it yet)
- `OptedOutAt == null` (not marked as part of opt-out batch dismissal)
- Most recent summary (in case of duplicates)

**State:**
- Visible → coaching card rendered in You tab
- Not visible → no card (204 No Content)

---

## Dismissal

### User-Initiated Dismissal

**Trigger:** User taps "Got it" or "Dismiss" on coaching card

**Frontend:**
```typescript
async dismissCoachingSummary() {
  if (!this.coachingSummary) return;
  await this.coachingService.dismiss(this.coachingSummary.id);
  this.coachingSummary = null;
  this.cdr.markForCheck();
}
```

**Backend:** `POST /coaching/{id}/dismiss`

```csharp
var summary = await db.CoachingSummaries
    .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

if (summary == null) return Results.NotFound();

summary.DismissedAt = DateTimeOffset.UtcNow;
await db.SaveChangesAsync(ct);
```

**Effect:**
- `DismissedAt` set to now
- Next `GET /coaching/current-summary` returns 204 (card disappears)
- User won't see this summary again

---

### Opt-Out Batch Dismissal

**Trigger:** User toggles "Coaching summaries" off in Settings

**Frontend:**
```typescript
await this.coachingService.optOut();
```

**Backend:** `POST /coaching/opt-out`

```csharp
var now = DateTimeOffset.UtcNow;

// Set user preference
await db.Users
    .Where(u => u.Id == userId)
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.CoachingOptedOut, true), ct);

// Dismiss all unread summaries
await db.CoachingSummaries
    .Where(c => c.UserId == userId && c.DismissedAt == null)
    .ExecuteUpdateAsync(s =>
        s.SetProperty(c => c.OptedOutAt, now)
         .SetProperty(c => c.DismissedAt, now), ct);
```

**Effect:**
- All unread summaries dismissed immediately
- Both `OptedOutAt` and `DismissedAt` set to now (audit trail)
- Future weekly runs skip this user (eligibility check fails)

**Distinguishing opt-out dismissals:**
- `OptedOutAt != null` → dismissed as part of opt-out
- `OptedOutAt == null` → user dismissed manually

---

## Opt-In

**Trigger:** User toggles "Coaching summaries" back on in Settings

**Frontend:**
```typescript
await this.coachingService.optIn();
```

**Backend:** `POST /coaching/opt-in`

```csharp
await db.Users
    .Where(u => u.Id == userId)
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.CoachingOptedOut, false), ct);
```

**Effect:**
- `User.CoachingOptedOut = false`
- Next Wednesday, user is eligible again
- Previously dismissed summaries remain dismissed (no resurrection)

---

## WeekStartDate

**Column:** `CoachingSummaries.WeekStartDate`  
**Type:** `DateOnly` (no time component)  
**Value:** Monday of the week the summary was generated

### Calculation

```csharp
var now = DateTimeOffset.UtcNow;
var weekStart = DateOnly.FromDateTime(now.DateTime).AddDays(-(int)now.DayOfWeek);
```

**Example:**
- Worker runs: **Wednesday 2026-10-07 18:00 UTC**
- `WeekStartDate`: **Monday 2026-10-05** (3 days prior)

**Why Monday:**
- Coaching covers signals from "the prior 7 days" (relative to Wednesday)
- Monday is the conventional ISO week start
- Allows easy grouping (`GROUP BY WeekStartDate`)

**Duplicate prevention:**
```sql
SELECT UserId FROM CoachingSummaries
WHERE WeekStartDate = @weekStart
```

If a user already has a summary for this `WeekStartDate`, they're skipped (prevents duplicate summaries if worker crashes and retries).

---

## TTL (90-Day Cleanup)

**When:** Runs in preamble of `CoachingSummaryWorker` (before batch processing)

```csharp
await db.CoachingSummaries
    .Where(c => c.CreatedAt < now.AddDays(-90))
    .ExecuteDeleteAsync(ct);
```

**Why 90 days:**
- Coaching is ephemeral (not archival)
- Reduces DB bloat
- Privacy-friendly (old behavioral insights deleted)

**Edge case:** User who hasn't opened app in >90 days won't see their old summaries (intentional).

---

## Delivery Timing

### Wednesday 18:00 UTC

**Why Wednesday:**
- Mid-week check-in (not Monday "start fresh" or Friday "week's over")
- Covers signals from prior 7 days (Thu → Wed)
- Matches ECHO's "trusted friend" persona (casual, not corporate)

**Why 18:00 UTC:**
- 11:00 AM PST / 2:00 PM EST / 7:00 PM BST / 11:30 PM IST
- Daytime in major markets (not middle of night)
- After `ConnectionScoreBatchWorker` (03:50 UTC) — fresh match scores available

**Delay from signals:**
- Signals: prior 7 days (Thu → Wed)
- Worker runs: Wednesday 18:00 UTC
- Delivery: ~30 seconds after worker starts (streaming write)

**User experience:**
- Summary appears Wednesday afternoon/evening
- Reflects the week they just lived (not stale)

---

## No Push Notifications

**Deliberate design choice:**
- No FCM push when summary created
- No email notification
- No in-app badge count

**Why:**
- Coaching is a **discovery** feature, not a **demand** feature
- Push notifications feel like pressure ("you should read this now")
- Organic discovery aligns with "trusted friend" tone

**How users find it:**
- Open You tab (any time after Wednesday 18:00)
- Card appears at top of tab
- Dismissed once, won't reappear until next Wednesday

---

## Frontend Display

**Location:** You tab (top of page, above profile tiles)

**Card design:**
- Subtle background (not attention-grabbing)
- No "NEW" badge (not urgent)
- Dismissible (one-tap "Got it")

**Example UI:**

```
┌─────────────────────────────────────────┐
│ 💬 Your Week                            │
│                                         │
│ You showed up this week — not just     │
│ scrolling, actually engaging. The fact │
│ that you're willing to send the first  │
│ message when something feels right     │
│ says a lot.                            │
│                                         │
│                          [Got it]       │
└─────────────────────────────────────────┘
```

**Interaction:**
- Tap "Got it" → card disappears
- Pull-to-refresh → card persists (until dismissed)
- Navigate away → card persists (until dismissed)

---

## Multiple Summaries

**Scenario:** Worker runs twice in one week (crash + retry)

**Prevention:**
```csharp
var alreadySummarized = await db.CoachingSummaries
    .Where(c => c.WeekStartDate == weekStart && activeUserIds.Contains(c.UserId))
    .Select(c => c.UserId)
    .ToListAsync(ct);

var toProcess = activeUserIds.Except(alreadySummarized).ToList();
```

**Behavior:**
- Only one summary per `(UserId, WeekStartDate)` tuple
- If duplicate exists, user is skipped

**Edge case (manually deleted rows):**
- If admin manually deletes a summary row, user becomes eligible again
- Worker will recreate summary (signals are still in `MatchSignalLogs`)

---

## Failure Modes

### OpenAI API Down

**Behavior:**
- Worker logs error: `[CoachingSummary] OpenAI call failed`
- No summary written for this user
- Worker continues to process other users
- Next Wednesday retries (no permanent data loss)

**User experience:**
- No coaching card this week
- Next week resumes normally

---

### User Deleted Account

**Current behavior:**
- `CoachingSummaries` rows orphaned (no FK cascade)
- Rows cleaned up by 90-day TTL

**Future migration (TODO):**
```sql
ALTER TABLE coaching_summaries
  ADD CONSTRAINT fk_user
  FOREIGN KEY (user_id) REFERENCES users(id)
  ON DELETE CASCADE;
```

---

### User Opts Out Mid-Batch

**Scenario:**
1. Worker starts processing (18:00 UTC)
2. User opts out (18:05 UTC)
3. Worker processes this user (18:10 UTC)

**Behavior:**
- Eligibility check runs at batch start (18:00)
- User was eligible at that time
- Summary is written (one last time)
- User sees it in You tab
- Next Wednesday, opt-out is respected

**Intentional:** One final summary isn't harmful (user can dismiss it).

---

## Analytics

**Useful queries:**

### Weekly delivery rate
```sql
SELECT
  week_start_date,
  COUNT(*) AS summaries_delivered,
  COUNT(DISTINCT user_id) AS unique_users
FROM coaching_summaries
GROUP BY week_start_date
ORDER BY week_start_date DESC;
```

### Dismissal latency
```sql
SELECT
  AVG(EXTRACT(EPOCH FROM (dismissed_at - delivered_at)) / 3600) AS avg_hours_to_dismiss
FROM coaching_summaries
WHERE dismissed_at IS NOT NULL;
```

### Opt-out batch dismissals
```sql
SELECT COUNT(*) AS opt_out_dismissals
FROM coaching_summaries
WHERE opted_out_at IS NOT NULL;
```

---

## See Also

- [summary-generation.md](./summary-generation.md) — How summaries are generated
- [workers.md](./workers.md) — CoachingSummaryWorker schedule
- [api.md](./api.md) — Coaching endpoints
