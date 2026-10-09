# ECHO Batch Workers

ECHO runs 6 nightly/weekly batch jobs to aggregate signals, compute embeddings, learn weights, and generate coaching summaries. All workers use Redis distributed locks to prevent duplicate execution across multiple pods.

---

## Worker Schedule (UTC)

| Time | Worker | Frequency | Purpose | Duration |
|---|---|---|---|---|
| **02:30** | EmbeddingBatchWorker | Daily | Compute 9 embeddings per user | 2-4h |
| **03:50** | ConnectionScoreBatchWorker | Daily | Aggregate signals → ConnectionScore | 5-15min |
| **04:00** | WeightLearningBatchWorker | Sunday | Learn personalized weights | 1-3h |
| **04:20** | LinUcbBatchWorker | Daily | Update LinUCB bandit models | 10-30min |
| **05:00** | CfScoreBatchWorker | Daily | Collaborative filtering | 15-45min |
| **18:00 Wed** | CoachingSummaryWorker | Wednesday | Weekly coaching summaries | 30-60min |

---

## 1. EmbeddingBatchWorker

**Schedule:** Daily 02:30 UTC  
**File:** `backend/WovenBackend/Services/Embeddings/EmbeddingBatchWorker.cs`

### What It Does
Computes 9 embedding modalities for all users with `ProfileStatus == COMPLETE`.

