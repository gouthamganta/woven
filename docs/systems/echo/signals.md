# ECHO Behavioral Signals

ECHO learns from 40+ behavioral signal types, all logged to the `MatchSignalLogs` table. These signals feed into `ConnectionScore` aggregation (nightly 03:50 UTC) and ultimately into weight learning (Sunday 04:00 UTC).

**Entity:** `backend/WovenBackend/Data/Entities/MatchSignalLog.cs`  
**Service:** `IMatchSignalService.RecordAsync(...)`

---

## Signal Schema

```csharp
public class MatchSignalLog
{
    public long Id { get; set; }
    public int ViewerId { get; set; }          // User who generated the signal
    public int CandidateId { get; set; }       // User being viewed/interacted with
    public string EventType { get; set; }      // One of MatchSignalEventTypes constants
    public float EventValue { get; set; }      // Interpretation depends on EventType
    public string? MetadataJson { get; set; }  // Optional context (tile type, match stage, etc.)
    public DateTimeOffset OccurredAt { get; set; }
}
```

---

## Signal Categories

### 1. Profile Engagement Signals

Passive interest signals from browsing profiles.

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **TileDwell** | Dwell time (ms) | User views a tile for ≥2s | Measures content engagement |
| **VoiceDwell** | Dwell time (ms) | User plays voice note for ≥2s | Measures voice engagement |
| **ProfileVisitDepth** | Tile count viewed | User scrolls through profile | Measures curiosity/interest depth |

**Example:**
```csharp
await _signalService.RecordAsync(new MatchSignalLog
{
    ViewerId = userId,
    CandidateId = tileOwnerId,
    EventType = MatchSignalEventTypes.TileDwell,
    EventValue = 12500f, // 12.5 seconds
    MetadataJson = JsonSerializer.Serialize(new { tileType = "TEXT" }),
    OccurredAt = DateTimeOffset.UtcNow
});
```

---

### 2. Balloon & Trial Signals

Match progression through the balloon → trial → full chat flow.

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **BalloonPop** | 1.0 | User pops balloon (mutual like) | Explicit mutual interest |
| **TrialRequested** | 1.0 | User requests trial (Drawn action) | Chose to unlock chat |
| **TrialAccepted** | 1.0 | User continues after 3min trial | High-intent positive signal |
| **TrialRejected** | 0.0 | User ends trial (END/BLOCK) | Explicit rejection |

**Trial end reasons** (separate signals):

