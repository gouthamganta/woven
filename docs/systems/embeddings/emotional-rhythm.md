# Emotional Rhythm Embedding (48-dim)

**Last Updated:** 2026-10-07  
**Status:** Production  
**File:** `backend/WovenBackend/Services/Embeddings/EmotionalRhythmService.cs`

---

## Overview

Emotional Rhythm embeddings capture **temporal and emotional patterns** in user behavior: when they post, when they chat, how their energy varies over time. This is about **compatibility in daily rhythms**, not personality traits.

**Storage:** `UserVector.EmotionalRhythmEmbedding VECTOR(48)`  
**Update Frequency:** Nightly (02:30 UTC) via `EmbeddingBatchWorker`  
**Minimum Data:** ≥10 activities (tiles + messages) in last 28 days

---

## Why It Exists

**Research:** Chronotype mismatch (morning person + night owl) is a predictor of relationship conflict.

**Beyond sleep schedules:**
- Posting times → social energy patterns (morning posts = morning person)
- Chat times → availability + communication preferences
- Energy variance → consistency vs mood swings
- Weekend vs weekday ratio → work-life rhythm

**Why it matters:**
- User A: posts at 7am, replies at 8pm → morning creativity, evening availability
- User B: posts at 11pm, replies at 2pm → night owl, afternoon availability
- Mismatch → frustration ("Why don't they reply when I'm active?")

---

## 48 Dimensions

### Feature Clusters

| Cluster | Features | Data Source |
|---|---|---|
| **0-7** | Tile posting patterns | Tiles (hour-of-day, weekday vs weekend) |
| **8-15** | Chat message patterns | ChatMessages (hour-of-day, weekday vs weekend) |
| **16-23** | Energy variance | UserEnergyMeters (tiles viewed per day) |
| **24-47** | Reserved | Future: sleep schedule inference, mood variance |

---

## Feature Extraction

### Cluster 0-7: Tile Posting Patterns
**Data:** User's tiles from last 28 days.

**Features:**
```csharp
var tileHours = await _db.Tiles
    .Where(t => t.UserId == userId && t.CreatedAt >= cutoff)
    .Select(t => t.CreatedAt)
    .ToListAsync(ct);

if (tileHours.Count > 0)
{
    // Feature 0: morning activity fraction (6am-12pm)
    features[0] = (float)tileHours.Count(t => t.Hour >= 6 && t.Hour < 12) / tileHours.Count;

    // Feature 1: afternoon activity (12pm-6pm)
    features[1] = (float)tileHours.Count(t => t.Hour >= 12 && t.Hour < 18) / tileHours.Count;

    // Feature 2: evening activity (6pm-12am)
    features[2] = (float)tileHours.Count(t => t.Hour >= 18 && t.Hour < 24) / tileHours.Count;

    // Feature 3: night activity (12am-6am)
    features[3] = (float)tileHours.Count(t => t.Hour >= 0 && t.Hour < 6) / tileHours.Count;

    // Feature 4: weekday vs weekend ratio
    features[4] = (float)tileHours.Count(t =>
        t.DayOfWeek != DayOfWeek.Saturday && t.DayOfWeek != DayOfWeek.Sunday)
        / tileHours.Count;

    // Feature 5: posting regularity (active days / 28)
    var activeDays = tileHours.Select(t => t.Date).Distinct().Count();
    features[5] = Math.Min(1f, activeDays / 28f);
}
```

**Why these features:**
- Hour distribution → chronotype (morning/afternoon/evening/night)
- Weekday ratio → work-focused vs leisure-focused
- Regularity → consistency vs sporadic activity

---

### Cluster 8-15: Chat Message Patterns
**Data:** User's chat messages from last 28 days.

**Features:**
```csharp
var messages = await _db.ChatMessages
    .Where(m => m.SenderUserId == userId && m.CreatedAt >= cutoff)
    .Select(m => new { m.CreatedAt, BodyLen = m.Body.Length })
    .ToListAsync(ct);

if (messages.Count > 0)
{
    // Feature 8: morning chat fraction (6am-12pm)
    features[8] = (float)messages.Count(m => m.CreatedAt.Hour >= 6 && m.CreatedAt.Hour < 12) / messages.Count;

    // Feature 9: evening chat fraction (6pm-12am)
    features[9] = (float)messages.Count(m => m.CreatedAt.Hour >= 18 && m.CreatedAt.Hour < 24) / messages.Count;

    // Feature 10: night chat fraction (12am-6am)
    features[10] = (float)messages.Count(m => m.CreatedAt.Hour >= 0 && m.CreatedAt.Hour < 6) / messages.Count;

    // Feature 11: weekend chat ratio
    features[11] = (float)messages.Count(m =>
        m.CreatedAt.DayOfWeek == DayOfWeek.Saturday || m.CreatedAt.DayOfWeek == DayOfWeek.Sunday)
        / messages.Count;

    // Feature 12: message length variance proxy (avg length normalized)
    features[12] = Math.Min(1f, (float)messages.Average(m => m.BodyLen) / 200f);
}
```

