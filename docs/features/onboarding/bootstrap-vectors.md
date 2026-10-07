# Bootstrap Vector Generation

**Last Updated:** 2026-08-17

---

## Overview

When a user completes onboarding (`POST /onboarding/complete`), the backend triggers a **background task** to build the user's initial ECHO matching vector (version 1). This vector is the foundation for all matchmaking algorithms.

**Key principle:** Vector generation is **non-blocking** and **fault-tolerant**. If it fails, onboarding still completes — the user can enter the app, and the vector is retried later.

---

## What Is a User Vector?

A **user vector** is a multi-dimensional representation of a user's personality, preferences, and lifestyle for compatibility scoring. It contains:

1. **Intent metadata** — Extracted from primary intent + reflection sentence
2. **Pillar scores** — 8 numerical values (0.0–1.0) per canonical pillar
3. **Foundational tags** — Keywords extracted from foundational answers
4. **Lifestyle section** — Key-value pairs from optional fields
5. **Pulse features** — Temporal signals (updated separately, not at onboarding)

**Storage:**
- **VectorJson** (TEXT) — Full structured data (JSON)
- **PillarScoresJson** (TEXT) — 8-pillar scores for fast queries
- **PillarEmbedding** (vector(8)) — pgvector column for cosine similarity

---

## Trigger: Onboarding Completion

**File:** `OnboardingEndpoints.cs:1019–1093`

When user submits the final step:

```csharp
app.MapPost("/onboarding/complete", async (
    WovenDbContext db,
    IAnalyticsService analytics,
    ClaimsPrincipal user,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(user);

    // ... validation (profile, photos, intent, foundational, bio) ...

    u.ProfileStatus = ProfileStatus.COMPLETE;
    await db.SaveChangesAsync(ct);

    // ✅ TRIGGER: Build initial user vector (v1) (non-blocking)
    try
    {
        var scopeFactory = http.RequestServices.GetRequiredService<IServiceScopeFactory>();
        var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var vectorBuilder = scope.ServiceProvider
                    .GetRequiredService<WovenBackend.Services.Matchmaking.IUserVectorBuilder>();

                await vectorBuilder.BuildAndSaveV1Async(userId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Onboarding] Failed to build vector for user {UserId}", userId);
            }
        });
    }
    catch
    {
        // Silent fail - vector build is non-critical for onboarding
    }

    return Results.Ok(new { profileStatus = "COMPLETE", nextRoute = "/home" });
});
```

**Key details:**
- **New scope created** — `using var scope = scopeFactory.CreateScope()` ensures scoped services (DbContext) are not captured from request scope (which is disposed after response)
- **Fire-and-forget** — `_ = Task.Run(...)` does not await (user gets 200 OK immediately)
- **Error handling** — Inner try-catch logs failure but doesn't crash app

---

## Vector Builder Service

**File:** `UserVectorBuilder.cs`

### Interface

```csharp
public interface IUserVectorBuilder
{
    Task<int> BuildAndSaveV1Async(int userId, CancellationToken ct = default);
    Task UpdatePulseAsync(int userId, Dictionary<string, string> pulseAnswers, CancellationToken ct = default);
}
```

### Implementation: `BuildAndSaveV1Async`

**High-level flow:**

1. Check if v1 vector already exists (skip if yes)
2. Load user data from DB (profile, intent, optional fields, foundational answers)
3. Call OpenAI to extract metadata and pillar scores
4. Assemble UserVectorDto
5. Save to `user_vectors` table + extract tags to `user_vector_tags`
6. Invalidate Redis cache for pillar embeddings

---

## Step 1: Check Existing Vector

```csharp
var existing = await _db.UserVectors
    .FirstOrDefaultAsync(v => v.UserId == userId && v.Version == 1, ct);

if (existing != null)
{
    _logger.LogInformation("[VectorBuilder] v1 already exists for user {UserId}, skipping", userId);
    return existing.Id;
}
```

**Why check?** Background task may run twice if onboarding completion is retried (e.g., network timeout on first attempt).

---

## Step 2: Load User Data

