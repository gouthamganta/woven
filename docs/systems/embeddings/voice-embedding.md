# Voice Embedding (192-dim)

**Last Updated:** 2026-10-07  
**Status:** Production (user voice signature) / Planned (voice preference learning)  
**File:** `backend/WovenBackend/Services/Embeddings/VoiceEmbeddingService.cs`

---

## Overview

Voice embeddings capture **speaker characteristics** (tone, pitch, cadence, speaking style) from voice tiles using **ECAPA-TDNN** (Emphasized Channel Attention, Propagation and Aggregation in Time Delay Neural Network), a state-of-the-art speaker recognition model.

**Two distinct embeddings:**
1. **User's own voice signature** (`UserVector.VoiceEmbedding`) — 192-dim mean of user's posted voice tiles
2. **User's voice preference** (`UserVoicePreference.PreferenceEmbedding`) — 192-dim mean of YES decisions on others' voice tiles (**planned, not yet wired**)

---

## Model Specifications

| Attribute | Value |
|---|---|
| **Model** | ECAPA-TDNN (SpeechBrain `spkrec-ecapa-voxceleb`) |
| **Dimensions** | 192 |
| **Input** | Audio WAV (16kHz mono recommended) |
| **Output** | 192-dim speaker embedding (L2-normalized) |
| **Inference** | HTTP endpoint (production) or Python subprocess (dev) |
| **Cost** | Free (self-hosted) |

---

## Voice Signature (User's Own Voice)

### Purpose
Captures user's voice characteristics for matching. Used in **ECHO Component #6 (VoiceScore)** to measure voice compatibility.

### Storage
**Table:** `user_vectors`  
**Column:** `voice_embedding VECTOR(192)`

### Update Trigger
**When:** User posts a voice tile (ContentType = "voice")  
**Where:** `VoiceEmbeddingService.EmbedVoiceAsync(tileId, userId, audioUrl, ct)`

### Computation
```csharp
public async Task EmbedVoiceAsync(Guid tileId, int userId, string audioUrl, CancellationToken ct)
{
    // 1. Download audio bytes
    var audioBytes = await _http.GetByteArrayAsync(audioUrl, ct);

    // 2. Call ECAPA-TDNN endpoint (or subprocess fallback)
    var embedding = await GetEmbeddingAsync(audioBytes, ct);
    if (embedding == null || embedding.Length != 192) return;

    // 3. Store embedding in Tile
    var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
    tile.VoiceEmbedding = new Vector(embedding);
    await _db.SaveChangesAsync(ct);

    // 4. Update user's voice signature (running mean)
    await UpdateUserVoiceEmbeddingAsync(userId, embedding, ct);
}
```

### Running Mean Update
```csharp
private async Task UpdateUserVoiceEmbeddingAsync(int userId, float[] newEmbedding, CancellationToken ct)
{
    var userVector = await _db.UserVectors
        .Where(v => v.UserId == userId)
        .OrderByDescending(v => v.Version)
        .FirstOrDefaultAsync(ct);

    if (userVector.VoiceEmbedding == null)
    {
        // First voice tile → initialize
        userVector.VoiceEmbedding = new Vector(newEmbedding);
    }
    else
    {
        // Running mean: (current × (n-1) + new) / n
        var n = await _db.Tiles
            .CountAsync(t => t.UserId == userId && t.VoiceEmbedding != null, ct);
        n = Math.Max(n, 2);  // Guard: at least 2

        var current = userVector.VoiceEmbedding.Memory.ToArray();
        var updated = new float[192];
        for (int i = 0; i < 192; i++)
            updated[i] = (current[i] * (n - 1) + newEmbedding[i]) / n;

        userVector.VoiceEmbedding = new Vector(updated);
    }

    userVector.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

**Why running mean:**
- Single voice tile might have background noise, hesitation, or atypical recording
- Mean across 3-5 voice tiles → stable speaker signature
- L2-normalized embeddings → mean stays within unit sphere

---

## Voice Preference (What User Likes)

### Purpose
Learns what voice characteristics attract a user, based on YES/NO decisions on voice tiles in Commons.

### Storage (Planned)
**Table:** `user_voice_preferences`  
**Schema:**
```sql
CREATE TABLE user_voice_preferences (
    user_id INT PRIMARY KEY,
    preference_embedding VECTOR(192),  -- Mean of YES voice tiles
    yes_sample_count INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);
```

### Update Trigger (Planned)
**When:** User marks a voice tile as YES in Commons (or dwells ≥8s)  
**Worker:** `TileViewProcessorWorker` (every 30 min)

### Computation (Planned)
```csharp
// Pseudocode (not yet implemented)
var yesVoiceTiles = await _db.TileViews
    .Where(v => v.UserId == userId && v.Decision == "YES" && v.Tile.VoiceEmbedding != null)
    .Join(_db.Tiles, v => v.TileId, t => t.Id, (v, t) => t.VoiceEmbedding)
    .ToListAsync(ct);

