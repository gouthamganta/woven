# Embeddings System

**Last Updated:** 2026-10-07  
**Status:** Production  
**Directory:** `backend/WovenBackend/Services/Embeddings/`

---

## Overview

Woven's embeddings system is the foundation of ECHO's multi-modal matching algorithm. Instead of relying solely on stated preferences, we compute dense vector representations across **9 modalities** to capture both explicit (what users say) and implicit (what users do) compatibility signals.

All embeddings are stored in `UserVector` using pgvector for efficient cosine similarity queries.

---

## 9 Embedding Modalities

| Modality | Dimensions | Data Source | Update Frequency | Purpose |
|---|---|---|---|---|
| **Pillar** | 8 | Foundational questions | Onboarding + edits | Core personality alignment |
| **Expression** | 1536 | User's posted tiles | Real-time (tile creation) | Content similarity |
| **Reception** | 1536 | Tiles user dwells on | Every 30 min (worker) | Consumption taste |
| **Preference** | 1536 | ChatNote text | Every 4 hours | Stated attraction patterns |
| **Voice** | 192 | User's voice tiles | Real-time (tile creation) | Voice signature |
| **Style** | 128 | All text corpus | Nightly (02:30 UTC) | Writing style |
| **Humor** | 64 | Game outcomes + emojis | Nightly (02:30 UTC) | Humor compatibility |
| **Lifestyle** | 128 | Optional fields + behavior | Nightly (02:30 UTC) | Lifestyle alignment |
| **Emotional Rhythm** | 48 | Activity patterns | Nightly (02:30 UTC) | Temporal/emotional patterns |

---

## Additional Computed Vectors

### Behavioral Fingerprint (16-dim)
**Not in UserVector** — stored in `UserBehavioralFingerprint` table.

Captures 16 behavioral dimensions from `MatchSignalLogs`:
- Response speed, message volume, disclosure balance
- Trial affinity, love expressiveness, feedback positivity
- Tile/voice curiosity, first message speed, date progression
- Profile exploration, game engagement, balloon eagerness
- Engagement breadth, signal density

