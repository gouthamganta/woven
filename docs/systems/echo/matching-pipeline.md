# ECHO Matching Pipeline

This document walks through ECHO's daily deck generation — the core matchmaking flow that runs on-demand (with 24h cache) or as part of nightly batch prep.

---

## Pipeline Entry Point

**Method:** `DailyDeckOrchestrator.GetOrCreateDeckAsync(int userId, DateOnly dateUtc)`  
**Caller:** `GET /moments` (frontend), `DeckGenerationBatchWorker` (nightly prep, future)  
**File:** `backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs`

### Flow Summary

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Redis Fast-Path (Phase 1B)                              │
│    ✓ Check Redis: DailyDeck:{userId}:{dateUtc}             │
│    ✓ If hit → return cached List<DeckItem>, skip generation│
└─────────────────────────────────────────────────────────────┘
         │ (cache miss)
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. DB Check                                                 │
│    ✓ SELECT FROM DailyDecks WHERE UserId AND DateUtc       │
│    ✓ If exists → backfill Redis, return                    │
└─────────────────────────────────────────────────────────────┘
         │ (no existing deck)
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. CandidatePoolService — Eligible Candidates              │
│    SQL filters (no in-memory loops):                       │
│    • Exclude: self, blocks (bidirectional), active balloons│
│    • Exclude: shown today (CandidateExposures)             │
│    • Gender reciprocity (viewer's gender in candidate's    │
│      InterestedInJson, and vice versa)                     │
│    • Age range (candidate.Age in [viewer.AgeMin, AgeMax])  │
│    • Trust gate (candidate.TrustScore >= 0.25)             │
│    Returns: List<int> candidateIds                         │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. HardFilterService — Binary Exclusions                   │
│    • Age reciprocity (mutual age range overlap)            │
│    • Distance (if location shared, haversine < MaxDistance)│
│    Returns: filtered List<int>                             │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. MatchScoringService — Score All Candidates              │
│    For each candidate:                                      │
│    • Load UserVectors (viewer + candidate, latest version) │
│    • Compute 16 component scores [0, 100]                  │
│    • Apply intent multiplier [0.7–1.05]                    │
│    • Apply trust penalty [0.5–1.0]                         │
│    • Use learned weights if available (≥8 components)      │
│    Returns: List<MatchScore> (one per candidate)           │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. Boost Maps                                               │
│    • DeliveryBoostService: recency/re-delivery [0–10]      │
│    • LinUcbService: exploration bonus [0–10]               │
│    Combined cap ~20 (additive on TotalScore [0–100])       │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 7. DeckSelectionService — Pick Top 5 with Diversity        │
│    Distribution-first selection:                           │
│    • 2 CORE_FIT (high intent + pillar)                     │
│    • 1 LIFESTYLE_FIT (high lifestyle score)                │
│    • 1 CONVERSATION_FIT (high pulse score)                 │
│    • 1 EXPLORER (low pillar from top-20 pool = different)  │
│    Returns: List<(CandidateId, Bucket)>                    │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 8. Mood Line Generation                                    │
│    Deterministic C# logic based on score distribution:     │
│    • top ≥85, avg ≥80 → "Something feels right today."     │
│    • avg ≥77 → "Your deck today is sharp."                 │
│    • avg <58, pool ≥15 → "ECHO is finding your frequency." │
│    • pool <8 → "ECHO is still building your pool."         │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 9. MatchExplanationService — Generate Explanations         │
│    For each of the 5 selected candidates:                  │
│    • Call GPT-4.1-mini with MatchScore breakdown           │
│    • Generate 2-sentence explanation                       │
│    • Store in MatchExplanations table                      │
│    Returns: explanationId per candidate                    │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 10. (Optional) MatchNarratorService — Cinematic Enrichment │
│     For each of the 5 selected candidates (parallel):      │
│     • Load 3 profile photos → KenBurnsPhotoUrls            │
│     • Pick curated quote (bio/answers, ≤120 chars)         │
│     • Generate TTS (cached in Azure Blob, 48h TTL)         │
│     Returns: NarrationUrl (or null if TTS fails)           │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 11. Record Exposures                                       │
│     INSERT INTO CandidateExposures (                       │
│       ViewerUserId, ShownUserId, Surface='DECK',           │
│       Bucket, ScoreSnapshot, DateUtc                       │
│     )                                                       │
│     Prevents re-showing same candidates today.             │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 12. Save Deck (Dual-Write)                                 │
│     • DailyDecks: ItemsJson (backward compat), MoodLine   │
│     • DailyDeckItems: normalized rows (analytics-friendly) │
│     • Cache in Redis until UTC midnight                    │
│     • Send push notification: DeckReadyAsync               │
└─────────────────────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────────────────────┐
│ 13. Return DailyDeckResult                                 │
│     {                                                       │
│       Items: List<DeckItem>,                               │
│       Generated: true,                                     │
│       MoodLine: "Your deck today is sharp."                │
│     }                                                       │
└─────────────────────────────────────────────────────────────┘
```

---

## Step-by-Step Breakdown

### Step 1: Redis Fast-Path

**Why:** Avoid DB round-trip + re-scoring for repeated calls on same day.

```csharp
var cacheKey = CacheKeys.DailyDeck(userId, dateUtc);
var cached = await _cache.GetAsync<List<DeckItem>>(cacheKey, ct);
if (cached != null)
{
    _logger.LogInformation("[DeckOrchestrator] Redis cache hit for user {UserId}", userId);
    return new DailyDeckResult { Items = cached, Generated = false };
}
```

**TTL:** Until UTC midnight (so deck refreshes daily at 00:00 UTC)

---

### Step 2: DB Check

If Redis miss, check if deck exists in `DailyDecks` table (maybe cache was cleared).

```csharp
var existingDeck = await _db.DailyDecks
    .AsNoTracking()
    .FirstOrDefaultAsync(d => d.UserId == userId && d.DateUtc == dateUtc, ct);

