# Summary Generation

How weekly coaching summaries are created from behavioral signals.

---

## Overview

Coaching summary generation is a **two-stage pipeline**:

1. **C# Narrative Builder** — deterministic signal interpretation (no AI)
2. **GPT-4.1-mini** — warm, human-like rewrite of C# narrative

This separation ensures:
- No metric leakage ("you sent 47 messages")
- Consistent quality (not dependent on GPT hallucinating from sparse data)
- Privacy (GPT never sees raw user IDs or PII)

---

## Stage 1: Signal Aggregation

**Time window:** Prior 7 days (from `now - 7 days` to `now`)

### Deck Engagement

```csharp
var deckInteractions = await db.DailyInteractions
    .Where(d => d.UserId == userId && d.DateUtc >= signalFrom)
    .SumAsync(d => d.TotalUsed, ct);
```

**What this captures:** How many Moments cards the user engaged with (MAGICAL/RESONANT/PASS).

**Bucketing:**
- `≥ 10` → "showed up consistently"
- `3-9` → "engaged with their daily deck, taking their time"

---

### Match Signals

```csharp
var signalCounts = await db.MatchSignalLogs
    .Where(s => s.ViewerId == userId && s.OccurredAt >= signalFrom)
    .GroupBy(s => s.EventType)
    .Select(g => new CoachingSignalStat(
        g.Key,
        g.Count(),
        (double)g.Average(s => s.EventValue)
    ))
    .ToListAsync(ct);
```

**Permitted event types only:**

| EventType | What it means |
|---|---|
| `TimeToFirstMessageMs` | User sent the first message in a match |
| `TrialMessageCount` | Total messages sent during trial period |
| `DateIdeaAccepted` | User chose a date idea (shows intent) |
| `MutualVoiceExchange` | Both users sent voice notes (vulnerability) |
| `TrialAccepted` | User chose CONTINUE (not END or BLOCK) |

**Access control:** Hardcoded whitelist in `BuildNarrative()`. No other signal types are read.

---

## Stage 2: C# Narrative Builder

**Function:** `BuildNarrative(userId, deckInteractions, signalMap)`  
**Returns:** Plain text paragraph (English prose, no numbers)

### Logic

```csharp
private static string BuildNarrative(
    int userId,
    int deckInteractions,
    Dictionary<string, CoachingSignalStat> signalMap)
{
    var sb = new StringBuilder();
    sb.Append("This person has been active on the app this week. ");

    // ── Deck engagement (bucketed) ────────────────────────────────────
    if (deckInteractions >= 10)
        sb.Append("They showed up consistently, reviewing a solid set of potential connections. ");
    else if (deckInteractions >= 3)
        sb.Append("They engaged with their daily deck, taking their time with the profiles they saw. ");

    // ── Conversations started ─────────────────────────────────────────
    var firstMessages = GetCount(signalMap, MatchSignalEventTypes.TimeToFirstMessageMs);
    if (firstMessages > 1)
        sb.Append("When they felt a connection, they were willing to make the first move. ");
    else if (firstMessages == 1)
        sb.Append("They took the step of sending the first message in at least one conversation. ");

    // ── Conversation depth ────────────────────────────────────────────
    var avgMessages = GetAvg(signalMap, MatchSignalEventTypes.TrialMessageCount);
    if (avgMessages >= 15)
        sb.Append("Their conversations have gone deep — there''s real exchange happening, not just surface pleasantries. ");
    else if (avgMessages >= 5)
        sb.Append("They''re having real conversations, not just exchanging pleasantries. ");

    // ── Date ideas ────────────────────────────────────────────────────
    var dateIdeasAccepted = GetCount(signalMap, MatchSignalEventTypes.DateIdeaAccepted);
    if (dateIdeasAccepted > 0)
        sb.Append("They showed genuine interest in meeting — that takes courage. ");

    // ── Voice exchange ────────────────────────────────────────────────
    var voiceExchanges = GetCount(signalMap, MatchSignalEventTypes.MutualVoiceExchange);
    if (voiceExchanges > 0)
        sb.Append("Voice notes were exchanged — something about that connection felt worth hearing in a real voice. ");

    // ── Trial continuation ────────────────────────────────────────────
    var trialsAccepted = GetCount(signalMap, MatchSignalEventTypes.TrialAccepted);
    if (trialsAccepted > 0)
        sb.Append("They chose to continue at least one trial — they''re not just going through motions. ");

    return sb.ToString().Trim();
}
```

