# Find Love Stage

**Last Updated:** 2026-08-17

## What It Is

Find Love is the final unlock stage where users can plan a real-world date. It includes AI-generated date ideas, venue recommendations, and availability sharing.

**Trigger conditions:**
1. **Path A (Natural):** Both users messaged → 5-minute reflection window → unlock
2. **Path B (Trial success):** Both users chose CONTINUE in trial → immediate unlock

**Database field:** `Match.FindLoveAt` (timestamp)

**Frontend check:** `findLoveAt != null && new Date(findLoveAt) <= now`

---

## How It Unlocks

### Path A: Natural Unlock (Reflection Window)

**What happens:**
1. User A sends first message in thread
2. User B sends first message in thread
3. Backend detects both have messaged → sets `Match.BothMessagedAt = now`
4. Backend sets `Match.FindLoveAt = now + 5 minutes`
5. Frontend shows countdown timer: "Find Love opens in 04:32"
6. After 5 minutes, Find Love UI appears

**Code reference:** `ChatEndpoints.cs:492-511`

```csharp
// Set BothMessagedAt + FindLoveAt ONCE when both users have messaged
if (match.BothMessagedAt == null)
{
    var otherHasMessaged = await db.ChatMessages.AsNoTracking()
        .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == otherUserId, ct);

    if (otherHasMessaged)
    {
        match.BothMessagedAt = now;

        // Reflection unlock after 5 minutes
        if (match.FindLoveAt == null)
            match.FindLoveAt = now.Add(ReflectionWindow);

        await db.SaveChangesAsync(ct);

        var otherUserIdForUnlock = match.UserAId == me ? match.UserBId : match.UserAId;
        _ = analytics.TrackAsync(me, null, AnalyticsEvents.FindLoveUnlocked, null);
        _ = analytics.TrackAsync(otherUserIdForUnlock, null, AnalyticsEvents.FindLoveUnlocked, null);
    }
}
```

**Reflection window duration:** `TimeSpan.FromMinutes(5)` (line 24)

**Why 5 minutes?**  
Prevents rushing into date planning. Gives users time to establish conversation flow.

---

### Path B: Trial Success (Immediate)

**What happens:**
1. Both users submit trial decision: CONTINUE
2. Backend sets `Match.IsTrial = false`, `Match.FindLoveAt = now` (no delay)
3. Frontend refreshes, shows Find Love UI immediately

**Code reference:** `ChatEndpoints.cs:719-730`

```csharp
if (match.UserADecision == "CONTINUE" && match.UserBDecision == "CONTINUE")
{
    match.IsTrial = false;
    match.FindLoveAt = now;
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { status = "MATCH_CONTINUES", findLoveAt: match.FindLoveAt });
}
```

**Why immediate?**  
Trial already provided 3 minutes of conversation. No need for additional reflection.

---

## Date Ideas (AI-Generated)

### Backend Generation

**When:** During match creation or explanation generation.

**Service:** `MatchExplanationService.cs` (enhanced June 3-4 session)

**Model:** `gpt-4.1-mini` via `OpenAiClient`

**Prompt:** Generates 3 date ideas based on:
- Both users' AI Profiles (8 pillars)
- Shared interests (from Commons tiles)
- Location data (if available)

**Storage:** `MatchExplanation` entity

**Fields:**
- `DateIdea` (string) — legacy single idea
- `DateIdeasJson` (jsonb) — array of 3 ideas

**Example:**
```json
{
  "dateIdeas": [
    "Sunset hike at Nandi Hills → chai at a roadside stall",
    "Pottery workshop in Indiranagar",
    "Stand-up comedy show at The Humming Tree"
  ]
}
```

**Code reference:** `MatchExplanationService.cs` (implementation details in June 3 session notes)

---

### Frontend Reveal (10-Minute Delay)

**Why delayed?**  
Prevents users from opening Find Love → immediately seeing date ideas → skipping conversation. Forces authentic chat first.

**How it works:**
1. Find Love unlocks → `findLoveAt <= now`
2. Frontend stores `localStorage.setItem('woven:dateIdeaUnlockAt:{threadId}', now)`
3. Timer runs client-side: `now - startTime < 10 minutes` → hide ideas
4. After 10 minutes → reveal ideas

**Code reference:** `chat-thread.component.ts:224-271`