if (existingDeck != null)
{
    var items = JsonSerializer.Deserialize<List<DeckItem>>(existingDeck.ItemsJson);
    await _cache.SetAsync(cacheKey, items, CacheTtl.UntilMidnightUtc(), ct);
    return new DailyDeckResult { Items = items, Generated = false, MoodLine = existingDeck.MoodLine };
}
```

---

### Step 3: CandidatePoolService

**File:** `backend/WovenBackend/Services/Matchmaking/CandidatePoolService.cs`

**Goal:** Return all eligible candidate IDs. All filters run in SQL — no in-memory loops.

**SQL Filters Applied:**
1. **Self-exclusion:** `UserId != viewer.Id`
2. **Blocks (bidirectional):**
   ```sql
   UserId NOT IN (
       SELECT BlockedId FROM Blocks WHERE BlockerId = @viewerId
       UNION
       SELECT BlockerId FROM Blocks WHERE BlockedId = @viewerId
   )
   ```
3. **Active balloons:**
   ```sql
   UserId NOT IN (
       SELECT CASE WHEN UserAId = @viewerId THEN UserBId ELSE UserAId END
       FROM Matches
       WHERE BalloonState = 'ACTIVE' AND (@viewerId IN (UserAId, UserBId))
   )
   ```
4. **Shown today:**
   ```sql
   UserId NOT IN (
       SELECT ShownUserId FROM CandidateExposures
       WHERE ViewerUserId = @viewerId AND DateUtc = @today
   )
   ```
5. **Gender reciprocity (viewer's pref):**
   ```sql
   Gender IN (SELECT value FROM json_each(@viewerInterestedIn))
   OR @viewerInterestedIn IS NULL
   ```
6. **Age range (viewer's pref):**
   ```sql
   Age BETWEEN @viewerAgeMin AND @viewerAgeMax
   ```
7. **Trust gate:**
   ```sql
   TrustScore >= 0.25
   ```
8. **Gender reciprocity (candidate's pref):**
   ```sql
   InterestedInJson LIKE '%"' || @viewerGender || '"%'
   OR @viewerGender IS NULL
   ```

**Return:** `List<int> candidateIds` (typically 50–500 depending on market)

**Performance:** ~100ms for 100k users in pool (indexed filters)

---

### Step 4: HardFilterService

**File:** `backend/WovenBackend/Services/Matchmaking/HardFilterService.cs`

**Filters:**
1. **Age reciprocity:** Mutual age range overlap
   ```csharp
   var candidateAgeMax = candidate.AgeMax;
   var candidateAgeMin = candidate.AgeMin;
   var viewerAge = viewer.Age;
   
   if (viewerAge < candidateAgeMin || viewerAge > candidateAgeMax)
       exclude();
   ```

2. **Distance:** Haversine < MaxDistance (if both users shared location)
   ```csharp
   var distance = Haversine(viewer.Lat, viewer.Lon, candidate.Lat, candidate.Lon);
   if (distance > viewer.MaxDistanceKm)
       exclude();
   ```

**Return:** Filtered `List<int>` (typically loses 10-20% of pool)

---

### Step 5: MatchScoringService

**File:** `backend/WovenBackend/Services/Matchmaking/MatchScoringService.cs`

**Input:** `List<int> candidateIds`  
**Output:** `List<MatchScore>` (one per candidate)

**See [scoring.md](./scoring.md) for full formula breakdown.**

**High-level steps:**
1. Load viewer's `UserVector` (latest version)
2. Load all candidates' `UserVectors` (latest per candidate)
3. Batch-load auxiliary data:
   - `CfScores` (collaborative filtering)
   - `OrbitGravities` (passive interest)
   - `UserVisualPreferences` (photo preference embedding)
   - `UserVoicePreferences` (voice preference embedding)
   - `UserOptionalFields` (lifestyle data)
   - `UserMatchingWeights` (learned weights, if ≥5 samples)
   - `PhotoEmbeddings` (primary photo per candidate)
   - `Tiles` with `VoiceEmbedding` (latest voice tile per candidate)
4. For each candidate:
   - Compute 16 component scores [0, 100]
   - Apply intent multiplier [0.7–1.05]
   - Apply trust penalty [0.5–1.0]
   - Use learned weights if available (≥8 components with data), else base weights
   - Total = weighted sum × intentMult × trustPenalty

**Performance:** ~1s per 100 candidates (batched SQL, parallel processing)

---

### Step 6: Boost Maps

**DeliveryBoostService:**
- **Recency boost:** +5 if not shown in past 7 days
- **Re-delivery boost:** +3 if shown 14+ days ago and high score
- **Cap:** 10

**LinUcbService:**
- **Exploration bonus:** UCB1 upper confidence bound [0–10]
- Uses 24-dim context vector (8 pillars + 16 behavioral fingerprint)
- Encourages trying candidates with high uncertainty

**Combined:**
```csharp
var boostMap = new Dictionary<int, double>();
foreach (var cid in candidateIds)
{
    var d = deliveryBoostMap.GetValueOrDefault(cid);
    var u = ucbBoostMap.GetValueOrDefault(cid);
    boostMap[cid] = d + u; // additive, cap ~20
}
```

**Effect:** Biases selection toward under-explored candidates without overriding core score.

---

### Step 7: DeckSelectionService

**File:** `backend/WovenBackend/Services/Matchmaking/DeckSelectionService.cs`

**Goal:** Pick 5 candidates with **diversity** across buckets.

**Strategy:**
1. **2 CORE_FIT:** Top 2 by `IntentScore + PillarScore + boost`
2. **1 LIFESTYLE_FIT:** Top 1 by `LifestyleScore + boost×0.5` (not already selected)
3. **1 CONVERSATION_FIT:** Top 1 by `PulseScore + boost×0.5` (not already selected)
4. **1 EXPLORER:** From top-20 by TotalScore, pick lowest `PillarScore` (most different)

**Fallback:** If <5 candidates remain, fill with top TotalScore (any bucket).

**Why this works:**
- Ensures users see variety (not 5 clones of their ideal)
- EXPLORER slot introduces serendipity (low pillar = different personality)
- Still respects quality (EXPLORER comes from top-20, not random)

---

### Step 8: Mood Line Generation

**Deterministic C# logic** (no AI):

```csharp
if (poolSize < 8)
    return "ECHO is still building your pool.";

