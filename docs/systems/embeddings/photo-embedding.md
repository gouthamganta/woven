# Photo Embedding

**Dimension:** 512 (CLIP model)  
**Purpose:** Capture visual style and aesthetic preferences  
**Updated:** When photos added/changed, nightly batch

---

## What It Captures

Visual style signals from profile photos:
- Aesthetic style (natural, polished, artistic, casual)
- Setting preferences (outdoor, urban, home, events)
- Presentation style (close-up, full-body, group, solo)
- Color palette (warm, cool, vibrant, muted)
- Authenticity signals (candid vs posed, filtered vs raw)

---

## Model

**Model:** OpenAI CLIP (ViT-B/32)  
**Endpoint:** OpenAI Embeddings API  
**Input:** User's primary profile photo  
**Output:** 512-dimensional vector

---

## Processing Pipeline

1. **Photo Selection:**
   - Primary photo if set
   - First uploaded photo if no primary
   - Skip if no photos

2. **Preprocessing:**
   - Resize to 224x224 (CLIP input size)
   - Center crop if aspect ratio mismatch
   - RGB conversion (no grayscale)

3. **Embedding Generation:**
   ```
   POST https://api.openai.com/v1/embeddings
   {
     "model": "clip-vit-base-patch32",
     "input": "<base64 image>"
   }
   ```

4. **Storage:**
   - Saved to `User.PhotoEmbedding` (vector column)
   - L2 normalized

---

## Similarity Matching

**Formula:**
```
photo_similarity = cosine_similarity(user_a.photo_emb, user_b.photo_emb)
```

**Interpretation:**
- `0.85-1.0`: Very similar visual style
- `0.7-0.85`: Compatible aesthetics
- `0.5-0.7`: Different but not incompatible
- `<0.5`: Divergent visual preferences

---

## Usage in ECHO

**Weight in Match Scoring:** Component #11 (base weight: 0.06)

**Purpose:**
- Not attractiveness matching
- Style compatibility (minimalist ↔ vibrant, candid ↔ polished)
- Aesthetic alignment indicator

**Why It Works:**
- People with similar photo styles often share values
- Visual aesthetics correlate with personality traits
- Not face-matching (that would be shallow)

---

## Privacy

**What We Do:**
- Embed style, not identity
- No face recognition
- No attractiveness scoring
- Embeddings are style vectors, not faces

**What We Don't Store:**
- Face landmarks
- Attractiveness scores
- Gender/age/race predictions

---

## Batch Processing

**Worker:** EmbeddingBatchWorker  
**Schedule:** Daily 02:30 UTC  
**Scope:** Users with photos but no `PhotoEmbedding`  
**Cost:** ~$0.001 per image (CLIP API)

---

## Edge Cases

**No Photos:**
- Embedding = null
- Component weight = 0 in scoring
- User still gets matches (other components compensate)

**Multiple Photos:**
- Only primary photo used for embedding
- Future: average of all photos

**Photo Updates:**
- Embedding regenerated next batch run
- Match scores recalculated overnight

---

## Related Documentation

- [Visual Preference](visual-preference.md)
- [Style Embedding](style-embedding.md)
- [Embeddings Overview](README.md)
- [ECHO Scoring](../echo/scoring.md)