```typescript
private storageKeyForIdea(threadId: string) {
  return `woven:dateIdeaUnlockAt:${threadId}`;
}

private markDateIdeaUnlockStart(threadId: string) {
  if (typeof window === 'undefined') return;
  const key = this.storageKeyForIdea(threadId);
  const existing = window.localStorage.getItem(key);
  if (existing) return;
  window.localStorage.setItem(key, String(this.now));
}

shouldRevealDateIdea(): boolean {
  const d = this.data;
  if (!d) return false;
  if (!this.isFindLoveReady()) return false;

  this.markDateIdeaUnlockStart(d.threadId);
  const startedAt = this.getIdeaUnlockStart(d.threadId);
  if (!startedAt) return false;

  return this.now - startedAt >= this.dateIdeaDelayMs;
}

dateIdeaCountdown(): string {
  const d = this.data;
  if (!d) return '';
  this.markDateIdeaUnlockStart(d.threadId);

  const startedAt = this.getIdeaUnlockStart(d.threadId) ?? this.now;
  const left = Math.max(0, this.dateIdeaDelayMs - (this.now - startedAt));
  const s = Math.ceil(left / 1000);
  const mm = Math.floor(s / 60);
  const ss = s % 60;
  return `${mm.toString().padStart(2, '0')}:${ss.toString().padStart(2, '0')}`;
}
```

**Delay constant:** `dateIdeaDelayMs = 10 * 60 * 1000` (line 55)

**UI states:**
- **Before reveal:** "Date ideas will appear in 08:24"
- **After reveal:** Shows 3 date idea cards

---

## Date Interest Flow

### 1. User Picks Idea

**Action:** User clicks "Plan It" on one of the 3 date ideas.

**Frontend:** `chat-thread.component.ts:765-783`

```typescript
planIt(index: number) {
  if (this.selectedIdeaIndex !== null) return;
  const idea = this.revealedIdeas[index];
  if (!idea || !this.data) return;

  this.selectedIdeaIndex = index;
  this.cdr.detectChanges();

  this.chatApi.expressDateInterest(this.data.threadId, index, idea).subscribe({
    next: (res) => {
      this.mutualInterest = res.mutualInterest;
      this.cdr.detectChanges();
    },
    error: () => {
      this.selectedIdeaIndex = null;
      this.cdr.detectChanges();
    }
  });
}
```

**Endpoint:** `POST /chats/{threadId}/date-interest`

**Body:**
```json
{
  "ideaIndex": 0,
  "ideaText": "Sunset hike at Nandi Hills → chai at a roadside stall"
}
```

---

### 2. Backend Tracks Interest

**Code reference:** `ChatEndpoints.cs:1060-1144`

```csharp
var isUserA = match.UserAId == me;
var otherUserId = isUserA ? match.UserBId : match.UserAId;

if (isUserA)
    match.DateIdeaInterestedA = true;
else
    match.DateIdeaInterestedB = true;

var mutualInterest = match.DateIdeaInterestedA && match.DateIdeaInterestedB;
if (mutualInterest)
    match.DateIdeaInterestedAt = DateTimeOffset.UtcNow;

await db.SaveChangesAsync(ct);
```

**ECHO signal:** `DateIdeaAccepted` (EventValue = 1.0, MetadataJson = chosen idea)

```csharp
var metaJson = JsonSerializer.Serialize(new
{
    chosenIdea = req?.IdeaText,
    ideaIndex = req?.IdeaIndex ?? 0
});
await matchSignal.RecordAsync(me, otherUserId, MatchSignalEventTypes.DateIdeaAccepted, 1f, metaJson, ct);
```

---

### 3. Mutual Interest Unlocks Venues

**Response (first user):**
```json
{
  "mutualInterest": false
}
```

**Response (second user, if both interested):**
```json
{
  "mutualInterest": true
}
```

**Notification sent to both:**
```
"You both want to meet up! Check out some nearby spots 🗺️"
```

**Code reference:** `ChatEndpoints.cs:1113-1122`

```csharp
if (mutualInterest)
{
    _ = analytics.TrackAsync(match.UserAId, null, AnalyticsEvents.DateInterestMutual, new { threadId });
    _ = analytics.TrackAsync(match.UserBId, null, AnalyticsEvents.DateInterestMutual, new { threadId });

    var msg = "You both want to meet up! Check out some nearby spots 🗺️";
    await Task.WhenAll(
        notify.SendPushAsync(match.UserAId, msg, ct),
        notify.SendPushAsync(match.UserBId, msg, ct));

    return Results.Ok(new { mutualInterest = true });
}
```

---

## Venue Recommendations

**Unlocked when:** `DateIdeaInterestedA == true && DateIdeaInterestedB == true`