**Why these features:**
- Chat hour distribution → availability patterns
- Weekend chat ratio → work-life separation
- Message length → depth vs quick replies

---

### Cluster 16-23: Energy Variance
**Data:** `UserEnergyMeters` table — daily tile view counts.

**Features:**
```csharp
var energyRows = await _db.UserEnergyMeters
    .Where(e => e.UserId == userId && e.DateUtc >= DateOnly.FromDateTime(cutoff.DateTime))
    .Select(e => (float)e.TilesViewed)
    .ToListAsync(ct);

if (energyRows.Count > 0)
{
    var avg = energyRows.Average();
    var variance = energyRows.Select(v => (v - avg) * (v - avg)).Average();

    // Feature 16: mean energy level (tiles viewed per day, normalized by 100)
    features[16] = Math.Min(1f, avg / 100f);

    // Feature 17: energy variance (SD normalized by 50)
    features[17] = Math.Min(1f, (float)Math.Sqrt(variance) / 50f);

    // Feature 18: energy trend (first half vs second half)
    var half = energyRows.Count / 2;
    if (half > 0)
    {
        var firstHalf = energyRows.Take(half).Average();
        var secondHalf = energyRows.Skip(half).Average();
        features[18] = Math.Clamp(0.5f + (secondHalf - firstHalf) / 100f, 0f, 1f);
    }
}
```

**Why these features:**
- Mean energy → baseline engagement level
- Variance → consistency (low variance = steady, high = mood swings)
- Trend → growing vs declining interest

---

### Clusters 24-47: Reserved
**Future expansion:**
- Sleep schedule inference (last activity time → estimated bedtime)
- Mood variance (sentiment analysis on messages over time)
- Response latency patterns (fast replies in morning, slow in evening)

---

## ECHO Integration

### Component #10: EmotionalRhythmScore (weight 0.04)

**Formula:**
```csharp
if (viewerEmotional != null && candEmotional != null)
{
    var similarity = CosineSimilarity(viewerEmotional, candEmotional);
    EmotionalRhythmScore = similarity * 100;  // [0, 100]
}
```

**Example:**
```
User A (Morning Person):
  - Morning posts: 0.7, Afternoon: 0.2, Evening: 0.1, Night: 0.0
  - Morning chat: 0.6, Evening: 0.3, Night: 0.1
  - Energy variance: 0.2 (steady)

User B (Morning Person):
  - Morning posts: 0.6, Afternoon: 0.3, Evening: 0.1, Night: 0.0
  - Morning chat: 0.7, Evening: 0.2, Night: 0.1
  - Energy variance: 0.3 (steady)

CosineSimilarity(A, B) = 0.95 → EmotionalRhythmScore = 95

User C (Night Owl):
  - Morning posts: 0.1, Afternoon: 0.2, Evening: 0.3, Night: 0.4
  - Morning chat: 0.0, Evening: 0.4, Night: 0.6
  - Energy variance: 0.5 (irregular)

CosineSimilarity(A, C) = 0.32 → EmotionalRhythmScore = 32
```

**Interpretation:**
- Score ≥ 80: Highly compatible rhythms (both active at same times)
- Score 50-80: Some overlap
- Score < 50: Mismatched rhythms

