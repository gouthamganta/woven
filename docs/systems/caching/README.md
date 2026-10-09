# Caching System

**Owner:** Backend team  
**Last Updated:** 2026-10-07  
**Status:** Production

---

## Overview

Woven uses Redis as a distributed cache layer for:
- **Performance:** Reduce database load for frequently accessed data
- **Rate limiting:** Per-user API quotas
- **Distributed coordination:** Worker locks, deduplication
- **Session state:** Temporary user data (encrypted)
- **SignalR backplane:** Cross-pod message broadcasting

Redis is treated as **ephemeral, non-authoritative storage**. Cache misses fall back to the database. Redis outages never block users — all operations gracefully degrade.

---

## Key Characteristics

| Property | Value |
|---|---|
| **Storage** | In-memory (volatile) |
| **Persistence** | AOF disabled (cache only, no durability guarantee) |
| **Eviction** | allkeys-lru (oldest keys evicted when full) |
| **Max memory** | 250MB (Azure Cache for Redis — Basic C0) |
| **Encryption at rest** | TLS in transit (Azure), encrypted for sensitive keys |
| **Replication** | None (single instance) |
| **Backplane** | SignalR uses Redis pub/sub |

---

## When to Use Cache

### ✅ Good Use Cases

- **Computed data that's expensive to rebuild:**
  - Daily decks (vector search + ECHO scoring)
  - Commons feed (personalized sorting)
  - User embeddings (OpenAI API calls)

- **Counters that reset daily:**
  - Spark spend
  - Games played
  - Pending matches

- **Rate limiting:**
  - Per-user API quotas
  - Anti-spam (chat messages, tile posts)

- **Distributed locks:**
  - Batch worker coordination (prevent duplicate runs)
  - Idempotency (prevent double-processing)

- **Session-scoped data:**
  - Temporary UI state
  - In-progress onboarding data

### ❌ Avoid Cache For

- **Critical writes:** Never cache data users expect to persist
- **Authorization state:** Always validate permissions against DB
- **Financial ledger:** Spark transactions go through DB first
- **Match state:** Trial/balloon timers live in Postgres
- **User-generated content:** Tiles, messages, profiles never cached before DB write

---

## Architecture

### Components

1. **Redis Server**
   - Azure Cache for Redis (Basic C0, 250MB)
   - Endpoint: `woven-prod-redis.redis.cache.windows.net:6380`
   - TLS required in production
   - Local dev: `localhost:6379` (Docker Compose)

2. **StackExchange.Redis Client**
   - NuGet package: `StackExchange.Redis 2.8.16`
   - `IConnectionMultiplexer` registered as singleton
   - Thread-safe, multiplexed TCP connection
   - `AbortOnConnectFail = false` → app starts even if Redis is down

3. **CacheService**
   - `ICacheService` interface
   - Wraps all Redis calls in try/catch
   - Encrypts sensitive keys (`session:*`, `embedding:*`)
   - Graceful degradation on failures

4. **SignalR Backplane**
   - Uses Redis pub/sub for message distribution
   - Allows multi-pod deployments to push notifications correctly
   - Same Redis instance as cache

### Data Flow

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │ HTTP GET /api/decks/today
       ▼
┌─────────────┐
│  Endpoint   │ ──────── CheckRateLimitAsync("rate-limit:deck:{userId}") ────►
└──────┬──────┘                                                                │
       │                                                                       ▼
       │                                                                  ┌─────────┐
       │ GetAsync("deck:{userId}:{date}")                                │  Redis  │
       ▼                                                                  └─────────┘
┌─────────────┐      Cache HIT → return                                      ▲
│CacheService │                                                               │
└──────┬──────┘      Cache MISS ↓                                             │
       │                                                                       │
       │ Compute deck (DB + ECHO scoring)                                     │
       ▼                                                                       │
┌─────────────┐                                                               │
│ PostgreSQL  │                                                               │
└──────┬──────┘                                                               │
       │                                                                       │
       │ Store computed deck ─────────────────────────────────────────────────┘
       ▼                    SetAsync("deck:...", deck, TTL)
    return deck
