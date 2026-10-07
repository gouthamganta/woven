# Weekly Coaching System

ECHO delivers a private, warm coaching summary every Wednesday to qualified users. It's not a metric dashboard — it's a perceptive friend reflecting back what they noticed about your week.

---

## Overview

**Worker:** `CoachingSummaryWorker`  
**Schedule:** Wednesday 18:00 UTC  
**Model:** `gpt-4.1-mini`  
**Temperature:** 0.8 (higher than other agents — encourages warmth)  
**Max Tokens:** 200  
**Output:** 3-5 sentences, plain text, no headers

---

## Eligibility

A user gets a coaching summary if:
1. **Account ≥14 days old** (CreatedAt ≤ now - 14 days)
2. **≥3 deck interactions in prior 7 days** (sum of DailyInteractions.TotalUsed ≥ 3)
3. **Not opted out** (CoachingOptedOut == false)
4. **No existing summary for this week** (no CoachingSummary row for this week_start_date)

**Batch eligibility check:**
```sql
SELECT UserId FROM Users
WHERE CreatedAt <= @cutoffDate
  AND CoachingOptedOut = FALSE
  
INTERSECT

SELECT UserId FROM DailyInteractions
WHERE DateUtc >= @signalFrom
GROUP BY UserId
HAVING SUM(TotalUsed) >= 3

EXCEPT

SELECT UserId FROM CoachingSummaries
WHERE WeekStartDate = @weekStart
```

---

## Data Captured (Permitted Signals Only)

CoachingSummaryWorker **only** uses permitted behavioral signals. It never accesses:
- Private chat content (messages, chat notes)
- Profile photos
- Voice note audio
- Match scores or compatibility data
- Demographic data (beyond age)

**What it does use:**
- `DailyInteractions.TotalUsed` — how many deck cards they engaged with
- `MatchSignalLogs` for these event types only:
  - `TimeToFirstMessageMs` (they sent first message)
  - `TrialMessageCount` (conversation depth)
  - `DateIdeaAccepted` (showed intent to meet)
  - `MutualVoiceExchange` (vulnerability signal)
  - `TrialAccepted` (chose to continue)

---

## Signal Aggregation

**7-day window:** signals from `now - 7 days` to `now`

**Aggregates computed:**
```csharp
var deckInteractions = await db.DailyInteractions
    .Where(d => d.UserId == userId && d.DateUtc >= signalFrom)
    .SumAsync(d => d.TotalUsed);

var signalCounts = await db.MatchSignalLogs
    .Where(s => s.ViewerUserId == userId && s.OccurredAt >= signalFrom)
    .GroupBy(s => s.EventType)
    .Select(g => new { EventType = g.Key, Count = g.Count(), AvgValue = g.Average(s => s.EventValue) })
    .ToListAsync();
```

---

## Narrative Generation (C# Pre-Processing)

Before calling GPT-4.1-mini, `CoachingSummaryWorker` builds a **deterministic narrative** in C# that interprets signals without showing raw numbers.

This ensures:
- No metrics leak to users ("you sent 47 messages")
- Consistent quality (not dependent on GPT hallucinating from sparse data)
- Privacy (GPT never sees PII)

### Narrative Builder Logic

```csharp
private static string BuildNarrative(
    int userId,
    int deckInteractions,
    Dictionary<string, CoachingSignalStat> signalMap)
{
    var sb = new StringBuilder();
    sb.Append("This person has been active on the app this week. ");

    // Deck engagement (bucketed)
    if (deckInteractions >= 10)
        sb.Append("They showed up consistently, reviewing a solid set of potential connections. ");
    else if (deckInteractions >= 3)
        sb.Append("They engaged with their daily deck, taking their time with the profiles they saw. ");

    // Conversations started
    var firstMessages = GetCount(signalMap, MatchSignalEventTypes.TimeToFirstMessageMs);
    if (firstMessages > 1)
        sb.Append("When they felt a connection, they were willing to make the first move. ");
    else if (firstMessages == 1)
        sb.Append("They took the step of sending the first message in at least one conversation. ");

    // Conversation depth
    var avgMessages = GetAvg(signalMap, MatchSignalEventTypes.TrialMessageCount);
    if (avgMessages >= 15)
        sb.Append("Their conversations have gone deep — there's real exchange happening, not just surface pleasantries. ");
    else if (avgMessages >= 5)
        sb.Append("They're having real conversations, not just exchanging pleasantries. ");

    // Date ideas
    var dateIdeasAccepted = GetCount(signalMap, MatchSignalEventTypes.DateIdeaAccepted);
    if (dateIdeasAccepted > 0)
        sb.Append("They showed genuine interest in meeting — that takes courage. ");

    // Voice exchange
    var voiceExchanges = GetCount(signalMap, MatchSignalEventTypes.MutualVoiceExchange);
    if (voiceExchanges > 0)
        sb.Append("Voice notes were exchanged — something about that connection felt worth hearing in a real voice. ");

    // Trial continuation
    var trialsAccepted = GetCount(signalMap, MatchSignalEventTypes.TrialAccepted);
    if (trialsAccepted > 0)
        sb.Append("They chose to continue at least one trial — they're not just going through motions. ");

    return sb.ToString().Trim();
}
```

### Example Narrative
```
This person has been active on the app this week. They showed up consistently, 
reviewing a solid set of potential connections. When they felt a connection, they 
were willing to make the first move. Their conversations have gone deep — there's 
real exchange happening, not just surface pleasantries. Voice notes were exchanged 
— something about that connection felt worth hearing in a real voice. They chose 
to continue at least one trial — they're not just going through motions.
```

**No numbers.** No comparisons. Just observed patterns.

---

## GPT-4.1-mini Call