**See:** [../echo/scoring.md](../echo/scoring.md#10-emotionalrhythmscore-weight-004)

---

## Computation Details

**Service:** `EmotionalRhythmService.ComputeEmotionalRhythmAsync(userId, ct)`

**Process:**
```csharp
public async Task ComputeEmotionalRhythmAsync(int userId, CancellationToken ct)
{
    var cutoff = DateTimeOffset.UtcNow.AddDays(-28);

    // 1. Check 28-day minimum history
    var messageCount = await _db.ChatMessages
        .Where(m => m.SenderUserId == userId && m.CreatedAt >= cutoff)
        .CountAsync(ct);

    var tileCount = await _db.Tiles
        .Where(t => t.UserId == userId && t.CreatedAt >= cutoff)
        .CountAsync(ct);

    if (messageCount + tileCount < 10)
    {
        _logger.LogInformation("[EmotionalRhythm] Skipping user {UserId} — insufficient activity", userId);
        return;
    }

    // 2. Extract 48 features
    var features = new float[48];
    // ... extract tile posting patterns (features 0-7)
    // ... extract chat message patterns (features 8-15)
    // ... extract energy variance (features 16-23)

    // 3. Clamp all to [0, 1]
    for (int i = 0; i < 48; i++)
        features[i] = Math.Clamp(features[i], 0f, 1f);

    // 4. Store in UserVector
    var vector = await _db.UserVectors
        .Where(v => v.UserId == userId)
        .OrderByDescending(v => v.Version)
        .FirstOrDefaultAsync(ct);

    if (vector == null) return;

    vector.EmotionalRhythmEmbedding = new Vector(features);
    vector.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    _logger.LogInformation("[EmotionalRhythm] Computed for user {UserId}", userId);
}
```

---

## Debugging

### Check User's Emotional Rhythm
```sql
SELECT
    user_id,
    emotional_rhythm_embedding IS NOT NULL AS has_rhythm,
    updated_at
FROM user_vectors
WHERE user_id = 123
ORDER BY version DESC
LIMIT 1;
```

### Analyze Activity Patterns
```sql
-- Tile posting by hour
SELECT
    EXTRACT(HOUR FROM created_at) AS hour,
    COUNT(*) AS count
FROM tiles
WHERE user_id = 123
  AND created_at >= NOW() - INTERVAL '28 days'
GROUP BY hour
ORDER BY hour;

-- Chat messages by hour
SELECT
    EXTRACT(HOUR FROM created_at) AS hour,
    COUNT(*) AS count
FROM chat_messages
WHERE sender_user_id = 123
  AND created_at >= NOW() - INTERVAL '28 days'
GROUP BY hour
ORDER BY hour;
```

### Check Energy Variance
```sql
SELECT
    date_utc,
    tiles_viewed
FROM user_energy_meters
WHERE user_id = 123
  AND date_utc >= CURRENT_DATE - INTERVAL '28 days'
ORDER BY date_utc;
```

---

## Known Issues

### Issue 1: Timezone Not Normalized
**Problem:** Server timestamps in UTC, but user might be in IST (UTC+5:30).  
**Impact:** Morning person in India (7am IST = 1:30am UTC) classified as night owl.  
**Mitigation:** Store user's timezone in `Users` table, normalize timestamps before feature extraction.  
**Status:** Not implemented yet (Q4 2026 roadmap).

---

### Issue 2: Low Activity Users
**Problem:** Users with <10 activities in 28 days → no emotional rhythm.  
**Impact:** EmotionalRhythmScore = 50 (neutral) for sparse users.  
**Mitigation:** Lower threshold to 5 activities (risky: unstable patterns).

---

### Issue 3: Energy Meter Data Gaps
**Problem:** `UserEnergyMeters` only populated for days with activity.  
**Impact:** Variance calculation skewed (missing days = 0 activity vs no data).  
**Fix:** Fill missing days with 0 before variance computation.

---

## Performance

**Nightly batch (10,000 users):**
- SQL queries: ~80ms per user × 10,000 = 800s = 13.3 min
- Feature extraction: <5ms per user × 10,000 = 50s
- DB write: ~10ms per user × 10,000 = 100s
- **Total:** ~14 minutes

---

## Future Enhancements

### Planned (Q4 2026)
1. **Timezone normalization** — store user timezone, convert all timestamps to local time before analysis.
2. **Sleep schedule inference** — estimate bedtime from last activity time.
3. **Mood variance** — sentiment analysis on messages over time.

### Researching (2027+)
1. **Circadian rhythm alignment** — match users with compatible sleep-wake cycles.
2. **Seasonal affective patterns** — detect mood changes in winter vs summer (India: monsoon vs summer).
3. **Response latency patterns** — detect "slow burn" vs "immediate responder" types.

---

## Related Documentation

- [README.md](./README.md) — Embeddings system overview
- [../echo/scoring.md](../echo/scoring.md#10-emotionalrhythmscore-weight-004) — EmotionalRhythmScore formula
- [workers.md](./workers.md) — Batch processing details

---

**Questions?** Check logs for `[EmotionalRhythm]` prefix.
