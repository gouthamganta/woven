# FeedbackInsightService

**Purpose:** Extract learnable signals from free-text feedback and update ECHO matching weights.

**File:** `backend/WovenBackend/Services/Feedback/FeedbackInsightService.cs`

---

## Overview

When users submit date feedback, `FeedbackInsightService` processes the text fields (`FeltRightText`, `FeltOffText`) to:

1. **Boost high-quality matches** — 5-star mutual dates → increase pillar weight
2. **Detect mismatches** — Low stars after promising explanation → audit log
3. **Extract preference keywords** — Update `UserIntent.ReflectionSentence` with learned signals

This service runs **asynchronously** after feedback submission (fire-and-forget background task).

---

## Dependencies

```csharp
public FeedbackInsightService(
    WovenDbContext db,
    IOpenAiResilientClient ai,       // GPT-4.1-mini for keyword extraction
    ICacheService cache,             // Invalidate pillar embeddings
    ISecurityAuditService audit,     // Log suspicious patterns
    ILogger<FeedbackInsightService> logger)
```

---

## Core Method

### `ProcessNewFeedbackAsync`

```csharp
public async Task ProcessNewFeedbackAsync(
    Guid matchId, 
    int userId, 
    int partnerId,
    DateFeedbackDto feedback, 
    CancellationToken ct = default)
```

**Called From:**
```csharp
// DateFeedbackService.SubmitFeedbackAsync (line 291)
_ = Task.Run(async () => {
    using var scope = _scopeFactory.CreateScope();
    var feedbackInsight = scope.ServiceProvider.GetRequiredService<FeedbackInsightService>();
    
    await feedbackInsight.ProcessNewFeedbackAsync(
        capturedMatchId, capturedUserId, 
        capturedPartnerId, capturedDto);
});
```

**Why Fire-and-Forget?**
- Non-critical (user already submitted feedback)
- Async AI call can take 2-5 seconds
- Don't block HTTP response

---

## Processing Pipeline

### 1. High-Quality Signal Boost

**Condition:** 5 stars + "yes" to meet again

```csharp
if (feedback.Stars == 5 && feedback.MeetAgain == "yes")
{
    var existing = await _db.UserMatchingWeights
        .FirstOrDefaultAsync(w => w.UserId == userId && w.Component == "pillar", ct);
    
    if (existing != null)
    {
        existing.LearnedWeight = Math.Min(1.0f, existing.LearnedWeight + 0.05f);
        existing.SampleCount++;
    }
    else
    {
        _db.UserMatchingWeights.Add(new UserMatchingWeight
        {
            UserId = userId,
            Component = "pillar",
            LearnedWeight = 0.55f,  // Start above default (0.50)
            SampleCount = 1
        });
    }
    await _db.SaveChangesAsync(ct);
}
```

**Logic:**
- **Best outcome signal:** User met IRL, rated 5 stars, wants to meet again
- **Action:** Increase `pillar` weight by 0.05 (max 1.0)
- **Effect:** ECHO prioritizes personality alignment for this user going forward
- **Sample count tracks reliability** (more samples = higher confidence)

**Example Progression:**
```
Baseline:    0.50 (default pillar weight)
First 5⭐:   0.55 (+0.05)
Second 5⭐:  0.60 (+0.05)
Third 5⭐:   0.65 (+0.05)
...
Max cap:     1.00
```

---

### 2. Low-Quality Mismatch Detection

**Condition:** 1-2 stars after in-person date + had match explanation

```csharp
if (feedback.Stars <= 2 && feedback.MetInPerson)
{
    var hasExplanation = await _db.MatchExplanations.AsNoTracking()
        .AnyAsync(e => e.UserId == userId && e.CandidateId == partnerId, ct);
    
    if (hasExplanation)
    {
        _audit.Log("suspicious_pattern", 
            userId: userId,
            service: "FeedbackInsightService",
            resourceType: "explanation_quality_mismatch",
            piiStripped: true);
    }
}
```