**Services called (in order):**
1. `IStyleEmbeddingService` → StyleEmbedding (128-dim)
2. `IHumorEmbeddingService` → HumorEmbedding (128-dim)
3. `ILifestyleEmbeddingService` → LifestyleEmbedding (128-dim)
4. `IEmotionalRhythmService` → EmotionalRhythmEmbedding (128-dim)
5. `IBehavioralFingerprintService` → BehavioralLifestyleEmbedding (16-dim fingerprint)
6. `IAttachmentProxyService` → AttachmentProxyEmbedding (128-dim, depends on #5)
7. `IVisualPreferenceService` → PreferenceEmbedding + AversionEmbedding (512-dim CLIP)

**Note:** Pillar + Expression embeddings computed at onboarding exit, not in batch.

### Output
- Updates `UserVectors` table (Version incremented)
- Updates `UserVisualPreferences` table (if ≥10 YES/NO samples)

### Performance
- **Users:** All with ProfileStatus == COMPLETE (~10k users)
- **Duration:** 2-4h (rate-limited by OpenAI API)
- **Cost:** ~$1-2/night (OpenAI embeddings)

### Lock
- **Key:** `lock:embedding-batch`
- **Expiry:** 4h

### Error Handling
- Individual service failures logged, don't block batch
- Skipped users retried next night

---

## 2. ConnectionScoreBatchWorker

**Schedule:** Daily 03:50 UTC  
**File:** `backend/WovenBackend/Services/Matchmaking/ConnectionScoreBatchWorker.cs`

### What It Does
Aggregates `MatchSignalLogs` (40+ event types) into composite `ConnectionScore` [0, 1] per (viewer, candidate) pair.

**Signal window:** 90 days (rolling)

**Formula:**
```csharp
var score = 
    0.05  × balloonPopped      +
    0.10  × trialRequested     +
    0.22  × trialAccepted      +
    0.20  × convDepth          +  // MessageSent count / 20
    0.15  × dateAccepted       +
    0.13  × explicitFb         +
    0.08  × loveReactions      +
    0.06  × voiceExchange      +
    0.03  × voiceCompleted;
```

**Weights configured in:**
```json
{
  "Echo": {
    "ConnectionScore": {
      "BalloonPopped": 0.05,
      "TrialRequested": 0.10,
      "TrialAccepted": 0.22,
      ...
    }
  }
}
```

### Incremental Mode
- First run: processes all signals in 90-day window
- Subsequent runs: processes only pairs with new signals since last run
- Stores last run timestamp in Redis: `connection-score-batch:last-run`

### Output
- Upserts into `ConnectionScores` table (bulk SQL: `INSERT ... ON CONFLICT DO UPDATE`)
- One row per (ViewerId, CandidateId) with `Score` [0, 1]

### Performance
- **Mode:** Incremental (only affected pairs)
- **Duration:** 5-15min (depends on daily signal volume)
- **SQL:** Chunked upserts (500 rows per batch)

### Lock
- **Key:** `lock:connection-score-batch`
- **Expiry:** 2h

---

## 3. WeightLearningBatchWorker

**Schedule:** Sunday 04:00 UTC (weekly)  
**File:** `backend/WovenBackend/Services/Matchmaking/WeightLearningBatchWorker.cs`

### What It Does
Learns personalized weights for the 16-component scoring formula via logistic regression.

**Eligibility:** Users with ≥10 ConnectionScore samples (Score ≥ 0.08)

**Algorithm:**
- Feature matrix: 16 component scores (from MatchScoringService)
- Label: ConnectionScore [0, 1]
- Model: Logistic regression with L2 regularization (100 iterations)
- Output: 16 learned weights per user, normalized to sum = 1.0

### Output
- Upserts into `UserMatchingWeights` table (one row per user per component)
- Logs top component per user (for analytics)

### Performance
- **Eligible users:** ~1000-5000 (grows over time)
- **Duration:** 1-3h
- **Per user:** 1-3s (score candidates + gradient descent)

### Lock
- **Key:** `lock:weight-learning-batch`
- **Expiry:** 6h

### Why Weekly (Not Daily)
- Weights are slow-changing (personality preferences stable)
- Reduces API cost (scoring candidates is expensive)
- Prevents overfitting on daily noise

---

## 4. LinUcbBatchWorker

**Schedule:** Daily 04:20 UTC  
**File:** `backend/WovenBackend/Services/Matchmaking/LinUcbBatchWorker.cs`

### What It Does
Updates LinUCB bandit models to compute exploration bonuses for candidates.

**LinUCB:** Upper Confidence Bound (UCB1) for contextual bandits
- Context: 24-dim vector (8 pillars + 16 behavioral fingerprint)
- Reward: ConnectionScore [0, 1]
- Bonus: Exploration term [0–10] added to TotalScore

**Sherman-Morrison update:**
- Incremental A_inv matrix update (avoids O(d³) solve)
- Efficient for daily updates

### Output
- Updates `LinUcbModels` table (A_inv matrix + b vector per user)
- Used by `LinUcbService.GetBoostMapAsync` during deck generation

### Performance
- **Users:** All with ≥1 ConnectionScore
- **Duration:** 10-30min

### Lock
- **Key:** `lock:linucb-batch`
- **Expiry:** 30min

---

## 5. CfScoreBatchWorker

**Schedule:** Daily 05:00 UTC  
**File:** `backend/WovenBackend/Services/Matchmaking/CfScoreBatchWorker.cs`

### What It Does
Runs `CollaborativeFilteringService` to compute Jaccard similarity between users based on shared orbit/dwell interactions.

**Jaccard formula:**
```
Jaccard(A, B) = |A ∩ B| / |A ∪ B|
```

Where A = set of candidates viewer A dwelled on, B = same for viewer B.

**Output:**
- Populates `CfScores` table with Jaccard scores [0, 1]
- Used by MatchScoringService component #14 (CfScore, weight 0.03)

### Unblocks
- **SharedTileAffinity component** (component #14) — was blocked on CfScore data, now available

### Performance
- **Duration:** 15-45min
- **Algorithm:** Sparse matrix Jaccard (efficient for large user base)

### Lock
- None specified (uses service-level lock in `CollaborativeFilteringService`)

---

## 6. CoachingSummaryWorker

**Schedule:** Wednesday 18:00 UTC (weekly)  
**File:** `backend/WovenBackend/Services/Coaching/CoachingSummaryWorker.cs`

### What It Does
Generates weekly coaching summaries for qualified users.

**Eligibility:**
- Account ≥14 days
- ≥3 deck interactions in prior 7 days
- Not opted out
- No existing summary for this week

**Algorithm:**
1. Aggregate permitted signals (DeckInteractions, TrialAccepted, MessageSent, etc.)
2. Build C# narrative (deterministic, no raw numbers)
3. Call GPT-4.1-mini (temp 0.8, max 200 tokens)
4. Suppress if output = "SUPPRESS" or <50 chars
5. Save to `CoachingSummaries` table

**Model:** `gpt-4.1-mini`, temperature 0.8 (warmer than other agents)

### Output
- Inserts into `CoachingSummaries` table (90-day TTL)
- Frontend fetches via `GET /coaching/latest`

### Performance
- **Eligible users:** ~200-1000 per week
- **Duration:** 30-60min
- **Suppression rate:** ~15-20%

### Lock
- **Key:** `lock:coaching-summary-batch`
- **Expiry:** 4h

---

## Worker Configuration

**Disable all batch workers:**
```bash
export WOVEN_DISABLE_BATCH_WORKERS=true
```

**Used for:**
- Web pods (only handle HTTP, no background jobs)
- Dev environments (run workers manually)

**Worker pods:**
- Don't set this flag
- Run all 6 workers in background

---

## Lock Strategy (Distributed Coordination)

All workers use Redis distributed locks to prevent duplicate execution:

```csharp
if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
{
    _logger.LogInformation("[WorkerName] Skipping — another pod holds the lock");
    return;
}

try
{
    // Run batch job
}
finally
{
    await _cache.ReleaseLockAsync(LockKey, ct);
}
```

**Lock expiry:**
- Short jobs (10-30min): 30min-2h expiry
- Long jobs (2-4h): 4-6h expiry

**Why:** If pod crashes mid-job, lock auto-expires and next pod picks up.

---

## Dependency Graph

```
02:30  EmbeddingBatchWorker
         ↓ (writes UserVectors)
         
03:50  ConnectionScoreBatchWorker
         ↓ (writes ConnectionScores)
         
04:00  WeightLearningBatchWorker  (Sunday only)
         ↓ (reads ConnectionScores, writes UserMatchingWeights)
         
04:20  LinUcbBatchWorker
         ↓ (reads ConnectionScores, writes LinUcbModels)
         
05:00  CfScoreBatchWorker
         ↓ (writes CfScores)
         
[Deck generation uses: UserVectors, UserMatchingWeights, CfScores, LinUcbModels]

18:00  CoachingSummaryWorker (Wednesday only)
       (reads MatchSignalLogs, writes CoachingSummaries)
```

---

## Monitoring

**Key metrics per worker:**
- Execution duration (p50, p95, p99)
- Success rate (% of runs that complete)
- Lock acquisition rate (% of runs that acquire lock vs skip)
- Processed count (users, pairs, etc.)
- Error count

**Alerts:**
- Worker failed 2 consecutive runs
- Duration p95 exceeds 2× normal
- Lock never acquired (stuck in another pod?)
- Error rate >10%

**Logs:**
```
[EmbeddingBatch] Starting embedding batch at 2026-08-17 02:30:00 UTC
[EmbeddingBatch] Processed 9847 users in 2h 15m 32s — errors=12
[ConnectionScore] Incremental mode: 2347 pairs affected since last run
[ConnectionScore] Upserted 2347 scores via bulk SQL
[WeightLearning] Processing 1823 eligible users
[WeightLearning] Completed in 1h 42m — 1823 users updated
```

---

## Testing Workers

**Unit test: Lock behavior**
```csharp
[Test]
public async Task Worker_LockHeld_SkipsExecution()
{
    // Arrange: acquire lock manually
    await cache.AcquireLockAsync(LockKey, TimeSpan.FromMinutes(5));

    // Act
    await worker.ExecuteAsync(ct);

    // Assert: worker should skip, not execute
    Assert.That(executionCount, Is.EqualTo(0));
}
```

**Integration test: End-to-end**
```csharp
[Test]
public async Task EmbeddingBatch_CompleteUsers_UpdatesVectors()
{
    // Arrange: seed 10 users with ProfileStatus.COMPLETE
    SeedUsers(count: 10, status: ProfileStatus.COMPLETE);

    // Act
    await worker.RunBatchAsync(ct);

    // Assert
    var vectors = await db.UserVectors
        .Where(v => v.CreatedAt >= testStart)
        .CountAsync();

    Assert.That(vectors, Is.EqualTo(10));
}
```

---

## Future Workers (Planned)

| Worker | Schedule | Purpose |
|---|---|---|
| **PreferenceEmbeddingWorker** | Daily 03:00 | Extract preferences from ChatNotes → PreferenceEmbedding |
| **TrustScoreRecalcWorker** | Daily 06:00 | Recompute TrustScore from UserFlagged signals |
| **DeckGenerationBatchWorker** | Daily 07:00 | Pre-generate decks for all active users (reduce on-demand latency) |

---

## See Also

- [embeddings.md](./embeddings.md) — What EmbeddingBatchWorker computes
- [signals.md](./signals.md) — What ConnectionScoreBatchWorker aggregates
- [learning.md](./learning.md) — WeightLearningBatchWorker algorithm
- [coach.md](./coach.md) — CoachingSummaryWorker details
