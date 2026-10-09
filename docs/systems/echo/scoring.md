# ECHO 16-Component Scoring Formula

MatchScoringService computes compatibility across 16 weighted dimensions, producing a TotalScore [0, 100] that drives daily deck selection.

**File:** `backend/WovenBackend/Services/Matchmaking/MatchScoringService.cs`

---

## Formula Overview

```
TotalScore = (Σ [weight_i × score_i × available_i]) × intentMult × trustPenalty
```

Where:
- `score_i` = component score [0, 100]
- `weight_i` = base weight (or learned weight if user has ≥8 learned components with ≥5 samples)
- `available_i` = 1 if component computable, 0 if missing data
- `intentMult` = intent alignment multiplier [0.7–1.05]
- `trustPenalty` = trust score penalty [0.5–1.0]

**Normalization:** Weights always sum to 1.0 after learning, so weighted sum stays [0, 100].

---

## Base Weights

```csharp
private static readonly double[] BaseWeights = new[]
{
    0.19,  // #1  Pillar
    0.12,  // #2  Intent
    0.09,  // #3  Expression
    0.09,  // #4  Style
    0.10,  // #5  Visual
    0.08,  // #6  Voice
    0.07,  // #7  Humor
    0.08,  // #8  Lifestyle
    0.05,  // #9  Behavioral Lifestyle
    0.04,  // #10 Emotional Rhythm
    0.04,  // #11 Attachment
    0.08,  // #12 Orbit Gravity
    0.06,  // #13 Pulse
    0.03,  // #14 CF (Collaborative Filtering)
    0.05,  // #15 Shared Tile Affinity
    0.04   // #16 Preference Affinity
};
```

**Total:** 1.00 (weights are normalized)

---

## Component Breakdown

### 1. PillarScore (weight 0.19) — Highest Priority

**What it measures:** Core personality alignment across 8 dimensions.

**Pillars:**
- Lifestyle
- Energy
- Communication
- Affection
- Stability
- Values
- Curiosity
- Emotional Rhythm

**Computation:**
```csharp
if (viewerPillarEmbedding != null && candidatePillarEmbedding != null)
{
    var similarity = CosineSimilarity(viewerPillarEmbedding, candidatePillarEmbedding);
    score = (similarity + 1.0) / 2.0 * 100.0;  // [-1,1] → [0,100]
}
else
{
    // Fallback: raw pillar score cosine (8-dim vector)
    var similarity = viewerPillarScores.CosineSimilarity(candidatePillarScores);
    score = similarity * 100.0;
}
```

**Why highest weight:**
- Foundational compatibility (values, communication style, emotional needs)
- Data available from day 1 (foundational questions)
- Stable over time (personality is slow-changing)

---

### 2. IntentScore (weight 0.12)

**What it measures:** Alignment on relationship seriousness and commitment readiness.

**Data source:** `IntentMetadata` from user vector:
```json
{
  "seriousness": 0.85,        // [0,1]: casual → very serious
  "commitmentReadiness": 0.72, // [0,1]: exploring → ready now
  "tags": ["long-term", "marriage-minded"]
}
```

**Computation:**
```csharp
var seriousnessDiff = Math.Abs(userIntent.Seriousness - candIntent.Seriousness);
var seriousnessScore = 100 * (1 - seriousnessDiff);

var commitmentDiff = Math.Abs(userIntent.CommitmentReadiness - candIntent.CommitmentReadiness);
var commitmentScore = 100 * (1 - commitmentDiff);

var sharedTags = userIntent.Tags.Intersect(candIntent.Tags).Count();
var tagBonus = Math.Min(20, sharedTags * 10);

IntentScore = Clamp(seriousnessScore * 0.5 + commitmentScore * 0.3 + tagBonus, 0, 100);
```

**Why important:**
- Prevents wasted time (casual vs serious mismatch)
- Strong predictor of match success (intent alignment → faster decisions)

---

### 3. ExpressionScore (weight 0.09)

**What it measures:** Similarity in what users post (text tiles).

**Computation:**
```csharp
if (viewerExpression != null && candExpression != null)
{
    score = CosineSimilarityToScore(viewerExpression, candExpression);
}
```

**Why useful:**
- Matches users who post about similar topics
- Proxy for shared interests (hiking, philosophy, art, etc.)

