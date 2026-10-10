# Feed Algorithm — Content Ranking

**Path:** `docs/features/commons/feed-algorithm.md`  
**Last Updated:** 2026-10-07  
**Feature:** Commons

---

## Overview

The Commons feed is a **personalized discovery surface** that balances resonance (similarity to viewer's taste) with serendipity (novel but behaviorally adjacent content). Unlike traditional engagement-optimized feeds, it prioritizes **multi-dimensional alignment** over pure recency or popularity.

**Service:** `CommonsFeedService.cs`  
**Schedule:** On-demand per request (Redis cached 2h per session)  
**Pool size:** 200 tiles (scored), 20 tiles per page

---

## Scoring Formula

Every feed request scores up to **600 eligible tiles** (3× over-fetch) using a 5-component similarity vector, then prunes to 200 final tiles.

### 5-Component Similarity (Renormalized)

Each component contributes to a weighted similarity score. If a component is unavailable (e.g., missing embedding), weights are **renormalized** across available components only.

| Component | Weight | Input A (Viewer) | Input B (Tile/Owner) | Metric |
|---|---|---|---|---|
| **1. Pillar Scores** | 0.28 | Viewer's 8-pillar values | Tile owner's 8-pillar values | Float cosine similarity |
| **2. Reception Embedding** | 0.32 | Viewer's dwell behavior embedding | Tile's content embedding | Pgvector cosine |
| **3. Expression Embedding** | 0.18 | Viewer's posting style embedding | Tile owner's posting style | Pgvector cosine |
| **4. Preference Embedding** | 0.12 | Viewer's ChatNote preferences | Tile's content embedding | Pgvector cosine |
| **5. Intent Tags** | 0.10 | Viewer's intent tags | Tile owner's intent tags | Jaccard similarity |

**Pillar Scores (8 dimensions):**
```
Lifestyle | Energy | Values | Communication | Ambition | Stability | Curiosity | Affection
```

**Why these weights?**
- **Reception (0.32)** — strongest signal. What you dwell on reveals true taste.
- **Pillar (0.28)** — values alignment is foundational compatibility.
- **Expression (0.18)** — creative wavelength matters, but less than what you consume.
- **Preference (0.12)** — stated attraction patterns from ChatNotes (weak signal, often aspirational vs. revealed).
- **Intent (0.10)** — deliberately low to prevent demographic silos (gender-correlated tags).

### Final Score

```csharp
var combinedScore = similarity × 0.60 
                  + recencyScore × 0.25 
                  + cfScore × 0.15;
```

**Recency Score:**  
Exponential decay by tile age: `exp(-ageHours / 24.0)`

**CF Score (Collaborative Filtering):**  
Jaccard similarity from shared orbit/dwell patterns (0-1 normalized). Falls back to 0.5 if no CF data exists.

---

## Bucketing & Interleaving

### Resonant Bucket
**Threshold:** `similarity >= 0.65`  
**Sort:** By `combinedScore` descending  
**Composition:** 70% of feed

High-similarity tiles that align with viewer's taste across multiple dimensions. This is the **comfort zone** — familiar wavelengths, low cognitive friction.

### Discovery Bucket
**Threshold:** `similarity < 0.65`  
**Sort:** By `cfScore × 0.5 + recencyScore × 0.5` descending  
**Composition:** 30% of feed

Lower-similarity tiles ranked by **CF affinity + recency**, not recency-only. This surfaces tiles from behaviorally similar users (high CF score) even when pillar/embedding distance is large.

**Why CF-weighted discovery?**  
Pure recency produces random noise. CF-weighted discovery finds **adjacent taste clusters** — people who orbit/dwell similarly but have different stated preferences or pillar scores.

### Interleaving Logic

```csharp
var result = new List<CommonsFeedTile>(200);
int ri = 0, di = 0, resonantAdded = 0;

while (result.Count < 200 && (ri < resonant.Count || di < discovery.Count))
{
    int resonantTarget = (int)((result.Count + 1) * 0.70);
    if (ri < resonant.Count && resonantAdded < resonantTarget)
    {
        result.Add(resonant[ri++]);
        resonantAdded++;
    }
    else if (di < discovery.Count)
        result.Add(discovery[di++]);
    else if (ri < resonant.Count)
    {
        result.Add(resonant[ri++]);
        resonantAdded++;
    }
}
```

**Output:** 200-tile pool, 70/30 interleaved, served in 20-tile pages.

---

## Eligibility Filters

Tiles appear in feed only if they pass **all** filters:

```csharp
.Where(t =>
    t.IsModerated &&           // approved by ModerationService
    !t.IsExpired &&            // not past 48h TTL
    t.UserId != userId &&      // not viewer's own tile
    !blockedIds.Contains(t.UserId) &&  // not from blocked user
    !viewedTileIds.Contains(t.Id))     // not viewed today
```

**Moderation:** Only approved tiles appear (`is_moderated = true`). See **[moderation.md](./moderation.md)**.

**Block enforcement:** Bidirectional. If A blocks B or B blocks A, neither sees the other's tiles.

**Viewed-today filter:** Prevents duplicate tile views within same UTC day. Tracked in `tile_views` table + Redis cache.

---

## Vector Inputs

### Viewer Vectors
**Source:** `user_vectors` table (latest version by `Version` desc)

```csharp
var viewerVector = await _db.UserVectors
    .Where(v => v.UserId == userId)
    .OrderByDescending(v => v.Version)
    .Select(v => new {
        v.PillarScoresJson,    // {"Lifestyle": 0.72, "Energy": 0.88, ...}
        v.VectorJson,          // {"intent": {"tags": ["art", "travel"]}}
        v.ReceptionEmbedding,  // 1536-dim (what viewer dwells on)
        v.ExpressionEmbedding, // 1536-dim (what viewer posts)
        v.PreferenceEmbedding  // 1536-dim (ChatNote preferences)
    })
    .FirstOrDefaultAsync(ct);
```

**Fallback:** Missing vectors → component weight = 0 (renormalized away).

### Tile Owner Vectors
**Batch-loaded per feed request:**

```csharp
var ownerIds = tiles.Select(t => t.UserId).Distinct().ToList();
var ownerVectors = await _db.UserVectors
    .Where(v => ownerIds.Contains(v.UserId))
    .GroupBy(v => v.UserId)
    .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Version).First());
```

**Why batch?** Single query vs N+1. Critical for 600-tile over-fetch.

---

## Caching Strategy

### Redis Feed Cache
**Key:** `commons:feed:{userId}:{sessionId[0..8]}`  
**TTL:** 2 hours  
**Scope:** Entire 200-tile pool (shared across all pages)

**Why session-scoped?**  
Frontend generates a `sessionId` (Guid) per session. Same sessionId = same cached pool = consistent pagination. Refresh button clears sessionStorage → new sessionId → cache miss → recomputed feed.

**Cache hit:**  
Returns page slice immediately (no DB query).

**Cache miss:**  
Computes full 200-tile pool, stores in Redis, returns page 1.

### Energy Gate (View Cap)
**Redis key:** `commons:energy:{userId}:{date_utc}`  
**TTL:** Until midnight UTC  
**Limit:** 100 tiles per day

```csharp
var redisKey = $"commons:energy:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var count = await _cache.IncrementAsync(redisKey, TimeSpan.FromSeconds(ttl));
if (count > 100) return Results.StatusCode(429); // ENERGY_DEPLETED
```

**Write-through to DB:**  
Every view increments `user_energy_meter.tiles_viewed` (upserted daily).

**Why cap at 100?**  
Prevents infinite scroll addiction. Encourages intentional browsing. Resets at UTC midnight (global boundary, simpler than per-user timezone).

---

## Performance Notes

### Over-Fetch Pruning
**Over-fetch:** 600 tiles  
**Score:** All 600 tiles  
**Prune:** Top 200 by `combinedScore`  
**Serve:** 20 per page

**Why 3× over-fetch?**  
Ensures sufficient resonant bucket depth even when filters (blocked, viewed-today) remove many tiles. 200 final tiles = 10 full pages.

### Vector Batch Loading
All owner vectors loaded in **1 query** via `IN` clause:

```csharp
.Where(v => ownerIds.Contains(v.UserId))
```

**Optimization:** EF translates to `WHERE user_id = ANY(@p0)` (PostgreSQL array param). O(1) round-trip vs O(N) individual queries.

### Embedding Cosine (Pgvector)
**Function:** `CosineSimilarity(Vector a, Vector b)`  
**Implementation:** Manual dot-product loop (EF Core cannot translate Pgvector ops to C#)

```csharp
private static double CosineSimilarity(Vector a, Vector b)
{
    var aSpan = a.Memory.Span;
    var bSpan = b.Memory.Span;
    double dot = 0, normA = 0, normB = 0;
    for (int i = 0; i < aSpan.Length && i < bSpan.Length; i++)
    {
        dot += aSpan[i] * bSpan[i];
        normA += aSpan[i] * aSpan[i];
        normB += bSpan[i] * bSpan[i];
    }
    return (normA == 0 || normB == 0) ? 0.0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
}
```

**Why in-memory?**  
DB-level vector distance ops (`<=>`) don't help here — we're scoring a fixed pool of 600 tiles (already fetched), not nearest-neighbor search over millions.

---

## Design Rationale

### Why 70/30 Resonant/Discovery Split?
**Pure resonant (100/0):** Echo chamber. Viewer only sees pillar-similar content.  
**Pure discovery (0/100):** Random noise. No alignment to taste.  
**70/30:** Optimal balance. Viewer gets familiar content (low cognitive load) + 30% serendipity.

### Why Intent Tags Capped at 0.10?
Intent tags correlate with demographics (e.g., "travel" skews higher income, "family" skews older). Letting intent dominate creates **demographic silos** rather than behavioral alignment. Capping at 0.10 keeps them as a soft signal.

### Why Reception > Pillar?
**Pillar scores** = onboarding self-report (stated values).  
**Reception embedding** = dwell behavior (revealed taste).  
Revealed preference is stronger predictor of resonance than stated preference.

### Why CF Score in Discovery Bucket Only?
**Resonant bucket:** Already high similarity across 5 components. CF is redundant.  
**Discovery bucket:** Low multi-dimensional similarity, but CF finds behaviorally adjacent users. This is where CF adds unique value.

---

## Signal Flow

```
User opens /commons
 ↓
[Check Redis cache: commons:feed:{userId}:{sessionId}]
 ↓ (cache miss)
[Fetch 600 eligible tiles: moderated, not expired, not blocked, not viewed]
 ↓
[Batch-load viewer vectors + owner vectors]
 ↓
[Score all 600 tiles: 5-component similarity + recency + CF]
 ↓
[Bucket: resonant (≥0.65 sim) vs discovery (<0.65 sim)]
 ↓
[Interleave 70/30 → 200-tile pool]
 ↓
[Cache pool in Redis (2h TTL)]
 ↓
[Return page 1 (first 20 tiles)]
```

**Subsequent pages:**  
Redis cache hit → slice pool by offset → return.

---

## Related Entities

### UserVector
**Table:** `user_vectors`  
**Fields:**
- `pillar_scores_json` — 8 float values (Lifestyle, Energy, ...)
- `vector_json` — intent tags + metadata
- `reception_embedding` — 1536-dim (dwell-based taste)
- `expression_embedding` — 1536-dim (posting style)
- `preference_embedding` — 1536-dim (ChatNote preferences)

### CfScore
**Table:** `cf_scores`  
**Fields:**
- `user_id`, `candidate_id`, `score` (Jaccard similarity from orbits/dwell)
- Populated by `CfScoreBatchWorker` (daily 05:00 UTC)

### TileView
**Table:** `tile_views`  
**Fields:**
- `user_id`, `tile_id`, `viewed_at`, `duration_ms`
- Tracks viewed-today filter + dwell signals (≥8000ms → `TileDwell` signal)

---

## Next Steps

- **Preference embedding from ChatNotes** — Worker stub exists, not wired. Would improve component #4 accuracy.
- **Voice embedding matching** — `VoiceEmbedding` field exists on `Tile`, not used in scoring yet.
- **Adaptive weights** — Learn component weights per user (some users dwell-driven, others pillar-driven).

---

## Related Docs

- **[README.md](./README.md)** — Commons overview
- **[tiles.md](./tiles.md)** — Tile entity and lifecycle
- **[orbit.md](./orbit.md)** — Orbit gravity mechanics
- **[moderation.md](./moderation.md)** — Content moderation
- **[backend.md](./backend.md)** — CommonsFeedService implementation
- **[api.md](./api.md)** — Full endpoint specs
