# ECHO System Overview

**Last Updated:** 2026-08-17  
**Status:** Production

---

## What Is ECHO?

ECHO is Woven's behavioral AI matching system. It does three things:

1. **Learns from actions, not words** — 40+ behavioral signals (time-to-first-message, trial decisions, voice exchanges) feed a per-user weight-learning loop
2. **Scores compatibility across 16 dimensions** — pillar alignment, intent match, visual preference, voice similarity, orbit gravity, collaborative filtering, etc.
3. **Curates a daily deck of 5 people** — selected from hundreds of candidates, ranked and explained

**The name:** ECHO echoes back what you're not saying. It learns from what you do, not what you claim to want.

---

## Design Principles

### 1. Invisible AI
ECHO is infrastructure, not a feature. Users never see:
- Compatibility scores (e.g., "87.3% match")
- AI badges ("AI recommends this person")
- Community ratings ("other users rated 8.5/10")
- Leaderboards or gamified metrics

What they see instead:
- A curated daily deck with brief, grounded explanations
- Weekly coaching reflections (opt-in, warm, no metrics)
- Conversational games that reveal compatibility organically

### 2. Outcome-Driven, Not Engagement-Driven
ECHO optimizes for **good matches** (measured via ConnectionScore), not endless swiping.

ConnectionScore [0, 1] is a composite of 7 weighted signals:
- BalloonPopped (0.05) — mutual interest
- TrialRequested (0.10) — chose to unlock chat
- TrialAccepted (0.22) — chose to continue after 3min trial
- ConversationDepth (0.20) — message count / 20, capped at 1.0
- DateAccepted (0.15) — accepted a date idea
- ExplicitFeedback (0.13) — 5-star feedback normalized to [0,1]
- LoveReactions (0.08) — heart reactions on messages/notes
- VoiceExchange (0.06) — both sent voice notes (vulnerability signal)
- VoiceCompleted (0.03) — listened to voice note to completion

**This is ECHO's ground truth.** Weight learning uses ConnectionScore as the outcome label, not "user spent 5min on profile" or "user came back next day."

### 3. Privacy-First Signals
ECHO never uses:
- Location tracking beyond city-level (distance filter only)
- Device fingerprinting
- Social graph scraping
- Third-party data brokers
- Biometric analysis (photos are embedded, not analyzed for age/ethnicity/attractiveness)

All signals are first-party, consensual interactions within the app.

### 4. Trust Filtering, Not Trust Scoring
UserFlagged signals (safety flags from other users) feed TrustScore but **never mix into compatibility scoring.**

- TrustScore < 0.25 → excluded from candidate pool entirely (hard filter in SQL)
- TrustScore ≥ 0.25 → eligible, but TrustScore is a multiplicative penalty on final score (0.5–1.0 range)

This keeps compatibility clean: a low-trust user doesn't get matched *because* they're controversial — they're simply filtered out.

---

## Architecture Layers

