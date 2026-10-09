# Behavioral Fingerprint (16-dim)

**Last Updated:** 2026-10-07  
**Status:** Production  
**File:** `backend/WovenBackend/Services/Embeddings/BehavioralFingerprintService.cs`

---

## Overview

The Behavioral Fingerprint is a **16-dimensional vector** that captures how users behave in the app, derived from `MatchSignalLogs`. Unlike stated preferences (e.g., "I prefer outdoor activities"), this embedding reflects actual behavior (e.g., user responds within 10 minutes 80% of the time, sends long messages, rarely dwells on voice tiles).

**Storage:** `UserBehavioralFingerprint` table (NOT in `UserVector`)  
**Update Frequency:** Nightly (02:30 UTC) via `EmbeddingBatchWorker`  
**Data Window:** 180 days of MatchSignalLogs

---

## Why It Exists

1. **Actions > Words:** Users often misreport their preferences. Behavioral data is ground truth.
2. **Attachment Proxy:** The fingerprint is used to derive 4-dim attachment style (secure/anxious/avoidant/disorganised).
3. **ECHO Component #9:** Behavioral Lifestyle Score compares users' behavioral fingerprints for rhythm compatibility.
4. **Learning Signal:** Component weights are personalized by comparing fingerprint deltas to match outcomes.

---

## 16 Dimensions

All dimensions are normalized to **[0, 1]**, with **0.5 = neutral** when data is missing or insufficient.

```csharp
// Order in the float[] array:
[0]  response_speed           // Fast replies score high (30-min half-life)
[1]  message_volume           // Total MessageSent / (candidates × 10 expected)
[2]  self_disclosure_mean     // Mean SelfDisclosureRatio across threads
[3]  disclosure_balance       // How close to 0.5 the ratio is (1 = balanced)
[4]  trial_affinity           // P(TrialAccepted | any trial decision)
[5]  love_expressiveness      // (ChatNoteLove + MessageLove) per candidate
[6]  feedback_positivity      // Mean ExplicitFeedback value
[7]  tile_curiosity           // Mean TileDwell / 8000ms threshold
[8]  voice_curiosity          // Mean VoiceDwell / 8000ms threshold
[9]  first_msg_speed          // Quick first-message score (24-hour half-life)
[10] date_progression         // DateIdeaAccepted / (accepted + rejected + 1)
[11] profile_exploration      // Mean ProfileVisitDepth / 5 tiles
[12] game_engagement          // count(GameCompleted) / 10
[13] balloon_eagerness        // count(BalloonPop) / 20
[14] engagement_breadth       // Distinct candidates with signals / 50
[15] signal_density           // Total signal count / 500
```

---

## Computation Details

### [0] response_speed
**What:** How fast user responds to messages.

**Formula:**
```csharp
var meanLatencyMs = signals.Where(s => s.EventType == "MessageResponseLatencyMs")
    .Average(s => s.EventValue);
responseSpeed = 1.0 / (1.0 + meanLatencyMs / 1_800_000);  // 30-min half-life
```

**Range:**
- 1.0 = instant replies (0ms latency)
- 0.5 = 30-minute average latency
- 0.25 = 90-minute average latency
- 0.0 = never replies

**Missing data:** 0.5 (neutral)

---

### [1] message_volume
**What:** How many messages user sends per candidate (normalized).

**Formula:**
```csharp
var totalMessagesSent = signals.Where(s => s.EventType == "MessageSent").Count();
var candidates = signals.Select(s => s.CandidateId).Distinct().Count();
messageVolume = Math.Min(1.0, totalMessagesSent / (candidates × 10));
```

**Range:**
- 1.0 = ≥10 messages per candidate (high volume)
- 0.5 = 5 messages per candidate
- 0.0 = 0 messages sent

**Missing data:** 0.5 (neutral)

---

### [2] self_disclosure_mean
**What:** Mean self-disclosure ratio across threads (0 = all questions, 1 = all statements).

**Data Source:** `SelfDisclosureRatio` signal logged per thread.

**Formula:**
```csharp
var ratios = signals.Where(s => s.EventType == "SelfDisclosureRatio")
    .Select(s => s.EventValue);
selfDisclosureMean = ratios.Any() ? ratios.Average() : 0.5f;
```

**Range:**
- 1.0 = only statements ("I", "me", declarative)
- 0.5 = balanced (50% questions, 50% statements)
- 0.0 = only questions ("you", "how", interrogative)

**Missing data:** 0.5 (neutral)

---

### [3] disclosure_balance
**What:** How close user is to balanced disclosure (0.5 ideal).