```csharp
var profile = await _db.UserProfiles.AsNoTracking()
    .FirstOrDefaultAsync(p => p.UserId == userId, ct);

var intent = await _db.UserIntents.AsNoTracking()
    .FirstOrDefaultAsync(i => i.UserId == userId, ct);

var optionalFields = await _db.UserOptionalFields.AsNoTracking()
    .Where(f => f.UserId == userId)
    .ToListAsync(ct);

var foundational = await _db.UserFoundationalQuestionSets.AsNoTracking()
    .Where(f => f.UserId == userId && f.AnsweredAt != null)
    .OrderByDescending(f => f.Version)
    .FirstOrDefaultAsync(ct);
```

**Note:** `AsNoTracking()` is used because data is read-only (no updates). This improves performance (no change tracking overhead).

---

## Step 3: Build Intent Metadata

**Method:** `BuildIntentMetadataAsync`

Calls OpenAI to extract structured metadata from:
- `PrimaryIntent` (e.g., "long_term")
- `ReflectionSentence` (e.g., "Someone who values honesty...")

**OpenAI prompt** (simplified):
```
Extract intent metadata from this user's reflection:

Primary intent: long_term
Reflection: "Someone who values honesty and wants to build something lasting."

Return JSON:
{
  "intentClarity": 0.9,      // How clear is their intent? (0.0–1.0)
  "commitmentLevel": 0.85,   // How committed to this intent? (0.0–1.0)
  "emotionalTone": "hopeful", // warm | cautious | hopeful | pragmatic
  "keywords": ["honesty", "lasting", "build"]
}
```

**Result stored in:**
```json
{
  "Intent": {
    "intentClarity": 0.9,
    "commitmentLevel": 0.85,
    "emotionalTone": "hopeful",
    "keywords": ["honesty", "lasting", "build"]
  }
}
```

---

## Step 4: Compute Pillar Scores

**Method:** `BuildPillarScoresAsync`

Calls OpenAI to analyze foundational answers and assign scores (0.0–1.0) to each of the 8 pillars.

**Input:**
- `QuestionsJson` — 5 questions the user answered
- `AnswersJson` — User's 5 answers

**OpenAI prompt** (simplified):
```
Analyze these answers and score the user on 8 personality pillars (0.0–1.0):

Q1: "When you have a free evening, what do you usually crave doing most?"
A1: "I usually stay home, cook something nice, and read or watch a show. I recharge alone."

Q2: "What kind of connection makes you feel most comfortable with someone new?"
A2: "Someone who listens more than they talk, and doesn't rush things."

... (3 more)

Return JSON:
{
  "Lifestyle": 0.72,       // Homebody, enjoys routine
  "Energy": 0.35,          // Low-key, introverted
  "Values": 0.68,
  "Communication": 0.55,   // Prefers slow, deliberate
  "Ambition": 0.40,
  "Stability": 0.80,       // Values routine, predictability
  "Curiosity": 0.60,
  "Affection": 0.50
}
```

**Result stored in:**
```json
{
  "PillarScores": {
    "Lifestyle": 0.72,
    "Energy": 0.35,
    "Values": 0.68,
    "Communication": 0.55,
    "Ambition": 0.40,
    "Stability": 0.80,
    "Curiosity": 0.60,
    "Affection": 0.50
  }
}
```

---

## Step 5: Extract Foundational Tags

**Method:** `ExtractFoundationalTagsAsync`

Calls OpenAI to extract **keywords per pillar** from answers.

**OpenAI prompt** (simplified):
```
Extract tags for each pillar from these answers:

Q1: "When you have a free evening..."
A1: "I usually stay home, cook something nice, and read or watch a show."

Q2: "What kind of connection..."
A2: "Someone who listens more than they talk, and doesn't rush things."

... (3 more)

Return JSON:
{
  "Lifestyle": ["homebody", "cooking", "reading"],
  "Energy": ["introverted", "recharge-alone"],
  "Communication": ["listening", "slow-paced"],
  "Affection": ["patient", "gentle"],
  ...
}
```

**Result stored in:**
```json
{
  "FoundationalTags": {
    "Lifestyle": ["homebody", "cooking", "reading"],
    "Energy": ["introverted", "recharge-alone"],
    "Communication": ["listening", "slow-paced"],
    "Affection": ["patient", "gentle"]
  }
}
```

---

## Step 6: Build Lifestyle Section