if (yesVoiceTiles.Count >= 10)
{
    var meanEmbedding = ElementWiseMean(yesVoiceTiles);
    var pref = new UserVoicePreference
    {
        UserId = userId,
        PreferenceEmbedding = new Vector(meanEmbedding),
        YesSampleCount = yesVoiceTiles.Count,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    _db.UserVoicePreferences.Upsert(pref);
}
```

**Status:** Schema exists, learning logic NOT wired (Q4 2026 roadmap).

---

## ECHO Integration

### Component #6: VoiceScore (weight 0.08)

**Formula:**
```csharp
if (viewerVoicePref != null && candVoiceEmbedding != null)
{
    var dotProduct = DotProduct(viewerVoicePref, candVoiceEmbedding);
    VoiceScore = Clamp(50 + dotProduct * 50, 0, 100);
}
```

**Current Behavior (before preference learning):**
```csharp
// VoiceScore = 50 (neutral) for all pairs
// Why: viewerVoicePref is always null
```

**After preference learning wired:**
```csharp
// Example: Viewer likes deep, calm voices
// → viewerVoicePref is mean of YES decisions on deep voices
// → Candidate A has deep voice → dotProduct = 0.7 → VoiceScore = 85
// → Candidate B has high-pitched voice → dotProduct = -0.3 → VoiceScore = 35
```

**See:** [../echo/scoring.md](../echo/scoring.md#6-voicescore-weight-008)

---

## HTTP Endpoint (Production)

**Configuration:**
```json
{
  "SpeechBrain": {
    "EndpointUrl": "https://speechbrain.woven.internal/embed"
  }
}
```

**Request:**
```http
POST /embed HTTP/1.1
Host: speechbrain.woven.internal
Content-Type: audio/wav

[binary WAV data]
```

**Response:**
```json
[0.023, -0.045, 0.112, ..., 0.078]  // 192 floats, L2-normalized
```

**Expected Latency:**
- Model load (cold start): ~2s
- Inference (warm): ~200ms per 10s audio
- Network overhead: ~50ms

**Deployment:**
- Container: `speechbrain/spkrec-ecapa-voxceleb:latest` (Hugging Face)
- Resources: 2 vCPU, 4GB RAM (CPU inference, no GPU needed)
- Scale: Auto-scale on queue depth (Azure Container Apps)

---

## Python Subprocess Fallback (Dev Only)

**When endpoint not configured:**
```csharp
var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "speechbrain_embed.py");
var psi = new ProcessStartInfo("python3", $"\"{scriptPath}\" \"{tempWavPath}\"")
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
};

using var proc = Process.Start(psi);
var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
var embedding = JsonSerializer.Deserialize<float[]>(stdout.Trim());
```

**Script:** `scripts/speechbrain_embed.py`
```python
#!/usr/bin/env python3
import sys
import json
from speechbrain.pretrained import EncoderClassifier

classifier = EncoderClassifier.from_hparams(
    source="speechbrain/spkrec-ecapa-voxceleb",
    savedir="/tmp/speechbrain_cache"
)

audio_path = sys.argv[1]
embeddings = classifier.encode_batch([audio_path])
print(json.dumps(embeddings[0].tolist()))
```

**Dependencies:**
```bash
pip install speechbrain torchaudio
```

**Why fallback:** Dev convenience (no Docker required locally). **Not available in production containers.**

---

## Data Flow

### Voice Tile Creation Flow
```
User records voice note (frontend)
    ↓
POST /media/upload-token → SAS token
    ↓
PUT to Azure Blob (direct from client)
    ↓
POST /media/confirm → MediaService validates upload
    ↓
POST /tiles { contentType: "voice", audioUrl, durationSecs }
    ↓
TileService.CreateTileAsync()
    ↓
VoiceEmbeddingService.EmbedVoiceAsync(tileId, userId, audioUrl)
    ↓
1. Download audio bytes
2. POST to SpeechBrain endpoint → 192-dim embedding
3. Store in Tile.VoiceEmbedding
4. Update UserVector.VoiceEmbedding (running mean)
```

**Latency:** ~1.5s (800ms download + 200ms inference + 500ms DB writes)

---

## Voice Listening Flow (Preference Learning — Planned)

```
User dwells on voice tile ≥8s in Commons
    ↓
POST /tiles/{tileId}/dwell { durationMs: 8500 }
    ↓
TileView record created (Decision = null initially)
    ↓
TileViewProcessorWorker (every 30 min)
    ↓
1. Load TileViews where durationMs ≥ 8000 and Decision = null
2. Mark Decision = "YES" (implicit positive signal)
3. Collect all YES voice tiles for user
4. If count ≥ 10:
     - Compute mean embedding
     - Upsert to UserVoicePreference