The C# narrative is passed to GPT-4.1-mini with this system prompt:

```
You are a warm, perceptive friend who happens to understand how dating apps work. 
You know this person has been putting themselves out there this week. 

Your job is to reflect back what you noticed — genuine, specific, encouraging 
without being hollow. 

Never mention numbers directly. Never compare them to others. Never imply they 
should change who they are. 

If there is nothing genuinely positive or actionable to say, return the string "SUPPRESS".
```

**User prompt:** the C# narrative (above)

**Temperature:** 0.8 (higher than usual — encourages natural, varied language)  
**Max tokens:** 200

### Example GPT Output
```
You showed up this week — not just scrolling, actually engaging. The fact that you're 
willing to send the first message when something feels right says a lot. And those deeper 
conversations? That's not everyone. Keep trusting your instincts about who's worth the energy.
```

---

## Suppression Logic

A summary is **not saved** if:
1. GPT returns the string `"SUPPRESS"` (case-insensitive)
2. GPT output is `< 50 characters` (too short to be meaningful)
3. GPT output is empty/whitespace

This prevents hollow coaching like "Great week!" when nothing meaningful happened.

**Suppression rate:** ~15-20% of eligible users (expected — not everyone has signal-rich weeks)

---

## Storage

```sql
INSERT INTO CoachingSummaries (
    UserId,
    WeekStartDate,
    SummaryText,
    InterpretedNarrative,  -- C# narrative for debugging
    DeliveredAt
)
VALUES (
    @userId,
    @weekStart,            -- Monday of this week (DateOnly)
    @summaryText,          -- GPT output
    @narrative,            -- C# pre-processing output
    @now
);
```

**TTL:** 90 days (cleanup runs in preamble of CoachingSummaryWorker)

```sql
DELETE FROM CoachingSummaries WHERE CreatedAt < NOW() - INTERVAL '90 days';
```

---

## Delivery

**Backend:**
- `CoachingSummaryWorker` writes to DB
- No push notification sent (user discovers it organically in /you tab)

**Frontend:**
- `GET /coaching/latest` returns most recent summary (if any)
- Displayed in **You** tab as a card with subtle styling
- Users can opt out via `PATCH /settings { coachingOptedOut: true }`

---

## Privacy & Trust

**What coaching knows:**
- You engaged with 10+ deck cards this week
- You sent the first message in 2 conversations
- You accepted a trial continuation
- You exchanged voice notes

**What coaching never sees:**
- Who you messaged
- What you said
- Which profiles you skipped
- Your match scores
- Your photos

**Why this matters:**
- Users trust coaching because it's grounded in their own behavior
- No "the algorithm thinks you should..." framing
- No shaming ("you only messaged 1 person this week")
- No pressure ("users like you typically message 5 people")

---

## Tone Calibration

**Good coaching:**
```
"You showed up this week — not just scrolling, actually engaging. The fact that 
you're willing to send the first message when something feels right says a lot."
```

**Bad coaching (what we avoid):**
```
"Great job this week! You had 12 interactions and sent 3 first messages. That's 
above average! Keep it up!"
```

The difference:
- Good = perceptive reflection
- Bad = metric dashboard with cheerleading

---

## Failure Modes

**OpenAI API down:**
- Worker logs error
- No summary written for this user
- Next week retries (no permanent data loss)

**User deleted account:**
- CoachingSummaries rows orphaned (UserId FK set to ON DELETE CASCADE in future migration)

**User opts out mid-batch:**
- Eligibility check runs at batch start, so they might get one last summary
- Subsequent weeks respect opt-out

---

## Monitoring

**Key metrics:**
- Eligible users per week
- Suppression rate (% where GPT returned SUPPRESS or <50 chars)
- Avg summary length (chars)
- OpenAI token usage per summary
- Batch duration (should be <30min for 1000 users)

**Alerts:**
- Suppression rate >40% (GPT prompt degraded)
- Batch duration >1h (performance regression)
- OpenAI API failure rate >10% (upstream issue)

---

## Testing

**Unit test: Narrative builder**
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
}
```

**Integration test: Full flow**
```csharp
[Test]
public async Task CoachingSummaryWorker_QualifiedUser_CreatesSummary()
{
    // Arrange: seed user + 7 days of signals
    var user = CreateTestUser(createdAt: now.AddDays(-20));
    SeedDailyInteractions(user.Id, totalUsed: 8, dateRange: last7Days);
    SeedSignals(user.Id, new[] { TrialAccepted, MessageSent, DateIdeaAccepted });

    // Act
    await worker.ProcessUserAsync(db, apiKey, user.Id, weekStart, signalFrom, now, ct);

    // Assert
    var summary = await db.CoachingSummaries.FirstOrDefaultAsync(c => c.UserId == user.Id);
    Assert.That(summary, Is.Not.Null);
    Assert.That(summary.SummaryText.Length, Is.GreaterThan(50));
    Assert.That(summary.SummaryText, Does.Not.Contain("12")); // No raw numbers
}
```

---

## Future Enhancements

**Personalization:**
- After 4+ weeks, detect patterns: "You tend to be more active on weekends."
- Celebrate milestones: "First time you sent a voice note — that's a big step."

**Actionable tips (opt-in):**
- If TrialMessageCount consistently low: "You're starting conversations but not digging deeper. Try asking follow-up questions."
- If DateIdeaAccepted == 0 after 8 weeks: "You're building rapport in chat — when's the right time to suggest meeting?"

**Voice delivery (future):**
- TTS version of coaching summary (same voice as narrator: `nova`)
- User toggles text vs voice in settings

---

## See Also

- [agents.md](./agents.md) — Other ECHO agents
- [signals.md](./signals.md) — All signal types
- [workers.md](./workers.md) — Batch job schedules
