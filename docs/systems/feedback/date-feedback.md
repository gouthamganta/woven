# Date Feedback Form

**Purpose:** Structured post-date feedback collection interface.

---

## Form Fields

### 1. Met In Person
**Type:** Boolean (checkbox)  
**Required:** Yes  
**Storage:** `DateFeedback.MetInPerson`

**UI Copy:**
```
Did you meet [FirstName] in person?
```

**Logic:**
- If `false`, stars field is disabled and saved as `null`
- If `true`, stars field becomes required

---

### 2. Star Rating
**Type:** Integer (1-5)  
**Required:** Only if `MetInPerson = true`  
**Validation:** `1 <= stars <= 5`  
**Storage:** `DateFeedback.Stars`

**UI Copy:**
```
How would you rate your time together?
⭐⭐⭐⭐⭐
```

**Error Message:**
```json
{ "error": "INVALID_STARS" }
```

**ECHO Integration:**
- Normalized: `(stars - 1) / 4.0` → 0.0 (1 star) to 1.0 (5 stars)
- Recorded as `MatchSignalEventTypes.ExplicitFeedback`
- Triggers `WeightLearningService` re-computation

---

### 3. What Felt Right
**Type:** Text  
**Required:** No  
**Max Length:** 300 characters  
**Storage:** `DateFeedback.FeltRightText`

**UI Copy:**
```
What felt right? (Optional)
[Textarea: 300 char limit]
```

**Processing:**
- PII sanitized before AI extraction
- Keywords extracted via `FeedbackInsightService`
- Appended to `UserIntent.ReflectionSentence`

**Example Input:**
```
We had great chemistry and similar sense of humor. 
Conversation felt natural.
```

**Extracted Signals:**
```json
["values chemistry", "enjoys humor", "wants natural flow"]
```

---

### 4. What Felt Off
**Type:** Text  
**Required:** No  
**Max Length:** 300 characters  
**Storage:** `DateFeedback.FeltOffText`

**UI Copy:**
```
What felt off? (Optional)
[Textarea: 300 char limit]
```

**Processing:**
- Same extraction pipeline as "What Felt Right"
- Treated as negative preference signals
- Used to detect mismatches in ECHO explanations

**Example Input:**
```
Conversation felt one-sided. I did most of the talking.
```

**Extracted Signals:**
```json
["prefers balanced conversation", "wants active listener"]
```

---

### 5. Meet Again
**Type:** Enum  
**Required:** No  
**Values:** `"yes" | "no" | "maybe"`  
**Storage:** `DateFeedback.MeetAgain`

**UI Copy:**
```
Would you like to meet again?
[ ] Yes
[ ] No
[ ] Maybe
```

**Error Message:**
```json
{ "error": "INVALID_MEET_AGAIN" }
```

**ECHO Integration:**
- `stars == 5 && meetAgain == "yes"` → Strongest positive signal
- Boosts pillar weight by 0.05 (capped at 1.0)

---

## Validation Rules

### Client-Side
```typescript
interface FeedbackForm {
  metInPerson: boolean;
  stars?: number;  // Required if metInPerson === true
  feltRightText?: string;
  feltOffText?: string;
  meetAgain?: 'yes' | 'no' | 'maybe';
}

function validate(form: FeedbackForm): string[] {
  const errors: string[] = [];
  
  if (form.metInPerson && !form.stars) {
    errors.push('Star rating required for in-person dates');
  }
  
  if (form.stars && (form.stars < 1 || form.stars > 5)) {
    errors.push('Stars must be between 1 and 5');
  }
  
  if (form.feltRightText && form.feltRightText.length > 300) {
    errors.push('Positive feedback too long (max 300 chars)');
  }
  
  if (form.feltOffText && form.feltOffText.length > 300) {
    errors.push('Negative feedback too long (max 300 chars)');
  }
  
  if (form.meetAgain && !['yes', 'no', 'maybe'].includes(form.meetAgain)) {
    errors.push('Invalid meet-again value');
  }
  
  return errors;
}
```