**Purpose:**
- ECHO gave user a confident match explanation (Magical/Resonant)
- Date went poorly (≤2 stars)
- **Potential issues:**
  - Explanation was wrong/misleading
  - User changed preferences
  - External factors (bad timing, venue, etc.)

**Action:** Audit log for manual review (not automated penalty).

**Use Case:** Improve explanation quality over time.

---

### 3. Free-Text Keyword Extraction

**Goal:** Extract preference signals from `FeltRightText` and `FeltOffText` → append to `ReflectionSentence`.

#### Step 3.1: Combine & Sanitize
```csharp
var texts = new[] { feedback.FeltRightText, feedback.FeltOffText }
    .Where(t => !string.IsNullOrWhiteSpace(t))
    .ToList();

if (texts.Count == 0) return;

var combined = string.Join(" ", texts);
var sanitized = PiiSanitizer.SanitizeForAi(combined);
```

**Sanitization:**
- Strips names, phone numbers, emails
- Safe to send to OpenAI
- Audit logged after sanitization

**Example:**
```
Input:  "Sarah was so warm and genuine. We talked about our families."
Sanitized: "[NAME] was so warm and genuine. We talked about our families."
```

#### Step 3.2: AI Extraction
```csharp
var systemPrompt =
    "Extract 3 dating preference signals from this feedback. " +
    "Return JSON array of short strings only. No explanation. " +
    "Example: [\"values honesty\",\"enjoys humor\",\"wants depth\"]";

var raw = await _ai.ExecuteAsync("feedback_insight", 
    $"{systemPrompt}\n\n{sanitized}",
    useJsonMode: false, ct);
```

**Model:** `gpt-4.1-mini` (from `appsettings.json`)

**Example Call:**
```
Prompt:
Extract 3 dating preference signals from this feedback. Return JSON array of short strings only. No explanation. Example: ["values honesty","enjoys humor","wants depth"]

Great conversation about books and travel. Felt like we were on the same wavelength.

Response:
["enjoys intellectual conversations", "values shared interests", "wants mental connection"]
```

#### Step 3.3: Parse & Filter
```csharp
List<string> keywords;
try
{
    keywords = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
}
catch
{
    return;  // Invalid JSON → skip silently
}

if (keywords.Count == 0) return;
```

**Error Handling:**
- AI returns non-JSON → skip (non-critical)
- AI returns empty array → skip
- No exceptions thrown (logged at Warning level)

#### Step 3.4: Append to ReflectionSentence
```csharp
var intent = await _db.UserIntents
    .FirstOrDefaultAsync(i => i.UserId == userId, ct);

if (intent == null) return;

var current = intent.ReflectionSentence ?? string.Empty;
var newKeywords = keywords
    .Where(k => !current.Contains(k, StringComparison.OrdinalIgnoreCase))
    .ToList();

if (newKeywords.Count == 0) return;

var appended = (current + " " + string.Join(", ", newKeywords)).Trim();
if (appended.Length > 300)
    appended = appended[..300];  // Truncate to 300 chars

intent.ReflectionSentence = appended;
intent.UpdatedAt = DateTime.UtcNow;
await _db.SaveChangesAsync(ct);
```

**Deduplication:**
- Only append keywords not already in `ReflectionSentence`
- Case-insensitive check (`OrdinalIgnoreCase`)

**Example Progression:**
```
Initial: "Looking for someone kind and thoughtful"

After feedback 1: "Looking for someone kind and thoughtful, enjoys humor, values depth"

After feedback 2: "Looking for someone kind and thoughtful, enjoys humor, values depth, wants intellectual connection"

Max length: 300 chars (truncated if exceeded)
```

#### Step 3.5: Invalidate Embedding Cache
```csharp
await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);

_logger.LogInformation("[FeedbackInsight] Updated reflection for user {UserId}", userId);
```

**Why Invalidate?**
- `ReflectionSentence` is embedded as part of pillar vector
- Old cached embedding now stale
- Next ECHO run will re-embed with updated preferences

---

## Error Handling

### Try-Catch Wrapper
```csharp
try
{
    // All processing logic
}
catch (Exception ex)
{
    _logger.LogWarning(ex, 
        "[FeedbackInsight] Failed for user {UserId}, match {MatchId}", 
        userId, matchId);
}
```