```

---

## Configuration

See **[redis.md](./redis.md)** for connection strings, environments, and deployment config.

---

## Cache Keys

See **[cache-keys.md](./cache-keys.md)** for naming conventions, key patterns, and TTL values.

---

## Invalidation

See **[invalidation.md](./invalidation.md)** for cache invalidation strategies and patterns.

---

## Implementation

See **[implementation.md](./implementation.md)** for CacheService code walkthrough, usage examples, and error handling.

---

## Monitoring

### Key Metrics

| Metric | What to Watch | Alert Threshold |
|---|---|---|
| **Hit rate** | `cache_hits / (cache_hits + cache_misses)` | < 70% (investigate key patterns) |
| **Memory usage** | Redis `used_memory` | > 200MB (eviction imminent) |
| **Evicted keys** | `evicted_keys` counter | > 100/min (increase max memory) |
| **Connection errors** | `IConnectionMultiplexer` exceptions | > 5/min (Redis unreachable) |
| **Rate limit rejections** | HTTP 429 responses | > 50/min (abuse or legitimate spike?) |

### Logs

All cache operations log to `[Cache]` prefix:

```
[Cache] GetAsync miss (Redis unavailable) for key deck:123:2026-10-07
[Cache] SetAsync failed (Redis unavailable) for key session:456
[Cache] AcquireLockAsync failed for connection-score-batch — failing open
```

Structured logs include:
- `{Key}` — cache key
- `{CorrelationId}` — request trace ID

### Azure Portal

Production Redis metrics:
- Azure Portal → woven-prod-redis → Metrics
- Charts: Cache Hits, Cache Misses, Used Memory, Connected Clients

---

## Failure Modes

### Redis Unreachable

**Symptom:** Connection timeouts, `StackExchange.Redis.RedisConnectionException`

**Behavior:**
- `GetAsync` returns `default(T)` → cache miss, falls back to DB
- `SetAsync` logs warning, does not throw
- `CheckRateLimitAsync` returns `true` (allow request — fail open)
- `AcquireLockAsync` returns `true` (workers proceed)

**Impact:**
- Slower responses (no cache acceleration)
- Workers may run in parallel (lock acquisition always succeeds)
- Rate limits temporarily disabled (protects UX over abuse)

**Resolution:**
- Check Redis connection string in App Settings
- Verify Azure Cache for Redis is running (Portal)
- Check firewall rules (Container Apps → Redis network path)

### Memory Eviction

**Symptom:** `evicted_keys` counter increases, cache hit rate drops

**Behavior:**
- Redis evicts **least recently used** keys to stay under max memory
- Affected users see cache misses, triggering recomputation
- No errors thrown (eviction is normal Redis behavior)

**Impact:**
- Increased DB load (more vector searches, ECHO scoring)
- Slower API responses (cache misses)

**Resolution:**
- Scale Redis to higher tier (Basic C1 = 1GB, Standard C2 = 2.5GB)
- Reduce TTL for large cached objects (e.g., daily decks)
- Remove stale keys (clear old dates via scheduled cleanup)

### Corrupted Data

**Symptom:** Deserialization errors, `JsonException`

**Behavior:**
- `GetAsync` catches exception, logs warning, returns `default(T)`
- Falls back to DB (same as cache miss)

**Impact:**
- One user affected (corrupted key is isolated)
- Next request recomputes and overwrites bad data

**Resolution:**
- Delete corrupted key: `await _cache.DeleteAsync(key)`
- Investigate root cause (schema change without migration?)

---

## Performance

### Latency Budget

| Operation | p50 | p95 | p99 |
|---|---|---|---|
| `GetAsync<T>` | 1ms | 3ms | 8ms |
| `SetAsync<T>` | 1ms | 4ms | 10ms |
| `IncrementAsync` | < 1ms | 2ms | 5ms |
| `AcquireLockAsync` | < 1ms | 2ms | 5ms |

Encryption adds ~0.5ms overhead for `session:*` and `embedding:*` keys.

### Throughput

- Redis: ~10,000 simple GET/SET per second (Azure Basic C0)
- SignalR backplane: ~500 messages/sec (pub/sub)
- Current load: ~50 cache ops/sec (well under capacity)

---

## Local Development

### Start Redis (Docker Compose)

```bash
cd backend
docker-compose up redis -d
```

Redis runs on `localhost:6379`, no password.

### Verify Connection

```bash
redis-cli ping
# Expected: PONG
```

### Inspect Keys

```bash
redis-cli keys "*"
redis-cli get "deck:1:2026-10-07"
redis-cli ttl "session:123"
```

### Clear All Cache

```bash
redis-cli flushdb
```

---

## Production Access

### Azure CLI

```bash
# Get Redis connection string
az redis list-keys \
  --resource-group woven-prod-rg \
  --name woven-prod-redis