```
┌─────────────────────────────────────────────────────────────────┐
│ Layer 1: DATA CAPTURE                                          │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ • Foundational Questions → 8 Pillars + Intent metadata         │
│ • User interactions → MatchSignalLogs (40+ event types)        │
│ • Visual decisions → UserVisualDecisions (YES/NO on photos)    │
│ • Voice tiles → VoiceEmbeddings (ECAPA-TDNN 192-dim)           │
│ • Tile dwell → OrbitGravity (passive interest signal)          │
└─────────────────────────────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 2: EMBEDDING GENERATION (nightly 02:30 UTC)              │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ EmbeddingBatchWorker runs 9 embedding services:                │
│ 1. PillarEmbedding (8-dim pillar scores → 128-dim OpenAI)      │
│ 2. ExpressionEmbedding (text tiles → 128-dim OpenAI)           │
│ 3. StyleEmbedding (writing style from tiles → 128-dim)         │
│ 4. HumorEmbedding (humor sense from answers → 128-dim)         │
│ 5. LifestyleEmbedding (optional fields → 128-dim)              │
│ 6. EmotionalRhythmEmbedding (emotional patterns → 128-dim)     │
│ 7. AttachmentProxyEmbedding (behavioral fingerprint → 128-dim) │
│ 8. VisualPreferenceEmbedding (YES photos mean → 512-dim CLIP) │
│ 9. VoiceEmbedding (voice tiles → 192-dim ECAPA-TDNN)           │
│                                                                 │
│ Also computes:                                                  │
│ • BehavioralLifestyleEmbedding (actual usage patterns)         │
│ • ReceptionEmbedding (content consumption taste, dwell ≥8s)    │
│ • PreferenceEmbedding (from ChatNotes — stated interests)      │
│                                                                 │
│ Output: UserVectors table (latest version per user)            │
└─────────────────────────────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 3: MATCHING PIPELINE (on-demand + nightly prep)          │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ DailyDeckOrchestrator.GetOrCreateDeckAsync(userId, dateUtc):   │
│                                                                 │
│ 1. CandidatePoolService                                        │
│    → SQL filters: blocks, active balloons, shown-today,        │
│      gender reciprocity, age range, trust gate (≥0.25)         │
│    → Returns: List<int> candidateIds                           │
│                                                                 │
│ 2. HardFilterService                                           │
│    → Age reciprocity (mutual age range overlap)                │
│    → Distance (if location shared)                             │
│    → Returns: filtered List<int>                               │
│                                                                 │
│ 3. MatchScoringService                                         │
│    → Scores all candidates via 16-component formula            │
│    → Each component [0, 100], weighted and summed              │
│    → Intent multiplier [0.7–1.05] based on seriousness match   │
│    → Trust penalty [0.5–1.0] multiplied at end                 │
│    → Returns: List<MatchScore>                                 │
│                                                                 │
│ 4. DeliveryBoostService + LinUcbService                        │
│    → DeliveryBoost: recency/re-delivery bonus [0–10]           │
│    → LinUcbBoost: exploration bonus [0–10]                     │
│    → Combined cap ~20 (biases but doesn't override score)      │
│                                                                 │
│ 5. DeckSelectionService.SelectTop5                             │
│    → 2 CORE_FIT (high intent + pillar)                         │
│    → 1 LIFESTYLE_FIT (high lifestyle score)                    │
│    → 1 CONVERSATION_FIT (high pulse score)                     │
│    → 1 EXPLORER (low pillar from top-20 pool = different)      │
│                                                                 │
│ 6. MatchExplanationService                                     │
│    → Generates 2-sentence explanation per candidate            │
│    → Optionally: MatchNarratorService adds cinematic fields    │
│      (KenBurnsPhotoUrls, CuratedQuote, NarrationUrl via TTS)   │
│                                                                 │
│ 7. Save DailyDeck + DailyDeckItems + CandidateExposures        │
│    → Cache in Redis until UTC midnight                         │
│    → Send push notification: DeckReadyAsync                    │
└─────────────────────────────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 4: SIGNAL AGGREGATION (nightly 03:50 UTC)                │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ ConnectionScoreBatchWorker:                                     │
│ • Loads MatchSignalLogs from last 90 days                      │
│ • Groups by (ViewerId, CandidateId, EventType)                 │
│ • Computes composite ConnectionScore [0, 1]                    │
│ • Bulk upserts into ConnectionScores table                     │
│                                                                 │
│ This is ECHO's outcome label for learning.                     │
└─────────────────────────────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 5: WEIGHT LEARNING (Sunday 04:00 UTC)                    │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ WeightLearningBatchWorker:                                      │
│ • For each user with ≥10 ConnectionScore samples:              │
│   1. Load ConnectionScores (outcome labels)                    │
│   2. Score same candidates via MatchScoringService (features)  │
│   3. Build feature matrix X (16 components) + label vector y   │
│   4. Run logistic regression (gradient ascent, 100 iter)       │
│   5. Normalize weights to sum=1, clamp [0.01, 0.50]            │
│   6. Save to UserMatchingWeights table                         │
│                                                                 │
│ Next deck uses learned weights instead of base weights.        │
└─────────────────────────────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 6: AUXILIARY SYSTEMS                                     │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ • CfScoreBatchWorker (daily 05:00 UTC)                         │
│   → Collaborative filtering: Jaccard similarity on orbit/dwell │
│   → Populates CfScores table (component #14)                   │
│                                                                 │
│ • LinUcbBatchWorker (daily 04:20 UTC)                          │
│   → Updates LinUCB bandit models (exploration bonus)           │
│   → Sherman-Morrison incremental A_inv update                  │
│                                                                 │
│ • CoachingSummaryWorker (Wed 18:00 UTC)                        │
│   → Weekly coaching summaries (gpt-4.1-mini, temp 0.8)         │
│   → Eligible: ≥14 days, ≥3 deck interactions/week              │
│   → 3-5 sentences, warm tone, no metrics                       │
│                                                                 │
│ • GameAgents (on-demand via WebSocket)                         │
│   → KnowMeAgent: generates guessing game questions             │
│   → RedGreenFlagAgent: generates red/green flag statements     │
│   → Both use IOpenAiResilientClient with circuit breaker       │
└─────────────────────────────────────────────────────────────────┘
```