**Formula:**
```csharp
disclosureBalance = 1.0 - 2.0 * Math.Abs(selfDisclosureMean - 0.5);
```

**Range:**
- 1.0 = perfectly balanced (self_disclosure_mean = 0.5)
- 0.5 = 25% skew (self_disclosure_mean = 0.25 or 0.75)
- 0.0 = extreme skew (self_disclosure_mean = 0 or 1)

**Missing data:** 0.5 (neutral)

---

### [4] trial_affinity
**What:** Probability user continues after trial period.

**Formula:**
```csharp
var accepted = signals.Where(s => s.EventType == "TrialAccepted").Count();
var rejected = signals.Where(s => s.EventType == "TrialRejected").Count();
trialAffinity = (accepted + rejected == 0) ? 0.5f : (float)accepted / (accepted + rejected);
```

**Range:**
- 1.0 = always continues trials
- 0.5 = 50/50 continue rate (or no trial data)
- 0.0 = always ends trials

**Missing data:** 0.5 (neutral)

---

### [5] love_expressiveness
**What:** How often user expresses love/affection (ChatNoteLove + MessageLove).

**Formula:**
```csharp
var loves = signals.Where(s => s.EventType == "ChatNoteLove" || s.EventType == "MessageLove").Count();
var candidates = signals.Select(s => s.CandidateId).Distinct().Count();
loveExpressiveness = Math.Min(1.0, loves / (candidates × 2));  // Cap at 2 loves per candidate
```

**Range:**
- 1.0 = ≥2 love signals per candidate
- 0.5 = 1 love per candidate
- 0.0 = no love signals

**Missing data:** 0.5 (neutral)

---

### [6] feedback_positivity
**What:** Mean ExplicitFeedback value (1 = positive, 0 = negative).

**Data Source:** `ExplicitFeedback` signal (from post-match survey).

**Formula:**
```csharp
var feedbacks = signals.Where(s => s.EventType == "ExplicitFeedback")
    .Select(s => s.EventValue);
feedbackPositivity = feedbacks.Any() ? feedbacks.Average() : 0.5f;
```

**Range:**
- 1.0 = all positive feedback
- 0.5 = mixed (or no feedback data)
- 0.0 = all negative feedback

**Missing data:** 0.5 (neutral)

---

### [7] tile_curiosity
**What:** How long user dwells on text/photo tiles (mean dwell time).

**Formula:**
```csharp
var dwells = signals.Where(s => s.EventType == "TileDwell")
    .Select(s => s.EventValue);  // ms
tileCuriosity = dwells.Any() ? Math.Min(1.0, dwells.Average() / 8000) : 0.5f;
```

**Range:**
- 1.0 = ≥8s mean dwell time
- 0.5 = 4s mean dwell time
- 0.0 = <1s mean dwell time (skips tiles)

**Missing data:** 0.5 (neutral)

---

### [8] voice_curiosity
**What:** How long user dwells on voice tiles (mean dwell time).

**Formula:**
```csharp
var dwells = signals.Where(s => s.EventType == "VoiceDwell")
    .Select(s => s.EventValue);  // ms
voiceCuriosity = dwells.Any() ? Math.Min(1.0, dwells.Average() / 8000) : 0.5f;
```

**Range:**
- 1.0 = ≥8s mean dwell time (listens to completion)
- 0.5 = 4s mean dwell time
- 0.0 = <1s mean dwell time (skips voice tiles)

**Missing data:** 0.5 (neutral)

---

### [9] first_msg_speed
**What:** How fast user sends first message after match.

**Formula:**
```csharp
var meanLatencyMs = signals.Where(s => s.EventType == "TimeToFirstMessageMs")
    .Average(s => s.EventValue);
firstMsgSpeed = 1.0 / (1.0 + meanLatencyMs / 86_400_000);  // 24-hour half-life
```

**Range:**
- 1.0 = instant first message (0ms)
- 0.5 = 24-hour average latency
- 0.25 = 72-hour average latency
- 0.0 = never sends first message

**Missing data:** 0.5 (neutral)

---

### [10] date_progression
**What:** Probability user accepts date ideas.

**Formula:**
```csharp
var accepted = signals.Where(s => s.EventType == "DateIdeaAccepted").Count();
var rejected = signals.Where(s => s.EventType == "DateIdeaRejected").Count();
dateProgression = (float)accepted / (accepted + rejected + 1);  // +1 smoothing
```

**Range:**
- 1.0 = always accepts date ideas (or accepted >> rejected)
- 0.5 = 50/50 accept rate
- 0.0 = always rejects date ideas

