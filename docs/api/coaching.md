# Coaching API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Weekly coaching summaries provide personalized behavioral insights and suggestions. Delivered Sunday nights, users can dismiss or opt out.

**Key Concepts:**
- **Coaching Summary** — AI-generated weekly insight
- **Delivery** — Sunday nights via background worker
- **Opt-out** — Users can disable coaching summaries

**Source:** [`backend/WovenBackend/Endpoints/CoachingEndpoints.cs`](../../backend/WovenBackend/Endpoints/CoachingEndpoints.cs)

---

## Endpoints

### GET /coaching/current-summary

**Description:** Fetch latest unread coaching summary (if available).

**Authentication:** Required (JWT)

**Response (200 OK) — Summary Available:**
```json
{
  "id": 12345,
  "summaryText": "This week you connected well with profiles that emphasize outdoor activities. Consider exploring more conversations about weekend plans—your best exchanges started there.",
  "deliveredAt": "2026-10-06T03:00:00Z",
  "weekStart": "2026-09-30"
}
```

**Response (204 No Content) — No Summary Available**

**Conditions for 204:**
- No coaching summaries exist
- Latest summary already dismissed
- User opted out

**Source:** [`CoachingEndpoints.cs:16-37`](../../backend/WovenBackend/Endpoints/CoachingEndpoints.cs)

---

### POST /coaching/{id}/dismiss

**Description:** Mark a coaching summary as read/dismissed.

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "dismissed": true
}
```

**Errors:**
- `404` — Summary not found or does not belong to user

**Side Effects:**
- Sets `dismissed_at` timestamp
- Removes from "unread" summaries

**Source:** [`CoachingEndpoints.cs:40-56`](../../backend/WovenBackend/Endpoints/CoachingEndpoints.cs)

---

### POST /coaching/opt-out

**Description:** Disable all future coaching summaries.

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "optedOut": true
}
```

**Side Effects:**
- Sets `coaching_opted_out = true` on user record
- Dismisses all unread summaries (sets `opted_out_at` + `dismissed_at`)
- Prevents future summary generation

**Source:** [`CoachingEndpoints.cs:59-79`](../../backend/WovenBackend/Endpoints/CoachingEndpoints.cs)

---

### POST /coaching/opt-in

**Description:** Re-enable coaching summaries after opt-out.

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "optedIn": true
}
```

**Side Effects:**
- Sets `coaching_opted_out = false`
- User will receive next week's summary

**Source:** [`CoachingEndpoints.cs:82-94`](../../backend/WovenBackend/Endpoints/CoachingEndpoints.cs)

---

## Coaching Summary Generation

**Schedule:** Sunday nights, 03:00 UTC

**Worker:** `CoachingSummaryWorker.cs`

**Generation Logic:**
1. Fetch user's week of activity (moments, chats, match outcomes)
2. Analyze patterns via OpenAI (behavioral trends, conversation quality)
3. Generate personalized 1-2 sentence summary
4. Store in `coaching_summaries` table

**Example Summaries:**
- "This week you connected well with profiles that emphasize outdoor activities. Consider exploring more conversations about weekend plans—your best exchanges started there."
- "You've been matching with highly intentional people. Your reflective answers resonate most with users who value deep communication."
- "Your response rate improved by 30% this week. The common thread: candidates who shared their vulnerabilities early."

**Source:** [`CoachingSummaryWorker.cs`](../../backend/WovenBackend/Services/Coaching/CoachingSummaryWorker.cs)

---

## Database Schema

**Table:** `coaching_summaries`

**Columns:**
- `id` — Primary key (bigint)
- `user_id` — Foreign key to users
- `week_start_date` — Monday of the week being summarized
- `summary_text` — Generated coaching insight
- `delivered_at` — When summary was generated
- `dismissed_at` — When user dismissed (nullable)
- `opted_out_at` — Set if user opted out while unread (nullable)

**User Flag:**
- `users.coaching_opted_out` — Boolean opt-out flag

---

## Opt-Out Behavior

**When user opts out:**
1. All unread summaries marked as `opted_out_at` + `dismissed_at`
2. Worker skips opted-out users in future runs
3. User can re-enable via `/coaching/opt-in`

**Implementation:**
```csharp
await db.Users
    .Where(u => u.Id == userId)
    .ExecuteUpdateAsync(s => s.SetProperty(u => u.CoachingOptedOut, true), ct);

await db.CoachingSummaries
    .Where(c => c.UserId == userId && c.DismissedAt == null)
    .ExecuteUpdateAsync(s =>
        s.SetProperty(c => c.OptedOutAt, now)
         .SetProperty(c => c.DismissedAt, now), ct);
```

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/CoachingEndpoints.cs`
- `backend/WovenBackend/Services/Coaching/CoachingSummaryWorker.cs`
- `backend/WovenBackend/Data/Entities/CoachingSummary.cs`
