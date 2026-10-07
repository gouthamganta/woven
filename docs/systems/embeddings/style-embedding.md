# Style Embedding

**Dimension:** 128  
**Purpose:** Capture communication and expression style  
**Updated:** After tile posts, chat patterns, weekly

---

## What It Captures

Personal expression style from tiles and chat:
- Writing style (casual, formal, poetic, direct)
- Humor type (sarcastic, witty, wholesome, dry)
- Emotional expression (expressive, reserved, vulnerable, guarded)
- Topic preferences (deep, lighthearted, intellectual, practical)
- Content type (text-heavy, voice-heavy, photo-heavy)
- Authenticity markers (raw vs curated, personal vs surface)

---

## Source Data

**Primary:** Tiles posted to Commons  
**Secondary:** Chat messages (when FindLove reached)  
**Tertiary:** ChatNote overlays

---

## Generation Method

**Hybrid Approach:**

1. **Text Style (64 dims):**
   - Sentence length distribution
   - Vocabulary complexity (word rarity scores)
   - Punctuation patterns (!!! vs. minimal)
   - Emoji usage frequency + type
   - First-person pronoun usage (I/me/my frequency)

2. **Content Style (64 dims):**
   - Topic distribution (from text embeddings)
   - Tone markers (positive, reflective, humorous, serious)
   - Vulnerability indicators (sharing personal struggles)
   - Depth signals (abstract concepts vs concrete details)

**Embedding Model:**
- Custom weighted TF-IDF with style features
- Not OpenAI (too expensive for all tiles)
- Updated via TileEmbeddingService

---

## Similarity Matching

**Formula:**
```
style_similarity = cosine_similarity(user_a.style_emb, user_b.style_emb)
```

**Interpretation:**
- `0.8-1.0`: Very similar communication styles
- `0.6-0.8`: Compatible expression styles
- `0.4-0.6`: Different but complementary
- `<0.4`: May struggle to connect

---

## Usage in ECHO

**Weight in Match Scoring:** Component #12 (base weight: 0.07)

**Why It Matters:**
- Communication compatibility predicts conversation quality
- Similar humor styles → easier connection
- Matched depth preferences → satisfying exchanges
- Not "opposites attract" (data doesn't support it)

---

## Examples

**User A (Casual, Expressive):**
```
"omg had the best weekend!! finally tried that new coffee spot everyone's 
been raving about ☕️ the vibe was immaculate and the barista gave me a 
free pastry because i complimented their tattoo lol. anyone else obsessed 
with finding the perfect third place? 💕"
```

**User B (Formal, Reserved):**
```
"I spent the weekend exploring a new café that recently opened downtown. 
The atmosphere was pleasant and the service was friendly. It's interesting 
how certain spaces can feel welcoming. What makes a good 'third place' 
for you?"
```

**Similarity:** ~0.45 (different styles, may struggle to match pace)

---

## Batch Processing

**Service:** TileEmbeddingService  
**Trigger:** Real-time (Service Bus queue after tile post)  
**Aggregation:** Weekly average of all tiles  
**Fallback:** Uses ChatNote if no tiles posted

---

## Edge Cases

**No Tiles Posted:**
- Embedding = null initially
- Filled from ChatNotes on first match
- Cold-start users rely on other components

**Style Evolution:**
- Embedding is rolling average (last 30 days of content)
- Older tiles fade out over time
- Captures current style, not historical

**Mixed Styles:**
- Some users are versatile (formal in tiles, casual in chat)
- Embedding captures dominant style
- Future: separate tile_style vs chat_style embeddings

---

## Related Documentation

- [Humor Embedding](humor-embedding.md)
- [Emotional Rhythm](emotional-rhythm.md)
- [Embeddings Overview](README.md)
- [ECHO Scoring](../echo/scoring.md)
