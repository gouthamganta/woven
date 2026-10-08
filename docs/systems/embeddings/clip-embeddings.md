# CLIP Embeddings

**System:** Embeddings / Visual Matching  
**Related:** [Photo Embeddings](./photo-embedding.md) | [Visual Preference](./visual-preference.md)

---

## Overview

CLIP (Contrastive Language-Image Pre-training) embeddings encode photos into vector space for visual similarity matching.

---

## What CLIP Captures

**Visual attributes:**
- Style (casual, formal, artistic)
- Setting (outdoor, urban, nature)
- Composition (portrait, full-body, group)
- Aesthetic (colors, lighting, mood)

**Not facial recognition** — CLIP does not identify individuals, only visual patterns.

---

## Usage in Woven

**Photo verification:**
- Compare user photo to reference photo
- Detect if same person (via CLIP similarity)
- Evidence: [PhotoVerificationService.cs](../../../backend/WovenBackend/Services/Trust/PhotoVerificationService.cs)

**Visual preference learning:**
- Track which photos user orbits/engages with
- Build visual preference embedding
- Evidence: [VisualPreferenceService.cs](../../../backend/WovenBackend/Services/Embeddings/VisualPreferenceService.cs)

---

## Model

**Provider:** OpenAI CLIP (via API)  
**Dimensions:** 512 (stored as `vector(512)` in PostgreSQL)

---

## Related

- [Photo Embedding](./photo-embedding.md)
- [Visual Preference](./visual-preference.md)
- [Photo Verification](../trust/photo-verification.md)