**Missing data:** 0.0 (conservative — no date ideas yet)

---

### [11] profile_exploration
**What:** How deep user explores candidate profiles (mean tiles viewed per visit).

**Formula:**
```csharp
var depths = signals.Where(s => s.EventType == "ProfileVisitDepth")
    .Select(s => s.EventValue);  // tile count
profileExploration = depths.Any() ? Math.Min(1.0, depths.Average() / 5) : 0.5f;
```

**Range:**
- 1.0 = ≥5 tiles viewed per profile visit (thorough)
- 0.5 = 2-3 tiles viewed (medium)
- 0.0 = <1 tile viewed (superficial)

**Missing data:** 0.5 (neutral)

---

### [12] game_engagement
**What:** How many icebreaker games user completes.

**Formula:**
```csharp
var completed = signals.Where(s => s.EventType == "GameCompleted").Count();
gameEngagement = Math.Min(1.0, completed / 10);
```

**Range:**
- 1.0 = ≥10 games completed
- 0.5 = 5 games completed
- 0.0 = 0 games completed

**Missing data:** 0.5 (neutral)

---

### [13] balloon_eagerness
**What:** How often user pops balloons (unlocks chat threads).

**Formula:**
```csharp
var pops = signals.Where(s => s.EventType == "BalloonPop").Count();
balloonEagerness = Math.Min(1.0, pops / 20);
```

**Range:**
- 1.0 = ≥20 balloon pops
- 0.5 = 10 pops
- 0.0 = 0 pops

**Missing data:** 0.5 (neutral)

---

### [14] engagement_breadth
**What:** How many distinct candidates user has engaged with.

**Formula:**
```csharp
var candidates = signals.Select(s => s.CandidateId).Distinct().Count();
engagementBreadth = Math.Min(1.0, candidates / 50);
```

**Range:**
- 1.0 = ≥50 distinct candidates
- 0.5 = 25 candidates
- 0.0 = 0-5 candidates (narrow engagement)

**Missing data:** 0.5 (neutral)

---

### [15] signal_density
**What:** Total number of signals logged (proxy for app engagement).

**Formula:**
```csharp
signalDensity = Math.Min(1.0, signals.Count / 500);
```

**Range:**
- 1.0 = ≥500 signals (very active)
- 0.5 = 250 signals (medium activity)
- 0.0 = <50 signals (low activity)

**Missing data:** 0.5 (neutral)

---

## Usage in ECHO

### 1. Behavioral Lifestyle Score (Component #9)
**Weight:** 0.05 (5%)

**Computation:**
```csharp
if (viewerBehavioral != null && candBehavioral != null)
{
    var similarity = CosineSimilarity(viewerBehavioral, candBehavioral);
    BehavioralLifestyleScore = similarity * 100;  // [0, 100]
}
```

**Why:** Matches users with similar behavioral patterns (both reply fast, both thorough, etc.)

---

### 2. Attachment Proxy Derivation
**Service:** `AttachmentProxyService.Derive(fingerprint)`

**Formula:**
```csharp
// Secure: balanced disclosure + willing to deepen + positive outcomes
secure = 0.30 × fp[3]   // disclosure_balance
       + 0.25 × fp[4]   // trial_affinity
       + 0.25 × fp[6]   // feedback_positivity
       + 0.20 × fp[10]; // date_progression

// Anxious: hypervigilant + activity flooding + over-sharing
anxious = 0.30 × fp[0]            // response_speed (fast)
        + 0.20 × fp[9]            // first_msg_speed (eager)
        + 0.25 × fp[15]           // signal_density (high)
        + 0.25 × (1 - fp[3]);     // 1 - disclosure_balance (imbalanced)

// Avoidant: slow + unexpressive + rejects deepening
avoidant = 0.25 × (1 - fp[0])   // 1 - response_speed
         + 0.25 × (1 - fp[5])   // 1 - love_expressiveness
         + 0.25 × (1 - fp[4])   // 1 - trial_affinity
         + 0.25 × (1 - fp[10]); // 1 - date_progression

// Disorganised: approach-avoidance conflict
disorganised = Math.Min(anxious, avoidant);
```

**Result:** 4-dim vector stored in `UserVector.AttachmentProxyEmbedding`

