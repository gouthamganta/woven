# ECHO Embedding Modalities

ECHO uses **9 core embedding modalities** to represent users in vector space, plus 3 auxiliary embeddings for specialized matching components. All embeddings are computed nightly by `EmbeddingBatchWorker` (02:30 UTC) and stored in the `UserVectors` table.

---

## Embedding Generation Pipeline

**Worker:** `EmbeddingBatchWorker`  
**Schedule:** Daily 02:30 UTC  
**Eligible users:** `ProfileStatus == COMPLETE`  
**File:** `backend/WovenBackend/Services/Embeddings/EmbeddingBatchWorker.cs`

**Services called (in order):**
1. `IStyleEmbeddingService`
2. `IHumorEmbeddingService`
3. `ILifestyleEmbeddingService`
4. `IEmotionalRhythmService`
5. `IBehavioralFingerprintService`
6. `IAttachmentProxyService` (depends on #5)
7. `IVisualPreferenceService`
8. (Pillar + Expression embeddings computed at onboarding exit, not in batch)
9. (Voice embeddings computed on-demand when voice tile posted)

---

## 1. Pillar Embedding (Foundational AI Profile)

**Source:** Foundational question answers → 8 pillar scores  
**Dimensions:** 1536 column (`vector(1536)`); the builder currently writes the 8 pillar scores (see note below)  
**When computed:** Onboarding exit (after user completes foundational questions)  
**Storage:** `UserVectors.PillarEmbedding`

### Algorithm
1. User answers foundational questions → `PillarScores` (8-dim vector [0,1])
   ```
   Lifestyle, Energy, Communication, Affection, Stability, Values, Curiosity, Emotional Rhythm
   ```
2. Pillar scores serialized to JSON text:
   ```json
   {
     "Lifestyle": 0.72,
     "Energy": 0.85,
     "Communication": 0.68,
     "Affection": 0.75,
     "Stability": 0.62,
     "Values": 0.80,
     "Curiosity": 0.90,
     "Emotional Rhythm": 0.55
   }
   ```
3. `UserVectorBuilder.BuildAndSaveV1Async` currently stores the 8 pillar scores directly as `PillarEmbedding` (order: Lifestyle, Energy, Values, Communication, Ambition, Stability, Curiosity, Affection); no OpenAI call is made for this column.
4. Stored as `Pgvector.Vector` in PostgreSQL (pgvector extension)

> **Dimension note (source: `UserVectorBuilder.cs`, `WovenDbContext.cs`, migration `20260517042811_AddInsightsAndOpinion`).** Three different things share the word "pillar":
> - **8 scalar pillar scores** — `PillarScoresJson`, the values above.
> - **`PillarEmbedding` column** — mapped `vector(1536)` (altered from `vector(8)` in `20260517042811`). The builder still writes an 8-float vector, which does not match a 1536-dim column; the code comment says a 1536-dim text embedding was the Phase 3B plan. Verify before relying on `PillarEmbedding` being populated.
> - **Other modalities** — 128/64/48/4/192/512-dim columns documented per section.

### Scoring Component
**Component #1:** PillarScore (weight 0.19)  
**Computation:** Cosine similarity between viewer and candidate PillarEmbeddings → [0, 100]

**If embedding missing:** Falls back to raw pillar score cosine similarity (8-dim).

---

## 2. Expression Embedding

**Source:** User's text tiles (what they post)  
**Dimensions:** 1536 (`vector(1536)`, `text-embedding-3-small`; mean of last 30 tile embeddings)  
**When computed:** After first text tile posted, updated nightly  
**Storage:** `UserVectors.ExpressionEmbedding`

### Algorithm
1. Collect all text tiles authored by user
2. Concatenate text (up to 2000 chars, newest first)
3. Embed via OpenAI → vector (dimension per the section header)
4. Store in UserVectors

### Scoring Component
**Component #3:** ExpressionScore (weight 0.09)  
**Computation:** Cosine similarity between viewer and candidate ExpressionEmbeddings → [0, 100]

**Purpose:** Matches users who post similar types of content (topics, tone, style).

---

## 3. Style Embedding

**Source:** Writing style from text tiles + bio  
**Dimensions:** 128 (`vector(128)`)  
**When computed:** Nightly batch (02:30 UTC)  
**File:** `backend/WovenBackend/Services/Embeddings/StyleEmbeddingService.cs`

### Algorithm
1. Extract text from:
   - Bio (if present)
   - Text tiles (newest 5)
   - Foundational question answers (long-form)
2. Analyze style markers (not content):
   - Sentence length variance
   - Punctuation density
   - Emoji usage
   - Formality score (LLM-based)
3. Generate style description text:
   ```
   "This person writes in short, punchy sentences with moderate emoji use. 
   Tone is casual and conversational."
   ```
4. Embed description via OpenAI → vector (dimension per the section header)

### Scoring Component
**Component #4:** StyleScore (weight 0.09)  
**Computation:** Cosine similarity → [0, 100]

**Purpose:** Matches users with compatible communication styles (formal/casual, terse/verbose, emoji-heavy/minimal).

---

## 4. Humor Embedding

**Source:** Humor sense from foundational answers + tiles  
**Dimensions:** 64 (`vector(64)`)  
**When computed:** Nightly batch (02:30 UTC)  
**File:** `backend/WovenBackend/Services/Embeddings/HumorEmbeddingService.cs`

### Algorithm
1. Extract text likely to reveal humor sense:
   - "What makes you laugh?" (foundational question)
   - Text tiles tagged as humor/joke
   - Profile bio (if contains humor markers)
2. Generate humor profile:
   ```
   "Dry wit, sarcasm, absurdist humor. Laughs at wordplay and observational comedy."
   ```
3. Embed via OpenAI → vector (dimension per the section header)

### Scoring Component
**Component #7:** HumorScore (weight 0.07)  
**Computation:** Cosine similarity → [0, 100]

**Purpose:** Matches users who find the same things funny (critical for relationship compatibility).

---

## 5. Lifestyle Embedding

**Source:** Optional lifestyle fields (diet, workout, smoking, drinking, etc.)  
**Dimensions:** 128 (`vector(128)`)  
**When computed:** Nightly batch (02:30 UTC)  
**File:** `backend/WovenBackend/Services/Embeddings/LifestyleEmbeddingService.cs`

### Algorithm
1. Collect `UserOptionalFields` (key-value pairs):
   ```
   diet: vegetarian
   workout: 3-4x/week
   smoking: never
   drinking: socially
   religion: spiritual but not religious
   ```
2. Serialize to text:
   ```
   "Diet: vegetarian. Workout: 3-4x/week. Smoking: never. Drinking: socially. 
   Religion: spiritual but not religious."
   ```
3. Embed via OpenAI → vector (dimension per the section header)

### Scoring Component
**Component #8:** LifestyleScore (weight 0.08)

**Computation:**
- If both users have LifestyleEmbedding → cosine similarity → [0, 100]
- Else → `ComputeLifestyleScore(userFields, candidateFields)` (rule-based fallback)

**Fallback scoring:**
- +20 if diet matches (or both unspecified)
- -30 if diet mismatch on deal-breakers (vegan vs non-vegan)
- +15 if smoking matches
- -15 if smoking mismatch
- +10 if religion matches
- ±10 for drinking/workout alignment
- Clamped [0, 100]

---

## 6. Emotional Rhythm Embedding

**Source:** Emotional expression patterns from foundational answers  
**Dimensions:** 48 (`vector(48)`)  
**When computed:** Nightly batch (02:30 UTC)  
**File:** `backend/WovenBackend/Services/Embeddings/EmotionalRhythmService.cs`

### Algorithm
1. Analyze foundational answers for:
   - Emotional vocabulary richness
   - Sentiment variance (how much they fluctuate)
   - Self-disclosure depth
   - Emotional awareness (meta-commentary on feelings)
2. Generate emotional rhythm profile:
   ```
   "High emotional awareness. Expresses feelings clearly and variably. 
   Comfortable with vulnerability."
   ```
3. Embed via OpenAI → vector (dimension per the section header)

### Scoring Component
**Component #10:** EmotionalRhythmScore (weight 0.04)  
**Computation:** Cosine similarity → [0, 100]

**Purpose:** Matches users with compatible emotional communication styles (expressiveness, vulnerability comfort).

---

## 7. Attachment Proxy Embedding

**Source:** Behavioral fingerprint (app usage patterns)  
**Dimensions:** 4 (`vector(4)`)  
**When computed:** Nightly batch (02:30 UTC), **after** BehavioralFingerprint computed  
**File:** `backend/WovenBackend/Services/Embeddings/AttachmentProxyService.cs`

### Algorithm
1. Load user's behavioral fingerprint (16-dim vector):
   - Message frequency, response latency variance
   - Session duration patterns
   - Re-engagement rate (days between logins)
   - Conversation initiation rate
   - Trial acceptance rate
   - Ghost risk score
2. Map behavioral patterns to attachment-style proxies:
   ```
   "Consistent engagement, moderate response latency, high re-engagement. 
   Suggests secure attachment patterns."
   ```
3. Embed description via OpenAI → vector (dimension per the section header)

### Scoring Component
**Component #11:** AttachmentScore (weight 0.04)  
**Computation:** Cosine similarity → [0, 100]

**Purpose:** Matches users with compatible behavioral patterns (anxious vs avoidant vs secure proxies).

**Why "proxy":** We don't ask users about attachment style directly (too clinical). We infer it from behavior.

---

## 8. Visual Preference Embedding (Learned)

**Source:** User's YES/NO decisions on photos  
**Dimensions:** 512 (CLIP image embedding)  
**When computed:** Nightly batch (02:30 UTC), **requires ≥10 YES or ≥10 NO samples**  
**File:** `backend/WovenBackend/Services/Embeddings/VisualPreferenceService.cs`

### Algorithm
1. Load photos user said YES to → `PhotoEmbeddings` (CLIP 512-dim)
2. Load photos user said NO to → `PhotoEmbeddings`
3. Compute element-wise mean of YES embeddings → `PreferenceEmbedding`
4. Compute element-wise mean of NO embeddings → `AversionEmbedding`
5. Store both in `UserVisualPreferences` table

### Scoring Component
**Component #5:** VisualScore (weight 0.10)

**Computation:**
```csharp
var dotProduct = DotProduct(viewerPreferenceEmbedding, candidatePhotoEmbedding);
var score = Clamp(50 + dotProduct * 50, 0, 100);
```

**Photo selection:**
- Uses candidate's primary photo (first by SortOrder)
- If viewer has no PreferenceEmbedding yet → component skipped (available[4] = false)

**Purpose:** Matches users to candidates whose photos match their learned visual preferences.

**Privacy note:** We never analyze photos for age, race, or attractiveness. CLIP embedding is content-agnostic.

---

## 9. Voice Embedding

**Source:** Voice tiles (audio)  
**Dimensions:** 192 (ECAPA-TDNN speaker embedding)  
**When computed:** On-demand when user posts voice tile  
**File:** `backend/WovenBackend/Services/Embeddings/VoiceEmbeddingService.cs`

### Algorithm
1. User posts voice tile → audio uploaded to Azure Blob
2. Backend calls SpeechBrain ECAPA-TDNN model:
   - **Production:** HTTP endpoint `SpeechBrain:EndpointUrl`
   - **Dev fallback:** Python subprocess (not available in containers)
3. Returns 192-dim speaker embedding
4. Stored in `Tiles.VoiceEmbedding` (latest voice tile per user)

### Scoring Component
**Component #6:** VoiceScore (weight 0.08)

**Computation:**
```csharp
var dotProduct = DotProduct(viewerVoicePreference, candidateVoiceEmbedding);
var score = Clamp(50 + dotProduct * 50, 0, 100);
```

**Voice preference:**
- Computed from user's YES decisions on voice tiles (similar to VisualPreference)
- Stored in `UserVoicePreferences.PreferenceEmbedding`
- Requires ≥10 YES samples

**Purpose:** Matches users who like each other's voice (tone, pitch, speaking style).

**Model:** ECAPA-TDNN (SpeechBrain) is a speaker verification model → captures voice characteristics, not content.

---

## Auxiliary Embeddings (Not Core 9)

### 10. Behavioral Lifestyle Embedding

**Source:** Actual app usage patterns (not stated preferences)  
**Dimensions:** 16-dim fingerprint (not embedded further)  
**When computed:** Nightly batch (02:30 UTC)  
**File:** `backend/WovenBackend/Services/Embeddings/BehavioralFingerprintService.cs`

**Features (16-dim vector):**
1. Avg session duration
2. Sessions per week
3. Avg message length
4. Message frequency
5. Response latency (median)
6. Response latency (variance)
7. Re-engagement rate (days between logins)
8. Conversation initiation rate
9. Trial acceptance rate
10. Ghost risk score
11. Dwell time avg (tiles)
12. Voice note frequency
13. Game completion rate
14. Date idea acceptance rate
15. Explicit feedback given count
16. Self-disclosure score avg

### Scoring Component
**Component #9:** BehavioralLifestyleScore (weight 0.05)

**Computation:**
```csharp
var similarity = VectorCosineSimilarity(viewerBehavioral, candidateBehavioral);
var score = (similarity + 1.0) / 2.0 * 100.0; // [-1,1] → [0,100]
```

**Purpose:** Matches users with similar behavioral patterns (e.g., both send long messages, both reply quickly).

---

### 11. Reception Embedding (Content Consumption)

**Source:** Tiles the user dwelled on (≥8s)  
**Dimensions:** 1536 (`vector(1536)`, `text-embedding-3-small`; weighted mean of last 50 dwelled tile embeddings)  
**When computed:** By `TileViewProcessorWorker` every 30min  
**Storage:** `UserVectors.ReceptionEmbedding`

### Algorithm
1. Load tiles user dwelled on (≥8s) in past 90 days
2. Extract tile text → concat
3. Embed via OpenAI → vector (dimension per the section header)
4. Exponentially-weighted mean (more recent = higher weight)
5. Store in UserVectors

### Scoring Component
**Component #15:** SharedTileAffinityScore (weight 0.05)

**Computation:**
```csharp
var similarity = CosineSimilarity(viewerReception, candidateReception);
```

**Purpose:** Matches users who consume similar types of content (shared taste in what they read/watch).

**Difference from Expression:**
- Expression = what you post
- Reception = what you engage with
- Can be mismatched (e.g., you post travel photos but dwell on philosophy tiles)

---

### 12. Preference Embedding (Stated Interests from ChatNotes)

**Source:** ChatNotes (private notes users write about matches)  
**Dimensions:** 1536 (`vector(1536)`, `text-embedding-3-small` of last 20 ChatNote texts)  
**When computed:** By `ChatNoteEmbeddingWorker` (nightly, future)  
**Storage:** `UserVectors.PreferenceEmbedding`

### Algorithm
1. Load user's ChatNotes (notes about their matches)
2. Extract phrases about what they find interesting:
   ```
   "I like that she's into hiking" → interested in outdoorsy people
   "His sense of humor is great" → values humor
   ```
3. Aggregate into preference profile → embed
4. Store in UserVectors

### Scoring Component
**Component #16:** PreferenceAffinityScore (weight 0.04)

**Computation:**
```csharp
var similarity = CosineSimilarity(viewerPreference, candidateExpression);
```

**Purpose:** Cross-modal matching — what viewer says they like vs what candidate posts.

**Example:**
- Viewer's ChatNotes mention "I like people who are adventurous"
- Candidate's Expression is "I love spontaneous road trips"
- High PreferenceAffinityScore

**Status:** Worker stub exists, not fully wired yet.

---

## Embedding Storage

**Table:** `UserVectors`

```sql
CREATE TABLE UserVectors (
    Id SERIAL PRIMARY KEY,
    UserId INT NOT NULL,
    Version INT NOT NULL,  -- Increments on each recompute
    
    -- Core 9 embeddings
    PillarEmbedding vector(1536),
    ExpressionEmbedding vector(1536),
    IntentEmbedding vector(1536),
    StyleEmbedding vector(128),
    HumorEmbedding vector(64),
    LifestyleEmbedding vector(128),
    EmotionalRhythmEmbedding vector(48),
    AttachmentProxyEmbedding vector(4),
    VoiceEmbedding vector(192),
    -- VisualPreference stored in separate table (UserVisualPreferences)
    -- VoiceEmbedding stored in Tiles table
    
    -- Auxiliary embeddings
    ReceptionEmbedding vector(1536),
    PreferenceEmbedding vector(1536),
    
    -- Pillar scores (8-dim, for fallback)
    PillarScoresJson TEXT,
    
    -- Behavioral fingerprint (16-dim)
    BehavioralLifestyleJson TEXT,  -- JSON array [f1, f2, ..., f16]
    
    -- Full vector JSON (for debugging)
    VectorJson TEXT,
    
    CreatedAt TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(UserId, Version)
);

CREATE INDEX idx_uservectors_userid_latest ON UserVectors (UserId, Version DESC);
```

**Versioning:**
- Each nightly recompute increments `Version`
- Scoring always uses `Version` with max value (latest)
- Old versions retained for 90 days (debugging)

---

## Embedding Quality

**High-quality embeddings:**
- PillarEmbedding (based on structured questions, consistent)
- VisualPreference (learned from binary YES/NO, clean signal)
- VoiceEmbedding (ECAPA-TDNN is robust)

**Lower-quality embeddings:**
- StyleEmbedding (writing style is subtle, harder to capture)
- HumorEmbedding (sparse data, humor is subjective)
- PreferenceEmbedding (depends on user writing ChatNotes)

**Mitigation:**
- Scoring uses `available[]` mask — missing embeddings → component skipped
- Total score normalizes by sum of available weights (stays [0, 100])

---

## Cold Start (New Users)

**Day 1 (onboarding exit):**
- PillarEmbedding ✓ (from foundational questions)
- ExpressionEmbedding ✗ (no tiles yet)
- All others ✗

**Day 2 (after first tile posted):**
- ExpressionEmbedding ✓
- LifestyleEmbedding ✓ (if optional fields filled)
- All others ✗

**Week 1 (after engagement):**
- VisualPreference ✗ (needs ≥10 YES decisions)
- BehavioralLifestyle ✓ (minimal data, low quality)

**Month 1 (mature profile):**
- All core 9 embeddings available
- VisualPreference ✓ (learned from 10+ decisions)
- ReceptionEmbedding ✓ (from tile dwell)

**See [cold-start.md](./cold-start.md) for full strategy.**

---

## Performance

**Embedding generation cost (per user):**
- OpenAI API: ~$0.0001 per embedding (dimension varies by modality; see each section)
- 9 embeddings × 1000 users = $0.90/night
- ECAPA-TDNN: free (self-hosted or cached HTTP endpoint)

**Latency:**
- EmbeddingBatchWorker: 2–4h for 10k users (rate-limited by OpenAI)
- On-demand voice embedding: 200–500ms (ECAPA-TDNN inference)

**Storage:**
- Storage scales with dimension: a 1536-dim float vector is ~6 KB, a 128-dim vector ~512 bytes (pgvector)
- 9 embeddings × 10k users = ~46 MB (negligible)

---

## See Also

- [scoring.md](./scoring.md) — How embeddings feed into 16-component scoring
- [cold-start.md](./cold-start.md) — Embedding availability for new users
- [workers.md](./workers.md) — EmbeddingBatchWorker schedule