---

### 4. StyleScore (weight 0.09)

**What it measures:** Writing style compatibility (formal/casual, terse/verbose, emoji usage).

**Computation:**
```csharp
if (viewerStyle != null && candStyle != null)
{
    score = CosineSimilarityToScore(viewerStyle, candStyle);
}
```

**Why useful:**
- Communication style affects relationship satisfaction
- Mismatch (one texts "ok", other sends paragraphs) causes friction

---

### 5. VisualScore (weight 0.10)

**What it measures:** How well candidate's photo matches viewer's learned visual preferences.

**Computation:**
```csharp
if (viewerVisualPref != null && candPhoto != null)
{
    var dotProduct = DotProduct(viewerVisualPref, candPhoto);
    score = Clamp(50 + dotProduct * 50, 0, 100);
}
```

**Photo used:** Candidate's primary photo (first by SortOrder)

**Preference learning:**
- Viewer's YES decisions on photos → mean embedding
- Requires ≥10 YES samples
- Updates nightly

**Why controversial but necessary:**
- Physical attraction matters (user feedback confirms this)
- CLIP embedding is content-agnostic (no age/race/beauty analysis)
- Learned, not stated (no "I prefer tall/blonde" filters)

---

### 6. VoiceScore (weight 0.08)

**What it measures:** Voice compatibility (tone, pitch, speaking style).

**Computation:**
```csharp
if (viewerVoicePref != null && candVoiceEmbedding != null)
{
    var dotProduct = DotProduct(viewerVoicePref, candVoiceEmbedding);
    score = Clamp(50 + dotProduct * 50, 0, 100);
}
```

**Voice embedding:** ECAPA-TDNN 192-dim speaker embedding (from voice tiles)

**Preference learning:** Same as visual (YES decisions on voice tiles → mean)