**Used for:**
- BehavioralLifestyleScore (component #9 in ECHO scoring)
- AttachmentProxy derivation (secure/anxious/avoidant/disorganised)

**See:** [behavioral-fingerprint.md](./behavioral-fingerprint.md)

---

### Attachment Proxy (4-dim)
Derived from Behavioral Fingerprint via `AttachmentProxyService`.

**Dimensions:**
- [0] Secure — balanced disclosure, willing to deepen
- [1] Anxious — hypervigilant responsiveness, activity flooding
- [2] Avoidant — slow to respond, unexpressive, rejects deepening
- [3] Disorganised — approach-avoidance conflict (min of anxious + avoidant)

**Why proxy:** Asking "what's your attachment style?" is too clinical. We infer from behavior.

---

### Visual Preference (CLIP-dim, typically 512)
**Not in UserVector** — stored in `UserVisualPreference` table.

Learned from user's YES/NO decisions on photos via `VisualPreferenceService`.

**Two vectors:**
- `PreferenceEmbedding` — mean of YES decisions (≥10 samples)
- `AversionEmbedding` — mean of NO decisions (≥10 samples)

**Used for:**
- VisualScore (component #5 in ECHO scoring)
- Best photo selection per viewer (personalized candidate photo)

**See:** [visual-preference.md](./visual-preference.md)

---

### Voice Preference (192-dim)
**Not in UserVector** — stored in `UserVoicePreference` table (planned).

Learned from user's YES/NO decisions on voice tiles.

**Status:** Schema exists, preference learning not yet wired (only user's own voice embedding is computed).

**See:** [voice-embedding.md](./voice-embedding.md)

---

## Embedding Generation Flow

### Onboarding Bootstrap
**When:** User completes foundational questions.  
**Where:** `OnboardingEndpoints.cs` (lines 1055-1082)

```csharp
// Phase 1: Create UserVector (version 1)
var userVector = new UserVector
{
    UserId = userId,
    Version = 1,
    VectorJson = JsonSerializer.Serialize(vectorData),
    PillarScoresJson = JsonSerializer.Serialize(pillarScores),
    PillarEmbedding = new Vector(pillarArray)
};
_db.UserVectors.Add(userVector);

// Phase 2: Queue embedding jobs
await _embeddingQueue.QueuePhotoEmbeddingAsync(userId, photoUrl, ct);
await _embeddingQueue.QueueVectorBuildAsync(userId, ct);
```

**Initial embeddings:**
- `PillarEmbedding` — 8-dim from foundational answers
- `PhotoEmbedding` — CLIP embedding of profile photo (async via queue)

---

### Nightly Batch Processing
**When:** Every night at 02:30 UTC  
**Worker:** `EmbeddingBatchWorker`

**For all users with `ProfileStatus = COMPLETE`:**
1. Style embedding — 128-dim from text corpus
2. Humor embedding — 64-dim from game outcomes
3. Lifestyle embedding — 128-dim from optional fields + behavior
4. Emotional Rhythm — 48-dim from activity patterns
5. **Behavioral Fingerprint** — 16-dim from MatchSignalLogs (MUST run first)
6. **Attachment Proxy** — 4-dim derived from fingerprint
7. Visual Preference — learned from YES/NO photo decisions

**See:** [workers.md](./workers.md)

---

### Real-Time Updates

#### Tile Creation (Text/Photo/Voice/Video)
**When:** User posts a tile.  
**Service:** `TileService.CreateTileAsync()`

**Updates:**
1. **Expression Embedding** — running mean of user's last 30 tile embeddings
   - Queued async via `_embeddingQueue.QueueTileEmbeddingAsync(tileId, ct)`
2. **Voice Embedding** — running mean of user's voice tiles (if ContentType = voice)
   - `VoiceEmbeddingService.EmbedVoiceAsync()` called inline
3. **Photo Embedding** — CLIP embedding of photo tile (if ContentType = photo)

---

#### Commons Browsing (Tile Views)
**When:** User dwells on a tile ≥8s.  
**Worker:** `TileViewProcessorWorker` (every 30 min)

**Updates:**
1. **Reception Embedding** — weighted mean of last 50 dwelled tiles
   - Represents what user consumes (vs what they post)

---

#### ChatNote Writing
**When:** User writes a private note about a match.  
**Worker:** `ChatNoteEmbeddingWorker` (every 4 hours)

**Updates:**
1. **Preference Embedding** — text-embedding-3-small of last 20 ChatNote texts
   - Represents stated attraction patterns ("I like people who are...")
   - Requires ≥3 ChatNotes

---

## Storage Schema

### UserVector Table
```sql
CREATE TABLE user_vectors (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL,
    version INT NOT NULL,
    vector_json TEXT NOT NULL,             -- Complete matchable state as JSON
    pillar_scores_json TEXT NOT NULL,      -- 8 pillar scores [0-1]
    pillar_embedding VECTOR(8),            -- 8-dim pgvector for ANN queries
    expression_embedding VECTOR(1536),     -- What user posts
    reception_embedding VECTOR(1536),      -- What user consumes
    preference_embedding VECTOR(1536),     -- What user says they like (ChatNotes)
    voice_embedding VECTOR(192),           -- User's own voice signature
    intent_embedding VECTOR(1536),         -- Intent metadata (future)
    style_embedding VECTOR(128),           -- Writing style
    humor_embedding VECTOR(64),            -- Humor compatibility
    lifestyle_embedding VECTOR(128),       -- Lifestyle alignment
    emotional_rhythm_embedding VECTOR(48), -- Activity/emotional patterns
    attachment_proxy_embedding VECTOR(4),  -- Attachment style proxy
    behavioral_lifestyle_json TEXT,        -- Behavioral metadata (future)
    created_at TIMESTAMP NOT NULL,
    updated_at TIMESTAMP NOT NULL,
    UNIQUE(user_id, version)
);

CREATE INDEX idx_user_vectors_user_id ON user_vectors(user_id);
CREATE INDEX idx_user_vectors_pillar_embedding ON user_vectors USING ivfflat (pillar_embedding vector_cosine_ops);
```

---

### Auxiliary Tables

```sql
-- Photo embeddings (CLIP)
CREATE TABLE photo_embeddings (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL,
    photo_url TEXT NOT NULL,
    embedding VECTOR(512),  -- CLIP ViT-B/32 default
    embedded_at TIMESTAMPTZ NOT NULL
);

-- Visual preferences (learned from YES/NO decisions)
CREATE TABLE user_visual_preferences (
    user_id INT PRIMARY KEY,
    preference_embedding VECTOR(512),  -- Mean of YES decisions
    aversion_embedding VECTOR(512),    -- Mean of NO decisions
    yes_sample_count INT NOT NULL,
    no_sample_count INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

-- Voice preferences (planned)
CREATE TABLE user_voice_preferences (
    user_id INT PRIMARY KEY,
    preference_embedding VECTOR(192),  -- Mean of YES voice tiles
    yes_sample_count INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

-- Behavioral fingerprint (16-dim from MatchSignalLogs)
CREATE TABLE user_behavioral_fingerprints (
    user_id INT PRIMARY KEY,
    vector_json TEXT NOT NULL,         -- float[] as JSON
    computed_at TIMESTAMP NOT NULL
);

-- Reference photo embeddings (for bias detection)
CREATE TABLE reference_photo_embeddings (
    id UUID PRIMARY KEY,
    label TEXT NOT NULL,               -- e.g., "fair_skin_male", "dark_skin_female"
    embedding VECTOR(512),
    added_at TIMESTAMPTZ NOT NULL
);
```

---

## Model Specifications

| Embedding Type | Model | API/Library | Dimensions | Cost |
|---|---|---|---|---|
| **Photo (CLIP)** | `openai/clip-vit-base-patch32` | Replicate | 512 | $0.00023/img |
| **Voice (Speaker)** | `ECAPA-TDNN` | SpeechBrain (self-hosted) | 192 | Free |
| **Text (Semantic)** | `text-embedding-3-small` | OpenAI | 1536 | $0.020/1M tokens |
| **Style** | Hand-crafted features | — | 128 | Free |
| **Humor** | Hand-crafted features | — | 64 | Free |
| **Lifestyle** | Hand-crafted features | — | 128 | Free |
| **Emotional Rhythm** | Hand-crafted features | — | 48 | Free |
| **Behavioral** | Hand-crafted features | — | 16 | Free |
| **Attachment** | Derived from Behavioral | — | 4 | Free |

**Total embedding cost per user per month:** ~$0.15 (mostly from text embeddings)

---

## Performance

### Batch Processing (EmbeddingBatchWorker)
**Nightly run at 02:30 UTC:**
- 10,000 active users
- 7 embeddings per user × ~200ms avg = ~1400s = **23 minutes**
- Peak memory: ~2GB (batched SQL, no full user set in memory)

**Bottlenecks:**
- OpenAI API rate limits (text-embedding-3-small: 3000 RPM on tier 2)
- PostgreSQL connection pool (20 connections, scoped per user)

---

### Real-Time Updates
**Tile creation:**
- Photo embedding: ~800ms (Replicate async poll)
- Voice embedding: ~1200ms (SpeechBrain HTTP endpoint)
- Expression embedding update: <50ms (DB update only, embedding computed async)

**ChatNote creation:**
- No real-time cost (processed by worker every 4 hours)

---

## ECHO Integration

**How embeddings feed into ECHO scoring:**

```csharp
// MatchScoringService.cs — 16-component formula

1. PillarScore       (weight 0.19) — CosineSimilarity(viewerPillar, candPillar)
2. IntentScore       (weight 0.12) — Intent alignment (seriousness + commitment + tags)
3. ExpressionScore   (weight 0.09) — CosineSimilarity(viewerExpression, candExpression)
4. StyleScore        (weight 0.09) — CosineSimilarity(viewerStyle, candStyle)
5. VisualScore       (weight 0.10) — DotProduct(viewerVisualPref, candPhoto)
6. VoiceScore        (weight 0.08) — DotProduct(viewerVoicePref, candVoice)
7. HumorScore        (weight 0.07) — CosineSimilarity(viewerHumor, candHumor)
8. LifestyleScore    (weight 0.08) — CosineSimilarity(viewerLifestyle, candLifestyle)
9. BehavioralScore   (weight 0.05) — CosineSimilarity(viewerBehavioral, candBehavioral)
10. EmotionalRhythm  (weight 0.04) — CosineSimilarity(viewerEmotional, candEmotional)
11. AttachmentScore  (weight 0.04) — CosineSimilarity(viewerAttachment, candAttachment)
12-16. [Other components not embedding-based]

TotalScore = (Σ weighted scores) × intentMult × trustPenalty
```

**See:** [../echo/scoring.md](../echo/scoring.md)

---

## Related Documentation

- [behavioral-fingerprint.md](./behavioral-fingerprint.md) — 16-dim behavioral embedding
- [voice-embedding.md](./voice-embedding.md) — Voice note embeddings
- [visual-preference.md](./visual-preference.md) — Photo preference learning
- [humor-embedding.md](./humor-embedding.md) — Humor detection
- [emotional-rhythm.md](./emotional-rhythm.md) — Emotional pattern embeddings
- [lifestyle-embedding.md](./lifestyle-embedding.md) — Lifestyle compatibility
- [photo-embedding.md](./photo-embedding.md) — Profile photo embeddings
- [style-embedding.md](./style-embedding.md) — Writing style embeddings
- [workers.md](./workers.md) — Batch processing workers
- [configuration.md](./configuration.md) — Model configs, API endpoints

---

## Design Principles

### 1. Multi-Modal by Default
No single modality dominates. Visual attraction (10%) is balanced with personality (19%), style (9%), humor (7%), etc.

### 2. Behavior Over Stated Preferences
- Behavioral fingerprint (what users do) > optional fields (what users say)
- Reception embedding (what users consume) complements Expression (what users post)
- Visual/voice preferences learned from YES/NO decisions, not checkboxes

### 3. Graceful Degradation
Missing embeddings don't crash scoring:
- `available_i = 0` if embedding is null
- Weights renormalized across available components
- Pillar embedding always available (from onboarding)

### 4. Privacy-First
- EXIF stripped from all photos before embedding
- ChatNotes never leave the database (only embeddings computed)
- Behavioral fingerprint aggregates 180 days of signals (no raw log access)

### 5. Testable
All embedding computation is pure (no I/O in core logic):
```csharp
internal static float[] ExtractStyleFeatures(string text) { ... }  // Pure
internal static float[] Derive(float[] fingerprint) { ... }        // Pure
internal static float[] Compute(List<(EventType, Value, CandidateId)> signals) { ... }  // Pure
```

I/O wrapper methods call these pure functions:
```csharp
public async Task ComputeStyleEmbeddingAsync(int userId, CancellationToken ct)
{
    var corpus = await FetchCorpus(userId, ct);  // I/O
    var features = ExtractStyleFeatures(corpus); // Pure
    await SaveEmbedding(userId, features, ct);   // I/O
}
```

---

## Known Gaps

| Gap | Impact | Planned Fix |
|---|---|---|
| VoicePreference learning not wired | VoiceScore always 50 (neutral) | Q4 2026 |
| IntentEmbedding unused | IntentScore uses JSON metadata only | Phase 3D |
| PreferenceEmbedding low coverage | Only 15% of users write ChatNotes | Add tooltips prompting notes |
| BehavioralLifestyleJson unused | Reserved field, no data written yet | Future analytics |
| No embedding versioning | Can't A/B test new models | Add `embedding_version` column |

---

## Debugging

### Check Embedding Coverage
```sql
SELECT
    COUNT(*) FILTER (WHERE pillar_embedding IS NOT NULL) AS pillar,
    COUNT(*) FILTER (WHERE expression_embedding IS NOT NULL) AS expression,
    COUNT(*) FILTER (WHERE reception_embedding IS NOT NULL) AS reception,
    COUNT(*) FILTER (WHERE preference_embedding IS NOT NULL) AS preference,
    COUNT(*) FILTER (WHERE voice_embedding IS NOT NULL) AS voice,
    COUNT(*) FILTER (WHERE style_embedding IS NOT NULL) AS style,
    COUNT(*) FILTER (WHERE humor_embedding IS NOT NULL) AS humor,
    COUNT(*) FILTER (WHERE lifestyle_embedding IS NOT NULL) AS lifestyle,
    COUNT(*) FILTER (WHERE emotional_rhythm_embedding IS NOT NULL) AS emotional,
    COUNT(*) FILTER (WHERE attachment_proxy_embedding IS NOT NULL) AS attachment
FROM user_vectors
WHERE user_id IN (SELECT id FROM users WHERE profile_status = 'COMPLETE');
```

### Inspect User's Embeddings
```sql
SELECT
    pillar_embedding IS NOT NULL AS pillar,
    expression_embedding IS NOT NULL AS expr,
    reception_embedding IS NOT NULL AS recep,
    preference_embedding IS NOT NULL AS pref,
    voice_embedding IS NOT NULL AS voice,
    style_embedding IS NOT NULL AS style,
    humor_embedding IS NOT NULL AS humor,
    lifestyle_embedding IS NOT NULL AS lifestyle,
    emotional_rhythm_embedding IS NOT NULL AS emotional,
    attachment_proxy_embedding IS NOT NULL AS attach,
    updated_at
FROM user_vectors
WHERE user_id = 123
ORDER BY version DESC
LIMIT 1;
```

### Check Batch Worker Runs
```sql
-- Redis lock check
GET lock:embedding-batch

-- Log search (requires Serilog sink to DB)
SELECT * FROM logs
WHERE message LIKE '%EmbeddingBatchWorker%'
ORDER BY timestamp DESC
LIMIT 20;
```

---

## Future Enhancements

### Planned (Q4 2026)
1. **Embedding versioning** — track model changes, allow A/B tests
2. **VoicePreference learning** — complete the YES/NO voice tile workflow
3. **IntentEmbedding** — semantic embedding of intent tags
4. **BehavioralLifestyleJson** — store structured behavioral metadata

### Researching (2027+)
1. **Multi-lingual embeddings** — Hindi/Tamil support (India expansion)
2. **Video embeddings** — ViViT or similar for video tiles
3. **Audio sentiment** — emotion detection in voice notes (Wav2Vec2 + classification head)
4. **Embedding drift monitoring** — alert when user's embedding changes >0.3 cosine in 7 days
5. **Collaborative filtering embeddings** — matrix factorization for cold start

---

**Questions?** See [configuration.md](./configuration.md) for API keys and service setup.