**Behavior:**
- Failures logged at Warning level (not Error)
- No exceptions propagate to caller
- Non-critical: feedback submission already succeeded

### Common Failure Modes
| Failure | Cause | Impact |
|---|---|---|
| AI timeout | OpenAI slow/down | No keywords extracted, user not affected |
| Invalid JSON | GPT hallucination | Logged, skip keyword update |
| DB deadlock | Rare concurrent update | Logged, retry next feedback |
| Empty reflection | User has no `UserIntent` | Skipped, logged |

---

## Security & Privacy

### PII Sanitization
```csharp
var sanitized = PiiSanitizer.SanitizeForAi(combined);
```

**Redacted:**
- Names (`[NAME]`)
- Emails (`[EMAIL]`)
- Phone numbers (`[PHONE]`)

**Not Redacted:**
- Relationship descriptors ("my sister", "his job")
- Venues ("met at Starbucks")
- Activities ("went hiking")

**Rationale:** Preference signals need context. "Met at a coffee shop" tells us user likes casual dates.

### Audit Logging
```csharp
_audit.Log("suspicious_pattern", 
    userId: userId,
    service: "FeedbackInsightService",
    resourceType: "explanation_quality_mismatch",
    piiStripped: true);
```

**Logged Events:**
- `suspicious_pattern` (low stars after good explanation)
- PII access implied (sanitization happened)

---

## Integration with ECHO

### 1. Weight Learning
```csharp
// DateFeedbackService.cs (line 305)
if (capturedDto.MetInPerson && capturedStars.HasValue)
{
    var weightSvc = scope.ServiceProvider.GetRequiredService<IWeightLearningService>();
    await weightSvc.LearnWeightsAsync(capturedUserId);
    // ...
}
```

**Flow:**
1. User submits feedback with stars
2. `FeedbackInsightService` boosts pillar weight (+0.05 for 5⭐)
3. `WeightLearningService` re-runs full weight learning
4. New weights used in next `DailyDeckOrchestrator` run

### 2. Insight Delivery
```csharp
await insightSvc.ComputeInsightsAsync(capturedUserId);
await insightSvc.DeliverInsightAtMomentAsync(capturedUserId, "date_feedback_submitted");
```

**Example Insight:**
```
"You've had 3 great dates this month! You seem to connect best with people 
who share your love of [extracted keyword: outdoor activities]."
```

### 3. Embedding Refresh
- `ReflectionSentence` updated → cache invalidated
- Next ECHO run re-embeds updated preferences
- Future matches biased toward extracted keywords

---

## Performance

### Async Execution
- Runs in background (`Task.Run`)
- No blocking on HTTP response
- Uses scoped `IServiceScope` (DB context isolation)

### AI Call Latency
- **Typical:** 1-3 seconds
- **Timeout:** 30 seconds (OpenAI client default)
- **Retries:** 3 attempts (exponential backoff)

### Database Impact
```sql
-- 3 queries per feedback:
SELECT * FROM user_matching_weights WHERE user_id = ? AND component = 'pillar';
SELECT * FROM match_explanations WHERE user_id = ? AND candidate_id = ?;
SELECT * FROM user_intents WHERE user_id = ?;

-- 1-2 writes:
UPDATE user_matching_weights SET learned_weight = ?, sample_count = ?;
UPDATE user_intents SET reflection_sentence = ?, updated_at = ?;
```

**Concurrency:** Rare. Most users don't submit multiple feedbacks simultaneously.

---

## Future Enhancements

- [ ] **Sentiment analysis** on `FeltOffText` → safety flags
- [ ] **Venue extraction** → recommend similar places
- [ ] **Partner comparison** → cross-check both sides' feedback
- [ ] **Keyword clustering** → identify user archetype (intellectual, adventurous, etc.)
- [ ] **Negative signal handling** — Currently treats all keywords as positive preferences

---

**Last Updated:** 2026-10-07
**Related:** [date-feedback.md](./date-feedback.md), [workers.md](./workers.md)