**See:** [README.md](./README.md#attachment-proxy-4-dim)

---

## Storage Schema

```sql
CREATE TABLE user_behavioral_fingerprints (
    user_id INT PRIMARY KEY,
    vector_json TEXT NOT NULL,         -- float[16] as JSON
    computed_at TIMESTAMP NOT NULL
);

CREATE INDEX idx_behavioral_fingerprints_user_id ON user_behavioral_fingerprints(user_id);
```

**Sample Row:**
```json
{
  "user_id": 123,
  "vector_json": "[0.82, 0.65, 0.58, 0.76, 0.91, 0.42, 0.68, 0.55, 0.33, 0.87, 0.71, 0.49, 0.62, 0.78, 0.41, 0.86]",
  "computed_at": "2026-10-07T02:30:00Z"
}
```

---

## Update Workflow

**Worker:** `EmbeddingBatchWorker`  
**Schedule:** Daily at 02:30 UTC  
**Data Window:** 180 days of `MatchSignalLogs`

**Process:**
1. Load all signals for user where `OccurredAt >= now - 180 days`
2. Compute 16 dimensions via pure function `Compute(signals)`
3. Upsert to `user_behavioral_fingerprints` table
4. Run `AttachmentProxyService.ComputeAttachmentProxyAsync()` (depends on fingerprint)

**Code Path:**
```csharp
// EmbeddingBatchWorker.cs (line 63)
await sp.GetRequiredService<IBehavioralFingerprintService>().ComputeAsync(userId, ct);
await sp.GetRequiredService<IAttachmentProxyService>().ComputeAttachmentProxyAsync(userId, ct);
```

---

## Debugging

### Inspect User's Fingerprint
```sql
SELECT
    user_id,
    vector_json,
    computed_at
FROM user_behavioral_fingerprints
WHERE user_id = 123;
```

### Check Signal Coverage
```sql
-- Count signals per EventType for a user
SELECT
    event_type,
    COUNT(*) AS count
FROM match_signal_logs
WHERE viewer_id = 123
  AND occurred_at >= NOW() - INTERVAL '180 days'
GROUP BY event_type
ORDER BY count DESC;
```

### Manual Recompute
```bash
# Trigger recompute for user 123 (via psql)
DELETE FROM user_behavioral_fingerprints WHERE user_id = 123;

# Wait for next batch run at 02:30 UTC
# OR manually invoke via admin endpoint (if exposed)
curl -X POST https://api.woven.me/admin/embeddings/recompute/123
```

---

## Known Issues

### Issue 1: Cold Start (New Users)
**Problem:** Users with <10 signals have mostly 0.5 (neutral) fingerprints.  
**Impact:** Behavioral Lifestyle Score = 50 (neutral) for all new users.  
**Mitigation:** Weight learning ignores components with <5 samples.

---

### Issue 2: Signal Sparsity
**Problem:** Some dimensions (e.g., `date_progression`) require rare events (DateIdeaAccepted).  
**Impact:** Most users have 0.5 for these dimensions.  
**Mitigation:** Dimensions with <5 samples are treated as 0.5 (neutral) in scoring.

---

### Issue 3: 180-Day Window Drift
**Problem:** Users who dramatically change behavior (e.g., after breakup) carry old signals for 180 days.  
**Impact:** Fingerprint lags behind current behavior.  
**Mitigation:** Consider 90-day window with exponential decay for recent signals.

---

## Future Enhancements

### Planned (Q4 2026)
1. **Exponential decay:** Weight recent signals more heavily (half-life 60 days).
2. **Dimension expansion:** Add 8 more dimensions (32-dim total):
   - `weekend_activity_ratio` (weekend vs weekday engagement)
   - `emoji_density` (emoji chars / total chars in messages)
   - `photo_posting_freq` (photo tiles per month)
   - `voice_posting_freq` (voice tiles per month)
   - `message_length_variance` (SD of message lengths)
   - `session_duration_mean` (mean app session length)
   - `re_engagement_rate` (return after 7-day gap)
   - `unmatch_rate` (unmatches / total matches)

### Researching (2027+)
1. **Cross-user fingerprint clustering:** Identify behavioral archetypes (e.g., "fast responder + low depth", "slow burn + high depth").
2. **Fingerprint drift alerts:** Notify users when their behavior changes >0.4 cosine in 30 days (e.g., "You've become less responsive lately").
3. **Fingerprint-based daily deck tuning:** Adjust deck composition based on user's fingerprint (e.g., high `game_engagement` → more playful candidates).

---

## Related Documentation

- [README.md](./README.md) — Embeddings system overview
- [../echo/scoring.md](../echo/scoring.md) — How fingerprint feeds into ECHO
- [workers.md](./workers.md) — Batch processing details
- [configuration.md](./configuration.md) — Service registration

---

**Questions?** Check logs for `[BehavioralFingerprint]` prefix.