if (top >= 85 && avg >= 80)
    return "Something feels right about today.";

if (avg >= 77)
    return "Your deck today is sharp.";

if (avg < 58 && poolSize >= 15)
    return "ECHO is still finding your frequency.";

return null; // No mood line
```

**Purpose:** Set user expectations without showing raw scores.

---

### Step 9: MatchExplanationService

**File:** `backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs`

For each of the 5 selected candidates:
1. Load `MatchScore` breakdown (16 component scores)
2. Identify top 2-3 dimensions
3. Call GPT-4.1-mini with structured prompt
4. Generate 2-sentence explanation
5. Store in `MatchExplanations` table with `explanationId`

**See [agents.md](./agents.md#3-matchexplanationservice) for details.**

---

### Step 10: MatchNarratorService (Optional)

**File:** `backend/WovenBackend/Services/Matchmaking/MatchNarratorService.cs`

If `IMatchNarratorService` is registered (non-null):
- Runs in parallel for all 5 candidates (Task.WhenAll)
- Adds: `KenBurnsPhotoUrls`, `CuratedQuote`, `NarrationUrl`
- TTS cached in Azure Blob (`tts/{candidateId}/{yyyyMMdd}.mp3`, 48h TTL)

**See [agents.md](./agents.md#4-matchnarratorservice) for details.**

---

### Step 11: Record Exposures

**Purpose:** Prevent re-showing same candidates today across all surfaces (DECK, MOMENTS, PENDING).

```csharp
var exposures = deckItems.Select(x => new CandidateExposure
{
    ViewerUserId = userId,
    ShownUserId = x.CandidateId,
    Surface = "DECK",
    Bucket = x.Bucket,
    ScoreSnapshot = x.Score,
    DateUtc = dateUtc,
    CreatedAt = DateTime.UtcNow
});