### Example Output

**Input signals:**
- `deckInteractions = 12`
- `TimeToFirstMessageMs` count = 2
- `TrialMessageCount` avg = 18.0
- `MutualVoiceExchange` count = 1
- `TrialAccepted` count = 1

**C# narrative:**
```
This person has been active on the app this week. They showed up consistently, 
reviewing a solid set of potential connections. When they felt a connection, they 
were willing to make the first move. Their conversations have gone deep — there''s 
real exchange happening, not just surface pleasantries. Voice notes were exchanged 
— something about that connection felt worth hearing in a real voice. They chose 
to continue at least one trial — they''re not just going through motions.
```

**No raw numbers.** No comparisons. Just observed patterns.

---

## Stage 3: GPT-4.1-mini Call

**Model:** `gpt-4.1-mini`  
**Endpoint:** `https://api.openai.com/v1/chat/completions`  
**Temperature:** 0.8 (higher than other agents — encourages warmth)  
**Max Tokens:** 200

### System Prompt

```
You are a warm, perceptive friend who happens to understand how dating apps work. 
You know this person has been putting themselves out there this week. 

Your job is to reflect back what you noticed — genuine, specific, encouraging 
without being hollow. 

Never mention numbers directly. Never compare them to others. Never imply they 
should change who they are. 

If there is nothing genuinely positive or actionable to say, return the string "SUPPRESS".
```

### User Prompt

The C# narrative (from Stage 2).

### Request Body

```json
{
  "model": "gpt-4.1-mini",
  "temperature": 0.8,
  "max_tokens": 200,
  "messages": [
    {
      "role": "system",
      "content": "<system prompt>"
    },
    {
      "role": "user",
      "content": "<C# narrative>"
    }
  ]
}
```

### Response Parsing

```csharp
var json = await response.Content.ReadAsStringAsync(ct);
using var doc = JsonDocument.Parse(json);
return doc.RootElement
    .GetProperty("choices")[0]
    .GetProperty("message")
    .GetProperty("content")
    .GetString();
```

### Example GPT Output

