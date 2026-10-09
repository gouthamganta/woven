# Humor Embedding (64-dim)

**Last Updated:** 2026-10-07  
**Status:** Production  
**File:** `backend/WovenBackend/Services/Embeddings/HumorEmbeddingService.cs`

---

## Overview

Humor embeddings capture **humor compatibility** through behavioral signals: game outcomes (RedGreenFlag), emoji usage in messages, and engagement patterns. This is **NOT** semantic analysis of joke quality — it's about whether two users laugh at the same things.

**Storage:** `UserVector.HumorEmbedding VECTOR(64)`  
**Update Frequency:** Nightly (02:30 UTC) via `EmbeddingBatchWorker`  
**Minimum Data:** ≥2 completed games OR ≥50 chat messages

---

## Why It Exists

**Research:** Shared humor is a top-3 predictor of long-term relationship satisfaction (Gottman Institute).

**Problem:** Can't ask "What's your humor style?" (people don't know or misreport).

**Solution:** Observe behavior:
- RedGreenFlag game outcomes → reveals playfulness + shared references
- Emoji density in messages → expressiveness + tone
- Question/exclamation usage → communication energy

---

## 64 Dimensions

All features normalized to **[0, 1]**.

### Feature Clusters

| Cluster | Features | Data Source |
|---|---|---|
| **0-15** | Game signals | RedGreenFlag rounds (GuesserUserId, TargetUserId, Score) |
| **16-31** | Chat emoji density | ChatMessages (emoji chars / total chars) |
| **32-47** | Game outcome stats | GameOutcomes (InitiatorUserId, PartnerUserId) |
| **48-63** | Reserved | Future expansion (voice laughter detection, meme sharing) |

---

## Feature Extraction

### Cluster 0-15: Game Round Signals
**Data:** `GameRounds` table — RedGreenFlag icebreaker game.

**Features:**
```csharp
var rounds = await _db.GameRounds
    .Where(r => r.GuesserUserId == userId || r.TargetUserId == userId)
    .Select(r => new { r.GuesserUserId, r.Score })
    .ToListAsync(ct);

var totalRounds = rounds.Count;
if (totalRounds > 0)
{
    // Feature 0: fraction of rounds where user scored > 0
    features[0] = (float)rounds.Count(r => r.Score > 0) / totalRounds;

    // Feature 1: fraction where user was guesser (proactive vs reactive)
    features[1] = (float)rounds.Count(r => r.GuesserUserId == userId) / totalRounds;

    // Feature 2: avg normalized score (0-10 scale → 0-1)
    features[2] = Math.Min(1f, (float)rounds.Where(r => r.Score.HasValue)
        .Average(r => r?.Score ?? 0) / 10f);
}
```

**Why these features:**
- High score → shared references, similar sense of humor
- Guesser ratio → initiative in playful interaction
- Avg score → consistency of alignment

---

### Cluster 16-31: Chat Emoji Density
**Data:** Last 200 chat messages user sent.

**Features:**
```csharp
var messages = await _db.ChatMessages
    .Where(m => m.SenderUserId == userId)
    .Select(m => m.Body)
    .Take(200)
    .ToListAsync(ct);

if (messages.Count > 0)
{
    var totalChars = (float)messages.Sum(m => m.Length);
    var emojiChars = (float)messages.Sum(m => m.Count(c => c > 0x1F300));  // Unicode emoji range

    // Feature 16: emoji density (emoji chars / total chars × 50 for normalization)
    features[16] = totalChars > 0 ? Math.Min(1f, emojiChars / totalChars * 50f) : 0f;

    // Feature 17: avg message length (normalized by 100 chars)
    features[17] = Math.Min(1f, (float)messages.Average(m => m.Length) / 100f);

    // Feature 18: question messages ratio
    features[18] = (float)messages.Count(m => m.TrimEnd().EndsWith('?')) / messages.Count;

    // Feature 19: exclamation messages ratio
    features[19] = (float)messages.Count(m => m.TrimEnd().EndsWith('!')) / messages.Count;
}
```

**Why these features:**
- Emoji density → expressiveness, playfulness
- Message length → detail-oriented vs terse
- Questions → curiosity, engagement
- Exclamations → energy, enthusiasm

