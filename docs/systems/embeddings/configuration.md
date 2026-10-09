# Embeddings Configuration

**Last Updated:** 2026-10-07

---

## appsettings.json

```json
{
  "Embeddings": {
    "Enabled": true,
    "Provider": "OpenAI",
    "Models": {
      "Photo": "clip-vit-base-patch32",
      "Voice": "whisper-1",
      "Text": "text-embedding-3-small"
    },
    "Batch": {
      "Schedule": "0 30 2 * * *",
      "MaxUsersPerRun": 300,
      "ParallelDegree": 1
    },
    "RealTime": {
      "TileQueue": "tile-embeddings-queue",
      "MaxConcurrency": 1
    }
  }
}
```

---

## Configuration Options

### Embeddings:Enabled

**Type:** `bool`  
**Default:** `true`  
**Purpose:** Master switch for all embedding generation

**When false:**
- EmbeddingBatchWorker skips execution
- TileEmbeddingService skips queue processing
- Match scoring uses only non-embedding components

---

### Embeddings:Provider

**Type:** `string`  
**Default:** `"OpenAI"`  
**Options:** `"OpenAI"` | `"Local"` (planned)

**OpenAI:** Production, uses OpenAI API  
**Local:** Development, uses local models (not implemented)

---

### Embeddings:Models

**Photo Model:**
- **Current:** `clip-vit-base-patch32`
- **Dimension:** 512
- **Cost:** $0.001 per image

**Voice Model:**
- **Current:** `whisper-1` (transcription)
- **Dimension:** 128 (prosody features)
- **Cost:** $0.002 per voice note

**Text Model:**
- **Current:** `text-embedding-3-small`
- **Dimension:** 1536 (not used yet, reserved for future)
- **Cost:** $0.0001 per 1K tokens

---

### Embeddings:Batch

**Schedule:**
- **Format:** Cron expression
- **Default:** `"0 30 2 * * *"` (02:30 UTC daily)
- **Meaning:** Every day at 2:30 AM UTC

**MaxUsersPerRun:**
- **Default:** 300
- **Purpose:** Limit API cost per night
- **Range:** 100-1000

**ParallelDegree:**
- **Default:** 1 (sequential)
- **Purpose:** Control OpenAI API rate limit
- **Range:** 1-10

---

### Embeddings:RealTime

**TileQueue:**
- **Default:** `"tile-embeddings-queue"`
- **Purpose:** Azure Service Bus queue name

**MaxConcurrency:**
- **Default:** 1
- **Purpose:** Limit parallel tile processing
- **Range:** 1-10

---

## Environment Variables

**Production (Azure Key Vault):**
```
OPENAI_API_KEY=<secret>
AZURE_SERVICE_BUS_CONNECTION=<secret>
```

**Local (User Secrets):**
```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "ServiceBus:ConnectionString" "Endpoint=sb://..."
```

---

## Feature Flags

**Per-Embedding Toggle:**
```json
{
  "Embeddings": {
    "Components": {
      "BehavioralFingerprint": true,
      "Lifestyle": true,
      "Photo": true,
      "Voice": true,
      "Style": true,
      "Humor": false,
      "EmotionalRhythm": false
    }
  }
}
```

**Note:** Humor and EmotionalRhythm are planned, not yet implemented.

---

## Database Schema

**Embedding Columns on User Table:**
```sql
ALTER TABLE users
ADD COLUMN behavioral_fingerprint vector(16),
ADD COLUMN lifestyle_embedding vector(64),
ADD COLUMN photo_embedding vector(512),
ADD COLUMN voice_embedding vector(128),
ADD COLUMN style_embedding vector(128);
```

**Indexes:**
```sql
CREATE INDEX CONCURRENTLY idx_users_behavioral_fingerprint_ivfflat
ON users USING ivfflat (behavioral_fingerprint vector_cosine_ops)
WITH (lists = 100);

CREATE INDEX CONCURRENTLY idx_users_lifestyle_embedding_ivfflat
ON users USING ivfflat (lifestyle_embedding vector_cosine_ops)
WITH (lists = 100);

CREATE INDEX CONCURRENTLY idx_users_photo_embedding_ivfflat
ON users USING ivfflat (photo_embedding vector(512) vector_cosine_ops)
WITH (lists = 100);
```

**Note:** pgvector extension required.

---

## Cost Controls

**Daily Cost Cap:**
```json
{
  "Embeddings": {
    "CostControls": {
      "MaxDailySpend": 5.00,
      "AlertThreshold": 4.00
    }
  }
}
```

**Monitoring:**
- Track API calls per day
- Calculate cost based on model pricing
- Alert when approaching limit
- Stop batch worker if limit exceeded

---

## Testing Configuration

**appsettings.Development.json:**
```json
{
  "Embeddings": {
    "Enabled": false,
    "Batch": {
      "Schedule": "0 0 0 31 2 *",
      "MaxUsersPerRun": 10
    }
  }
}
```

**Disabled in development:**
- Prevents accidental API costs
- Tests use mocked IEmbeddingService

---

## Related Documentation

- [Embeddings Overview](README.md)
- [Workers](workers.md)
- [ECHO Scoring](../echo/scoring.md)
- [Queue System](../queue/README.md)