**Input:** (C# narrative from above)

**Output:**
```
You showed up this week — not just scrolling, actually engaging. The fact that 
you''re willing to send the first message when something feels right says a lot. 
And those deeper conversations? That''s not everyone. Keep trusting your instincts 
about who''s worth the energy.
```

---

## Suppression Logic

A summary is **not saved** if:

1. GPT returns `"SUPPRESS"` (case-insensitive)
2. Output is `< 50 characters` (too short to be meaningful)
3. Output is empty/whitespace

```csharp
if (string.IsNullOrWhiteSpace(summaryText) ||
    summaryText.Trim().Equals("SUPPRESS", StringComparison.OrdinalIgnoreCase) ||
    summaryText.Trim().Length < MinSummaryChars)
{
    _logger.LogInformation("[CoachingSummary] Suppressed for user {UserId}", userId);
    return false;
}
```

**Why suppress:**
- Prevents hollow coaching ("Great week!") when nothing meaningful happened
- Respects user attention — only deliver when there''s something worth reflecting

**Typical suppression rate:** 15-20% of eligible users

---

## Privacy Guarantees

### What GPT-4.1-mini sees:

```
This person has been active on the app this week. They showed up consistently, 
reviewing a solid set of potential connections. When they felt a connection, they 
were willing to make the first move.
```

### What GPT-4.1-mini never sees:

- User ID (referred to as "this person")
- Raw numbers (12 deck interactions → "consistently")
- Chat messages
- Match names
- Profile photos
- Voice note audio
- Compatibility scores

**Audit trail:** `InterpretedNarrative` column stores the C# narrative (debugging only, never shown to users).

---

## Error Handling

### OpenAI API Failure

```csharp
try
{
    using var response = await http.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
    // ... parse response
}
catch (Exception ex)
{
    _logger.LogError(ex, "[CoachingSummary] OpenAI call failed");
    return null;
}
```

**Behavior on failure:**
- No summary written for this user
- Next week retries (no permanent data loss)
- Worker continues to process other users

### Rate Limiting

**OpenAI API rate limits:**
- Model: `gpt-4.1-mini`
- Tier: Pay-as-you-go
- Limit: ~3500 requests/min

**Mitigation:**
- Process users sequentially (no parallel OpenAI calls)
- Typical batch: 200-300 users/week → ~5 min at 1 req/sec
- Well below rate limits

---

## Token Usage

**Typical request:**
- System prompt: ~80 tokens
- C# narrative: ~120 tokens
- GPT output: ~60 tokens
- **Total:** ~260 tokens/summary (~20 output tokens billed)

**Monthly cost (1000 active users):**
- 1000 summaries/week × 260 tokens = 260k tokens/week
- 1M tokens/month × $0.15/1M input + $0.60/1M output
- **~$0.15/month** (negligible)

---

## Testing

### Unit Test: Narrative Builder

```csharp
[Test]
public void BuildNarrative_HighEngagement_ReturnsPositiveTone()
{
    var signals = new Dictionary<string, CoachingSignalStat>
    {
        [MatchSignalEventTypes.TimeToFirstMessageMs] = new(2, 0),
        [MatchSignalEventTypes.TrialMessageCount] = new(1, 18.0),
        [MatchSignalEventTypes.MutualVoiceExchange] = new(1, 1.0),
    };

    var narrative = BuildNarrative(userId: 1, deckInteractions: 12, signals);

    Assert.That(narrative, Does.Contain("showed up consistently"));
    Assert.That(narrative, Does.Contain("willing to make the first move"));
    Assert.That(narrative, Does.Contain("conversations have gone deep"));
    Assert.That(narrative, Does.Not.Contain("12")); // No raw numbers
}
```

### Integration Test: Full Flow

```csharp
[Test]
public async Task ProcessUserAsync_QualifiedUser_CreatesSummary()
{
    // Arrange
    var user = CreateTestUser(createdAt: now.AddDays(-20));
    SeedDailyInteractions(user.Id, totalUsed: 8, dateRange: last7Days);
    SeedSignals(user.Id, new[] { TrialAccepted, MessageSent, DateIdeaAccepted });

    // Act
    var saved = await worker.ProcessUserAsync(db, apiKey, user.Id, weekStart, signalFrom, now, ct);

    // Assert
    Assert.That(saved, Is.True);
    var summary = await db.CoachingSummaries.FirstOrDefaultAsync(c => c.UserId == user.Id);
    Assert.That(summary, Is.Not.Null);
    Assert.That(summary.SummaryText.Length, Is.GreaterThan(50));
    Assert.That(summary.InterpretedNarrative, Does.Not.Contain(user.Id.ToString()));
}
```

---

## Tone Calibration

### Good Coaching (what we want)

```
You showed up this week — not just scrolling, actually engaging. The fact that 
you''re willing to send the first message when something feels right says a lot.
```

**Why it works:**
- Perceptive ("not just scrolling")
- Specific ("send the first message")
- Encouraging without being hollow ("says a lot")

### Bad Coaching (what we avoid)

```
Great job this week! You had 12 interactions and sent 3 first messages. That''s 
above average! Keep it up!
```

**Why it''s bad:**
- Metric dashboard ("12 interactions")
- Comparison ("above average")
- Hollow cheerleading ("Great job!")

**The difference:** reflection vs. metrics.

---

## See Also

- [workers.md](./workers.md) — CoachingSummaryWorker schedule + batch logic
- [delivery.md](./delivery.md) — When summaries are delivered
- [api.md](./api.md) — Coaching endpoints
- [docs/systems/echo/coach.md](../echo/coach.md) — ECHO''s coaching persona
