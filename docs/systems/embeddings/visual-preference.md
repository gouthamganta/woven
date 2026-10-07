# Visual Preference (CLIP-based Photo Learning)

**Last Updated:** 2026-10-07  
**Status:** Production  
**File:** `backend/WovenBackend/Services/Embeddings/VisualPreferenceService.cs`

---

## Overview

Visual preference embeddings capture **what kinds of photos attract a user**, learned from their YES/NO decisions on candidate photos. Unlike stated preferences ("I prefer tall people"), this is **revealed preference** — what users actually choose.

**Storage:** `UserVisualPreference` table (NOT in `UserVector`)  
**Model:** CLIP ViT-B/32 (512-dim)  
**Update Frequency:** Nightly (02:30 UTC) via `EmbeddingBatchWorker`

---

## Why It Exists

**Problem:** Users are bad at stating visual preferences.
- Survey: "I don't care about looks" → Reality: swipes left on 80% of profiles.
- Survey: "I prefer athletic build" → Reality: likes diverse body types.

**Solution:** Learn from behavior.
- User sees 50 photos in Moments → marks 10 as YES, 40 as NO.
- Compute mean embedding of YES photos → `PreferenceEmbedding`
- Compute mean embedding of NO photos → `AversionEmbedding`
- Match score = similarity to preference - similarity to aversion

---

## Storage Schema

```sql
CREATE TABLE user_visual_preferences (
    user_id INT PRIMARY KEY,
    preference_embedding VECTOR(512),  -- Mean of YES decisions (≥10 samples)
    aversion_embedding VECTOR(512),    -- Mean of NO decisions (≥10 samples)
    yes_sample_count INT NOT NULL,
    no_sample_count INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_user_visual_preferences_user_id ON user_visual_preferences(user_id);
```

**Fields:**
- `preference_embedding` — Mean CLIP embedding of photos user marked YES (explicit or implicit)
- `aversion_embedding` — Mean CLIP embedding of photos user marked NO
- `yes_sample_count` / `no_sample_count` — Training sample counts (min 10 to activate)
- `updated_at` — Last recompute timestamp

---

## How Decisions Are Collected

### Explicit Decisions (Not Yet Implemented)
**Planned:** Binary YES/NO buttons on Moments cards.

**Flow:**
```
User sees candidate photo in Moments
    ↓
Clicks YES or NO button
    ↓
POST /moments/respond { candidateId, choice: "YES" | "NO" }
    ↓
UserVisualDecision record created
```

**Status:** UI not built yet. Only implicit signals exist.

---

### Implicit Decisions (Current)
**When:** User responds to a Moment (MAGICAL or LOGICAL choice).

**Assumption:**
- MAGICAL or LOGICAL response → YES signal (user engaged with photo)
- SKIP or no response within 24h → NO signal (user rejected photo)

**Flow:**
```
User responds to Moment (any choice except SKIP)
    ↓
POST /moments/respond { momentId, choice: "MAGICAL" | "LOGICAL" }
    ↓
UserVisualDecision record created
    ↓
{
  ViewerUserId: 123,
  CandidateUserId: 456,
  PhotoEmbeddingId: 789,  // Candidate's primary photo embedding
  Choice: "YES",
  CreatedAt: ...
}
```

**Limitation:** Only captures binary YES/NO, not degree of attraction.

---

## Photo Embedding (CLIP)

**Model:** `openai/clip-vit-base-patch32` via Replicate  
**Dimensions:** 512 (ViT-B/32 default)  
**API Cost:** $0.00023 per image

### Embedding Process
**Service:** `PhotoEmbeddingService.EmbedPhotoAsync(userId, photoUrl, ct)`

**Steps:**
1. Download image bytes from Azure Blob
2. Strip EXIF metadata (remove GPS, camera info)
3. Base64 encode image
4. POST to Replicate CLIP endpoint
5. Store 512-dim embedding in `PhotoEmbedding` table
6. Link to User via `user_id`

**Code:**
```csharp
var payload = new
{
    version = "75b33f253f7714a281ad3e9b28f63e3232d583716ef6718f2e46641077ea040a",
    input = new { image = $"data:image/jpeg;base64,{base64}" }
};

using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/predictions");
req.Headers.Authorization = new AuthenticationHeaderValue("Token", apiToken);
req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

var resp = await _http.SendAsync(req, ct);
var embedding = ParseReplicateEmbedding(await resp.Content.ReadAsStringAsync(ct));

var row = new PhotoEmbedding
{
    UserId = userId,
    PhotoUrl = photoUrl,
    Embedding = new Vector(embedding),
    EmbeddedAt = DateTimeOffset.UtcNow
};
_db.PhotoEmbeddings.Add(row);
```

