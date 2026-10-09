# Batch Workers

**Last Updated:** 2026-10-07  
**Total Workers:** 13 (12 batch + 1 real-time)

---

## Overview

All workers are **.NET BackgroundService** implementations that run on the **workers pod only** (via `WOVEN_DISABLE_BATCH_WORKERS=false`).

Each batch worker:
- Acquires a **Redis distributed lock** before running (except real-time workers)
- Logs start/end time with correlation ID
- Handles cancellation gracefully (CancellationToken)
- Releases lock in `finally` block

---

## Worker Catalog

### 1. TrustBatchWorker

**Schedule:** Daily at **02:00 UTC**  
**File:** `backend/WovenBackend/Services/Trust/TrustBatchWorker.cs`

**What It Does:**
Runs bot detection and trust scoring for active users (last 30 days).

**Algorithm:**
```
FOR each active user:
  - Analyze behavioral signals (login patterns, interaction velocity, content quality)
  - Compute TrustScore [0, 1] using TrustService.RunBotDetectionAsync()
  - Flag suspected bots (score < 0.3) for manual review
```

**Lock:** `lock:trust-batch` (4h expiry)

**Key Metrics:**
- Users processed
- Bots flagged
- Errors

**Dependencies:**
- `ITrustService` (bot detection ML model)
- `WovenDbContext` (Users table)

---

### 2. EmbeddingBatchWorker

**Schedule:** Daily at **02:30 UTC**  
**File:** `backend/WovenBackend/Services/Embeddings/EmbeddingBatchWorker.cs`

**What It Does:**
Computes 7 types of embeddings for all users with complete profiles.

**Embeddings Computed:**
1. **StyleEmbedding** — fashion, aesthetic preferences (from photos + bio)
2. **HumorEmbedding** — humor style (from tiles, chat messages)
3. **LifestyleEmbedding** — activity level, interests (from tiles, foundational questions)
4. **EmotionalRhythmEmbedding** — emotional expressiveness (from chat cadence, emoji use)
5. **BehavioralFingerprint** — 16-dim vector from MatchSignalLogs (no OpenAI)
6. **AttachmentProxyEmbedding** — attachment style (anxious/avoidant/secure) from chat behavior
7. **VisualPreferenceEmbedding** — learned from liked/passed photos (CF on photo embeddings)

**Lock:** `lock:embedding-batch` (4h expiry)

**Processing:**
- ~5,000 users/night (complete profiles only)
- ~10 seconds/user (7 OpenAI calls + DB writes)
- **Total runtime:** ~14 hours (runs overnight, overlaps with other workers)

**Key Metrics:**
- Users processed
- Users skipped (incomplete profile)
- Errors (OpenAI failures)

**Cost:**
- ~$25/night (text-embedding-ada-002 + GPT-4o-mini calls)

**Dependencies:**
- `IStyleEmbeddingService`, `IHumorEmbeddingService`, etc.
- `IOpenAiResilientClient` (circuit breaker, cost tracking)

---

### 3. CfBatchWorker

**Schedule:** Daily at **03:00 UTC**  
**File:** `backend/WovenBackend/Services/Recommendations/CfBatchWorker.cs`

**What It Does:**
Computes collaborative filtering scores (Jaccard similarity on shared tile interactions).

**Algorithm:**
```
FOR each (userA, userB) pair:
  SharedOrbits = orbits_A ∩ orbits_B
  TotalOrbits  = orbits_A ∪ orbits_B
  JaccardScore = |SharedOrbits| / |TotalOrbits|
  
  INSERT INTO CfScores (ViewerId, CandidateId, Score)
  ON CONFLICT UPDATE Score
```

**Lock:** None (no lock — runs via `ICollaborativeFilteringService.RunAsync()`)

**Processing:**
- ~1M user pairs/night
- **Runtime:** ~10 minutes

**Key Metrics:**
- Pairs processed
- Scores upserted

**Dependencies:**
- `ICollaborativeFilteringService`
- `WovenDbContext` (TileInteractions, CfScores)

---

### 4. SelfDisclosureBatchWorker

**Schedule:** Daily at **03:45 UTC**  
**File:** `backend/WovenBackend/Services/Moments/SelfDisclosureBatchWorker.cs`