**Endpoint:** `GET /chats/{threadId}/venue-suggestions`

**Guard:**
```csharp
if (!match.DateIdeaInterestedA || !match.DateIdeaInterestedB)
    return Results.Json(new { error = "MUTUAL_INTEREST_REQUIRED" }, statusCode: 403);
```

**Service:** `IVenueService.GetVenueSuggestionsAsync(meUserId, partnerUserId, ct)`

**Implementation:** AI-powered venue finder based on:
- Chosen date idea
- User locations
- Shared preferences
- Time/day availability (if provided)

**Response:**
```json
{
  "venues": [
    {
      "name": "Nandi Hills Viewpoint",
      "address": "Nandi Hills, Chikkaballapur District",
      "type": "Outdoor",
      "estimatedCost": "₹200/person",
      "whyWeChose": "Perfect sunset spot, matches your chosen hike idea"
    },
    // ... more venues
  ]
}
```

**Code reference:** `ChatEndpoints.cs:1147-1174`

---

## Availability Signals

**Purpose:** Let users share when they're free without coordinating via text.

**Endpoint:** `POST /chats/{threadId}/availability`

**Body:**
```json
{
  "signalText": "Free Saturday afternoon!"
}
```

**Validation:**
- Max 200 characters
- Cannot be empty

**Storage:** `ChatAvailabilitySignal` entity (table: `chat_availability_signals`)

**Notification sent to partner:**
```
"{FirstName} is free: Free Saturday afternoon!"
```

**Code reference:** `ChatEndpoints.cs:1177-1223`

---

## UI Components

### Find Love Badge (Chat Header)

**States:**
- **Locked:** "Balloon · 02:34" (countdown to FindLoveAt)
- **Unlocked:** "Find Love" (green badge)

**Code reference:** `chat-thread.component.ts:386-404`

```typescript
statusText(): string {
  const iso = this.data?.findLoveAt;
  if (!iso) return 'Balloon';

  const t = new Date(iso).getTime();
  if (t > this.now) return `Balloon · ${this.countdown(iso)}`;
  return 'Find Love';
}
```

---

### Date Idea Cards

**Reveal states:**
1. **Locked (0-10 min after unlock):** "Date ideas will appear in 08:24"
2. **Revealed (10+ min):** 3 cards with "Plan It" buttons

**Card UI:**
```
┌──────────────────────────────────────┐
│  ◈ Date Idea #1                      │
│  Sunset hike at Nandi Hills →        │
│  chai at a roadside stall            │
│                                      │
│                     [Plan It ◈]      │
└──────────────────────────────────────┘
```

**After selection:**
```
┌──────────────────────────────────────┐
│  ◈ Date Idea #1                      │
│  Sunset hike at Nandi Hills →        │
│  chai at a roadside stall            │
│                                      │
│  ✓ You're interested                 │
│  Waiting for them...                 │
└──────────────────────────────────────┘
```

**Mutual interest:**
```
┌──────────────────────────────────────┐
│  ◈ Date Idea #1                      │
│  Sunset hike at Nandi Hills →        │
│  chai at a roadside stall            │
│                                      │
│  🗺️ Both interested!                 │
│  [View Nearby Spots]                 │
└──────────────────────────────────────┘
```

---

## Database Schema

**Table:** `matches`

```sql
-- Find Love unlock
both_messaged_at TIMESTAMPTZ,
find_love_at TIMESTAMPTZ,

-- Date coordination
date_idea_interested_a BOOLEAN DEFAULT FALSE,
date_idea_interested_b BOOLEAN DEFAULT FALSE,
date_idea_interested_at TIMESTAMPTZ,
date_agreed_at TIMESTAMPTZ
```

**Table:** `chat_availability_signals`

```sql
CREATE TABLE chat_availability_signals (
    id UUID PRIMARY KEY,
    thread_id UUID NOT NULL,
    user_id INT NOT NULL,
    signal_text VARCHAR(200) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);
```

---

## Analytics Events

| Event | When Fired |
|---|---|
| `FindLoveUnlocked` | Both users messaged (5-min countdown starts) |
| `DateInterestExpressed` | User picks a date idea |
| `DateInterestMutual` | Both users picked (same or different) ideas |
| `VenueSuggestionsViewed` | User opens venue list |

---

## Related Documentation

- [Trial Period Mechanics](./trial-period.md) (Path B unlock)
- [Backend Implementation](./backend.md)
- [Frontend Implementation](./frontend.md)