**Why important:**
- Voice is underrated in dating apps (Woven's differentiator)
- Strong predictor of in-person chemistry

---

### 7. HumorScore (weight 0.07)

**What it measures:** Compatibility in humor sense (dry/sarcastic/absurdist/wholesome).

**Computation:**
```csharp
if (viewerHumor != null && candHumor != null)
{
    score = CosineSimilarityToScore(viewerHumor, candHumor);
}
```

**Why important:**
- Shared humor is critical for long-term relationship satisfaction
- Mismatch (one finds sarcasm offensive, other uses it constantly) causes conflict

---

### 8. LifestyleScore (weight 0.08)

**What it measures:** Lifestyle compatibility (diet, workout, smoking, drinking, religion).

**Computation:**
```csharp
if (viewerLifestyle != null && candLifestyle != null)
{
    score = CosineSimilarityToScore(viewerLifestyle, candLifestyle);
}
else
{
    // Fallback: rule-based scoring from optional fields
    score = ComputeLifestyleScore(viewerFields, candFields);
}
```

**Fallback rules:**
- +20 if diet matches (or both unspecified)
- -30 if diet mismatch on vegan/vegetarian
- +15 if smoking matches, -15 if mismatch
- +10 if religion matches
- ±10 for drinking/workout alignment
- Clamped [0, 100]

**Why important:**
- Deal-breakers (e.g., vegan vs meat-eater) surface early
- Lifestyle alignment → fewer conflicts

---

### 9. BehavioralLifestyleScore (weight 0.05)

**What it measures:** Similarity in actual app usage patterns (not stated preferences).

**Data:** 16-dim behavioral fingerprint (session duration, message frequency, response latency, etc.)

**Computation:**
```csharp
if (viewerBehavioral != null && candBehavioral != null)
{
    score = VectorCosineSimilarityToScore(viewerBehavioral, candBehavioral);
}
```

**Why useful:**
- Actions > words (stated preferences often don't match behavior)
- E.g., both send long messages + reply quickly → good rhythm

---

### 10. EmotionalRhythmScore (weight 0.04)

**What it measures:** Compatibility in emotional expression patterns.

**Computation:**
```csharp
if (viewerEmotional != null && candEmotional != null)
{
    score = CosineSimilarityToScore(viewerEmotional, candEmotional);
}
```

**Why useful:**
- Emotional expressiveness mismatch (one shares feelings openly, other is reserved) causes friction

---

### 11. AttachmentScore (weight 0.04)

**What it measures:** Compatibility in attachment style (secure/anxious/avoidant proxies).

**Computation:**
```csharp
if (viewerAttachment != null && candAttachment != null)
{
    score = CosineSimilarityToScore(viewerAttachment, candAttachment);
}
```

**Proxy:** Inferred from behavioral patterns (response latency variance, re-engagement rate, etc.)

**Why proxy:** Asking "what's your attachment style?" is too clinical. We infer from behavior.

---

### 12. OrbitGravityScore (weight 0.08)

**What it measures:** Candidate's passive interest in viewer (orbit = viewed viewer's tiles, high dwell time).

**Computation:**
```csharp
if (orbitGravity exists for this pair)
{
    var daysSince = (now - orbitGravity.LastOrbitAt).TotalDays;
    var decayed = orbitGravity.Score * Math.Exp(-0.1 * daysSince);
    score = Clamp(decayed, 0, 100);
}
```

**Orbit gravity:**
- Built from candidate's tile dwell on viewer's content
- Score [0, 100] based on dwell time + frequency
- Decays exponentially (half-life ~7 days)

**Why important:**
- One-sided interest is wasteful (viewer likes candidate, but candidate never engaged with viewer's content)
- High orbit → mutual curiosity (both have shown interest)

---

### 13. PulseScore (weight 0.06)

**What it measures:** Communication rhythm compatibility (initiative, social capacity, ghost risk).

**Data:** `Pulse` metadata from user vector:
```json
{
  "socialCapacity": 0.65,   // [0,1]: introverted → very social
  "initiative": 0.72,       // [0,1]: reactive → proactive
  "ghostRisk": 0.15         // [0,1]: low → high risk of ghosting
}
```

**Computation:**
```csharp
double score = 50.0;

// Social capacity alignment (prefer similar levels)
if (userPulse.socialCapacity and candPulse.socialCapacity exist)
    score += 20 * (1 - Math.Abs(userCap - candCap));

// Initiative complementarity (prefer one proactive, one reactive)
var initDiff = Math.Abs(userInit - candInit);
score += (initDiff > 0.4) ? 15 : (initDiff < 0.2 ? 5 : 0);

// Ghost risk penalty
if (candPulse.ghostRisk > 0.6)
    score -= 10;

return Clamp(score, 0, 100);
```

**Why useful:**
- Social capacity mismatch (introvert + extrovert) can work, but needs awareness
- Initiative complementarity (both passive = conversation dies, both aggressive = overwhelming)
- Ghost risk protects users from flaky matches

---

### 14. CfScore (weight 0.03)

**What it measures:** Collaborative filtering — "users who liked candidates you liked also liked this candidate."

**Computation:**
```csharp
if (cfScore exists)
{
    score = Math.Min(100.0, cfScore * 100.0);  // CfScore is [0,1], scale to [0,100]
}
```

**How CfScore is built:**
- `CfScoreBatchWorker` (daily 05:00 UTC) computes Jaccard similarity:
  ```
  Jaccard(A, B) = |A ∩ B| / |A ∪ B|
  ```
  Where A = set of candidates viewer dwelled on or orbited, B = same for other users.
- Users with high Jaccard → similar taste → their liked candidates are recommended to viewer

**Why low weight:**
- Cold start problem (new users have no overlap)
- Can amplify bias (everyone sees same "popular" candidates)
- Used for diversity, not primary ranking

---

### 15. SharedTileAffinityScore (weight 0.05)

**What it measures:** Similarity in content consumption taste (what tiles they dwell on).

**Computation:**
```csharp
if (viewerReception != null && candReception != null)
{
    score = CosineSimilarityToScore(viewerReception, candReception);
}
```

**Reception embedding:** Built from tiles user dwelled on (≥8s) in past 90 days.

**Why useful:**
- Shared taste in content (both dwell on philosophy tiles, or travel stories, etc.)
- Different from Expression (what you post vs what you consume)

---

### 16. PreferenceAffinityScore (weight 0.04)

**What it measures:** Cross-modal match — what viewer says they like (from ChatNotes) vs what candidate posts.

**Computation:**
```csharp
if (viewerPreference != null && candExpression != null)
{
    score = CosineSimilarityToScore(viewerPreference, candExpression);
}
```

**Preference embedding:** Built from ChatNotes (private notes viewer writes about matches).

**Example:**
- Viewer's ChatNotes: "I like people who are adventurous"
- Candidate's Expression: "I love spontaneous road trips"
- High PreferenceAffinityScore

**Status:** Worker stub exists, not fully wired yet.

---

## Intent Multiplier

**After computing weighted sum, apply intent alignment multiplier:**

```csharp
var diff = Math.Abs(userIntent.Seriousness - candIntent.Seriousness);

if (diff < 0.05) return 1.05;  // Exact alignment → 5% boost
if (diff < 0.3)  return 1.00;  // Close alignment → no change
if (diff < 0.6)  return 0.92;  // Moderate mismatch → -8%
if (diff > 0.6)  return 0.82;  // Large mismatch → -18%

// No intent data
return 0.70;  // -30% penalty (missing critical data)
```

**Effect:**
- Serious + Serious → 5% boost (1.05×)
- Serious + Casual → 18% penalty (0.82×)
- Missing intent → 30% penalty (0.70×)

**Why:**
- Intent mismatch wastes time (even if personality aligns)
- Strong predictor of match success

---

## Trust Penalty

**After intent multiplier, apply trust score penalty:**

```csharp
var trust = trustScores.GetValueOrDefault(candidateId, 0.5);
trustPenalty = Clamp(trust, 0.5, 1.0);  // [0.5, 1.0]
```

**Effect:**
- TrustScore 1.0 → no penalty (1.0×)
- TrustScore 0.5 → 50% penalty (0.5×)
- TrustScore < 0.25 → excluded from pool entirely (hard filter)

**Why:**
- Low-trust users (flagged by others) still get some matches, but deprioritized
- Prevents false positives (one bad flag shouldn't kill all matches)

---

## Final Formula

```
TotalScore = (
    0.19 × PillarScore +
    0.12 × IntentScore +
    0.09 × ExpressionScore +
    0.09 × StyleScore +
    0.10 × VisualScore +
    0.08 × VoiceScore +
    0.07 × HumorScore +
    0.08 × LifestyleScore +
    0.05 × BehavioralLifestyleScore +
    0.04 × EmotionalRhythmScore +
    0.04 × AttachmentScore +
    0.08 × OrbitGravityScore +
    0.06 × PulseScore +
    0.03 × CfScore +
    0.05 × SharedTileAffinityScore +
    0.04 × PreferenceAffinityScore
) × intentMult × trustPenalty
```

**With missing components:**
```
TotalScore = (Σ available weights normalized to 1.0) × intentMult × trustPenalty
```

**Example:**
- Only 8/16 components available
- Sum of available weights = 0.65
- Normalize: each weight × (1.0 / 0.65) = 1.54×
- Weighted sum stays [0, 100]

---

## Learned Weights

**After user has ≥10 ConnectionScore samples:**

`WeightLearningService` learns personalized weights via logistic regression.

**Activation condition:**
```csharp
if (learnedWeightMap.Count >= ComponentNames.Length / 2)  // ≥8 components
{
    learnedWeightsArray = new double[16];
    for (int i = 0; i < 16; i++)
        learnedWeightsArray[i] = learnedWeightMap.TryGetValue(ComponentNames[i], out var lw)
            ? lw : BaseWeights[i];
}
```

**Effect:**
- User A might learn: PillarScore 0.25, IntentScore 0.18 (values personality + intent more)
- User B might learn: VisualScore 0.20, VoiceScore 0.15 (values attraction more)
- Weights still sum to 1.0 (normalized after learning)

**See [learning.md](./learning.md) for algorithm details.**

---

## Performance

**Scoring 100 candidates:**
- Load viewer vector: ~10ms
- Load 100 candidate vectors: ~50ms (batch SQL)
- Batch-load auxiliary data (CfScores, OrbitGravities, etc.): ~100ms
- Compute 16 components × 100 candidates: ~500ms
- **Total:** ~1s

**Bottlenecks:**
- Cosine similarity (pgvector does this in-DB, fast)
- Batch SQL joins (indexed on UserId)

---

## See Also

- [learning.md](./learning.md) — Weight personalization
- [embeddings.md](./embeddings.md) — 9 embedding modalities
- [matching-pipeline.md](./matching-pipeline.md) — How scoring fits into deck generation
