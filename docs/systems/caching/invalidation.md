# Cache Invalidation Strategies

**Last Updated:** 2026-10-07

---

## Overview

Cache invalidation is one of the hardest problems in computer science. Woven uses **three complementary strategies**:

1. **Time-based expiry (TTL)** — Predictable, automatic
2. **Event-driven invalidation** — Immediate consistency on writes
3. **Version-based invalidation** — Session/request-scoped cache busting

This document covers **when to use each strategy**, **how to implement**, and **common pitfalls**.

---

## Strategy 1: Time-Based Expiry (TTL)

**Use when:**
- Data refreshes on a predictable schedule
- Stale data is acceptable for short windows
- No user action directly changes cached data

**Examples:**
- Daily decks (expire at midnight)
- Trending tiles (refresh every 5 minutes)
- User embeddings (recompute nightly)

### Implementation

```csharp
var key = CacheKeys.DailyDeck(userId, DateOnly.FromDateTime(DateTime.UtcNow));
var deck = await _cache.GetAsync<DailyDeck>(key, ct);

if (deck == null)
{
    deck = await _deckOrchestrator.BuildDeckAsync(userId, ct);
    await _cache.SetAsync(key, deck, CacheTtl.UntilMidnightUtc(), ct);
}

return deck;
```

**Key point:** TTL is set **once** on write. No manual invalidation needed.

---

### TTL Patterns

#### Until Midnight UTC

**Purpose:** Daily counters, daily decks, anything that resets at midnight.

**Helper:**
```csharp
public static TimeSpan UntilMidnightUtc()
{
    var now     = DateTime.UtcNow;
    var midnight = now.Date.AddDays(1);
    var ttl     = midnight - now;
    return ttl > TimeSpan.Zero ? ttl : TimeSpan.FromSeconds(1);
}
```

**Usage:**
```csharp
await _cache.SetAsync(
    CacheKeys.SparkCounter(userId, DateOnly.FromDateTime(DateTime.UtcNow)),
    count,
    CacheTtl.UntilMidnightUtc(),
    ct
);
```

**Edge case:** If called exactly at midnight, returns 1 second TTL (Redis rejects TTL = 0).

---

#### Fixed Duration

**Purpose:** Session state, hot tiles, rate limits.

**Examples:**
```csharp
public static readonly TimeSpan Session        = TimeSpan.FromHours(1);
public static readonly TimeSpan HotTile        = TimeSpan.FromMinutes(5);
public static readonly TimeSpan CommonsFeed    = TimeSpan.FromHours(1);
public static readonly TimeSpan EmbeddingLookup = TimeSpan.FromDays(1);
```

**Usage:**
```csharp
await _cache.SetAsync(CacheKeys.UserSession(userId), session, CacheTtl.Session, ct);
```

---

#### No Expiry (Persistent Cache)

**Purpose:** Reference data that never changes (rare).

**Example:** Worker timestamps (`connection-score-batch:last-run`)

**Usage:**
```csharp
// TTL = null means no expiry
await _cache.SetAsync("worker:last-run", timestamp, ttl: null, ct);
```

**Warning:** No-expiry keys must be **manually managed** or they leak memory.

---

### Pros & Cons

✅ **Pros:**
- Simple (no invalidation logic)
- Automatic cleanup (Redis evicts expired keys)
- Predictable behavior (refresh at known intervals)

❌ **Cons:**
- Stale data window (up to TTL duration)
- No immediate consistency on writes
- Overly aggressive TTL wastes CPU (constant recomputation)

---

## Strategy 2: Event-Driven Invalidation

**Use when:**
- User action changes cached data
- Immediate consistency required
- Staleness is user-visible (e.g., profile updates)

**Examples:**
- User updates pillar answers → invalidate `embedding:{userId}`
- User posts new tile → invalidate `commons:{userId}:{date}`
- User logs out → invalidate `session:{userId}`

---

### Implementation

#### Pattern: Write-Through Cache

