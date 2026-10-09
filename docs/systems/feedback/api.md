# Feedback API Endpoints

**File:** `backend/WovenBackend/Endpoints/FeedbackEndpoints.cs`  
**Group:** `/me` (user-scoped), `/matches` (match-scoped)

---

## Endpoints

### 1. `GET /me/feedback-prompt`

**Purpose:** Check if user has pending feedback prompt to display.

**Auth:** Required (`RequireAuthorization()`)

**Request:**
```http
GET /me/feedback-prompt HTTP/1.1
Authorization: Bearer <jwt>
```

**Response (Pending):**
```json
{
  "hasPendingPrompt": true,
  "matchId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "partnerFirstName": "Sarah",
  "triggerType": "interested_both"
}
```

**Response (None):**
```json
{
  "hasPendingPrompt": false
}
```

**Logic:**
```csharp
var prompt = await db.DateFeedbackPrompts.AsNoTracking()
    .Where(p => p.UserId == userId 
             && p.SentAt != null 
             && p.RespondedAt == null)
    .OrderByDescending(p => p.SentAt)
    .FirstOrDefaultAsync(ct);
```

**Criteria:**
- `SentAt != null` — prompt was sent (not just queued)
- `RespondedAt == null` — user hasn't submitted feedback yet
- **Latest prompt** — if multiple pending, returns most recent

**Partner Name Extraction:**
```csharp
var partnerName = await db.Users.AsNoTracking()
    .Where(u => u.Id == partnerId)
    .Select(u => u.FullName)
    .FirstOrDefaultAsync(ct) ?? "your match";

var firstName = partnerName.Split(' ')[0];
```

**Edge Cases:**
- Match deleted → returns `hasPendingPrompt: false`
- Partner deleted account → firstName = "your match"

---

### 2. `POST /matches/{matchId}/feedback`

**Purpose:** Submit date feedback for a specific match.

**Auth:** Required

**Request:**
```http
POST /matches/f47ac10b-58cc-4372-a567-0e02b2c3d479/feedback HTTP/1.1
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "metInPerson": true,
  "stars": 4,
  "feltRightText": "Great conversation, similar values",
  "feltOffText": null,
  "meetAgain": "yes"
}
```

**Request Schema:**
```csharp
private record FeedbackRequest(
    bool MetInPerson,
    int? Stars,
    string? FeltRightText,
    string? FeltOffText,
    string? MeetAgain);
```

**Validation:**

#### Stars
```csharp
if (req.Stars.HasValue && (req.Stars < 1 || req.Stars > 5))
    return Results.BadRequest(new { error = "INVALID_STARS" });
```

- **Required if** `metInPerson = true`
- **Range:** 1-5
- **Error:** `INVALID_STARS`

#### Meet Again
```csharp
if (req.MeetAgain != null && !ValidMeetAgain.Contains(req.MeetAgain))
    return Results.BadRequest(new { error = "INVALID_MEET_AGAIN" });

private static readonly HashSet<string> ValidMeetAgain =
    new(StringComparer.OrdinalIgnoreCase) { "yes", "no", "maybe" };
```

- **Valid values:** `"yes"`, `"no"`, `"maybe"` (case-insensitive)
- **Error:** `INVALID_MEET_AGAIN`

#### Text Length
```csharp
if (req.FeltRightText?.Length > 300 || req.FeltOffText?.Length > 300)
    return Results.BadRequest(new { error = "TEXT_TOO_LONG" });
```

- **Max:** 300 characters each
- **Error:** `TEXT_TOO_LONG`

---

### Response Codes

#### 200 OK (Success)
```json
{
  "submitted": true
}
```

**Triggers:**
1. `DateFeedback` record created/updated
2. `DateFeedbackPrompt.RespondedAt` marked
3. ECHO signal recorded (`MatchSignalEventTypes.ExplicitFeedback`)
4. Background processing:
   - `FeedbackInsightService.ProcessNewFeedbackAsync()`
   - `WeightLearningService.LearnWeightsAsync()`
   - `InsightService.ComputeInsightsAsync()`
   - `CheckBadRatingPatternAsync()`

#### 400 Bad Request
```json
{ "error": "INVALID_STARS" }
{ "error": "INVALID_MEET_AGAIN" }
{ "error": "TEXT_TOO_LONG" }
```

#### 404 Not Found
```json
{ "error": "NO_PROMPT_FOUND" }
```

**Cause:** No `DateFeedbackPrompt` exists for this user + match.

**Prevention:** Always call `GET /me/feedback-prompt` first.

#### 401 Unauthorized
No JWT or invalid user ID claim.

---

## Service Call

### `IDateFeedbackService.SubmitFeedbackAsync`

**Signature:**
```csharp
Task SubmitFeedbackAsync(
    int userId, 
    Guid matchId, 
    DateFeedbackDto dto, 
    CancellationToken ct = default);
```

