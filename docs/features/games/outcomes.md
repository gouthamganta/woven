# Game Outcomes — ECHO Signal Integration

**Status:** SHIPPED  
**Last Updated:** 2026-08-17

---

## Purpose

Games serve dual purposes:
1. **User-facing:** Fun icebreakers, learning about each other
2. **ECHO-facing:** Behavioral signals that improve matchmaking

**Critical:** Users are **never shown** that games feed ECHO. From their perspective, it's just entertainment.

---

## Signal Flow

```
Game Completed
    ↓
GameOutcomeService.RecordOutcomeAsync()
    ↓
GameOutcome entity saved
    ↓
LogGameSignalsAsync()
    ↓
MatchSignalLogs created
    ↓
ConnectionScoreBatchWorker (nightly)
    ↓
Updated ConnectionScores
    ↓
Future deck selection / match scoring
```

---

## GameOutcome Entity

**Table:** `game_outcomes`

**Fields:**
```csharp
public class GameOutcome
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public string GameType { get; set; }           // "KNOW_ME" | "RED_GREEN_FLAG"
    public int InitiatorUserId { get; set; }
    public int PartnerUserId { get; set; }
    public Guid MatchId { get; set; }
    
    // Personalization metadata (from GameSession.MetadataJson)
    public string Difficulty { get; set; }         // "EASY" | "MEDIUM" | "HARD"
    public string Tone { get; set; }               // "PLAYFUL" | "BALANCED" | "THOUGHTFUL"
    public string Bucket { get; set; }             // "CORE_FIT" | "LIFESTYLE_FIT" | etc.
    public double IntentAlignment { get; set; }    // 0.0 - 1.0
    
    // Performance metrics
    public int TotalRounds { get; set; }           // Usually 2
    public int CompletedRounds { get; set; }       // 0-2 (did they finish?)
    public int InitiatorScore { get; set; }        // Total points as guesser
    public int PartnerScore { get; set; }          // Total points as guesser
    public double AverageResponseTimeMs { get; set; }
    
    // Completion status
    public string CompletionStatus { get; set; }   // "COMPLETED" | "ABANDONED" | "EXPIRED"
    public string? UserFeedback { get; set; }      // Future: optional post-game feedback
    
    public DateTimeOffset CreatedAt { get; set; }
}
```

**Why metadata is stored:**
Allows future analytics:
- Which difficulty levels have highest completion rates?
- Do PLAYFUL games get better engagement than THOUGHTFUL?
- Do CORE_FIT matches complete more games?

---

## Signal Types

### 1. KnowMeDisclosureDepth

**Logged for:** KNOW_ME games  
**Event type:** `MatchSignalEventTypes.KnowMeDisclosureDepth`

**Formula:**
```csharp
var depthValue = (float)completedRounds / totalRounds;
```

**Examples:**
- Completed 2/2 rounds → 1.0
- Completed 1/2 rounds (abandoned) → 0.5
- Completed 0/2 rounds (rejected invite) → 0.0

**What it measures:**
- **Engagement depth:** Did they invest time learning about each other?
- **Mutual curiosity:** Finishing both rounds = both care about understanding the other
- **Icebreaker success:** Higher completion = game served its purpose

**Logged bidirectionally:**
```csharp
await _matchSignal.RecordAsync(initiatorUserId, partnerUserId,
    MatchSignalEventTypes.KnowMeDisclosureDepth, depthValue, ct: ct);
await _matchSignal.RecordAsync(partnerUserId, initiatorUserId,
    MatchSignalEventTypes.KnowMeDisclosureDepth, depthValue, ct: ct);
```