1. **Write to database first** (authoritative source)
2. **Invalidate cache** (force recomputation on next read)
3. **Return success**

```csharp
// User updates profile
await _db.Users.Where(u => u.Id == userId)
    .ExecuteUpdateAsync(u => u.SetProperty(x => x.Bio, newBio), ct);

// Invalidate embedding cache (pillar semantics changed)
await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);

return Ok();
```

**Why not update cache directly?**
- Cache is non-authoritative (Redis could go down)
- Next read will recompute from DB (guaranteed consistency)
- Simpler code (no dual-write logic)

---

#### Pattern: Write-Aside Cache

1. **Write to database**
2. **Invalidate old cache**
3. **Compute new value**
4. **Write new cache**
5. **Return result**

```csharp
// User completes season → pillar scores recalculated
await _db.SeasonCompletions.AddAsync(new SeasonCompletion { ... }, ct);
await _db.SaveChangesAsync(ct);

// Invalidate old embedding
await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);

// Recompute pillar vector
var newEmbedding = await _embeddingService.ComputePillarEmbeddingAsync(userId, ct);

// Cache new value
await _cache.SetAsync(CacheKeys.PillarEmbedding(userId), newEmbedding, CacheTtl.EmbeddingLookup, ct);

return newEmbedding;
```

**When to use:** Hot path that will **immediately re-read** the data (saves one cache miss).

---

### Invalidation Locations

#### 1. Pillar Embedding

**Invalidated when:**
- User completes season (`SeasonService.CompleteSeasonAsync`)
- User submits foundational answers (`OnboardingEndpoints.cs`)
- Admin updates user pillars (`InsightService.cs`)

**Code:**
```csharp
await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);
```

**Files:**
- `SeasonService.cs:100`
- `FeedbackInsightService.cs:127`
- `InsightService.cs:351`
- `UserVectorBuilder.cs:72`

---

#### 2. Commons Feed

**Invalidated when:**
- User posts new tile (affects feed ranking)
- User orbits tile (preference signal)
- User dwells on tile (implicit feedback)

**Code:**
```csharp
await _cache.DeleteAsync(CacheKeys.CommonsFeed(userId, DateOnly.FromDateTime(DateTime.UtcNow)), ct);
```

**File:** `CommonsFeedService.cs:138`

**Note:** Some endpoints use session-scoped keys (`commons:feed:{userId}:{sessionId}`). Invalidation there means "generate new sessionId on frontend."

---

#### 3. User Session

**Invalidated when:**
- User logs out
- Session timeout (automatic TTL expiry)
- Security event (password change, account lock)

**Code:**
```csharp
await _cache.DeleteAsync(CacheKeys.UserSession(userId), ct);
```

**Note:** Session invalidation is **critical for security**. Never skip this on logout.

---

### Pros & Cons

✅ **Pros:**
- Immediate consistency (no stale data)
- User sees changes instantly
- Control over when cache refreshes

❌ **Cons:**
- More code (invalidation scattered across services)
- Race conditions (write vs. invalidation order)
- Risk of missing invalidation (stale cache forever)

---

## Strategy 3: Version-Based Invalidation

**Use when:**
- Cache key itself should change to force miss
- Multiple versions of data may coexist
- Session-scoped caching (per-request)

**Examples:**
- `commons:feed:{userId}:{sessionId}` — Frontend generates new sessionId on refresh
- `deck:{userId}:{date}` — Date in key naturally versions daily

---

### Implementation

#### Pattern: Versioned Key

**Frontend:**
```typescript
// Generate new session ID on refresh
const sessionId = crypto.randomUUID();
const response = await http.get(`/api/commons/feed?sessionId=${sessionId}`);
```

**Backend:**
```csharp
var cacheKey = $"commons:feed:{userId}:{sessionId}";
var feed = await _cache.GetAsync<List<Tile>>(cacheKey, ct);

if (feed == null)
{
    feed = await ComputeFeedAsync(userId, ct);
    await _cache.SetAsync(cacheKey, feed, CacheTtl.CommonsFeed, ct);
}

return feed;
```