### Server-Side
```csharp
// FeedbackEndpoints.cs
if (req.Stars.HasValue && (req.Stars < 1 || req.Stars > 5))
    return Results.BadRequest(new { error = "INVALID_STARS" });

if (req.MeetAgain != null && !ValidMeetAgain.Contains(req.MeetAgain))
    return Results.BadRequest(new { error = "INVALID_MEET_AGAIN" });

if (req.FeltRightText?.Length > 300 || req.FeltOffText?.Length > 300)
    return Results.BadRequest(new { error = "TEXT_TOO_LONG" });
```

---

## Security & Privacy

### PII Handling
```csharp
// FeedbackInsightService.cs
var sanitized = PiiSanitizer.SanitizeForAi(combined);
```

- All free-text feedback sanitized before AI processing
- Names, phone numbers, emails stripped
- Security audit log on every feedback submission:

```csharp
_audit.Log("pii_access", 
    userId: userId, 
    service: "DateFeedbackService",
    resourceType: "feedback_text", 
    piiStripped: true);
```

### Storage
- No encryption at rest (non-sensitive data)
- `FeltRightText` and `FeltOffText` stored as plaintext
- Only accessible by user who wrote it + admin analytics
- Never shown to other users (including the person being rated)

---

## UX Flow

### 1. Trigger Point
- Woven Assistant shows pending task badge
- User opens Assistant → sees "How did it go with [Name]? 💙"

### 2. Form Open
```
GET /me/feedback-prompt
→ { hasPendingPrompt: true, matchId, partnerFirstName, triggerType }
```

### 3. Form Display
```
━━━━━━━━━━━━━━━━━━━━━━━━━━
How was your time with Sarah?
━━━━━━━━━━━━━━━━━━━━━━━━━━

☑ Did you meet Sarah in person?

How would you rate your time together?
⭐⭐⭐⭐☆  (4 stars selected)

What felt right? (Optional)
[Great conversation, similar values]

What felt off? (Optional)
[                              ]

Would you like to meet again?
◉ Yes  ○ No  ○ Maybe

[Submit Feedback]
━━━━━━━━━━━━━━━━━━━━━━━━━━
```

### 4. Submission
```
POST /matches/{matchId}/feedback
{
  "metInPerson": true,
  "stars": 4,
  "feltRightText": "Great conversation, similar values",
  "feltOffText": null,
  "meetAgain": "yes"
}

→ 200 OK { "submitted": true }
```

### 5. Confirmation
- Modal closes
- Assistant task cleared
- User returns to home feed

---

## Edge Cases

### No Prompt Found
```
POST /matches/{matchId}/feedback
→ 404 { "error": "NO_PROMPT_FOUND" }
```

**Cause:** User tried to submit feedback without an active prompt.

**Resolution:** Check `GET /me/feedback-prompt` first.

### Already Responded
```csharp
// DateFeedbackService.SubmitFeedbackAsync
var existing = await _db.DateFeedbacks
    .FirstOrDefaultAsync(f => f.UserId == userId && f.MatchId == matchId, ct);

if (existing != null) {
    // UPDATE existing record (allow revisions)
    existing.MetInPerson = dto.MetInPerson;
    existing.Stars = stars;
    // ...
}
```

**Behavior:** Overwrites previous feedback. Users can revise responses.

### Partner Blocked User
- Prompt still sent (match was closed normally before block)
- Feedback recorded but not shared with blocked user
- No impact on future matching (already blocked)

---

## Analytics

### Event Tracking
```csharp
AnalyticsEvents.DateFeedbackSubmitted
→ {
    metInPerson: true,
    stars: 4,
    meetAgain: "yes"
}
```

### Metrics to Monitor
- Feedback completion rate (submitted / prompts sent)
- Average stars by trigger type
- Reschedule effectiveness (response rate after 1st/2nd reschedule)
- Text feedback fill rate (% of submissions with free text)

---

**Last Updated:** 2026-10-07
**Related:** [triggers.md](./triggers.md), [api.md](./api.md)
