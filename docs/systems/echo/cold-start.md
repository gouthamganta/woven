# ECHO Cold Start

How ECHO works on day 1 — before behavioral signals, learned weights, and full embedding coverage.

---

## The Cold Start Problem

**Problem:** New users have:
- No ConnectionScore samples (can't learn weights)
- No behavioral signals (no trial decisions, message patterns, etc.)
- Sparse embeddings (only PillarEmbedding from foundational questions)
- No visual/voice preferences (haven't made YES/NO decisions)

**Solution:** ECHO gracefully degrades using base weights, cohort defaults, and rule-based fallbacks.

---

## Embedding Availability Timeline

### Day 1 (Onboarding Exit)

**Available embeddings:**
- ✓ **PillarEmbedding** (128-dim) — from foundational questions
- ✓ **PillarScores** (8-dim) — raw scores, used as fallback

**Missing embeddings:**
- ✗ ExpressionEmbedding (no text tiles posted yet)
- ✗ StyleEmbedding (no writing samples)
- ✗ HumorEmbedding (insufficient data)
- ✗ LifestyleEmbedding (optional fields may be empty)
- ✗ EmotionalRhythmEmbedding (needs richer answers)
- ✗ AttachmentProxyEmbedding (no behavioral data)
- ✗ VisualPreference (needs ≥10 YES decisions)
- ✗ VoiceEmbedding (no voice tile posted)
- ✗ BehavioralLifestyle (no usage patterns)

**Scoring:**
- **Components available:** 1/16 (PillarScore only)
- **Weight rebalancing:** PillarScore weight → 1.0 (100%)
- **IntentScore:** Computable from IntentMetadata (even without embedding)

**Result:** Users get matches on day 1, but only based on pillar + intent alignment.

---

### Week 1 (After First Interactions)

**User actions:**
- Posts 1-2 text tiles
- Fills out optional fields (diet, workout, etc.)
- Browses 10-20 profiles (tile dwell data)
- Pops 1-2 balloons

**Available embeddings:**
- ✓ PillarEmbedding
- ✓ ExpressionEmbedding (from text tiles)
- ✓ LifestyleEmbedding (from optional fields)
- ✓ BehavioralLifestyle (sparse, low quality)

**Missing embeddings:**
- ✗ StyleEmbedding (needs more writing samples)
- ✗ HumorEmbedding (needs explicit humor content)
- ✗ EmotionalRhythmEmbedding (needs richer data)
- ✗ AttachmentProxyEmbedding (needs more behavioral signals)
- ✗ VisualPreference (needs ≥10 YES decisions)
- ✗ VoiceEmbedding (no voice tile yet)

**Scoring:**
- **Components available:** 4-6/16
- **Weight rebalancing:** Available components share the load
  - PillarScore: 0.19 → 0.35 (rebalanced)
  - IntentScore: 0.12 → 0.22
  - ExpressionScore: 0.09 → 0.16
  - LifestyleScore: 0.08 → 0.15

**Result:** Matches improve (now considering expression + lifestyle), but still missing visual/voice/attachment.

---

### Month 1 (Mature Profile)

**User actions:**
- Posted 10+ text tiles
- Made 20+ YES/NO decisions on photos
- Sent 5+ voice notes
- Completed 2-3 trials (accepted or rejected)
- 50+ messages sent across conversations

**Available embeddings:**
- ✓ All core 9 embeddings
- ✓ VisualPreference (learned from 20+ YES decisions)
- ✓ BehavioralLifestyle (high quality)
- ✓ ReceptionEmbedding (from tile dwell)

**Missing embeddings:**
- ✗ PreferenceEmbedding (needs ChatNotes, not yet wired)

**Scoring:**
- **Components available:** 14-15/16
- **Weights:** Near base weights (only minor rebalancing)

**ConnectionScores:**
- 2-5 pairs with Score ≥ 0.08 (trial accepted + some conversation)
- Not enough for weight learning (needs ≥10)

**Result:** High-quality matches using almost full ECHO formula, but still using base weights (no personalization yet).

---

### Month 3 (Personalized)

**User actions:**
- 10+ trial decisions (accepted + rejected)
- 100+ messages across 5-10 conversations
- Multiple date ideas accepted/rejected
- Explicit feedback given (5-star ratings)

**ConnectionScores:**
- 15+ pairs with Score ≥ 0.08
- Triggers weight learning

**Learned weights:**
- User's personalized 16-component weights
- E.g., PillarScore 0.25, VisualScore 0.15, IntentScore 0.18 (values personality + attraction + intent)

**Result:** Fully personalized ECHO matching.

---

## Fallback Strategies

### 1. Missing Component → Skip + Rebalance

**MatchScoringService logic:**
```csharp
var available = new bool[16];  // Tracks which components are computable

// Example: ExpressionEmbedding missing
if (viewerExpression == null || candExpression == null)
{
    available[2] = false;  // ExpressionScore not available
}

// Compute total with rebalancing
score.ComputeTotal(
    available,           // [true, true, false, true, ...]
    BaseWeights,         // [0.19, 0.12, 0.09, ...]
    learnedWeights,      // null if not enough samples
    intentMult,
    trust,
    hasSeasonResponse
);
```

**ComputeTotal logic:**
```csharp
// Sum only available weights
var totalWeight = 0.0;
for (int i = 0; i < 16; i++)
    if (available[i])
        totalWeight += weights[i];

// Rebalance so available weights sum to 1.0
var rebalanceFactor = 1.0 / totalWeight;

// Weighted sum with rebalanced weights
var weightedSum = 0.0;
for (int i = 0; i < 16; i++)
    if (available[i])
        weightedSum += (weights[i] * rebalanceFactor) * componentScores[i];

return weightedSum × intentMult × trust;
```

**Effect:**
- Missing components don't drag down score
- Available components share the load proportionally
- TotalScore stays [0, 100]

---

### 2. Embedding Missing → Use Rule-Based Fallback

**Example: LifestyleScore**

```csharp
if (viewerLifestyle != null && candLifestyle != null)
{
    // Use embedding-based similarity
    score.LifestyleScore = CosineSimilarityToScore(viewerLifestyle, candLifestyle);
}
else
{
    // Fallback: rule-based scoring from optional fields
    score.LifestyleScore = ComputeLifestyleScore(viewerFields, candFields);
}
```

**Rule-based scoring:**
- +20 if diet matches
- -30 if diet mismatch on deal-breakers (vegan/non-vegan)
- +15 if smoking matches, -15 if mismatch
- +10 if religion matches
- ±10 for drinking/workout alignment

**Effect:** Lifestyle compatibility still considered, even without embeddings.

---

### 3. Visual/Voice Preference → Skip Until ≥10 Samples

**Visual preference:**
```csharp
if (viewerVisualPref != null && candPhoto != null)
{
    score.VisualScore = ComputeVisualScore(...);
    available[4] = true;
}
else
{
    available[4] = false;  // Skip VisualScore
}
```

**Why ≥10 samples:**
- <10 samples → noisy preference (might like first 3 people they see, then taste changes)
- ≥10 samples → stable preference (converged)

**Effect:** No visual matching for first 2-3 weeks (relies on other dimensions).

---

### 4. No Learned Weights → Use Base Weights

**MatchScoringService:**
```csharp
var learnedWeightMap = await db.UserMatchingWeights
    .Where(w => w.UserId == userId && w.SampleCount >= 5)
    .ToDictionaryAsync(w => w.Component, w => w.LearnedWeight);

double[]? learnedWeights = null;

if (learnedWeightMap.Count >= 8)  // Need ≥8 components learned
{
    learnedWeights = new double[16];
    for (int i = 0; i < 16; i++)
        learnedWeights[i] = learnedWeightMap.TryGetValue(ComponentNames[i], out var lw)
            ? lw : BaseWeights[i];
}

// Use learned if available, else base
score.ComputeTotal(..., BaseWeights, learnedWeights, ...);
```

**Effect:** New users get reasonable matches using base weights (population averages).

---

## Cohort Defaults (Future)

**Problem:** Some users have very sparse data (e.g., no text tiles, no voice tiles, minimal optional fields).

**Solution:** Use cohort-level defaults based on demographic + pillar scores.

**Example cohort:**
- Age 25-30, female, high Curiosity pillar, high Energy pillar
- Infer: likely to value ExpressionScore + StyleScore more than VisualScore

**Implementation (future):**
```csharp
if (userExpression == null && user.Age >= 25 && user.Age <= 30 && user.Gender == "female")
{
    // Use cohort-mean ExpressionEmbedding for this demographic
    userExpression = await GetCohortDefaultEmbedding("expression", user.Demographics);
}
```

**Status:** Not yet implemented. Current approach: skip missing components.

---

## Intent Multiplier on Day 1

**Intent data available from foundational questions:**
```json
{
  "seriousness": 0.85,         // [0,1]: casual → very serious
  "commitmentReadiness": 0.72,
  "tags": ["long-term", "marriage-minded"]
}
```

**IntentScore computable even without embeddings:**
```csharp
var seriousnessDiff = Math.Abs(userIntent.Seriousness - candIntent.Seriousness);
var seriousnessScore = 100 * (1 - seriousnessDiff);

var commitmentDiff = Math.Abs(userIntent.CommitmentReadiness - candIntent.CommitmentReadiness);
var commitmentScore = 100 * (1 - commitmentDiff);

var sharedTags = userIntent.Tags.Intersect(candIntent.Tags).Count();
var tagBonus = Math.Min(20, sharedTags * 10);

IntentScore = seriousnessScore * 0.5 + commitmentScore * 0.3 + tagBonus;
```

**Intent multiplier:**
- Exact alignment (diff < 0.05) → 1.05× boost
- Large mismatch (diff > 0.6) → 0.82× penalty

**Effect:** Even on day 1, serious + serious users are boosted, serious + casual users are penalized.

---

## Mood Line on Day 1

**If pool size < 8:**
```
"ECHO is still building your pool."
```

**If scores are low (avg < 58):**
```
"ECHO is still finding your frequency."
```

**Effect:** Sets user expectation that ECHO improves over time.

---

## Deck Quality Over Time

| Week | Components Available | Learned Weights | Match Quality |
|---|---|---|---|
| 1 | 2-4 / 16 | No | 40-60% (baseline) |
| 2 | 4-6 / 16 | No | 50-70% |
| 4 | 8-10 / 16 | No | 60-80% |
| 8 | 12-14 / 16 | No | 70-85% |
| 12 | 14-15 / 16 | Yes (≥10 samples) | 75-90% (personalized) |
| 24 | 14-15 / 16 | Yes (≥30 samples) | 80-95% (converged) |

**Match quality** = avg TotalScore of daily deck (out of 100).

**Plateau at week 12-24:** Most users converge to stable preferences.

---

## Bootstrap Signals (Passive Interest)

**Problem:** New users have no behavioral signals (trial decisions, messages, etc.).

**Solution:** Use passive interest signals to bootstrap:

**OrbitGravity:**
- Tracks which candidates the user dwells on (≥8s tile dwell)
- No action required (browsing is enough)
- Feeds into OrbitGravityScore component

**ReceptionEmbedding:**
- Built from tiles user dwells on (≥8s)
- Captures content taste
- Feeds into SharedTileAffinityScore component

**Effect:** Even before first balloon pop, ECHO learns what content they engage with.

---

## A/B Test: Cold Start Strategies

**Control group (current):**
- Skip missing components, rebalance weights
- Use base weights until ≥10 ConnectionScores

**Treatment group (future):**
- Use cohort defaults for missing embeddings
- Learn weights from ≥5 ConnectionScores (lower threshold)
- Boost PillarScore weight 0.19 → 0.30 on day 1

**Hypothesis:** Treatment group gets better early matches (fewer "meh" day-1 decks).

---

## Testing Cold Start

**Unit test: Missing components → rebalance**
```csharp
[Test]
public void ComputeTotal_HalfMissing_RebalancesWeights()
{
    var available = new bool[16];
    for (int i = 0; i < 8; i++) available[i] = true;  // Only 8/16 available

    var score = new MatchScore(candidateId: 1);
    score.ComputeTotal(available, BaseWeights, null, intentMult: 1.0, trust: 1.0, false);

    // Total should still be [0, 100] despite missing half the components
    Assert.That(score.TotalScore, Is.GreaterThan(0).And.LessThanOrEqualTo(100));
}
```

**Integration test: New user gets deck**
```csharp
[Test]
public async Task GetDeck_NewUser_ReturnsFiveCandidates()
{
    // Arrange: user just completed onboarding (only PillarEmbedding)
    var userId = CreateNewUser(withPillarOnly: true);
    SeedCandidates(count: 50);

    // Act
    var deck = await orchestrator.GetOrCreateDeckAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow));

    // Assert: should get 5 candidates even with sparse data
    Assert.That(deck.Items.Count, Is.EqualTo(5));
    Assert.That(deck.MoodLine, Is.EqualTo("ECHO is still building your pool."));
}
```

---

## See Also

- [embeddings.md](./embeddings.md) — Which embeddings are available when
- [scoring.md](./scoring.md) — How missing components are handled
- [matching-pipeline.md](./matching-pipeline.md) — Deck generation flow