```

**Status:** TileView collection works. Decision marking + preference computation NOT wired.

---

## Storage Schema

### Tile Table
```sql
CREATE TABLE tiles (
    id UUID PRIMARY KEY,
    user_id INT NOT NULL,
    content_type TEXT NOT NULL,  -- 'voice'
    audio_url TEXT,              -- Azure Blob URL
    duration_secs INT,
    voice_embedding VECTOR(192), -- ECAPA-TDNN embedding
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_tiles_voice_embedding ON tiles USING ivfflat (voice_embedding vector_cosine_ops);
```

### UserVector Table
```sql
-- voice_embedding column already exists
ALTER TABLE user_vectors
ADD COLUMN voice_embedding VECTOR(192);
```

### UserVoicePreference Table (Planned)
```sql
CREATE TABLE user_voice_preferences (
    user_id INT PRIMARY KEY,
    preference_embedding VECTOR(192),  -- Mean of YES decisions
    yes_sample_count INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_user_voice_preferences_user_id ON user_voice_preferences(user_id);
```

---

## Debugging

### Check User's Voice Signature
```sql
SELECT
    user_id,
    voice_embedding IS NOT NULL AS has_voice_embedding,
    updated_at
FROM user_vectors
WHERE user_id = 123
ORDER BY version DESC
LIMIT 1;
```

### Count Voice Tiles
```sql
SELECT COUNT(*) FROM tiles
WHERE user_id = 123
  AND content_type = 'voice'
  AND voice_embedding IS NOT NULL;
```

### Inspect Voice Embedding
```sql
SELECT
    id,
    user_id,
    audio_url,
    voice_embedding::TEXT AS embedding_preview,
    created_at
FROM tiles
WHERE user_id = 123
  AND content_type = 'voice'
ORDER BY created_at DESC
LIMIT 5;
```

### Manual Recompute
```bash
# Trigger re-embedding for tile
DELETE FROM tiles WHERE id = 'abc-123-def' RETURNING audio_url;

# Re-create tile (triggers embedding)
curl -X POST https://api.woven.me/tiles \
  -H "Authorization: Bearer $TOKEN" \
  -d '{ "contentType": "voice", "audioUrl": "..." }'
```

---

## Known Issues

### Issue 1: Voice Preference Learning Not Wired
**Problem:** `UserVoicePreference` table exists but no worker populates it.  
**Impact:** VoiceScore = 50 (neutral) for all pairs.  
**Mitigation:** Planned for Q4 2026. Until then, voice signature is used for diversity only.

---

### Issue 2: Short Voice Clips (<3s)
**Problem:** ECAPA-TDNN trained on 3-10s speech segments. <3s clips → unstable embeddings.  
**Impact:** Voice signature oscillates if user posts many short clips.  
**Mitigation:** Frontend enforces 3s minimum recording duration.

---

### Issue 3: Background Noise
**Problem:** Street noise, music, or multiple speakers → embedding contamination.  
**Impact:** Voice signature drifts away from user's actual voice.  
**Mitigation:**
- Frontend shows waveform + noise warning before upload
- Running mean reduces impact of single noisy clip

---

### Issue 4: Voice Mimicry (Edge Case)
**Problem:** User posts celebrity voice impressions → embedding doesn't represent their voice.  
**Impact:** Mismatched voice signatures.  
**Mitigation:** Trust scoring (if user flagged for "fake voice" → TrustScore penalty).

---

## Performance

### Batch Voice Tile Processing (Nightly)
**No dedicated worker.** Voice embeddings computed real-time on tile creation.

**If backlog exists (e.g., after migration):**
```sql
-- Find tiles missing embeddings
SELECT id, user_id, audio_url FROM tiles
WHERE content_type = 'voice'
  AND audio_url IS NOT NULL
  AND voice_embedding IS NULL
ORDER BY created_at DESC
LIMIT 1000;
```

**Backfill script:**
```bash
# Python script: scripts/backfill_voice_embeddings.py
for tileId, userId, audioUrl in missing_tiles:
    embedding = embed_voice(audioUrl)
    db.execute("UPDATE tiles SET voice_embedding = %s WHERE id = %s", (embedding, tileId))
    update_user_voice_signature(userId)
```

**Estimated time:** 10,000 tiles × 1.5s = 4.2 hours

---

## Future Enhancements

### Planned (Q4 2026)
1. **Wire voice preference learning** — populate `UserVoicePreference` from TileView dwell signals.
2. **Aversion embedding** — separate vector for NO decisions (voices user dislikes).
3. **Voice quality score** — detect low-quality audio (noise, clipping, echo) → exclude from signature.

### Researching (2027+)
1. **Emotion detection** — Wav2Vec2 + emotion classification head (happy/sad/calm/excited).
2. **Accent similarity** — cluster users by accent (Hindi-English, Tamil-English, etc.) for better matches.
3. **Voice aging** — ECAPA-TDNN is age-invariant (20yo vs 40yo sound similar). Add age-specific tuning.
4. **Cross-lingual embeddings** — Support Hindi, Tamil voice tiles (currently English-only training).

---

## Related Documentation

- [README.md](./README.md) — Embeddings system overview
- [../echo/scoring.md](../echo/scoring.md#6-voicescore-weight-008) — VoiceScore formula
- [workers.md](./workers.md) — Batch processing details
- [configuration.md](./configuration.md) — SpeechBrain endpoint setup

---

**Questions?** Check logs for `[VoiceEmbedding]` prefix.