---

### Cluster 32-47: Game Outcome Stats
**Data:** `GameOutcomes` table — completed RedGreenFlag games.

**Features:**
```csharp
var outcomes = await _db.GameOutcomes
    .Where(g => g.InitiatorUserId == userId || g.PartnerUserId == userId)
    .Take(50)
    .ToListAsync(ct);

if (outcomes.Count > 0)
{
    // Feature 32: game completion frequency (normalized by 20 expected games)
    features[32] = Math.Min(1f, completedGames / 20f);

    // Feature 33: initiator ratio (proactive vs reactive)
    features[33] = (float)outcomes.Count(o => o.InitiatorUserId == userId) / outcomes.Count;
}
```

**Why these features:**
- Completion frequency → engagement with playful content
- Initiator ratio → proactive vs reactive humor

---

### Clusters 48-63: Reserved
**Future expansion:**
- Voice laughter detection (Wav2Vec2 + emotion classification)
- Meme sharing frequency
- GIF usage in messages
- Sarcasm detection (NLP model)

---

## ECHO Integration

### Component #7: HumorScore (weight 0.07)

**Formula:**
```csharp
if (viewerHumor != null && candHumor != null)
{
    var similarity = CosineSimilarity(viewerHumor, candHumor);
    HumorScore = similarity * 100;  // [0, 100]
}
```

**Example:**
```
User A's humor embedding:
  - High emoji density (0.8)
  - High game scores (0.7)
  - Medium exclamation usage (0.5)

User B's humor embedding:
  - High emoji density (0.75)
  - High game scores (0.65)
  - Medium exclamation usage (0.6)

CosineSimilarity(A, B) = 0.92 → HumorScore = 92

User C's humor embedding:
  - Low emoji density (0.2)
  - Low game engagement (0.1)
  - Low exclamation usage (0.1)

CosineSimilarity(A, C) = 0.35 → HumorScore = 35
```

**Interpretation:**
- HumorScore ≥ 80: Very compatible humor styles
- HumorScore 50-80: Some overlap
- HumorScore < 50: Different humor styles