**Method:** `BuildLifestyleSection`

Extracts key-value pairs from `UserOptionalFields` (bio, job, education, etc.).

```csharp
private Dictionary<string, string> BuildLifestyleSection(List<UserOptionalField> optionalFields)
{
    var lifestyle = new Dictionary<string, string>();

    foreach (var field in optionalFields)
    {
        // Include matching-only and public fields
        if (field.Visibility == VisibilityLevel.Public || field.Visibility == VisibilityLevel.MatchingOnly)
        {
            lifestyle[field.Key] = field.Value;
        }
    }

    return lifestyle;
}
```

**Result:**
```json
{
  "Lifestyle": {
    "job": "Software Engineer",
    "education": "bachelors_degree",
    "pets": "dog",
    "habits": "runs 3x/week"
  }
}
```

---

## Step 7: Assemble Vector DTO

```csharp
var dto = new UserVectorDto
{
    UserId = userId,
    Version = 1,
    Intent = intentMetadata,
    PillarScores = pillarScores,
    FoundationalTags = tags,
    Lifestyle = lifestyleDict,
    PulseFeatures = new Dictionary<string, double>()  // Empty at onboarding
};
```

---

## Step 8: Convert to pgvector Embedding

**Pillar scores** (8 floats) are converted to a `Vector` for pgvector cosine similarity searches:

```csharp
var pillarFloats = dto.PillarScores.ToArray()
    .Select(d => (float)d)
    .ToArray();

var vector = new UserVector
{
    UserId = userId,
    Version = 1,
    VectorJson = dto.ToJson(),                     // Full JSON
    PillarScoresJson = dto.PillarScoresToJson(),   // Just pillar scores
    PillarEmbedding = new Vector(pillarFloats),    // pgvector column
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};
```

**PillarEmbedding** is used for fast cosine similarity queries:
```sql
SELECT user_id, 1 - (pillar_embedding <=> '[0.72, 0.35, 0.68, ...]'::vector) AS similarity
FROM user_vectors
WHERE version = 1
ORDER BY pillar_embedding <=> '[0.72, 0.35, 0.68, ...]'::vector
LIMIT 100;
```

---

## Step 9: Save to Database

```csharp
_db.UserVectors.Add(vector);
await _db.SaveChangesAsync(ct);

// Save tags (optional, for fast queries)
await SaveTagsAsync(userId, 1, dto, ct);

// Invalidate stale cache
await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);
```

**SaveTagsAsync** creates rows in `user_vector_tags`:
```sql
INSERT INTO user_vector_tags (user_id, version, pillar, tag)
VALUES
    (123, 1, 'Lifestyle', 'homebody'),
    (123, 1, 'Lifestyle', 'cooking'),
    (123, 1, 'Energy', 'introverted'),
    ...;
```

**Why separate tags table?** Fast filtering for tag-based searches (e.g., "Find all users with tag 'homebody'").

---

## Database Schema

### `user_vectors`

```sql
CREATE TABLE user_vectors (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL REFERENCES users(id),
    version INT NOT NULL,
    
    vector_json TEXT NOT NULL,           -- Full JSON
    pillar_scores_json TEXT NOT NULL,    -- 8-pillar JSON
    pillar_embedding vector(8),          -- pgvector column
    
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    
    UNIQUE(user_id, version)
);

CREATE INDEX idx_user_vectors_pillar_embedding
ON user_vectors USING ivfflat (pillar_embedding vector_cosine_ops);
```

### `user_vector_tags`

```sql
CREATE TABLE user_vector_tags (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL,
    version INT NOT NULL,
    pillar VARCHAR(50) NOT NULL,
    tag VARCHAR(100) NOT NULL,
    
    FOREIGN KEY (user_id, version) REFERENCES user_vectors(user_id, version),
    
    INDEX idx_vector_tags_pillar_tag (pillar, tag)
);
```

---

## Example: Complete Vector

After bootstrap, the `user_vectors` row looks like:

```json
{
  "UserId": 123,
  "Version": 1,
  "Intent": {
    "intentClarity": 0.9,
    "commitmentLevel": 0.85,
    "emotionalTone": "hopeful",
    "keywords": ["honesty", "lasting", "build"]
  },
  "PillarScores": {
    "Lifestyle": 0.72,
    "Energy": 0.35,
    "Values": 0.68,
    "Communication": 0.55,
    "Ambition": 0.40,
    "Stability": 0.80,
    "Curiosity": 0.60,
    "Affection": 0.50
  },
  "FoundationalTags": {
    "Lifestyle": ["homebody", "cooking", "reading"],
    "Energy": ["introverted", "recharge-alone"],
    "Communication": ["listening", "slow-paced"],
    "Affection": ["patient", "gentle"],
    "Stability": ["routine", "predictable"],
    "Values": ["honesty", "family"],
    "Curiosity": ["learning", "books"],
    "Ambition": ["steady", "balanced"]
  },
  "Lifestyle": {
    "job": "Software Engineer",
    "education": "bachelors_degree",
    "pets": "dog"
  },
  "PulseFeatures": {}
}
```

---

## Error Handling

### Scenario 1: OpenAI Call Fails

**Cause:** API timeout, rate limit, invalid response

**Behavior:**
1. `BuildAndSaveV1Async` throws exception
2. Caught by outer try-catch in `Task.Run`
3. Logged: `[Onboarding] Failed to build vector for user 123`
4. User is **not blocked** — they enter app without a vector

**Recovery:**
- Background worker (`VectorBootstrapWorker`) retries all users with `ProfileStatus.COMPLETE` but no v1 vector
- Runs every 6 hours
- Retries up to 3 times before alerting

---

### Scenario 2: Database Save Fails

**Cause:** DB deadlock, connection timeout, constraint violation

**Behavior:**
1. `SaveChangesAsync` throws exception
2. Caught by outer try-catch
3. Logged + alerted (Sentry)
4. User is not blocked

**Recovery:**
- Same as above (background worker retries)

---

### Scenario 3: Duplicate Vector (Race Condition)

**Cause:** User clicks "Complete" button twice (double-click)

**Behavior:**
1. First task starts
2. Second task starts (no existing vector yet)
3. First task saves vector
4. Second task tries to save → **UNIQUE constraint violation**
5. Second task catches exception, logs, exits

**Protection:** `UNIQUE(user_id, version)` constraint on `user_vectors` table

---

## Performance

**Typical timeline:**
1. User clicks "This is me — let's go" → **0ms**
2. `POST /onboarding/complete` returns 200 OK → **~150ms** (DB write only)
3. Background task starts → **+10ms**
4. OpenAI intent metadata → **+800ms**
5. OpenAI pillar scores → **+1200ms**
6. OpenAI tags → **+900ms**
7. DB save (vector + tags) → **+50ms**
8. **Total:** ~3 seconds (but user already navigated to `/onboarding/start`)

**Bottleneck:** OpenAI API calls (3 sequential requests). Future optimization: parallelize.

---

## Future Enhancements

1. **Parallelize OpenAI calls** — Run intent, pillar, tag extraction concurrently (reduce 3s → 1.2s)
2. **Rich embeddings** — Use `text-embedding-3-small` (1536-dim) for foundational answers instead of 8-dim pillar scores
3. **Voice analysis** — Extract tone/emotion from voice-recorded answers
4. **Image embeddings** — Embed profile photos for visual preference matching
5. **Incremental updates** — Update vector when user edits foundational answers (v2, v3, ...)

---

## Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| No vector after onboarding | Background task failed silently | Check logs for `[VectorBuilder]` errors |
| Pillar scores all 0.0 | OpenAI returned invalid JSON | Add JSON schema validation |
| Vector exists but tags missing | `SaveTagsAsync` failed | Re-run tag extraction job |
| Duplicate vector error | User double-clicked Complete button | Already handled by UNIQUE constraint |
| Stale cache after update | Cache not invalidated | Ensure `_cache.DeleteAsync` is called |

---

## Monitoring

**Key metrics:**
- **Bootstrap success rate** — % of users who get v1 vector within 1 hour of onboarding
- **Average build time** — Median time from completion to vector save
- **OpenAI failures** — Count of API errors per day
- **Retry count** — How many vectors needed >1 attempt

**Alerts:**
- Bootstrap success rate <95% (investigate OpenAI issues)
- Average build time >5 seconds (scale OpenAI quota or parallelize)