**What It Does:**
Computes self-disclosure ratio (message length balance) for active chat threads.

**Algorithm:**
```
FOR each active thread (last 7 days, ≥4 messages):
  charsA = SUM(message.Body.Length WHERE senderId = userA)
  charsB = SUM(message.Body.Length WHERE senderId = userB)
  
  ratioA = charsA / (charsA + charsB)
  ratioB = charsB / (charsA + charsB)
  
  RecordSignal(userA, userB, "SelfDisclosureRatio", ratioA)
  RecordSignal(userB, userA, "SelfDisclosureRatio", ratioB)
```

**Why It Matters:**
- Imbalanced ratios (0.8/0.2) signal one-sided effort
- Used by PreferenceDrift to adjust future matches
- NOT shown to users (background signal only)

**Lock:** `lock:self-disclosure-batch` (2h expiry)

**Processing:**
- ~500 active threads/night
- **Runtime:** ~2 minutes

**Key Metrics:**
- Threads processed
- Threads skipped (too few messages)

**Dependencies:**
- `IMatchSignalService` (records signals)
- `WovenDbContext` (ChatThreads, ChatMessages)

---

### 5. ConnectionScoreBatchWorker

**Schedule:** Daily at **03:50 UTC**  
**File:** `backend/WovenBackend/Services/Matchmaking/ConnectionScoreBatchWorker.cs`

**What It Does:**
Aggregates all MatchSignalLog events into a single composite **ConnectionScore** [0, 1] per (viewer, candidate) pair. This is ECHO's **primary outcome proxy** for match quality.

**Formula (weights from `appsettings.json` → `Echo:ConnectionScore:*`):**
```
ConnectionScore = 0.05 * BalloonPopped
                + 0.08 * TrialRequested
                + 0.22 * TrialAccepted
                + 0.20 * ConversationDepth (messages/20, capped at 1.0)
                + 0.15 * DateAccepted
                + 0.13 * ExplicitFeedback [0,1]
                + 0.08 * LoveReactions (count/3, capped at 1.0)
                + 0.06 * MutualVoiceExchange (binary)
                + 0.03 * VoiceCompleted (count/2, capped at 1.0)
```

**Incremental Processing (since 2026-06-04):**
- Reads `connection-score-batch:last-run` from Redis
- Only re-scores pairs with **new signals** since last run
- First run (or cache cleared): full scan of 90-day window
- **Runtime:** 5–10 minutes (incremental), 30 minutes (full scan)

**Lock:** `lock:connection-score-batch` (2h expiry)

**Processing:**
- ~10K pairs/night (incremental)
- ~500K pairs (full scan)

**Key Metrics:**
- Pairs affected (incremental mode)
- Scores upserted
- Mode (incremental vs full)

**Dependencies:**
- `ICacheService` (Redis — stores last run timestamp)
- `WovenDbContext` (MatchSignalLogs, ConnectionScores)

**Critical:**
- Must run **before** `WeightLearningBatchWorker` (Sunday 04:00)
- ConnectionScores are the **labels** for weight learning

---

### 6. WeightLearningBatchWorker

**Schedule:** Weekly on **Sunday at 04:00 UTC**  
**File:** `backend/WovenBackend/Services/Matchmaking/WeightLearningBatchWorker.cs`

**What It Does:**
Learns personalized **pillar weights** for each user's ECHO matching formula using **logistic regression** over ConnectionScores.

**Algorithm:**
```
FOR each user with ≥5 ConnectionScore outcomes:
  X = candidate pillar embeddings (8-dim)
  y = ConnectionScores [0, 1]
  
  weights = LogisticRegression.Fit(X, y)
  
  UPDATE UserVectors
  SET PreferenceEmbedding = weights
  WHERE UserId = user
```

**Why Weekly (Not Daily)?**
- Requires **≥5 scored candidates** → most users don't get 5 new scores/day
- Weight updates are **slow-moving** (preferences stable over weeks)
- Expensive computation (~5 seconds/user with sklearn)

**Lock:** `lock:weight-learning-batch` (6h expiry)

**Processing:**
- ~2,000 eligible users/week
- **Runtime:** 10–15 minutes

**Key Metrics:**
- Users updated
- Users skipped (< 5 outcomes)
- Errors