**Effect:** Every new `sessionId` is a cache miss → forces recomputation.

**Cleanup:** Old keys expire via TTL (no manual deletion needed).

---

#### Pattern: Date-Versioned Key

**Daily deck:**
```csharp
var key = CacheKeys.DailyDeck(userId, DateOnly.FromDateTime(DateTime.UtcNow));
```

**Effect:**
- 2026-10-07 → `deck:123:2026-10-07`
- 2026-10-08 → `deck:123:2026-10-08` (different key, cache miss)

**Cleanup:** Yesterday's key expires at midnight (TTL).

---

### Pros & Cons

✅ **Pros:**
- No explicit invalidation logic (key change forces miss)
- Multiple versions can coexist (no race conditions)
- Natural cleanup via TTL

❌ **Cons:**
- Memory bloat (old keys linger until TTL expires)
- Harder to debug (which version is active?)
- Requires coordinated key generation (frontend + backend)

---

## Invalidation Patterns by Feature

### Matchmaking

| Feature | Strategy | Trigger |
|---|---|---|
| **Daily decks** | TTL (until midnight) | Automatic at midnight UTC |
| **Pending counter** | TTL (until midnight) | Automatic at midnight UTC |
| **Spark counter** | TTL (until midnight) | Automatic at midnight UTC |
| **Worker locks** | TTL (5 min) | Worker completes or crashes |

**No manual invalidation needed** (time-based refresh).

---

### Content (Commons)

| Feature | Strategy | Trigger |
|---|---|---|
| **Feed cache** | Event-driven | User posts tile, orbits, dwells |
| **Hot tile** | TTL (5 min) | Automatic (trending recalc) |
| **Session feed** | Version-based | Frontend generates new sessionId |

**Code:**
```csharp
// Invalidate feed on user action
await _cache.DeleteAsync(CacheKeys.CommonsFeed(userId, DateOnly.FromDateTime(DateTime.UtcNow)), ct);
```

---

### AI / Embeddings

| Feature | Strategy | Trigger |
|---|---|---|
| **Pillar embedding** | Event-driven + TTL | Season completion, pillar update, OR 1 day TTL |

**Code:**
```csharp
// Explicit invalidation on data change
await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);

// Also expires after 1 day (TTL fallback)
await _cache.SetAsync(key, embedding, CacheTtl.EmbeddingLookup, ct);
```

**Hybrid strategy:** Event-driven for user actions, TTL for nightly batch updates.

---

### Auth / Session

| Feature | Strategy | Trigger |
|---|---|---|
| **User session** | Event-driven + TTL | Logout (manual) OR 1 hour inactivity (TTL) |

**Code:**
```csharp
// Explicit logout
await _cache.DeleteAsync(CacheKeys.UserSession(userId), ct);

// Auto-expire after 1 hour
await _cache.SetAsync(key, session, CacheTtl.Session, ct);
```

---

## Anti-Patterns

### ❌ Cache Without Invalidation

```csharp
// BAD: Set cache, never invalidate
await _cache.SetAsync("user:bio", bio, TimeSpan.FromDays(365), ct);
```

**Problem:** User updates bio → stale cache for 1 year.

**Fix:** Add event-driven invalidation on bio update.

---

### ❌ Invalidate Before Write

```csharp
// BAD: Delete cache BEFORE writing to DB
await _cache.DeleteAsync(key, ct);
await _db.SaveChangesAsync(ct);  // May fail!
```

**Problem:** If DB write fails, cache is already gone → next read recomputes stale data from old DB state.

**Fix:** Write to DB first, then invalidate cache.

---

### ❌ Partial Invalidation

```csharp
// BAD: Only invalidate embedding, forget to invalidate feed
await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);
// Oops: Feed still cached with old pillar-based ranking
```