# Connect via redis-cli (requires SSL)
redis-cli -h woven-prod-redis.redis.cache.windows.net -p 6380 --tls
```

### Debugging

```bash
# Monitor commands in real-time
redis-cli monitor

# Check memory usage
redis-cli info memory

# List keys by pattern (use SCAN, not KEYS in prod)
redis-cli --scan --pattern "deck:*"
```

---

## Migration Guide

### Adding a New Cache Key

1. Define key in `CacheKeys.cs`:
   ```csharp
   public static string MyFeature(int userId) => $"feature:{userId}";
   ```

2. Add TTL to `CacheTtl.cs` if not using `UntilMidnightUtc()`:
   ```csharp
   public static readonly TimeSpan MyFeature = TimeSpan.FromHours(6);
   ```

3. Use in service:
   ```csharp
   var cached = await _cache.GetAsync<MyData>(CacheKeys.MyFeature(userId), ct);
   if (cached != null) return cached;
   
   var computed = await ComputeExpensiveThingAsync(userId);
   await _cache.SetAsync(CacheKeys.MyFeature(userId), computed, CacheTtl.MyFeature, ct);
   return computed;
   ```

4. Add invalidation on data change:
   ```csharp
   await _cache.DeleteAsync(CacheKeys.MyFeature(userId), ct);
   ```

5. Document in `cache-keys.md`.

---

## Related Systems

- **[Redis Configuration](./redis.md)** — Connection strings, environments
- **[ECHO Workers](../echo/workers.md)** — Distributed lock usage
- **[Rate Limiting](../../technical/SECURITY.md#rate-limiting)** — Redis-based quotas
- **[SignalR](../../technical/ARCHITECTURE.md#signalr)** — Backplane configuration

---

## Security

### Encryption

- **In transit:** TLS 1.2+ required in production (port 6380)
- **At rest:** Sensitive keys encrypted via `IEncryptionService`
  - `session:*` — User session data
  - `embedding:*` — OpenAI embeddings (PII-adjacent)

### Access Control

- **Production:** Azure Private Link (Container Apps → Redis, no public internet)
- **Secrets:** Connection string stored in Azure Key Vault
- **Rotation:** Redis access key rotated quarterly (Terraform + GitHub Actions)

### Compliance

- **PII:** No plaintext PII in cache keys or values (encrypted or anonymized)
- **GDPR:** Cache invalidation on user deletion (`DELETE /api/me`)
- **Audit:** All cache operations logged with `{CorrelationId}`

---

## FAQs

**Q: What happens if Redis is down?**  
A: App continues working. Cache operations fail gracefully, falling back to Postgres. Performance degrades but functionality is preserved.

**Q: Can I use cache for write-through?**  
A: No. Always write to Postgres first, then cache. Cache is never authoritative.

**Q: Why not use IDistributedCache?**  
A: We use raw `IConnectionMultiplexer` for more control (locks, pub/sub, encrypted values). Could migrate to `IDistributedCache` if needed.

**Q: How do I debug cache misses?**  
A: Enable `[Cache]` logs at `Debug` level. Check TTL expiry, key format, or if data was never written.

**Q: Can I store files in Redis?**  
A: No. Use Azure Blob Storage. Redis max value size is 512MB but should only hold small objects (< 100KB).

---

## Changelog

| Date | Change |
|---|---|
| 2026-10-07 | Initial caching system documentation |
| 2026-06-04 | Added idempotency service (uses Redis locks) |
| 2026-06-03 | Moved from IDistributedCache → IConnectionMultiplexer |
| 2026-05-20 | Added encryption for sensitive keys |
| 2026-05-01 | Initial Redis integration (SignalR backplane) |
