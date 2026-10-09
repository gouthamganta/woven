# CacheService Implementation

**Last Updated:** 2026-10-07

---

## Overview

`CacheService` is the central abstraction over Redis caching. It provides:
- **Type-safe operations** (`GetAsync<T>`, `SetAsync<T>`)
- **Encryption for sensitive keys** (`session:*`, `embedding:*`)
- **Graceful degradation** (Redis failures don't break app)
- **Counter operations** (rate limits, quotas)
- **Distributed locks** (worker coordination)

**Files:**
- `backend/WovenBackend/Services/ICacheService.cs` — Interface
- `backend/WovenBackend/Services/CacheService.cs` — Implementation

---

## Interface: ICacheService

**File:** `backend/WovenBackend/Services/ICacheService.cs`

```csharp
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, TimeSpan? expireIn = null, CancellationToken ct = default);
    Task<long> GetCounterAsync(string key, CancellationToken ct = default);
    Task<bool> CheckRateLimitAsync(string key, int limit, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> AcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken ct = default);
    Task ReleaseLockAsync(string lockKey, CancellationToken ct = default);
}
```

---

## Dependencies

**File:** `backend/WovenBackend/Services/CacheService.cs` (lines 1–26)

```csharp
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using WovenBackend.Services.Security;

public class CacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IEncryptionService _enc;
    private readonly ILogger<CacheService> _logger;

    // Derived once at construction — never re-derive per call.
    private readonly byte[] _cacheKey;

    public CacheService(
        IConnectionMultiplexer redis,
        IEncryptionService enc,
        ILogger<CacheService> logger)
    {
        _redis = redis;
        _enc = enc;
        _logger = logger;
        _cacheKey = Convert.FromBase64String(_enc.DeriveKey("cache-encryption-v1"));
    }
```

### IConnectionMultiplexer

- **Type:** Singleton (one instance per app)
- **Purpose:** Thread-safe Redis connection pool
- **NuGet:** `StackExchange.Redis 2.8.16`
- **Lifecycle:** Lives for entire app lifetime, reconnects automatically

### IEncryptionService

- **Purpose:** Encrypt/decrypt sensitive cache values
- **Algorithm:** AES-256-GCM (authenticated encryption)
- **Key derivation:** PBKDF2 from master secret (stored in Key Vault)

### _cacheKey

- **Purpose:** Pre-derived encryption key for cache operations
- **Lifetime:** Derived once at service construction (performance optimization)
- **Context:** `"cache-encryption-v1"` (domain separation)

---

## Core Operations

### GetAsync<T>

**File:** `CacheService.cs` (lines 28–54)

```csharp
public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
{
    try
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(key);
        if (!raw.HasValue) return default;

        string json;
        if (IsSensitiveKey(key))
        {
            var decrypted = _enc.DecryptBytes(Convert.FromBase64String((string)raw!));
            json = Encoding.UTF8.GetString(decrypted);
        }
        else
        {
            json = (string)raw!;
        }

        return JsonSerializer.Deserialize<T>(json);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Cache] GetAsync miss (Redis unavailable) for key {Key}", key);
        return default;
    }
}
```

**Flow:**
1. Get Redis database (logical DB 0)
2. Read string value from Redis
3. If sensitive key → decrypt bytes → UTF8 string
4. Deserialize JSON → typed object
5. Return value (or `default(T)` on miss/error)

**Error handling:**
- Redis unreachable → catch exception, log warning, return `default`
- Cache miss → `raw.HasValue == false` → return `default`
- Deserialization error → caught by outer `try/catch`, return `default`

**Behavior:** Never throws. Always returns `T?` (nullable).

---

### SetAsync<T>

**File:** `CacheService.cs` (lines 56–80)

```csharp
public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
{
    try
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(value);

        string stored;
        if (IsSensitiveKey(key))
        {
            var encrypted = _enc.EncryptBytes(Encoding.UTF8.GetBytes(json));
            stored = Convert.ToBase64String(encrypted);
        }
        else
        {
            stored = json;
        }

        await db.StringSetAsync(key, stored, ttl);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Cache] SetAsync failed (Redis unavailable) for key {Key}", key);
    }
}
```

**Flow:**
1. Serialize value → JSON string
2. If sensitive key → UTF8 bytes → encrypt → base64 string
3. Write to Redis with TTL
4. No return value (fire-and-forget)

**Error handling:**
- Redis unreachable → catch exception, log warning, **do not throw**
- Write failure is **not user-facing** (cache is best-effort)

**Behavior:** Never throws. Silently fails on Redis errors.

---

### DeleteAsync

**File:** `CacheService.cs` (lines 82–93)

```csharp
public async Task DeleteAsync(string key, CancellationToken ct = default)
{
    try
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Cache] DeleteAsync failed (Redis unavailable) for key {Key}", key);
    }
}
```

**Flow:**
1. Delete key from Redis
2. No return value (idempotent — deleting non-existent key is OK)

**Error handling:**
- Redis unreachable → log warning, do not throw
- Invalidation failures are **logged but non-blocking**

---

## Counter Operations

### IncrementAsync

**File:** `CacheService.cs` (lines 95–110)

```csharp
public async Task<long> IncrementAsync(string key, TimeSpan? expireIn = null, CancellationToken ct = default)
{
    try
    {
        var db = _redis.GetDatabase();
        var count = await db.StringIncrementAsync(key);
        if (count == 1 && expireIn.HasValue)
            await db.KeyExpireAsync(key, expireIn.Value);
        return count;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Cache] IncrementAsync failed (Redis unavailable) for key {Key}", key);
        return -1;
    }
}
```

**Flow:**
1. Atomic increment (Redis `INCR` command)
2. If first increment (count == 1) → set TTL
3. Return new count

**Atomicity:** `INCR` is atomic (thread-safe, no race conditions).

**TTL logic:**
- Only set TTL on **first increment** (count == 1)
- Subsequent increments don't reset TTL (preserves expiry)

**Error handling:**
- Redis unreachable → return `-1` (sentinel value)
- Caller checks `count == -1` and handles gracefully

**Use cases:**
- Rate limiting (increment per request)
- Daily quotas (spark spend, games played)

---

### GetCounterAsync

**File:** `CacheService.cs` (lines 112–126)

```csharp
public async Task<long> GetCounterAsync(string key, CancellationToken ct = default)
{
    try
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        if (!value.HasValue) return 0;
        return (long)value;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Cache] GetCounterAsync failed (Redis unavailable) for key {Key}", key);
        return -1;
    }
}
```

**Flow:**
1. Read counter value as string
2. Cast to `long`
3. Return (or 0 if key doesn't exist)

**Error handling:**
- Redis unreachable → return `-1`
- Key not found → return `0` (counter starts at 0)

---

### CheckRateLimitAsync

**File:** `CacheService.cs` (lines 128–133)

```csharp
public async Task<bool> CheckRateLimitAsync(string key, int limit, TimeSpan ttl, CancellationToken ct = default)
{
    var current = await IncrementAsync(key, ttl, ct);
    if (current == -1) return true; // allow on Redis failure — never block users due to cache outage
    return current <= limit;
}
```

**Flow:**
1. Increment counter (atomic)
2. If Redis failed (`current == -1`) → **allow request** (fail open)
3. Otherwise → allow if `current <= limit`

**Fail-open behavior:**
- Redis down → rate limits **disabled** (protects UX over abuse)
- Design choice: availability > strict enforcement

**Example usage:**
```csharp
var allowed = await _cache.CheckRateLimitAsync(
    $"rate-limit:deck:{userId}",
    limit: 10,
    ttl: TimeSpan.FromHours(24),
    ct
);

if (!allowed)
    return Results.StatusCode(429); // Too Many Requests
```

---

## Distributed Locks

### AcquireLockAsync

**File:** `CacheService.cs` (lines 135–148)

```csharp
public async Task<bool> AcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken ct = default)
{
    try
    {
        var db = _redis.GetDatabase();
        // SET key "1" NX EX <seconds> — atomic, only sets if key doesn't exist.
        return await db.StringSetAsync(lockKey, "1", expiry, When.NotExists);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Cache] AcquireLockAsync failed for {Key} — failing open", lockKey);
        return true; // fail open: don't block workers on cache outage
    }
}
```

**Flow:**
1. Try to set key with `NX` (Not eXists) flag
2. Return `true` if lock acquired, `false` if already held
3. TTL auto-releases lock on crash

**Redis command:** `SET lock:name "1" EX 300 NX`

**Atomicity:** `SET NX` is atomic (thread-safe).

**Fail-open behavior:**
- Redis down → **allow worker to proceed** (risk: duplicate runs)
- Design choice: liveness > strict coordination

**Use cases:**
- Batch workers (prevent overlapping runs)
- Idempotency (ensure action runs once)

**Example usage:**
```csharp
if (await _cache.AcquireLockAsync("lock:connection-score-batch", TimeSpan.FromMinutes(5)))
{
    try
    {
        await RunBatchAsync();
    }
    finally
    {
        await _cache.ReleaseLockAsync("lock:connection-score-batch");
    }
}
else
{
    _logger.LogInformation("Another instance is running the batch — skipping");
}
```

---

### ReleaseLockAsync

**File:** `CacheService.cs` (lines 150–151)

```csharp
public Task ReleaseLockAsync(string lockKey, CancellationToken ct = default)
    => DeleteAsync(lockKey, ct);
```

**Flow:**
- Simply delete the lock key
- Idempotent (safe to call multiple times)

**Note:** Lock auto-expires via TTL (no manual release needed in crash scenarios).

---

## Encryption

### IsSensitiveKey

**File:** `CacheService.cs` (lines 153–156)

```csharp
private static bool IsSensitiveKey(string key)
    => key.StartsWith("session:", StringComparison.Ordinal)
    || key.StartsWith("embedding:", StringComparison.Ordinal);
```

**Purpose:** Determine if cache value should be encrypted.

**Sensitive keys:**
- `session:*` — User session data (OAuth tokens, onboarding progress)
- `embedding:*` — OpenAI embeddings (PII-adjacent, reveals profile semantics)

**Why encrypt:**
- Redis is **not encrypted at rest** (Basic tier)
- Network traffic uses TLS (in-transit encryption)
- But Redis admin could read raw values → encrypt sensitive data

**Future:** May add more patterns (`chat:*`, `profile:*`) as needed.

---

### Encryption Flow

**Write (SetAsync):**
```csharp
if (IsSensitiveKey(key))
{
    var encrypted = _enc.EncryptBytes(Encoding.UTF8.GetBytes(json));
    stored = Convert.ToBase64String(encrypted);
}
```

**Read (GetAsync):**
```csharp
if (IsSensitiveKey(key))
{
    var decrypted = _enc.DecryptBytes(Convert.FromBase64String((string)raw!));
    json = Encoding.UTF8.GetString(decrypted);
}
```

**Encryption details:**
- **Algorithm:** AES-256-GCM (authenticated encryption, prevents tampering)
- **Key:** Derived from master secret via PBKDF2
- **IV:** Random per encryption (prepended to ciphertext)
- **Overhead:** ~0.5ms per operation (negligible)

---

## Registration (Program.cs)

**File:** `backend/WovenBackend/Program.cs` (lines 476–483)

```csharp
// Singleton IConnectionMultiplexer — one TCP connection shared across the process.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConnStr = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    var cfg = ConfigurationOptions.Parse(redisConnStr);
    cfg.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(cfg);
});
builder.Services.AddSingleton<ICacheService, CacheService>();
```

**Why singleton?**
- `IConnectionMultiplexer` is thread-safe, expensive to create
- `CacheService` is stateless (just wraps multiplexer)
- One shared instance for entire app lifetime

**AbortOnConnectFail:**
- `false` → app starts even if Redis is down
- Multiplexer retries connection in background

---

## Usage Examples

### Simple Get/Set

```csharp
public class MyService
{
    private readonly ICacheService _cache;

    public async Task<MyData> GetDataAsync(int userId, CancellationToken ct)
    {
        var key = $"myfeature:{userId}";
        var cached = await _cache.GetAsync<MyData>(key, ct);

        if (cached != null)
            return cached;

        var computed = await ComputeExpensiveThingAsync(userId, ct);
        await _cache.SetAsync(key, computed, TimeSpan.FromHours(1), ct);
        return computed;
    }
}
```

---

### Rate Limiting

```csharp
public async Task<IResult> SendMessageAsync(int userId, string message, CancellationToken ct)
{
    // Rate limit: 10 messages per minute
    var allowed = await _cache.CheckRateLimitAsync(
        $"rate-limit:chat-send:{userId}",
        limit: 10,
        ttl: TimeSpan.FromMinutes(1),
        ct
    );

    if (!allowed)
        return Results.StatusCode(429); // Too Many Requests

    // Send message...
    return Results.Ok();
}
```

---

### Distributed Lock (Batch Worker)

```csharp
public class MyBatchWorker : BackgroundService
{
    private readonly ICacheService _cache;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (await _cache.AcquireLockAsync("lock:my-batch", TimeSpan.FromMinutes(5), ct))
            {
                try
                {
                    await RunBatchAsync(ct);
                }
                finally
                {
                    await _cache.ReleaseLockAsync("lock:my-batch", ct);
                }
            }

            await Task.Delay(TimeSpan.FromHours(1), ct); // Run hourly
        }
    }
}
```

---

### Counter Operations

```csharp
public async Task<int> GetSparkBalanceAsync(int userId, CancellationToken ct)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var spent = await _cache.GetCounterAsync(CacheKeys.SparkCounter(userId, today), ct);

    if (spent == -1) // Redis failure
        spent = await _db.SparkTransactions.Where(t => t.UserId == userId && t.Date == today).SumAsync(t => t.Amount, ct);

    return 5 - (int)spent; // Daily limit = 5
}
```

---

### Cache Invalidation

```csharp
public async Task UpdateProfileAsync(int userId, string newBio, CancellationToken ct)
{
    // Write to DB first
    await _db.Users.Where(u => u.Id == userId)
        .ExecuteUpdateAsync(u => u.SetProperty(x => x.Bio, newBio), ct);

    // Invalidate cache
    await _cache.DeleteAsync(CacheKeys.PillarEmbedding(userId), ct);

    return Results.Ok();
}
```

---

## Error Handling

### Graceful Degradation

**Design principle:** Redis is **non-critical infrastructure**. App must work without it.

**All cache operations:**
- Wrapped in `try/catch`
- Log warnings on failure
- **Never throw exceptions** to caller

**Failure modes:**
- `GetAsync` → return `default(T)` (cache miss)
- `SetAsync` → log warning, no-op (write fails silently)
- `DeleteAsync` → log warning, no-op (invalidation skipped)
- `IncrementAsync` → return `-1` (caller checks)
- `CheckRateLimitAsync` → return `true` (allow request — fail open)
- `AcquireLockAsync` → return `true` (allow worker — fail open)

**Impact on users:**
- Slower responses (no cache acceleration)
- Rate limits temporarily disabled (abuse risk)
- Workers may run in parallel (duplicate computation)

**Recovery:**
- Multiplexer auto-reconnects when Redis comes back
- Cache warms up as requests flow through
- No manual intervention needed

---

### Logging

All cache operations log warnings on failure:

```
[Cache] GetAsync miss (Redis unavailable) for key deck:123:2026-10-07
[Cache] SetAsync failed (Redis unavailable) for key session:456
[Cache] AcquireLockAsync failed for lock:connection-score-batch — failing open
```

**Structured logs include:**
- `{Key}` — cache key
- Exception details (connection error, timeout, etc.)

**Log level:** `Warning` (not `Error` — Redis failures are expected, not critical).

---

## Performance

### Latency

| Operation | Typical | p95 | p99 |
|---|---|---|---|
| `GetAsync<T>` | 1ms | 3ms | 8ms |
| `SetAsync<T>` | 1ms | 4ms | 10ms |
| `IncrementAsync` | < 1ms | 2ms | 5ms |
| `AcquireLockAsync` | < 1ms | 2ms | 5ms |

**Encryption overhead:** +0.5ms for sensitive keys.

**Network:** Azure Container Apps → Redis over private link (~0.5ms RTT).

---

### Memory Overhead

**Per cached object:**
- JSON serialization overhead (keys as strings, no schema compression)
- Encryption adds ~16 bytes (IV) + auth tag (16 bytes)

**Typical sizes:**
- Daily deck: ~5 KB (10 candidates × 500 bytes each)
- User session: ~500 bytes
- Embedding: 6 KB (1536 floats × 4 bytes)
- Counters: ~10 bytes (integer as string)

**Total cache usage:** ~20 MB (with 250 MB limit).

---

## Testing

### Unit Test: GetAsync

```csharp
[Fact]
public async Task GetAsync_ShouldReturnValue_WhenKeyExists()
{
    // Arrange
    var key = "test:123";
    await _cache.SetAsync(key, new MyData { Value = 42 }, TimeSpan.FromMinutes(5));

    // Act
    var result = await _cache.GetAsync<MyData>(key);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(42, result.Value);
}
```

---

### Unit Test: Rate Limiting

```csharp
[Fact]
public async Task CheckRateLimitAsync_ShouldBlock_WhenLimitExceeded()
{
    // Arrange
    var key = "rate-limit:test";

    // Act
    for (int i = 1; i <= 10; i++)
        Assert.True(await _cache.CheckRateLimitAsync(key, 10, TimeSpan.FromMinutes(1)));

    // Exceed limit
    var allowed = await _cache.CheckRateLimitAsync(key, 10, TimeSpan.FromMinutes(1));

    // Assert
    Assert.False(allowed);
}
```

---

### Integration Test: Redis Failure

```csharp
[Fact]
public async Task GetAsync_ShouldReturnDefault_WhenRedisDown()
{
    // Arrange
    StopRedis(); // Simulate Redis failure

    // Act
    var result = await _cache.GetAsync<MyData>("test:123");

    // Assert
    Assert.Null(result); // Graceful degradation
}
```

---

## FAQs

**Q: Why singleton instead of scoped?**  
A: `IConnectionMultiplexer` is thread-safe and expensive to create. One instance per app.

**Q: Why not use IDistributedCache?**  
A: We need raw Redis primitives (counters, locks, pub/sub). `IDistributedCache` only supports Get/Set/Remove.

**Q: What if Redis is down on startup?**  
A: `AbortOnConnectFail = false` → app starts, multiplexer retries connection in background.

**Q: Can I store files in Redis?**  
A: No. Redis max value size is 512 MB but should only hold small objects (< 100 KB). Use Azure Blob for files.

**Q: Why encrypt only some keys?**  
A: Performance. Only sensitive data (session, embeddings) needs encryption. Daily decks, counters don't.

**Q: What happens if TTL is 0?**  
A: Redis rejects it. `CacheTtl.UntilMidnightUtc()` ensures minimum 1 second TTL.

---

## Related Docs

- **[Caching Overview](./README.md)** — Architecture, use cases
- **[Redis Configuration](./redis.md)** — Connection setup
- **[Cache Keys](./cache-keys.md)** — Key naming conventions
- **[Cache Invalidation](./invalidation.md)** — Invalidation strategies

---

## Changelog

| Date | Change |
|---|---|
| 2026-10-07 | Initial CacheService implementation documentation |
| 2026-06-04 | Added distributed lock operations (AcquireLockAsync, ReleaseLockAsync) |
| 2026-05-20 | Added encryption for sensitive keys (session, embedding) |
| 2026-05-01 | Initial CacheService implementation (Get/Set/Delete) |