Both users get the same depth value (it's a pair-level metric).

---

### 2. RedFlagGameDepth

**Logged for:** RED_GREEN_FLAG games  
**Event type:** `MatchSignalEventTypes.RedFlagGameDepth`

**Formula:**
```csharp
var depthValue = (float)completedRounds / totalRounds;
```

**Same calculation as KnowMe** but tracked separately to distinguish game types.

**What it measures:**
- **Willingness to discuss boundaries:** Did they complete a values-exploration game?
- **Alignment exploration:** Higher depth = more investment in understanding compatibility
- **Red flag tolerance:** Completing the game = not scared off by hard questions

---

### 3. FlagAgreementRate

**Logged for:** RED_GREEN_FLAG games only  
**Event type:** `MatchSignalEventTypes.FlagAgreementRate`

**Formula:**
```csharp
// Load all scored rounds for this session
var rounds = await _db.GameRounds
    .Where(r => r.SessionId == sessionId && r.Score.HasValue)
    .Select(r => r.Score!.Value)
    .ToListAsync(ct);

if (rounds.Count > 0)
{
    var totalPossible = 3 * rounds.Count;  // 3 statements per round
    var agreementRate = (float)rounds.Sum() / totalPossible;
}
```

**Example calculation:**
- Round 1: User A guesses User B's flags → 2/3 matches
- Round 2: User B guesses User A's flags → 1/3 matches
- Total: (2 + 1) / (3 × 2) = 3/6 = **0.5**

**What it measures:**
- **Perception alignment:** How accurately do they read each other?
- **Values compatibility:** High agreement = similar worldviews on red/green flags
- **Communication clarity:** Are they expressing themselves in ways the other understands?

**Bidirectional logging:**
Both users receive the **same** agreement rate (it's a pair-level metric).

**ECHO use case:**
- Low agreement early → prioritize easier matches / more exploratory games
- High agreement → strong compatibility signal → boost in future match scoring

---

## Recording Flow

**Triggered by:** `GameService.CompleteGameAsync()`

**Steps:**

1. **Session completes** (both rounds finished)
2. **GameResult created** (scores, winner, AI insight)
3. **GameOutcomeService.RecordOutcomeAsync() called:**
   ```csharp
   await _gameOutcomeService.RecordOutcomeAsync(sessionId, new GameOutcomeData
   {
       TotalRounds = 2,
       CompletedRounds = rounds.Count(r => r.CompletedAt != null),
       InitiatorScore = rounds.Where(r => r.GuesserUserId == initiatorUserId).Sum(r => r.Score ?? 0),
       PartnerScore = rounds.Where(r => r.GuesserUserId != initiatorUserId).Sum(r => r.Score ?? 0),
       AverageResponseTimeMs = 0,  // Future: compute from round timestamps
       CompletionStatus = "COMPLETED"
   }, ct);
   ```

4. **GameOutcome entity created** in DB
5. **Metadata parsed** from `GameSession.MetadataJson`:
   ```csharp
   var metadata = ParseSessionMetadata(session.MetadataJson);
   gameOutcome.Difficulty = metadata.Difficulty ?? "MEDIUM";
   gameOutcome.Tone = metadata.Tone ?? "BALANCED";
   gameOutcome.Bucket = metadata.Bucket ?? "EXPLORER";
   gameOutcome.IntentAlignment = metadata.IntentAlignment;
   ```

6. **LogGameSignalsAsync() called** if `CompletedRounds > 0`

---

## LogGameSignalsAsync Implementation

**File:** `GameOutcomeService.cs:95-129`

```csharp
private async Task LogGameSignalsAsync(GameOutcome g, Guid sessionId, CancellationToken ct)
{
    var depthValue = (float)g.CompletedRounds / g.TotalRounds;

    if (g.GameType == "KnowMe")
    {
        // Log KnowMe depth for both users
        await _matchSignal.RecordAsync(g.InitiatorUserId, g.PartnerUserId,
            MatchSignalEventTypes.KnowMeDisclosureDepth, depthValue, ct: ct);
        await _matchSignal.RecordAsync(g.PartnerUserId, g.InitiatorUserId,
            MatchSignalEventTypes.KnowMeDisclosureDepth, depthValue, ct: ct);
    }
    else if (g.GameType == "RedGreenFlag")
    {
        // Log RedFlag depth for both users
        await _matchSignal.RecordAsync(g.InitiatorUserId, g.PartnerUserId,
            MatchSignalEventTypes.RedFlagGameDepth, depthValue, ct: ct);
        await _matchSignal.RecordAsync(g.PartnerUserId, g.InitiatorUserId,
            MatchSignalEventTypes.RedFlagGameDepth, depthValue, ct: ct);

        // Calculate and log flag agreement rate
        var rounds = await _db.GameRounds.AsNoTracking()
            .Where(r => r.SessionId == sessionId && r.Score.HasValue)
            .Select(r => r.Score!.Value)
            .ToListAsync(ct);

        if (rounds.Count > 0)
        {
            var totalPossible = 3 * rounds.Count;
            var agreementRate = (float)rounds.Sum() / totalPossible;
            
            await _matchSignal.RecordAsync(g.InitiatorUserId, g.PartnerUserId,
                MatchSignalEventTypes.FlagAgreementRate, agreementRate, ct: ct);
            await _matchSignal.RecordAsync(g.PartnerUserId, g.InitiatorUserId,
                MatchSignalEventTypes.FlagAgreementRate, agreementRate, ct: ct);
        }
    }
}
```

**IMatchSignalService.RecordAsync writes:**
- `MatchSignalLogs` table
- Fields: `UserAId`, `UserBId`, `EventType`, `SignalValue`, `OccurredAt`, `MatchId`, `Metadata`

---

## MatchSignalLogs Schema

**Table:** `match_signal_logs`

**Example rows after a RedGreenFlag game:**

| UserAId | UserBId | EventType | SignalValue | OccurredAt | MatchId | Metadata |
|---|---|---|---|---|---|---|
| 123 | 456 | RedFlagGameDepth | 1.0 | 2026-08-17 | abc-123 | `{}` |
| 456 | 123 | RedFlagGameDepth | 1.0 | 2026-08-17 | abc-123 | `{}` |
| 123 | 456 | FlagAgreementRate | 0.67 | 2026-08-17 | abc-123 | `{}` |
| 456 | 123 | FlagAgreementRate | 0.67 | 2026-08-17 | abc-123 | `{}` |

**Note:** Bidirectional logging = 2× rows (A→B and B→A) for every signal.

---

## ECHO Integration

### ConnectionScoreBatchWorker

**Runs:** Nightly at 03:50 UTC  
**File:** `Services/Matchmaking/ConnectionScoreBatchWorker.cs`

**What it does:**
- Aggregates `MatchSignalLogs` for each user pair
- Computes weighted connection score
- Upserts `ConnectionScores` table

**Game signal weights (as of June 2026):**
```csharp
// From ConnectionScoreBatchWorker formula:
KnowMeDisclosureDepth    → weight TBD (not yet in formula)
RedFlagGameDepth         → weight TBD (not yet in formula)
FlagAgreementRate        → weight TBD (not yet in formula)
```

**Current status:** Signal logging is **implemented** but weights are **not yet integrated** into the scoring formula. This is a **future enhancement**.

---

## Analytics Usage

**GameOutcomeService.GetGameAnalyticsAsync(userId)** provides:

```csharp
public class GameAnalyticsDto
{
    public int TotalGames { get; set; }
    public int CompletedGames { get; set; }
    public int AbandonedGames { get; set; }
    public double AverageScore { get; set; }
    public double WinRate { get; set; }
    
    // Breakdown by difficulty
    public Dictionary<string, DifficultyStats> ByDifficulty { get; set; }
    
    // Breakdown by tone
    public Dictionary<string, ToneStats> ByTone { get; set; }
    
    // Breakdown by game type
    public Dictionary<string, GameTypeStats> ByGameType { get; set; }
    
    // Best-performing settings
    public string BestPerformingDifficulty { get; set; }  // "MEDIUM"
    public string BestPerformingTone { get; set; }        // "PLAYFUL"
}
```

**Future use cases:**
- Show users their game stats in profile ("You've played 5 games, won 3")
- Badge for "Perfect Round" (3/3 in KnowMe)
- Adaptive difficulty (if user wins most EASY games, serve MEDIUM next time)

---

## Completion Status Values

| Status | Meaning |
|---|---|
| `COMPLETED` | All rounds finished successfully |
| `ABANDONED` | User exited mid-game (not yet implemented) |
| `EXPIRED` | Game invite expired before acceptance (not logged as outcome) |
| `REJECTED` | Invite rejected (not logged as outcome) |

**Note:** Only `COMPLETED` games generate ECHO signals currently.

---

## Privacy & Ethics

### User Consent

**Implicit consent via Terms of Service:**
- Users agree that gameplay data improves matchmaking
- No PII is shared — signals are aggregated behavioral metrics

**Not shown to users:**
- Raw signal values (e.g., "Your FlagAgreementRate is 0.67")
- How games affect their deck composition
- Connection score breakdowns

**User-facing only:**
- Game results (scores, winner, AI insight)
- Fun stats (games played, win rate) — **future feature**

### Data Retention

**GameOutcomes:** Stored indefinitely (analytics value)  
**MatchSignalLogs:** Stored indefinitely (ECHO requires historical data)  
**GameSessions:** Archived after 30 days (future: cleanup job)

---

## Future Enhancements

### Short-term
- [ ] **Integrate game signals into ConnectionScore formula** (assign weights)
- [ ] **Abandoned game tracking** (user exits mid-round)
- [ ] **Response time tracking** (how fast did they answer?)

### Medium-term
- [ ] **Adaptive difficulty** (adjust based on past performance)
- [ ] **Game stats in profile** (games played, win rate, badges)
- [ ] **Post-game feedback** ("Was this fun?" → improve agent prompts)

### Long-term
- [ ] **Game recommendation engine** (suggest KnowMe vs RedFlag based on pair context)
- [ ] **Custom game templates** (users submit question sets)
- [ ] **Seasonal games** (holiday-themed questions)

---

## Monitoring

**Logs to watch:**
```
[GameOutcome] Recording outcome for session {SessionId}, status={Status}
[GameOutcome] Recorded outcome {Id} for session {SessionId}
```

**Metrics to track:**
- Game completion rate (% of accepted games that finish both rounds)
- Average scores per game type
- Signal distribution (histogram of FlagAgreementRate values)
- Daily game volume (initiations, completions)

**Alerts:**
- Completion rate drops below 70% (indicates UX issue or bad questions)
- AI call failures exceed 5% (OpenAI outage or rate limit)
- Zero games completed in 24h (bug in game flow)