**See:** [photo-embedding.md](./photo-embedding.md)

---

## Preference Learning

**Service:** `VisualPreferenceService.UpdateVisualPreferenceAsync(userId, ct)`  
**Trigger:** Nightly batch worker (02:30 UTC)

**Process:**
```csharp
public async Task UpdateVisualPreferenceAsync(int userId, CancellationToken ct)
{
    // 1. Load all YES decisions with linked photo embeddings
    var yesEmbeddings = await _db.UserVisualDecisions
        .Where(d => d.ViewerUserId == userId && d.Choice == "YES" && d.PhotoEmbeddingId != null)
        .Join(_db.PhotoEmbeddings,
            d => d.PhotoEmbeddingId,
            e => e.Id,
            (d, e) => e.Embedding)
        .ToListAsync(ct);

    // 2. Load all NO decisions
    var noEmbeddings = await _db.UserVisualDecisions
        .Where(d => d.ViewerUserId == userId && d.Choice == "NO" && d.PhotoEmbeddingId != null)
        .Join(_db.PhotoEmbeddings,
            d => d.PhotoEmbeddingId,
            e => e.Id,
            (d, e) => e.Embedding)
        .ToListAsync(ct);

    // 3. Require minimum 10 samples to activate
    if (yesEmbeddings.Count < 10 && noEmbeddings.Count < 10) return;

    // 4. Compute mean embeddings
    Vector? preferenceVec = yesEmbeddings.Count >= 10
        ? new Vector(ElementWiseMean(yesEmbeddings))
        : null;

    Vector? aversionVec = noEmbeddings.Count >= 10
        ? new Vector(ElementWiseMean(noEmbeddings))
        : null;

    // 5. Upsert to UserVisualPreference
    var pref = await _db.UserVisualPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
    if (pref == null)
    {
        _db.UserVisualPreferences.Add(new UserVisualPreference
        {
            UserId = userId,
            PreferenceEmbedding = preferenceVec,
            AversionEmbedding = aversionVec,
            YesSampleCount = yesEmbeddings.Count,
            NoSampleCount = noEmbeddings.Count,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
    else
    {
        if (preferenceVec != null) pref.PreferenceEmbedding = preferenceVec;
        if (aversionVec != null) pref.AversionEmbedding = aversionVec;
        pref.YesSampleCount = yesEmbeddings.Count;
        pref.NoSampleCount = noEmbeddings.Count;
        pref.UpdatedAt = DateTimeOffset.UtcNow;
    }

    await _db.SaveChangesAsync(ct);
}
```

**Mean Computation:**
```csharp
private static float[] ElementWiseMean(List<Vector?> vectors)
{
    var dim = vectors.First()!.Memory.Length;  // 512
    var sum = new float[dim];
    foreach (var v in vectors)
    {
        var span = v!.Memory.Span;
        for (int i = 0; i < dim; i++)
            sum[i] += span[i];
    }
    for (int i = 0; i < dim; i++)
        sum[i] /= vectors.Count;
    return sum;
}
```

**Why element-wise mean:**
- CLIP embeddings are L2-normalized → mean stays on unit sphere
- Simple, fast, interpretable
- Works well for small-to-medium sample sizes (10-100)

---

## ECHO Integration

### Component #5: VisualScore (weight 0.10)

**Formula:**
```csharp
if (viewerVisualPref?.PreferenceEmbedding != null && candPhotoEmbedding != null)
{
    var dotProduct = DotProduct(viewerVisualPref.PreferenceEmbedding, candPhotoEmbedding);
    VisualScore = Clamp(50 + dotProduct * 50, 0, 100);
}
```

**Dot product range:**
- +1.0 = perfect alignment (candidate photo matches preference exactly)
- 0.0 = orthogonal (neutral)
- -1.0 = opposite (candidate photo matches aversion)

**Score mapping:**
- dotProduct = 1.0 → VisualScore = 100
- dotProduct = 0.0 → VisualScore = 50 (neutral)
- dotProduct = -1.0 → VisualScore = 0