**Dependencies:**
- `IWeightLearningService` (sklearn via Python bridge OR C# MathNet.Numerics)
- `WovenDbContext` (MatchOutcomes, ConnectionScores, UserVectors)

**Critical:**
- Must run **after** `ConnectionScoreBatchWorker` (03:50)
- ConnectionScores are the **training labels**

---

### 7. PreferenceDriftBatchWorker

**Schedule:** Daily at **04:15 UTC**  
**File:** `backend/WovenBackend/Services/Matchmaking/PreferenceDriftBatchWorker.cs`

**What It Does:**
Detects preference drift (user's behavior diverging from stated preferences) and adjusts future match recommendations.

**Algorithm:**
```
FOR each user with ≥1 ConnectionScore ≥ 0.15:
  - Compare UserVectors.PreferenceEmbedding (stated preferences from onboarding)
  - vs. recent ConnectionScores (revealed preferences from actions)
  - If drift > threshold (cosine distance > 0.3):
      → Adjust match weights to favor revealed preferences
      → Record PreferenceDriftSignal
```

**Why It Matters:**
- User says "I want ambitious partners" (onboarding)
- But consistently rates laid-back partners highly (actions)
- ECHO detects drift → shifts future deck toward laid-back candidates

**Lock:** `lock:preference-drift-batch` (3h expiry)

**Processing:**
- ~5,000 users/night (users with scores ≥ 0.15)
- **Runtime:** 8–12 minutes

**Key Metrics:**
- Users processed
- Drift signals recorded
- Errors

**Dependencies:**
- `IPreferenceDriftService`
- `WovenDbContext` (ConnectionScores, UserVectors)

---

### 8. LinUcbBatchWorker

**Schedule:** Daily at **04:20 UTC**  
**File:** `backend/WovenBackend/Services/Matchmaking/LinUcbBatchWorker.cs`

**What It Does:**
Updates per-user **LinUCB bandit models** using Sherman-Morrison incremental updates.

**Algorithm (LinUCB with Exploration):**
```
Context vector x = [8 pillar dims + 16 behavioral fingerprint dims] = 24-dim

FOR each user with ≥1 ConnectionScore:
  FOR each scored candidate:
    x = candidate context (24-dim)
    r = ConnectionScore [0, 1]
    
    A_inv = A_inv + x * x^T  (via Sherman-Morrison — O(d²) not O(d³))
    b = b + r * x
    
    θ = A_inv * b  (linear regression weights)
```

**Why LinUCB (Not Pure Exploitation)?**
- **Exploration bonus:** `score = θ^T * x + α * sqrt(x^T * A_inv * x)`
- Balances **exploitation** (known good candidates) vs **exploration** (uncertain candidates)
- α = 0.2 (tuned via A/B test)

**Lock:** `lock:linucb-batch` (30 min expiry)

**Processing:**
- ~10K users/night (users with scored candidates)
- **Runtime:** 15–20 minutes

**Key Metrics:**
- Users updated
- Errors (matrix inversion failures)

**Dependencies:**
- `ILinUcbService` (Sherman-Morrison matrix updates)
- `IBehavioralFingerprintService` (16-dim fingerprints)
- `WovenDbContext` (ConnectionScores, UserVectors, UserBehavioralFingerprints)

---

### 9. InsightBatchWorker

**Schedule:** Daily at **04:30 UTC** (polls every 30 minutes)  
**File:** `backend/WovenBackend/Services/Insights/InsightBatchWorker.cs`

**What It Does:**
1. Computes **behavioral insights** (e.g., "You tend to match better with people who...")
2. Sends **opinion prompts** via push notifications (e.g., "What matters more: humor or ambition?")

**Algorithm:**
```
FOR each active user (last 14 days):
  - ComputeInsightsAsync(userId):
      → Find patterns in ConnectionScores (e.g., high scores correlate with humor pillar)
      → Store in UserInsights table (not shown to users — internal only)
  
  - ShouldAskOpinionAsync(userId):
      → If user has 3+ insights AND hasn't been asked in 7 days:
          → Generate opinion prompt (GPT-4o-mini)
          → Send push notification
```

**Why Opinion Prompts?**
- Elicit explicit feedback to **validate** behavioral insights
- User answers → stored as MatchSignalLog → fed back into ECHO

**Processing:**
- ~3,000 active users/night
- ~50 opinion prompts sent/night
- **Runtime:** 10–15 minutes

**Key Metrics:**
- Users processed
- Opinion prompts sent
- Errors

**Dependencies:**
- `IInsightService`
- `INotificationService` (push notifications)
- `WovenDbContext` (Users, MatchSignalLogs, UserInsights)

---

### 10. CfScoreBatchWorker

**Schedule:** Daily at **05:00 UTC**  
**File:** `backend/WovenBackend/Services/Matchmaking/CfScoreBatchWorker.cs`

**What It Does:**
Same as `CfBatchWorker` (03:00) — computes collaborative filtering scores.

**Why Duplicate?**
- Legacy code — `CfScoreBatchWorker` was added June 4, 2026
- `CfBatchWorker` existed before (03:00 run)
- **TODO:** Consolidate into single worker at 03:00 (remove 05:00 run)

**Lock:** None (disabled via `WOVEN_DISABLE_BATCH_WORKERS` check inside worker)

**Status:** ⚠️ **Redundant** — should be removed

---

### 11. CoachingSummaryWorker

**Schedule:** Weekly on **Wednesday at 18:00 UTC**  
**File:** `backend/WovenBackend/Services/Coaching/CoachingSummaryWorker.cs`

**What It Does:**
Generates AI coaching summaries (3–5 sentences) for active users — like a trusted friend reflecting back their week.

**Eligibility:**
- Account ≥14 days old
- ≥3 deck interactions in prior 7 days
- Not opted out (`User.CoachingOptedOut = false`)
- No existing summary for current week

**Algorithm:**
```
FOR each eligible user:
  1. Aggregate signals (deck interactions, first messages, voice exchanges, trials)
  2. Build narrative (deterministic C# paragraph, no raw numbers)
  3. Call GPT-4.1-mini (temp=0.8, max_tokens=200):
       System: "You are a warm, perceptive friend..."
       User: narrative
  4. If response = "SUPPRESS" OR < 50 chars → skip
  5. Else: save to CoachingSummaries table (90-day TTL)
```

**Example Summary:**
> "This week you showed up consistently, taking your time with profiles instead of rushing through. When you felt a spark, you were willing to make the first move. One conversation went deep — there's real exchange happening, not just surface pleasantries. That takes courage."

**Lock:** `lock:coaching-summary-batch` (4h expiry)

**Processing:**
- ~500 eligible users/week
- ~300 summaries written (200 suppressed)
- **Runtime:** 15–20 minutes

**Key Metrics:**
- Users processed
- Summaries written
- Summaries suppressed (SUPPRESS or too short)

**Cost:**
- ~$1.50/week (GPT-4.1-mini at $0.003/summary)

**Dependencies:**
- `IHttpClientFactory` (OpenAI API calls)
- `IConfiguration` (OpenAI:ApiKey from Key Vault)
- `WovenDbContext` (CoachingSummaries, MatchSignalLogs, DailyInteractions)

**Critical:**
- Summaries are **private** — never shared, never compared across users
- Suppression threshold prevents hollow/generic feedback ("You used the app!")
- 90-day TTL → old summaries auto-deleted

---

### 12. ServiceBusEmbeddingWorker (Real-Time)

**Schedule:** **Real-time** (message-driven, not scheduled)  
**File:** `backend/WovenBackend/Services/Queue/ServiceBusEmbeddingQueue.cs`

**What It Does:**
Processes tile embedding messages from Azure Service Bus queue (`tile-embedding`).

**Flow:**
```
1. Pull message from Service Bus (max 4 concurrent)
2. Parse tileId from message body
3. Call TileEmbeddingService.EmbedTileAsync(tileId):
     → OpenAI text-embedding-ada-002 (caption + hashtags)
     → Save embedding to tile_embeddings table
4. CompleteMessageAsync() OR AbandonMessageAsync() (on failure)
```

**Max Concurrent Calls:** 4  
**Lock Duration:** 5 minutes  
**Max Delivery Count:** 10 (→ DLQ)  
**TTL:** 2 days

**Processing:**
- ~500 tiles/day
- ~5 seconds/tile (OpenAI call + DB write)

**Key Metrics:**
- Messages processed/hour
- Messages abandoned (failures)
- DLQ count (persistent failures)

**Cost:**
- ~$0.05/day (OpenAI embeddings)

**Dependencies:**
- `ServiceBusClient` (Azure Service Bus)
- `TileEmbeddingService`

**See:** [service-bus.md](service-bus.md) for full details

---

### 13. TileViewProcessorWorker (Real-Time)

**Schedule:** **Real-time** (aggregates tile views every 5 minutes)  
**File:** `backend/WovenBackend/Services/Commons/TileViewProcessorWorker.cs` (not shown)

**What It Does:**
Aggregates tile view events from Redis into PostgreSQL for analytics.

**Flow:**
```
Every 5 minutes:
  1. Read tile_view_events from Redis sorted set
  2. Aggregate counts per tile
  3. UPDATE Tiles SET ViewCount = ViewCount + delta
  4. Delete processed events from Redis
```

**Runs On:** **All pods** (not disabled by `WOVEN_DISABLE_BATCH_WORKERS`)

**Processing:**
- ~10K view events/5 min
- **Runtime:** < 1 second

**Key Metrics:**
- Events processed
- Tiles updated
- Errors

**Dependencies:**
- `ICacheService` (Redis)
- `WovenDbContext` (Tiles)

---

## Worker Dependencies (Topological Order)

Some workers depend on outputs from others. Run order matters.

```
02:00  TrustBatch ────┐
                      ├─→ (no dependencies)
02:30  EmbeddingBatch ┘

03:00  CfBatch ───────→ CfScores table
                      │
03:45  SelfDisclosure → MatchSignalLogs
                      │
03:50  ConnectionScore ─┴→ ConnectionScores table ──┐
                                                     │
04:00  WeightLearning ←────────────────────────────┘
                      └→ UserVectors.PreferenceEmbedding
                                   │
04:15  PreferenceDrift ←───────────┤
                                   │
04:20  LinUcbBatch ←───────────────┴→ UserVectors.BehavioralFingerprint

04:30  InsightBatch (reads ConnectionScores, MatchSignalLogs)

05:00  CfScoreBatch (⚠️ DUPLICATE — remove this)

Wed 18:00  CoachingSummary (reads MatchSignalLogs, DailyInteractions)
```

**Critical Path:**
1. `ConnectionScoreBatchWorker` (03:50) must complete before:
   - `WeightLearningBatchWorker` (Sunday 04:00)
   - `LinUcbBatchWorker` (04:20)

2. `EmbeddingBatchWorker` (02:30) must complete before:
   - `LinUcbBatchWorker` (04:20) — needs BehavioralFingerprint

**Failure Modes:**
- If `ConnectionScoreBatchWorker` fails → `WeightLearningBatchWorker` skips that week's update (weights become stale)
- If `EmbeddingBatchWorker` fails → `LinUcbBatchWorker` uses stale fingerprints (bandit model drift)

---

## Error Budget

**SLO:** 95% of batch workers complete successfully each night

**Acceptable Failures:**
- 1 worker failure/night → investigate next morning
- 2+ failures/night → page on-call

**Common Failures:**
1. **OpenAI rate limit (429)** → ConnectionScoreBatchWorker, CoachingSummaryWorker
   - Mitigation: Exponential backoff in `IOpenAiResilientClient`
2. **PostgreSQL connection timeout** → All workers
   - Mitigation: Connection pooling (Npgsql), retry logic
3. **Redis lock timeout** → Workers skip run if lock held too long
   - Mitigation: Lock expiry (2–6h), auto-release on pod crash

---

## Monitoring Checklist

**Daily:**
- [ ] All 12 workers logged "Done" message
- [ ] No errors in Azure Log Analytics
- [ ] ConnectionScores table updated (row count increased)
- [ ] WeightLearning ran (Sunday only)
- [ ] CoachingSummary ran (Wednesday only)

**Weekly:**
- [ ] WeightLearning completed (Sunday 04:00)
- [ ] CoachingSummary sent ~300 summaries (Wednesday 18:00)
- [ ] No DLQ messages in Service Bus

**Alerts:**
- Worker runtime > 30 minutes
- Error rate > 5%
- Lock acquisition failures > 3 consecutive days

---

**Next:** [scheduling.md](scheduling.md) — Cron schedules, pod topology, WOVEN_DISABLE_BATCH_WORKERS