---

## Data Flow

**User joins:**
1. Answers foundational questions → PillarScores + IntentMetadata
2. Adds photos → PhotoEmbeddings (CLIP)
3. Posts text tiles → ExpressionEmbedding
4. Optionally: posts voice tile → VoiceEmbedding

**Nightly 02:30 UTC:**
- EmbeddingBatchWorker runs 9 embedding services
- Writes latest UserVector per user

**User gets daily deck:**
- DailyDeckOrchestrator runs (cached for 24h)
- 5 candidates selected via 16-component scoring
- Explanations generated (optional narrator fields)

**User interacts:**
- BalloonPop, TrialAccepted, MessageSent → MatchSignalLogs
- Visual decisions (YES/NO on photos) → UserVisualDecisions
- Tile dwell ≥8s → OrbitGravity + ReceptionEmbedding update

**Nightly 03:50 UTC:**
- ConnectionScoreBatchWorker aggregates signals → ConnectionScores

**Sunday 04:00 UTC:**
- WeightLearningBatchWorker personalizes weights → UserMatchingWeights

**Next deck uses personalized weights.**

---

## Key Invariants

1. **No raw compatibility scores shown to users** — explanations only
2. **Trust filtering is binary** — in/out of pool, not a ranking dimension
3. **ConnectionScore is the only outcome label** — never optimize for engagement metrics
4. **Learned weights always sum to 1** — scoring stays a proper weighted average
5. **Signals are 90-day rolling window** — older signals decay out of training set
6. **Embeddings are versioned** — UserVectors.Version increments on each recompute

---

## Performance Characteristics

| Operation | Latency | Scale |
|---|---|---|
| GetOrCreateDeck (cached) | <50ms | Redis lookup |
| GetOrCreateDeck (fresh) | 2–5s | Score 50–200 candidates, generate 5 explanations |
| MatchScoringService.ScoreCandidatesAsync | ~1s / 100 candidates | Parallel SQL joins, batch loads |
| ConnectionScoreBatchWorker | 5–15min | 90-day window, incremental mode, bulk SQL upsert |
| WeightLearningBatchWorker | 1–3h | Per-user gradient descent, 100 iterations |
| EmbeddingBatchWorker | 2–4h | 9 services × all complete profiles, OpenAI API limited by rate |

---

## Failure Modes & Mitigations

**Cold start (new user, no signals):**
- Uses base weights (not learned)
- Uses cohort defaults for missing embeddings
- Still gets 5 candidates if pool size ≥5

**Insufficient candidate pool:**
- Gracefully returns <5 if pool exhausted
- Frontend shows "ECHO is building your pool" mood line

**OpenAI API rate limit:**
- IOpenAiClient has exponential backoff + jitter (3 retries: 1s/4s/12s)
- Respects Retry-After header on 429
- Correlation ID on every request for tracing

**Embedding generation failure:**
- Individual service failures logged but don't block batch
- Next nightly run retries
- Scoring gracefully skips missing components (available[] mask)

**Weight learning divergence:**
- Weights clamped [0.01, 0.50] per component
- L2 regularization (lambda 0.01) prevents overfitting
- Falls back to base weights if normalization fails

---

## Monitoring & Observability

**Structured logs (Serilog):**
- Every AI call logs: Model, Purpose, Tokens, CorrelationId
- Every batch worker logs: StartTime, Count, Duration, ErrorCount
- Every deck generation logs: UserId, CandidateCount, SelectedBuckets

**Key metrics to track:**
- Avg candidates in pool per user
- Avg deck generation latency
- Weight learning coverage (% users with ≥10 samples)
- ConnectionScore distribution (should be right-skewed)
- Component availability rate (% decks with all 16 components)

**Alerts:**
- Deck generation p95 > 10s
- ConnectionScoreBatchWorker failed 2 runs in a row
- Weight learning convergence rate < 80%
- Trust gate excluding >30% of pool

---

## See Also

- [agents.md](./agents.md) — Game agents, coaching, explanations
- [matching-pipeline.md](./matching-pipeline.md) — Detailed pipeline walkthrough
- [scoring.md](./scoring.md) — 16-component formula breakdown
- [learning.md](./learning.md) — Weight learning algorithm