**DTO:**
```csharp
public record DateFeedbackDto(
    bool MetInPerson,
    int? Stars,
    string? FeltRightText,
    string? FeltOffText,
    string? MeetAgain);
```

**Processing:**
```csharp
var prompt = await _db.DateFeedbackPrompts
    .FirstOrDefaultAsync(p => p.UserId == userId && p.MatchId == matchId, ct);

if (prompt == null)
    throw new InvalidOperationException("NO_PROMPT_FOUND");

var stars = dto.MetInPerson ? dto.Stars : null;  // Null out stars if didn't meet

var existing = await _db.DateFeedbacks
    .FirstOrDefaultAsync(f => f.UserId == userId && f.MatchId == matchId, ct);

if (existing == null)
{
    _db.DateFeedbacks.Add(new DateFeedback { /* ... */ });
}
else
{
    // UPDATE existing (allow revisions)
    existing.MetInPerson = dto.MetInPerson;
    existing.Stars = stars;
    // ...
}

prompt.RespondedAt = DateTimeOffset.UtcNow;
await _db.SaveChangesAsync(ct);
```

**Key Behavior:**
- **Upsert pattern** — allows users to revise feedback
- **Stars nulled** if `MetInPerson = false`
- **Prompt marked responded** even on revision

---

## ECHO Signal Recording

### Explicit Feedback Signal
```csharp
if (dto.Stars.HasValue)
{
    var normalizedRating = (dto.Stars.Value - 1) / 4f;  // 1→0.0, 5→1.0
    
    await _signals.RecordAsync(
        userId, 
        partnerId, 
        MatchSignalEventTypes.ExplicitFeedback, 
        normalizedRating, 
        ct: ct);
}
```

**Normalization:**
| Stars | Normalized |
|---|---|
| 1 | 0.00 |
| 2 | 0.25 |
| 3 | 0.50 |
| 4 | 0.75 |
| 5 | 1.00 |

**Usage in ECHO:**
- High-confidence outcome signal (user met IRL)
- Weighted heavily in `WeightLearningService`
- Overrides implicit signals (message count, response time)

---

## Background Processing

### Fire-and-Forget Task
```csharp
_ = Task.Run(async () =>
{
    try
    {
        using var scope = _scopeFactory.CreateScope();
        var insightSvc = scope.ServiceProvider.GetRequiredService<IInsightService>();
        var feedbackInsight = scope.ServiceProvider.GetRequiredService<FeedbackInsightService>();
        
        await feedbackInsight.ProcessNewFeedbackAsync(matchId, userId, partnerId, dto);
        
        if (dto.MetInPerson && dto.Stars.HasValue)
        {
            var weightSvc = scope.ServiceProvider.GetRequiredService<IWeightLearningService>();
            await weightSvc.LearnWeightsAsync(userId);
            await insightSvc.ComputeInsightsAsync(userId);
            await insightSvc.DeliverInsightAtMomentAsync(userId, "date_feedback_submitted");
            
            if (partnerId.HasValue)
                await CheckBadRatingPatternAsync(partnerId.Value, dto.Stars.Value, scope);
        }
    }
    catch { /* non-critical */ }
});
```

**Why Fire-and-Forget?**
- HTTP response returns immediately
- Async AI calls (2-5 seconds) don't block user
- Non-critical: feedback already saved

---

## Trust/Safety Pattern Detection

### `CheckBadRatingPatternAsync`

**Triggers on:** User receives ≤2 stars from anyone.

**Logic:**
```csharp
if (stars > 2) return;

var lowRaterCount = await (
    from f in db.DateFeedbacks.AsNoTracking()
    join m in db.Matches.AsNoTracking() on f.MatchId equals m.Id
    where (m.UserAId == ratedUserId || m.UserBId == ratedUserId)
       && f.UserId != ratedUserId  // Count OTHER people's ratings
       && f.Stars <= 2
    select f.UserId
).Distinct().CountAsync();

if (lowRaterCount >= 3)
{
    await trust.FlagAsync(ratedUserId, "LOW_ENGAGEMENT", 0.6f);
    audit.Log("suspicious_pattern", userId: ratedUserId, ...);
}
```

**Threshold:** 3+ users gave this person ≤2 stars.

**Action:** Flag as `LOW_ENGAGEMENT` (60% confidence).

**Effect:**
- User may be shadow-banned from recommendations
- Manual review triggered
- No notification sent to user

---

## Analytics Tracking

### Event Fired
```csharp
_ = _analytics.TrackAsync(userId, null, AnalyticsEvents.DateFeedbackSubmitted,
    new { metInPerson = dto.MetInPerson, stars = stars, meetAgain = dto.MeetAgain });
```

**Event Name:** `DateFeedbackSubmitted`

**Properties:**
```json
{
  "metInPerson": true,
  "stars": 4,
  "meetAgain": "yes"
}
```