**Example:**
```
Viewer 123 has marked YES on 15 photos of:
  - Outdoor photos (nature backgrounds)
  - Casual clothing
  - Smiling faces

Viewer 123's PreferenceEmbedding ≈ mean(outdoor, casual, smiling)

Candidate A's photo: Outdoor hiking shot, casual t-shirt, big smile
  → dotProduct(viewerPref, candPhotoA) = 0.72
  → VisualScore = 50 + 0.72 × 50 = 86

Candidate B's photo: Indoor gym selfie, tank top, serious face
  → dotProduct(viewerPref, candPhotoB) = 0.12
  → VisualScore = 50 + 0.12 × 50 = 56
```

**See:** [../echo/scoring.md](../echo/scoring.md#5-visualscore-weight-010)

---

## Personalized Photo Selection

**Service:** `VisualPreferenceService.GetBestPhotoUrlAsync(viewerUserId, candidateUserId, ct)`

**Purpose:** For candidates with multiple photos, show the one most likely to attract the viewer.

**Algorithm:**
```csharp
public async Task<string?> GetBestPhotoUrlAsync(int viewerUserId, int candidateUserId, CancellationToken ct)
{
    // 1. Load viewer's preference embedding
    var pref = await _db.UserVisualPreferences
        .FirstOrDefaultAsync(p => p.UserId == viewerUserId, ct);
    if (pref?.PreferenceEmbedding == null) return null;

    // 2. Load all candidate's photo embeddings
    var photos = await _db.PhotoEmbeddings
        .Where(e => e.UserId == candidateUserId && e.Embedding != null)
        .Select(e => new { e.PhotoUrl, e.Embedding })
        .ToListAsync(ct);

    if (photos.Count == 0) return null;
    if (photos.Count == 1) return photos[0].PhotoUrl;  // No choice

    // 3. Find photo with highest cosine similarity to preference
    var prefSpan = pref.PreferenceEmbedding.Memory.Span;
    string? bestUrl = null;
    double bestSim = double.MinValue;

    foreach (var p in photos)
    {
        var sim = CosineSimilarity(prefSpan, p.Embedding!.Memory.Span);
        if (sim > bestSim)
        {
            bestSim = sim;
            bestUrl = p.PhotoUrl;
        }
    }

    return bestUrl;
}
```

**Usage:**
```csharp
// In DailyDeckOrchestrator or MomentsEndpoints
var bestPhotoUrl = await _visualPrefService.GetBestPhotoUrlAsync(viewerId, candidateId, ct);
candidate.ProfilePhoto = bestPhotoUrl ?? candidate.ProfilePhoto;  // Fallback to default
```

**Status:** Code exists, NOT used in production yet (planned for Phase 3E).

---

## Privacy & Bias Mitigation

### EXIF Stripping
**Why:** Photos contain GPS coordinates, camera model, timestamp → PII risk.

**How:** `PhotoEmbeddingService.StripExif(jpeg)`
```csharp
// Minimal JPEG EXIF strip: remove APP1 (0xFFE1) segments
using var input = new MemoryStream(jpeg);
using var output = new MemoryStream();

// Copy JPEG SOI, then skip all APP1 segments (EXIF/XMP)
while (input.Position < input.Length - 1)
{
    if (input.ReadByte() != 0xFF) break;
    int marker = input.ReadByte();
    if (marker == 0xE1) continue;  // Skip EXIF
    // ... copy other segments
}

return output.ToArray();
```

**Result:** Embedding sees pixel data only, no metadata.

---

### Bias Detection (Planned)

**Problem:** CLIP embeddings encode race, age, gender.
- Risk: System learns "viewer prefers fair skin" → amplifies bias.

**Mitigation (Q4 2026):**
1. **Reference embeddings:** Compute CLIP embeddings for diverse reference photos:
   ```sql
   INSERT INTO reference_photo_embeddings (label, embedding)
   VALUES
     ('fair_skin_male', <vector>),
     ('dark_skin_male', <vector>),
     ('fair_skin_female', <vector>),
     ('dark_skin_female', <vector>),
     ...
   ```

2. **Bias audit:** For each user's PreferenceEmbedding, compute similarity to all reference embeddings.
   ```csharp
   var fairSkinSim = CosineSimilarity(userPref, refEmbeddings["fair_skin_male"]);
   var darkSkinSim = CosineSimilarity(userPref, refEmbeddings["dark_skin_male"]);
   var bias = fairSkinSim - darkSkinSim;  // >0.3 = red flag
   ```

3. **Debiasing:** If bias >0.3, project PreferenceEmbedding onto bias-orthogonal subspace.
   ```csharp
   var biasVector = refEmbeddings["fair_skin"] - refEmbeddings["dark_skin"];
   userPref = userPref - DotProduct(userPref, biasVector) * biasVector;
   ```

**Status:** Reference embeddings table exists, audit NOT wired yet.

---

## Debugging

### Check User's Preference Status
```sql
SELECT
    user_id,
    preference_embedding IS NOT NULL AS has_preference,
    aversion_embedding IS NOT NULL AS has_aversion,
    yes_sample_count,
    no_sample_count,
    updated_at
FROM user_visual_preferences
WHERE user_id = 123;
```

### Count Visual Decisions
```sql
SELECT
    choice,
    COUNT(*) AS count
FROM user_visual_decisions
WHERE viewer_user_id = 123
GROUP BY choice;
```

### Inspect Decision History
```sql
SELECT
    d.viewer_user_id,
    d.candidate_user_id,
    d.choice,
    d.created_at,
    e.photo_url,
    e.embedding IS NOT NULL AS has_embedding
FROM user_visual_decisions d
LEFT JOIN photo_embeddings e ON d.photo_embedding_id = e.id
WHERE d.viewer_user_id = 123
ORDER BY d.created_at DESC
LIMIT 20;
```

### Manual Recompute
```bash
# Trigger recompute for user 123
DELETE FROM user_visual_preferences WHERE user_id = 123;

# Wait for next batch run at 02:30 UTC
# OR manually invoke (requires admin endpoint)
curl -X POST https://api.woven.me/admin/embeddings/recompute-visual/123
```

---

## Known Issues

### Issue 1: Low Sample Count
**Problem:** Many users have <10 YES decisions.  
**Impact:** PreferenceEmbedding = null → VisualScore = 50 (neutral) for all candidates.  
**Mitigation:**
- Lower threshold to 5 samples (risky: unstable preferences)
- Add explicit YES/NO buttons (increase decision rate)
- Use implicit dwell time (≥8s on photo = YES)

---

### Issue 2: Sample Bias (Early Matches)
**Problem:** User's first 10 YES decisions = whoever was shown first (not random).  
**Impact:** Preference learns from biased sample.  
**Mitigation:**
- Daily deck selection is already randomized (EXPLORER bucket)
- Preference recomputed nightly → self-corrects over time

---

### Issue 3: Catfish Detection Trigger
**Problem:** `PhotoEmbeddingService` triggers `CatfishDetectionService` on every photo upload.  
**Impact:** Extra DB query + potential false positives.  
**Status:** Fire-and-forget async (non-blocking), logged for review.

---

## Performance

### Nightly Batch (EmbeddingBatchWorker)
**10,000 active users:**
- SQL query (load decisions): ~50ms per user × 10,000 = 500s = 8.3 min
- Mean computation: <1ms per user × 10,000 = 10s
- DB write: ~10ms per user × 10,000 = 100s = 1.7 min
- **Total:** ~10 minutes

**Bottleneck:** SQL query (batching not optimal). Consider chunking:
```csharp
var userBatches = userIds.Chunk(100);
foreach (var batch in userBatches)
{
    await Parallel.ForEachAsync(batch, async (userId, ct) =>
    {
        await UpdateVisualPreferenceAsync(userId, ct);
    });
}
```

---

## Future Enhancements

### Planned (Q4 2026)
1. **Explicit YES/NO buttons** — add to Moments UI for faster preference learning.
2. **Bias audit** — detect and mitigate race/age/gender bias in preferences.
3. **Temporal decay** — weight recent decisions more heavily (older decisions fade).

### Researching (2027+)
1. **Multi-photo aggregation** — use all candidate photos, not just primary.
2. **Attention heatmaps** — CLIP attention maps show which parts of photo user liked (face? background? clothing?).
3. **Explainability** — "You tend to like outdoor photos" (cluster analysis on preference embedding).
4. **Cross-modal alignment** — match visual preference to text description ("I like adventurous people" → outdoor photos).

---

## Related Documentation

- [README.md](./README.md) — Embeddings system overview
- [photo-embedding.md](./photo-embedding.md) — CLIP embedding details
- [../echo/scoring.md](../echo/scoring.md#5-visualscore-weight-010) — VisualScore formula
- [workers.md](./workers.md) — Batch processing details
- [configuration.md](./configuration.md) — Replicate API setup

---

**Questions?** Check logs for `[VisualPreference]` prefix.