_db.CandidateExposures.AddRange(exposures);
await _db.SaveChangesAsync(ct);
```

---

### Step 12: Save Deck

**Dual-write:**
1. **DailyDecks** table (backward compat):
   ```sql
   INSERT INTO DailyDecks (UserId, DateUtc, GeneratedAt, ItemsJson, MoodLine)
   ```
   `ItemsJson` = JSON array of `DeckItem` (for rollback)

2. **DailyDeckItems** table (analytics-friendly):
   ```sql
   INSERT INTO DailyDeckItems (DeckId, CandidateId, Score, Bucket, Rank, CreatedAt)
   ```
   One row per candidate, queryable for analytics.

**Cache:**
```csharp
await _cache.SetAsync(cacheKey, deckItems, CacheTtl.UntilMidnightUtc(), ct);
```

**Notify:**
```csharp
await _notifications.DeckReadyAsync(userId, dateUtc, ct);
```

---

### Step 13: Return Result

```csharp
return new DailyDeckResult
{
    Items = deckItems,       // List<DeckItem> (5 candidates)
    Generated = true,        // Was this freshly generated?
    MoodLine = moodLine      // "Your deck today is sharp."
};
```

**Frontend receives:**
- 5 candidates with: `CandidateId`, `Score`, `Bucket`, `ExplanationId`
- Optional: `KenBurnsPhotoUrls`, `CuratedQuote`, `NarrationUrl`
- Mood line (nullable)

---

## Performance Characteristics

| Step | Typical Latency | Bottleneck |
|---|---|---|
| Redis fast-path | <5ms | Network RTT |
| DB check | ~20ms | Indexed lookup |
| CandidatePoolService | 100–300ms | SQL joins (indexed) |
| HardFilterService | 10–50ms | Haversine computation |
| MatchScoringService | 0.5–2s | Batch SQL + cosine similarity |
| DeliveryBoost + LinUcb | 50–100ms | Redis lookups |
| DeckSelectionService | <10ms | In-memory sorting |
| MatchExplanationService | 2–5s | 5× OpenAI API calls (parallel) |
| MatchNarratorService | 1–3s | 5× TTS calls (parallel, cached) |
| Save + Cache | 50–100ms | DB write + Redis SET |

**Total (cache miss):** 3–8 seconds  
**Total (cache hit):** <50ms

---

## Failure Modes

**No candidates in pool:**
- Returns empty `Items = []`, `Generated = true`
- Frontend shows: "ECHO is still building your pool."

**OpenAI API failure:**
- MatchExplanationService logs error, uses fallback explanation
- MatchNarratorService sets `NarrationUrl = null` (Ken Burns plays silently)

**Scoring failure:**
- Logs error, skips candidate (fewer than 5 in deck)
- If all candidates fail: returns empty deck

**Boost service failure:**
- Falls back to `boostMap = null` (no boost applied)
- Deck still generated, just less exploration

---

## Monitoring

**Key metrics:**
- Deck generation latency (p50, p95, p99)
- Candidates in pool per user (avg, min)
- Selection bucket distribution (should be ~2:1:1:1)
- Explanation generation success rate
- Redis cache hit rate
- Avg score per deck (should be right-skewed)

**Alerts:**
- p95 latency >10s
- Avg pool size <10 (market too small or filters too tight)
- Cache hit rate <80% (Redis eviction or TTL issue)

---

## See Also

- [scoring.md](./scoring.md) — 16-component formula
- [agents.md](./agents.md) — Explanation + narrator agents
- [embeddings.md](./embeddings.md) — 9 embedding modalities
- [workers.md](./workers.md) — Nightly batch jobs
