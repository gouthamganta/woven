# ECHO Weight Learning

ECHO personalizes the 16-component scoring formula for each user via logistic regression. After a user has ≥10 ConnectionScore samples, `WeightLearningService` learns which compatibility dimensions best predict their outcomes.

**Worker:** `WeightLearningBatchWorker`  
**Schedule:** Sunday 04:00 UTC (weekly)  
**File:** `backend/WovenBackend/Services/Matchmaking/WeightLearningService.cs`

---

## Why Learn Weights?

**Problem:** Base weights (0.19 for Pillar, 0.12 for Intent, etc.) are averages across all users. But users differ:
- User A might value physical attraction (Visual, Voice) more than personality
- User B might value intent alignment above all else
- User C might care most about lifestyle compatibility

**Solution:** After observing user A's outcomes (which matches worked vs didn't), learn personalized weights that fit their preferences.

**Result:**
- Better matches (formula tuned to what they actually care about)
- Faster learning (fewer wasted matches on dimensions they don't value)

---

## Algorithm Overview

**Label:** ConnectionScore [0, 1] — composite of 9 behavioral signals (balloon pop, trial accepted, conversation depth, etc.)

**Features:** 16 component scores from MatchScoringService (each [0, 1] after normalization)

**Model:** Logistic regression with L2 regularization

**Output:** Learned weights per user, stored in `UserMatchingWeights` table

---

## Data Requirements

**Minimum samples:** 10 ConnectionScore rows (viewer-candidate pairs)

**Minimum ConnectionScore:** 0.08 (pairs with only BalloonPop ≈ 0.05 give almost no signal)

**Eligibility check:**
```csharp
var connectionScores = await db.ConnectionScores
    .Where(c => c.ViewerId == userId && c.Score >= 0.08)
    .ToListAsync();

if (connectionScores.Count < 10)
{
    logger.LogInformation("[WeightLearning] Skipping user {UserId} — only {N} samples",
        userId, connectionScores.Count);
    return;
}
```

---

## Feature Extraction

For each ConnectionScore row (viewer → candidate, score = y):

1. **Load candidate's MatchScore** (computed via MatchScoringService)
2. **Extract 16 features** (component scores normalized to [0, 1]):
   ```csharp
   var features = new[]
   {
       matchScore.PillarScore              / 100.0,
       matchScore.IntentScore              / 100.0,
       matchScore.ExpressionScore          / 100.0,
       matchScore.StyleScore               / 100.0,
       matchScore.VisualScore              / 100.0,
       matchScore.VoiceScore               / 100.0,
       matchScore.HumorScore               / 100.0,
       matchScore.LifestyleScore           / 100.0,
       matchScore.BehavioralLifestyleScore / 100.0,
       matchScore.EmotionalRhythmScore     / 100.0,
       matchScore.AttachmentScore          / 100.0,
       matchScore.OrbitGravityScore        / 100.0,
       matchScore.PulseScore               / 100.0,
       matchScore.CfScore                  / 100.0,
       matchScore.SharedTileAffinityScore  / 100.0,
       matchScore.PreferenceAffinityScore  / 100.0
   };
   ```

**Result:** Feature matrix `X` (N × 16) + label vector `y` (N × 1), where N = connectionScores.Count

---

## Logistic Regression with L2 Regularization

**Goal:** Find weights `w` that maximize log-likelihood (minimize binary cross-entropy):

```
L(w) = Σ [y_i × log(σ(w · x_i)) + (1 - y_i) × log(1 - σ(w · x_i))]
```

Where:
- `σ(z) = 1 / (1 + exp(-z))` (sigmoid)
- `w · x_i` = weighted sum of features

**Gradient ascent update:**
```
Δw = lr × Σ [(y_i - ŷ_i) × x_i] - 2λw
```

Where:
- `ŷ_i = σ(w · x_i)` (predicted probability)
- `lr = 0.01` (learning rate)
- `λ = 0.01` (L2 regularization)

**L2 term:** Prevents overfitting (keeps weights near base weights)

---

## Implementation

```csharp
// Initialize weights (warm start from existing learned weights, or base weights)
var w = new double[16];
for (int j = 0; j < 16; j++)
    w[j] = existingWeights.TryGetValue(ComponentNames[j], out var lw) ? lw : BaseWeights[j];

// Gradient ascent (100 iterations)
for (int iter = 0; iter < 100; iter++)
{
    var grad = new double[16];
    
    for (int i = 0; i < N; i++)
    {
        var predicted = Sigmoid(Dot(w, X[i]));
        var residual  = y[i] - predicted;
        
        for (int j = 0; j < 16; j++)
            grad[j] += residual * X[i][j];
    }
    
    // Update weights with L2 penalty
    for (int j = 0; j < 16; j++)
    {
        w[j] += LearningRate * (grad[j] / N - 2 * L2Lambda * w[j]);
        w[j]  = Math.Clamp(w[j], 0.01, 0.50);  // Prevent extreme weights
    }
}

// Normalize so weights sum to 1
var total = w.Sum();
for (int j = 0; j < 16; j++)
    w[j] = Math.Clamp(w[j] / total, 0.01, 0.50);
```

**Constants:**
- `LearningRate = 0.01`
- `Iterations = 100`
- `L2Lambda = 0.01`
- `MinWeight = 0.01` (no component can be zeroed out)
- `MaxWeight = 0.50` (no component can dominate)

---

## Weight Normalization

**After learning, weights always sum to 1.0:**

```csharp
var total = w.Sum();
if (total > 0)
    for (int j = 0; j < 16; j++)
        w[j] = Math.Clamp(w[j] / total, 0.01, 0.50);
```

**Why:**
- Keeps TotalScore in [0, 100] range (scoring formula is a weighted average)
- Makes weights interpretable (e.g., "Pillar is 25% of my score")

---

## Storage

**Table:** `UserMatchingWeights`

```sql
CREATE TABLE UserMatchingWeights (
    UserId INT NOT NULL,
    Component VARCHAR(50) NOT NULL,     -- "pillar", "intent", etc.
    LearnedWeight FLOAT NOT NULL,       -- [0.01, 0.50]
    SampleCount INT NOT NULL,           -- How many ConnectionScores used
    UpdatedAt TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (UserId, Component)
);
```

**Row per component:**
```sql
INSERT INTO UserMatchingWeights (UserId, Component, LearnedWeight, SampleCount, UpdatedAt)
VALUES
    (123, 'pillar', 0.25, 15, NOW()),
    (123, 'intent', 0.18, 15, NOW()),
    (123, 'visual', 0.15, 15, NOW()),
    ...
```

---

## Activation in Scoring

**MatchScoringService checks if user has learned weights:**

```csharp
var learnedWeightMap = await db.UserMatchingWeights
    .Where(w => w.UserId == userId && w.SampleCount >= 5)
    .ToDictionaryAsync(w => w.Component, w => (double)w.LearnedWeight);

double[]? learnedWeightsArray = null;

if (learnedWeightMap.Count >= ComponentNames.Length / 2)  // ≥8 components
{
    learnedWeightsArray = new double[16];
    for (int i = 0; i < 16; i++)
        learnedWeightsArray[i] = learnedWeightMap.TryGetValue(ComponentNames[i], out var lw)
            ? lw : BaseWeights[i];
}

// Use learned weights if available, else base weights
score.ComputeTotal(
    available,
    BaseWeights,
    learnedWeightsArray,  // null if not enough data
    intentMult,
    trust,
    hasSeasonResponse
);
```

**Threshold:** ≥8 components with ≥5 samples each

**Why 8/16:** Half the components must have data to avoid overfitting on sparse signals

---

## Example Learned Weights

**User A (values personality + intent):**
```
pillar:        0.25  (↑ from base 0.19)
intent:        0.18  (↑ from base 0.12)
expression:    0.10  (↑ from base 0.09)
visual:        0.05  (↓ from base 0.10) — cares less about photos
voice:         0.04  (↓ from base 0.08) — cares less about voice
...
```

**User B (values attraction + lifestyle):**
```
pillar:        0.12  (↓ from base 0.19)
visual:        0.20  (↑ from base 0.10) — photos matter more
voice:         0.15  (↑ from base 0.08) — voice matters more
lifestyle:     0.15  (↑ from base 0.08)
orbit_gravity: 0.12  (↑ from base 0.08) — wants mutual interest
...
```

**Result:**
- User A sees candidates with high pillar + intent scores
- User B sees candidates with high visual + voice + lifestyle scores
- Both get better matches (personalized to their preferences)

---

## Convergence & Overfitting Prevention

**L2 regularization:**
- Keeps weights near base weights (prevents wild swings)
- Penalty term: `-2 × λ × w` per iteration

**Clamping:**
- `[0.01, 0.50]` per component → no component can be zeroed out or dominate

**Sample size requirement:**
- ≥10 samples minimum (prevents overfitting on 1-2 matches)
- ≥5 samples per component to activate (prevents using noisy components)

**Warm start:**
- Initializes from existing learned weights (if user has them)
- Prevents weights from jumping wildly week-to-week

---

## Baseline Protection (NarrationExposed)

**When cinematic narrator launches:**

```csharp
// Filter out ConnectionScores where NarrationExposed == true for 30 days post-launch
// This prevents ECHO weights from being biased by the cinematic experience itself

var launchDate = config["Cinematic:NarrationLaunchDate"];
if (launchDate != null && (now - launchDate).TotalDays < 30)
{
    connectionScores = connectionScores
        .Where(c => !c.NarrationExposed)
        .ToList();
}
```

**Why:**
- Cinematic intro (Ken Burns + TTS) is a UX enhancement, not a compatibility signal
- If ECHO learns from data where narration was present, weights might become biased
- 30-day window establishes baseline (pre-narration weights)

**Status:** `NarrationExposed` is always `false` until narrator goes live.

---

## Batch Worker Schedule

**WeightLearningBatchWorker:**
- **Schedule:** Sunday 04:00 UTC
- **Frequency:** Weekly (not daily — weights are slow-changing)
- **Lock:** `lock:weight-learning-batch` (6h expiry)

**Why Sunday:**
- Gives users a full week to generate ConnectionScores
- Runs after ConnectionScoreBatchWorker (daily 03:50 UTC)

**Why weekly (not daily):**
- Weights are stable (personality preferences don't change day-to-day)
- Reduces OpenAI API cost (scoring candidates for feature extraction)
- Prevents overfitting on noisy weekly fluctuations

---

## Performance

**Per user:**
- Load 10-50 ConnectionScores: ~10ms
- Score same candidates via MatchScoringService: 0.5-2s (depends on candidate count)
- Run 100 gradient ascent iterations: ~50ms
- Save 16 weight rows: ~10ms
- **Total:** 1-3s per user

**Batch (1000 users):**
- **Total:** 30-60min

---

## Monitoring

**Key metrics:**
- % users with learned weights (should grow over time)
- Avg sample count per user (should be >10)
- Top component per user distribution (should be diverse)
- Weight drift per week (should be <0.05 per component)

**Alerts:**
- Learned weight convergence rate <80% (L2 lambda too high?)
- Top component always the same (overfitting?)
- Weight drift >0.2 per week (instability)

---

## Testing

**Unit test: Gradient ascent**
```csharp
[Test]
public void LearnWeights_HighPillarCorrelation_IncreasePillarWeight()
{
    // Arrange: mock data where high PillarScore → high ConnectionScore
    var samples = new[]
    {
        (features: new[] { 0.9, 0.5, 0.5, ... }, label: 0.8),  // High pillar → good outcome
        (features: new[] { 0.3, 0.5, 0.5, ... }, label: 0.2),  // Low pillar → bad outcome
        ...
    };

    // Act
    var learnedWeights = service.LearnWeights(samples);

    // Assert: pillar weight should increase
    Assert.That(learnedWeights["pillar"], Is.GreaterThan(BaseWeights[0]));
}
```

**Integration test: End-to-end**
```csharp
[Test]
public async Task WeightLearningBatch_QualifiedUser_UpdatesWeights()
{
    // Arrange: seed user with 15 ConnectionScores
    var userId = CreateTestUser();
    SeedConnectionScores(userId, count: 15);

    // Act
    await worker.RunBatchAsync(ct);

    // Assert
    var weights = await db.UserMatchingWeights
        .Where(w => w.UserId == userId)
        .ToListAsync();

    Assert.That(weights.Count, Is.EqualTo(16));
    Assert.That(weights.Sum(w => w.LearnedWeight), Is.EqualTo(1.0).Within(0.01));
}
```

---

## Future Enhancements

**Multi-armed bandit (LinUCB):**
- Already implemented (LinUcbService)
- Complements weight learning (exploration vs exploitation)
- LinUCB boosts candidates with high uncertainty (haven't been shown much)

**Context-aware learning:**
- Learn separate weights per context (e.g., "looking for casual" vs "looking for serious")
- Requires more data (10× samples per context)

**Feature selection:**
- Automatically zero out components with low learned weight
- Requires larger sample sizes (100+ per user)

---

## See Also

- [scoring.md](./scoring.md) — 16-component formula
- [signals.md](./signals.md) — ConnectionScore aggregation
- [workers.md](./workers.md) — Batch job schedules
