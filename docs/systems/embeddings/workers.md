# Embedding Workers

**Last Updated:** 2026-10-07

---

## Overview

Two workers handle embedding generation:

1. **EmbeddingBatchWorker** (scheduled, nightly)
2. **TileEmbeddingService** (real-time, Service Bus queue)

---

## EmbeddingBatchWorker

Nightly batch job to generate embeddings for users missing them.

**Schedule:** Daily 02:30 UTC  
**Duration:** ~25 minutes for 300 users  
**Lock:** Redis distributed lock (4-hour expiry)

### What It Processes

**Eligible Users:**
```sql
SELECT u.Id 
FROM users u
WHERE u.profile_status = 'COMPLETE'
  AND (
    u.behavioral_fingerprint IS NULL OR
    u.lifestyle_embedding IS NULL OR
    u.photo_embedding IS NULL OR
    u.voice_embedding IS NULL
  )
LIMIT 300;
```

**Processing Per User:**
1. **Behavioral Fingerprint** (if missing):
   - Aggregate onboarding answers
   - 16-dim vector from 8 pillars × 2 features each
   - Immediate generation (no API call)

2. **Lifestyle Embedding** (if missing):
   - Encode lifestyle choices
   - 64-dim categorical encoding
   - Immediate generation

3. **Photo Embedding** (if missing):
   - Call OpenAI CLIP API
   - 512-dim visual style vector
   - ~200ms per photo

4. **Voice Embedding** (if missing):
   - Call OpenAI Whisper API (transcription)
   - Extract prosody features (pitch, pace, tone)
   - 128-dim combined embedding
   - ~500ms per voice note

### Performance

**Single User:**
- No API calls: ~10ms
- With photo only: ~210ms
- With voice only: ~510ms
- With both: ~720ms

**Batch (300 users, 50% with photos/voice):**
- Total: ~25 minutes
- API cost: ~$15/month (300 users/day × $0.001/embedding × 30 days)

### Error Handling

**OpenAI API Failure:**
- Log error with correlation ID
- Skip that embedding (try again next night)
- Continue with other users

**Rate Limit (429):**
- Exponential backoff: 1s, 4s, 12s
- Max 3 retries per user
- Skip user after retries exhausted

**Database Deadlock:**
- Retry transaction (max 3 attempts)
- Skip user if still failing

---

## TileEmbeddingService

Real-time embedding generation for tile posts.

**Trigger:** Azure Service Bus message after tile post  
**Queue:** `tile-embeddings-queue`  
**Concurrency:** 1 processor (sequential processing)

### Flow

1. User posts tile → `POST /tiles`
2. Backend saves tile to DB
3. Backend sends message to Service Bus:
   ```json
   {
     "tileId": 123,
     "userId": 42,
     "content": "text of tile",
     "hasVoice": false
   }
   ```
4. TileEmbeddingService receives message
5. Generates style embedding from tile content
6. Updates user's style_embedding (rolling average)
7. Completes message (ACK)

### Style Embedding Generation

**Text-Only Tiles:**
- TF-IDF + style features
- 128-dim vector
- ~50ms processing

**Voice Tiles:**
- Transcribe with Whisper
- Extract style from transcript + prosody
- ~600ms processing

### Aggregation

**Rolling Average:**
```csharp
new_style_emb = (
    old_style_emb * (tile_count - 1) + tile_style_emb
) / tile_count;
```

**Decay:**
- Tiles older than 30 days excluded
- Recalculated weekly (EmbeddingBatchWorker)

### Error Handling

**Poison Messages:**
- Max delivery count: 3
- Dead letter queue after 3 failures
- Manual review required

**Missing Tile:**
- Tile deleted before processing
- Silent skip (log warning)

---

## ChatNoteEmbeddingWorker

Generates preference embeddings from ChatNotes.

**Status:** Stub exists, not yet wired  
**Planned Schedule:** Daily 03:30 UTC  
**Purpose:** Extract depth/intimacy preferences from ChatNotes

---

## Monitoring

**Metrics:**
- Embeddings generated per night
- API call latency (p50, p95, p99)
- API cost per day
- Error rate (by error type)
- Queue depth (tile embeddings)
- Dead letter queue count

**Alerts:**
- EmbeddingBatchWorker runtime > 45 minutes
- API error rate > 5%
- Queue depth > 100
- Dead letter count > 10

---

## Cost Analysis

**Monthly Costs (1,000 active users):**
- Photo embeddings: 30 new users/day × $0.001 × 30 = $0.90
- Voice embeddings: 20 voice notes/day × $0.002 × 30 = $1.20
- Tile style: 100 tiles/day × $0.0005 × 30 = $1.50
- **Total:** ~$4/month

**At Scale (10,000 users):**
- ~$40/month

---

## Related Documentation

- [Behavioral Fingerprint](behavioral-fingerprint.md)
- [Lifestyle Embedding](lifestyle-embedding.md)
- [Photo Embedding](photo-embedding.md)
- [Voice Embedding](voice-embedding.md)
- [Style Embedding](style-embedding.md)
- [Configuration](configuration.md)
