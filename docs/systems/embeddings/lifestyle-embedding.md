# Lifestyle Embedding

**Dimension:** 64  
**Purpose:** Capture lifestyle compatibility from onboarding choices  
**Updated:** After onboarding, on lifestyle change

---

## What It Captures

Lifestyle preferences and patterns:
- Work schedule (9-5, flexible, shift work, remote)
- Social energy (introvert/extrovert spectrum)
- Activity preferences (indoor/outdoor, active/relaxed)
- Living situation (alone, roommates, family)
- Pet ownership (dogs, cats, none, allergic)
- Dietary preferences (vegan, vegetarian, omnivore, specific restrictions)
- Alcohol/substance use (never, socially, regularly)
- Fitness routine (daily, weekly, rarely, never)

---

## Source Data

**Primary:** Onboarding lifestyle questions  
**Secondary:** Tile content (activity mentions, venue check-ins)  
**Tertiary:** Chat patterns (availability times, weekend vs weekday activity)

---

## Embedding Generation

**Method:** Weighted average of categorical encodings

Example vector components:
- `[0-15]`: Work schedule encoding
- `[16-31]`: Social energy + activity preferences
- `[32-47]`: Living situation + pets + diet
- `[48-63]`: Substance use + fitness routine

**Normalization:** L2 normalized (unit vector)

---

## Similarity Matching

**Formula:**
```
lifestyle_similarity = cosine_similarity(user_a.lifestyle_emb, user_b.lifestyle_emb)
```

**Interpretation:**
- `0.9-1.0`: Highly aligned lifestyles
- `0.7-0.9`: Compatible lifestyles
- `0.5-0.7`: Some differences, workable
- `<0.5`: Significant lifestyle mismatch

---

## Usage in ECHO

**Weight in Match Scoring:** Component #9 (base weight: 0.08)

**When It Matters Most:**
- Early-stage matching (before behavioral signals)
- Cold start users (no chat history)
- Filter mismatches (pet allergies, substance incompatibility)

---

## Edge Cases

**Incomplete Data:**
- Missing fields = neutral encoding (0.5)
- Partial lifestyle answers still generate embedding
- Minimum 5/10 questions required

**Updates:**
- User can update lifestyle in settings
- Embedding regenerated on save
- Match scores recalculated overnight (CfScoreBatchWorker)

---

## Related Documentation

- [Behavioral Fingerprint](behavioral-fingerprint.md)
- [Embeddings Overview](README.md)
- [ECHO Scoring](../echo/scoring.md)