**See:** [../echo/scoring.md](../echo/scoring.md#7-humorscore-weight-007)

---

## Computation Details

**Service:** `HumorEmbeddingService.ComputeHumorEmbeddingAsync(userId, ct)`

**Process:**
```csharp
public async Task ComputeHumorEmbeddingAsync(int userId, CancellationToken ct)
{
    // 1. Check minimum data (≥2 completed games)
    var completedGames = await _db.GameOutcomes
        .Where(g => g.InitiatorUserId == userId || g.PartnerUserId == userId)
        .CountAsync(ct);

    if (completedGames < 2)
    {
        _logger.LogInformation("[HumorEmbedding] Skipping user {UserId} — only {N} games", userId, completedGames);
        return;
    }

    // 2. Extract 64 features
    var features = new float[64];

    // ... extract game round signals (features 0-15)
    // ... extract chat emoji density (features 16-31)
    // ... extract game outcome stats (features 32-47)

    // 3. Clamp all to [0, 1]
    for (int i = 0; i < 64; i++)
        features[i] = Math.Clamp(features[i], 0f, 1f);

    // 4. Store in UserVector
    var vector = await _db.UserVectors
        .Where(v => v.UserId == userId)
        .OrderByDescending(v => v.Version)
        .FirstOrDefaultAsync(ct);

    if (vector == null) return;

    vector.HumorEmbedding = new Vector(features);
    vector.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    _logger.LogInformation("[HumorEmbedding] Computed for user {UserId}", userId);
}
```

---

## RedGreenFlag Game

**How it works:**
1. User A sends game invitation to User B
2. User A sees User B's statement (e.g., "Pineapple on pizza is great")
3. User A guesses: GREEN (B agrees) or RED (B disagrees)
4. User B's actual answer revealed
5. Score: +1 if guess matches, 0 if wrong

**Why it reveals humor:**
- Playful disagreement → comfort with teasing
- Absurd statements → shared absurdist humor
- Speed of completion → engagement with games

**Game table schema:**
```sql
CREATE TABLE game_rounds (
    id UUID PRIMARY KEY,
    game_outcome_id UUID NOT NULL,
    guesser_user_id INT NOT NULL,
    target_user_id INT NOT NULL,
    statement TEXT NOT NULL,
    guess TEXT NOT NULL,  -- 'GREEN' or 'RED'
    actual TEXT NOT NULL,  -- 'GREEN' or 'RED'
    score INT,  -- 1 if guess == actual, 0 otherwise
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE game_outcomes (
    id UUID PRIMARY KEY,
    initiator_user_id INT NOT NULL,
    partner_user_id INT NOT NULL,
    total_score INT,
    completed_at TIMESTAMPTZ NOT NULL
);
```

---

## Debugging

### Check User's Humor Embedding
```sql
SELECT
    user_id,
    humor_embedding IS NOT NULL AS has_humor,
    updated_at
FROM user_vectors
WHERE user_id = 123
ORDER BY version DESC
LIMIT 1;
```

### Count Game Activity
```sql
-- Completed games
SELECT COUNT(*) FROM game_outcomes
WHERE initiator_user_id = 123 OR partner_user_id = 123;

-- Game rounds
SELECT COUNT(*) FROM game_rounds
WHERE guesser_user_id = 123 OR target_user_id = 123;
```

### Inspect Game Scores
```sql
SELECT
    guesser_user_id,
    target_user_id,
    statement,
    guess,
    actual,
    score
FROM game_rounds
WHERE guesser_user_id = 123 OR target_user_id = 123
ORDER BY created_at DESC
LIMIT 10;
```

### Manual Recompute
```bash
# Clear humor embedding for user 123
UPDATE user_vectors
SET humor_embedding = NULL, updated_at = NOW()
WHERE user_id = 123;

# Wait for next batch run at 02:30 UTC
```

---

## Known Issues

### Issue 1: Low Game Adoption
**Problem:** Only 30% of users play RedGreenFlag games.  
**Impact:** 70% of users missing humor embeddings → HumorScore = 50 (neutral).  
**Mitigation:**
- Add game prompts in chat threads ("Want to break the ice?")
- Gamify (badges for completing 5, 10, 20 games)
- Add more game types (Would You Rather, Two Truths and a Lie)

---

### Issue 2: Emoji Encoding Issues
**Problem:** Unicode emoji range `c > 0x1F300` misses some emoji (skin tone modifiers, flags).  
**Impact:** Undercounts emoji usage.  
**Fix:** Use proper emoji regex:
```csharp
var emojiPattern = new Regex(@"\p{So}|\p{Cs}|‍");
var emojiCount = emojiPattern.Matches(text).Count;
```

---

### Issue 3: Sarcasm Not Detected
**Problem:** User A: "Oh great, another hiking photo 🙄" → Feature 16 (emoji density) = high, but sarcasm missed.  
**Impact:** Misclassified as "playful" when actually cynical.  
**Mitigation:** Add NLP sarcasm detection (future).

---

## Performance

**Nightly batch (10,000 users):**
- SQL queries: ~100ms per user × 10,000 = 1000s = 16.7 min
- Feature extraction: <5ms per user × 10,000 = 50s
- DB write: ~10ms per user × 10,000 = 100s
- **Total:** ~18 minutes

**Bottleneck:** SQL queries (GameRounds + ChatMessages). Consider caching aggregates.

---

## Future Enhancements

### Planned (Q4 2026)
1. **Meme sharing tracking** — count shared image tiles with humor intent.
2. **GIF usage** — track GIF sends in messages (via MetaJson).
3. **Sarcasm detection** — fine-tuned BERT model on sarcasm dataset.

### Researching (2027+)
1. **Voice laughter detection** — Wav2Vec2 + emotion classification on voice tiles.
2. **Cross-cultural humor** — detect cultural references (Bollywood vs Hollywood).
3. **Humor archetypes** — cluster users into humor styles (dry, absurdist, wholesome, dark).

---

## Related Documentation

- [README.md](./README.md) — Embeddings system overview
- [../echo/scoring.md](../echo/scoring.md#7-humorscore-weight-007) — HumorScore formula
- [workers.md](./workers.md) — Batch processing details

---

**Questions?** Check logs for `[HumorEmbedding]` prefix.