**Use Cases:**
- Conversion funnel: prompts sent → responses received
- Quality metrics: average stars by match explanation type
- A/B testing: reschedule strategy effectiveness

---

## Security & Privacy

### PII Access Audit
```csharp
_audit.Log("pii_access", 
    userId: userId, 
    service: "DateFeedbackService",
    resourceType: "feedback_text", 
    piiStripped: true);
```

**Logged on every submission** (free text contains PII).

### Authorization Check
```csharp
var userId = GetUserId(http.User);  // Throws UnauthorizedAccessException if invalid
```

**Claim Chain:**
1. `"uid"` → custom claim
2. `"sub"` → standard JWT subject
3. `ClaimTypes.NameIdentifier` → ASP.NET fallback

**Throws:** `UnauthorizedAccessException` → 401 via `AuthExceptionHandler`.

### Match Ownership Validation
```csharp
var prompt = await _db.DateFeedbackPrompts
    .FirstOrDefaultAsync(p => p.UserId == userId && p.MatchId == matchId, ct);

if (prompt == null)
    throw new InvalidOperationException("NO_PROMPT_FOUND");
```

**Prevents:** User A submitting feedback for User B's match.

---

## Frontend Integration

### Typical Flow

#### 1. Check for Pending Prompt
```typescript
// Woven Assistant calls this on mount
const res = await fetch('/me/feedback-prompt', {
  headers: { 'Authorization': `Bearer ${jwt}` }
});

const data = await res.json();

if (data.hasPendingPrompt) {
  // Show task in Assistant pending list
  setPendingTask({
    type: 'date_feedback',
    priority: 1,
    data: {
      matchId: data.matchId,
      partnerName: data.partnerFirstName
    }
  });
}
```

#### 2. User Opens Feedback Form
```typescript
// Modal opens with partnerFirstName pre-filled
<h2>How was your time with {partnerFirstName}?</h2>
```

#### 3. Submit Feedback
```typescript
const submitFeedback = async (form: FeedbackForm) => {
  const res = await fetch(`/matches/${matchId}/feedback`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${jwt}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(form)
  });
  
  if (!res.ok) {
    const { error } = await res.json();
    showError(error);  // INVALID_STARS, TEXT_TOO_LONG, etc.
    return;
  }
  
  closeModal();
  clearPendingTask();
  showSuccessToast('Thanks for your feedback! 💙');
};
```

---

## Error Handling

### Client-Side Retry
```typescript
if (res.status === 429) {
  // Rate limited (unlikely for feedback)
  await sleep(5000);
  return submitFeedback(form);
}

if (res.status === 500) {
  showError('Something went wrong. Please try again.');
  // Keep form data, allow retry
}
```

### Server-Side Idempotency
**Current:** Not idempotent (upsert pattern allows revisions).

**Future:** Add `X-Idempotency-Key` support for double-submit protection.

```csharp
// Check idempotency key before processing
if (await _idempotency.ExistsAsync(idempotencyKey, userId))
    return Results.Ok(new { submitted = true });  // Already processed
```

---

## Performance

### Database Queries Per Request

#### `GET /me/feedback-prompt`
```sql
SELECT * FROM date_feedback_prompts WHERE user_id = ? AND sent_at IS NOT NULL AND responded_at IS NULL ORDER BY sent_at DESC LIMIT 1;
SELECT * FROM matches WHERE id = ? LIMIT 1;
SELECT full_name FROM users WHERE id = ? LIMIT 1;
```

**Index Required:**
```sql
CREATE INDEX idx_feedback_prompts_user_pending 
    ON date_feedback_prompts(user_id, sent_at, responded_at);
```

#### `POST /matches/{matchId}/feedback`
```sql
SELECT * FROM date_feedback_prompts WHERE user_id = ? AND match_id = ? LIMIT 1;
SELECT * FROM date_feedbacks WHERE user_id = ? AND match_id = ? LIMIT 1;
INSERT INTO date_feedbacks ...;  -- OR UPDATE
UPDATE date_feedback_prompts SET responded_at = ? WHERE id = ?;
INSERT INTO match_signal_logs ...;
```

**Transaction:** Single `SaveChangesAsync()` wraps all writes.

### Expected Latency
- **GET prompt:** < 50ms
- **POST feedback:** 100-200ms (DB writes + signal recording)
- **Background tasks:** 2-5 seconds (async, doesn't block response)

---

## Future Enhancements

- [ ] **Idempotency keys** (prevent double-submit)
- [ ] **Batch feedback** (submit for multiple matches at once)
- [ ] **Photo upload** (attach date photos for good experiences)
- [ ] **Venue rating** (if user went to recommended place)
- [ ] **Partner comparison** (show aggregate stats: "80% of dates with this person went well")
- [ ] **Follow-up prompts** (after "no" to meet again, ask why)

---

**Last Updated:** 2026-10-07
**Related:** [date-feedback.md](./date-feedback.md), [triggers.md](./triggers.md)