| EventType | EventValue | Reason | Purpose |
|---|---|---|---|
| **TrialEndedNoSpark** | 1.0 | "no_spark" | Preference learning (what they didn't like) |
| **TrialEndedWrongTiming** | 1.0 | "wrong_timing" | Not about compatibility |
| **TrialEndedNotMyType** | 1.0 | "not_my_type" | Strong preference signal |

**ConnectionScore formula:**
- BalloonPopped: 0.05
- TrialRequested: 0.10
- TrialAccepted: 0.22

---

### 3. Conversation Signals

Chat depth, responsiveness, self-disclosure.

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **MessageSent** | 1.0 | User sends a chat message | Conversation depth (count) |
| **MessageResponseLatencyMs** | Latency (ms) | User replies to a message | Response speed (engagement) |
| **TimeToFirstMessageMs** | Latency (ms) | User sends first message in thread | Initiative + interest |
| **SelfDisclosureRatio** | [0.0, 1.0] | After each message (NLP analysis) | Emotional depth score |
| **TrialMessageCount** | Message count | At trial end | Conversation depth during trial window |

**SelfDisclosureRatio:**
- Computed via `SelfDisclosureBatchWorker` (analyzes message text for emotional depth)
- 0.0 = surface-level ("how's your day?")
- 1.0 = deep disclosure ("I've been thinking a lot about...")

**ConnectionScore formula:**
- ConversationDepth: 0.20 × (MessageSent count / 20, capped at 1.0)

---

### 4. Date Intent Signals

Explicit signals of wanting to meet IRL.

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **DateIdeaAccepted** | 1.0 | User accepts a date idea | High-intent positive signal |
| **DateIdeaRejected** | 0.0 | User rejects a date idea | Preference learning |

**ConnectionScore formula:**
- DateAccepted: 0.15

---

### 5. Feedback Signals

Explicit post-date or post-match feedback.

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **ExplicitFeedback** | [0.0, 1.0] | User submits 1-5 star feedback | Normalized to [0,1]: (rating-1)/4 |
| **ChatNoteLove** | 1.0 | User ❤️ reacts to a chat note | Positive sentiment |
| **MessageLove** | 1.0 | User ❤️ reacts to a message | Positive sentiment |

**ConnectionScore formula:**
- ExplicitFeedback: 0.13 × normalized value
- LoveReactions: 0.08 × (ChatNoteLove + MessageLove count / 3, capped at 1.0)

---

### 6. Game Signals

Engagement and alignment in conversational games.

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **GameCompleted** | 1.0 | User completes a game session | Engagement signal |
| **KnowMeDisclosureDepth** | [0.0, 1.0] | After Know Me game | completed_rounds / total_rounds |
| **RedFlagGameDepth** | [0.0, 1.0] | After Red/Green Flag game | completed_rounds / total_rounds |
| **FlagAgreementRate** | [0.0, 1.0] | After Red/Green Flag game | % of statements where both agreed |

**Use case:**
- GameCompleted → general engagement metric
- DisclosureDepth → measures willingness to play (completion rate)
- FlagAgreementRate → measures value alignment (do they judge behaviors similarly?)

---

### 7. Voice Signals

Voice note engagement — strong vulnerability/trust indicators.

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **MutualVoiceExchange** | 1.0 | Both users sent voice notes | Vulnerability signal (both stepped up) |
| **VoiceNoteListenComplete** | 1.0 | User played voice note to end | Genuine interest (didn't skip) |

**ConnectionScore formula:**
- VoiceExchange: 0.06
- VoiceCompleted: 0.03 × (listen count / 2, capped at 1.0)

**Why this matters:**
- Voice notes are high-effort (more vulnerability than text)
- Listening to completion = genuine curiosity
- Mutual exchange = both willing to be vulnerable

---

### 8. Safety Signals

Trust/safety flags — **never mixed into compatibility scoring.**

| EventType | EventValue | When Logged | Purpose |
|---|---|---|---|
| **UserFlagged** | 1.0 | User flags candidate (safety report) | Feeds TrustScore only |

**MetadataJson:**
```json
{
  "reason": "uncomfortable" | "inappropriate"
}
```

**Handling:**
- UserFlagged signals feed `TrustScore` computation
- TrustScore < 0.25 → excluded from candidate pool (hard filter)
- TrustScore ≥ 0.25 → eligible, but TrustScore is a penalty multiplier [0.5–1.0]
- **Never** used in weight learning (ConnectionScore aggregation excludes this type)

---

## Signal Aggregation (ConnectionScore)

**Worker:** `ConnectionScoreBatchWorker`  
**Schedule:** Daily 03:50 UTC  
**Lookback:** 90 days (rolling window)

**Formula:**
```csharp
var score = 
    0.05  × balloonPopped      +  // Binary [0,1]
    0.10  × trialRequested     +  // Binary [0,1]
    0.22  × trialAccepted      +  // Binary [0,1]
    0.20  × convDepth          +  // MessageSent count / 20, capped 1.0
    0.15  × dateAccepted       +  // Binary [0,1]
    0.13  × explicitFb         +  // [0,1] from 1-5 stars
    0.08  × loveReactions      +  // (ChatNoteLove + MessageLove) / 3, capped 1.0
    0.06  × voiceExchange      +  // Binary [0,1]
    0.03  × voiceCompleted;       // Listen count / 2, capped 1.0
```

**Output:** `ConnectionScores` table, one row per (ViewerId, CandidateId) with `Score` [0, 1].

**This is ECHO's outcome label for weight learning.**

---

## Signal Retention

**Window:** 90 days (rolling)  
**Cleanup:** ConnectionScoreBatchWorker only processes signals from `OccurredAt >= now - 90 days`

Older signals still exist in `MatchSignalLogs` (no automatic deletion) but aren't used for learning.

---

## Privacy & Consent

All signals are:
- **First-party** (generated within Woven app)
- **Consensual** (user initiated the action)
- **Anonymized in aggregates** (ConnectionScore doesn't reveal which candidate)

What ECHO **never** captures:
- Message content (only metadata: count, latency, self-disclosure score)
- Voice audio (only embeddings)
- Photo pixels (only embeddings)
- Location tracking (only distance for filtering)
- Social graph scraping
- Device fingerprinting

---

## Signal Quality

**Good signals:**
- TrialAccepted (high-intent, low-noise)
- MutualVoiceExchange (vulnerability proxy)
- ExplicitFeedback (direct outcome)

**Noisy signals:**
- TileDwell (can be accidental scroll)
- MessageResponseLatencyMs (affected by timezone, work schedule)

**Why we aggregate:**
- ConnectionScore combines 9 signal types → robust to individual noise
- Weight learning uses 10+ ConnectionScore samples → further smooths noise

---

## Logging Best Practices

**When to log:**
- Log immediately after user action (don't batch)
- Use `DateTimeOffset.UtcNow` (not client-side timestamp)

**Idempotency:**
- Most signals are count-based (multiple logs = higher count)
- Exception: BalloonPop (only logged once per match)

**Correlation:**
- Use `CorrelationIdMiddleware` to link signal to HTTP request
- Helps debug: "which deck view led to this signal?"

---

## Testing Signals

**Unit test: Record signal**
```csharp
[Test]
public async Task RecordSignal_TrialAccepted_StoresCorrectValues()
{
    await _signalService.RecordAsync(new MatchSignalLog
    {
        ViewerId = 1,
        CandidateId = 2,
        EventType = MatchSignalEventTypes.TrialAccepted,
        EventValue = 1.0f,
        OccurredAt = DateTimeOffset.UtcNow
    });

    var signal = await _db.MatchSignalLogs
        .FirstOrDefaultAsync(s => s.ViewerId == 1 && s.CandidateId == 2);

    Assert.That(signal, Is.Not.Null);
    Assert.That(signal.EventType, Is.EqualTo(MatchSignalEventTypes.TrialAccepted));
    Assert.That(signal.EventValue, Is.EqualTo(1.0f));
}
```

**Integration test: ConnectionScore aggregation**
```csharp
[Test]
public async Task ConnectionScoreBatch_TrialAccepted_ComputesScore()
{
    // Arrange: seed signals
    SeedSignal(viewerId: 1, candidateId: 2, MatchSignalEventTypes.BalloonPop, 1.0f);
    SeedSignal(viewerId: 1, candidateId: 2, MatchSignalEventTypes.TrialAccepted, 1.0f);
    SeedSignal(viewerId: 1, candidateId: 2, MatchSignalEventTypes.MessageSent, 1.0f, count: 10);

    // Act
    await _worker.RunBatchAsync(ct);

    // Assert
    var score = await _db.ConnectionScores
        .FirstOrDefaultAsync(c => c.ViewerId == 1 && c.CandidateId == 2);

    Assert.That(score, Is.Not.Null);
    var expected = 0.05 + 0.22 + 0.20 * (10 / 20.0); // Balloon + Trial + ConvDepth
    Assert.That(score.Score, Is.EqualTo(expected).Within(0.01));
}
```

---

## Future Signals (Planned)

| EventType | Purpose |
|---|---|
| **PhotoUploadEvent** | Track when users update photos (recency signal) |
| **ProfileEditDepth** | How many fields they filled out (completeness signal) |
| **TileEngagementRate** | % of shown tiles they dwelled on (selectivity signal) |
| **ResponseConsistency** | Variance in response latency (reliability signal) |

---

## See Also

- [learning.md](./learning.md) — How signals feed weight learning
- [coach.md](./coach.md) — Which signals coaching uses (subset)
- [workers.md](./workers.md) — ConnectionScoreBatchWorker schedule