**Problem:** Multiple caches depend on same data → must invalidate all.

**Fix:** Invalidate **all dependent caches** in one transaction:
```csharp
await Task.WhenAll(
    _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct),
    _cache.DeleteAsync(CacheKeys.CommonsFeed(userId, DateOnly.FromDateTime(DateTime.UtcNow)), ct)
);
```

---

### ❌ Over-Invalidation

```csharp
// BAD: Invalidate entire feed on every tile dwell (too aggressive)
await _cache.DeleteAsync(CacheKeys.CommonsFeed(userId, date), ct);
```

**Problem:** Cache never hits (constant recomputation).

**Fix:** Invalidate only when **semantically significant** (e.g., orbit, not dwell).

Or use TTL-based refresh (hourly) instead of per-action invalidation.

---

## Race Conditions

### Problem: Read-Modify-Write

**Thread 1:**
```csharp
var data = await _cache.GetAsync<Data>(key);  // null
// ... compute expensive result
await _cache.SetAsync(key, result, ttl);      // Write A
```

**Thread 2:**
```csharp
var data = await _cache.GetAsync<Data>(key);  // null (T1 hasn't written yet)
// ... compute same expensive result
await _cache.SetAsync(key, result, ttl);      // Write B (overwrites A)
```

**Impact:** Wasted CPU (duplicate computation). Both threads compute same thing.

**Fix 1: Distributed Lock**
```csharp
var lockKey = $"lock:compute:{key}";
if (await _cache.AcquireLockAsync(lockKey, TimeSpan.FromSeconds(10), ct))
{
    try
    {
        var data = await _cache.GetAsync<Data>(key);
        if (data == null)
        {
            data = await ComputeExpensiveThingAsync();
            await _cache.SetAsync(key, data, ttl, ct);
        }
        return data;
    }
    finally
    {
        await _cache.ReleaseLockAsync(lockKey, ct);
    }
}
else
{
    // Another thread is computing — wait and retry
    await Task.Delay(100, ct);
    return await _cache.GetAsync<Data>(key) ?? throw new Exception("Cache miss after lock");
}
```

**Fix 2: Accept Duplication (Simpler)**
- Let both threads compute
- Last write wins
- Cheaper than lock overhead for lightweight computations

**Woven uses:** Fix 2 for most caches (daily decks, feeds). Lock only for batch workers.

---

### Problem: Invalidate During Read

**Thread 1:**
```csharp
var data = await _cache.GetAsync<Data>(key);  // HIT (returns stale data)
return data;
```

**Thread 2:**
```csharp
await UpdateDatabaseAsync();
await _cache.DeleteAsync(key);  // Invalidate (T1 already read stale!)
```

**Impact:** T1 serves stale data to user.

**Fix:** Accept eventual consistency (TTL will refresh).

Or use **optimistic locking** (version numbers in cache value).

**Woven uses:** TTL-based eventual consistency. Most caches refresh within seconds/minutes.

---

## Testing Invalidation

### Unit Test: Event-Driven

```csharp
[Fact]
public async Task UpdateBio_ShouldInvalidatePillarEmbedding()
{
    // Arrange
    var userId = 123;
    await _cache.SetAsync(CacheKeys.PillarEmbedding(userId), oldEmbedding, ttl);

    // Act
    await _userService.UpdateBioAsync(userId, "New bio");

    // Assert
    var cached = await _cache.GetAsync<Embedding>(CacheKeys.PillarEmbedding(userId));
    Assert.Null(cached);  // Cache was invalidated
}
```

---

### Integration Test: TTL

```csharp
[Fact]
public async Task DailyDeck_ShouldExpireAtMidnight()
{
    // Arrange
    var userId = 123;
    var deck = await _deckService.GetDeckAsync(userId);
    
    // Assert: Cache hit on second call
    var cached = await _deckService.GetDeckAsync(userId);
    Assert.Same(deck, cached);

    // Act: Simulate time passing (mock clock or wait)
    _clock.Set(DateTime.UtcNow.Date.AddDays(1));  // Mock clock → next day

    // Assert: Cache miss (TTL expired)
    var newDeck = await _deckService.GetDeckAsync(userId);
    Assert.NotSame(deck, newDeck);
}
```

---

### Manual Test: Redis CLI

```bash
# Set key with 10-second TTL
redis-cli setex "test:ttl" 10 "hello"

# Check TTL (should be ~10)
redis-cli ttl "test:ttl"

# Wait 11 seconds
sleep 11

# Verify key expired (returns -2)
redis-cli ttl "test:ttl"
```

---

## Monitoring Invalidation

### Logs

All cache invalidations log:

```
[Cache] DeleteAsync called for key embedding:123
```

Structured logs include:
- `{Key}` — cache key
- `{CorrelationId}` — request trace ID

### Metrics

Track cache invalidation **frequency** per key pattern:

| Key Pattern | Invalidations/hour | Expected | Status |
|---|---|---|---|
| `embedding:*` | 5 | < 10 | ✅ Normal |
| `commons:*` | 200 | < 500 | ✅ Normal |
| `session:*` | 50 | < 100 | ✅ Normal |
| `deck:*` | 0 | 0 (TTL only) | ✅ Normal |

**Alert:** Invalidations > 10x expected → investigate hot loop or bug.

---

## Decision Tree

**Should I cache this data?**
```
┌─ Is data expensive to compute? (>100ms or API call)
│  ├─ Yes → Continue
│  └─ No  → Don't cache (premature optimization)
│
├─ Does data change frequently? (>1/min per user)
│  ├─ Yes → Don't cache (invalidation overhead > benefit)
│  └─ No  → Continue
│
├─ Is stale data acceptable for <1 hour?
│  ├─ Yes → Use TTL-based expiry
│  └─ No  → Continue
│
├─ Can you detect when data changes?
│  ├─ Yes → Use event-driven invalidation
│  └─ No  → Use short TTL (1-5 min) + version-based keys
```

---

## Best Practices

### ✅ Do

- **Invalidate on write** (DB first, cache second)
- **Use TTL as fallback** (belt-and-suspenders)
- **Log all invalidations** (debug cache bugs)
- **Test invalidation paths** (unit tests)
- **Invalidate dependent caches** (all or nothing)

### ❌ Don't

- **Cache authoritative data** (DB is source of truth)
- **Invalidate before write** (race condition)
- **Over-invalidate** (kills cache hit rate)
- **Forget to invalidate** (stale cache forever)
- **Invalidate in transaction** (cache is async, DB is transactional)

---

## FAQs

**Q: What if I forget to invalidate?**  
A: TTL is your safety net. Cache will refresh eventually. But users see stale data until then.

**Q: Should I invalidate inside a DB transaction?**  
A: No. Cache operations are async/non-transactional. Invalidate **after** transaction commits.

**Q: Can I invalidate by pattern (e.g., `deck:*`)?**  
A: Not easily. `DeleteAsync` takes exact key. For patterns, use SCAN + loop (expensive) or version-based keys.

**Q: What if invalidation fails (Redis down)?**  
A: Cache becomes stale. TTL eventually expires it. Or implement retry logic.

**Q: Should I invalidate cache on read?**  
A: Only if you detect corruption. Normal flow: read cache, compute on miss, write cache.

---

## Related Docs

- **[Caching Overview](./README.md)** — Architecture, use cases
- **[Cache Keys](./cache-keys.md)** — Key naming conventions
- **[CacheService Implementation](./implementation.md)** — Code walkthrough
- **[Redis Configuration](./redis.md)** — Connection setup

---

## Changelog

| Date | Change |
|---|---|
| 2026-10-07 | Initial cache invalidation documentation |
| 2026-06-04 | Added distributed lock pattern for workers |
| 2026-06-03 | Documented hybrid TTL + event-driven strategy |
