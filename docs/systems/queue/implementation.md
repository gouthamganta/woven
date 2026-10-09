# Queue Implementation Patterns

**Last Updated:** 2026-10-07  
**Framework:** .NET 10 BackgroundService

---

## BackgroundService Pattern

All batch workers inherit from `Microsoft.Extensions.Hosting.BackgroundService`.

### Base Template

```csharp
public class ExampleBatchWorker : BackgroundService
{
    // 1. Lock key + expiry (for distributed locking)
    private const string LockKey = "lock:example-batch";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(2);

    // 2. Dependencies (injected via constructor)
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<ExampleBatchWorker> _logger;

    public ExampleBatchWorker(
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        ILogger<ExampleBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache        = cache;
        _logger       = logger;
    }

    // 3. ExecuteAsync — runs when host starts
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[ExampleBatch] Started — nightly run at 03:00 UTC");

        while (!ct.IsCancellationRequested)
        {
            // 4. Wait for next scheduled run
            await WaitForNextRunAsync(ct);
            if (ct.IsCancellationRequested) break;

            // 5. Acquire distributed lock (skip if another pod running)
            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
            {
                _logger.LogInformation("[ExampleBatch] Skipping — another pod holds the lock");
                continue;
            }

            var start = DateTime.UtcNow;
            _logger.LogInformation("[ExampleBatch] Starting batch at {Time}", start);

            try
            {
                await RunBatchAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExampleBatch] Batch failed");
            }
            finally
            {
                // 6. Always release lock (even on failure)
                await _cache.ReleaseLockAsync(LockKey, ct);
                _logger.LogInformation("[ExampleBatch] Done in {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
        }
    }

    // 7. Calculate next run time (e.g., daily at 03:00 UTC)
    private static async Task WaitForNextRunAsync(CancellationToken ct)
    {
        var now  = DateTime.UtcNow;
        var next = now.Date.AddHours(3);  // 03:00 UTC today
        if (now >= next) next = next.AddDays(1);  // Already passed → tomorrow
        await Task.Delay(next - now, ct);
    }

    // 8. Core batch logic
    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        // Example: process all active users
        var userIds = await db.Users
            .Where(u => u.LastActiveAt >= DateTime.UtcNow.AddDays(-7))
            .Select(u => u.Id)
            .ToListAsync(ct);

        int processed = 0, errors = 0;

        foreach (var userId in userIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Per-user processing (use inner scope to avoid memory leaks)
                using var innerScope = _scopeFactory.CreateScope();
                var svc = innerScope.ServiceProvider.GetRequiredService<IExampleService>();
                await svc.ProcessAsync(userId, ct);
                processed++;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "[ExampleBatch] Failed for user {UserId}", userId);
            }
        }

        _logger.LogInformation("[ExampleBatch] Completed — processed={P} errors={E}",
            processed, errors);
    }
}
```

---

## Key Patterns

### 1. Dependency Injection (Scoped Services)

**Why `IServiceScopeFactory` (Not Direct Injection)?**

```csharp
// ❌ BAD — DbContext is singleton (memory leak + concurrency issues)
public ExampleBatchWorker(WovenDbContext db, IExampleService svc)
{
    _db  = db;  // Lives for entire pod lifetime → memory leak
    _svc = svc;
}

// ✅ GOOD — Create scope per batch run
public ExampleBatchWorker(IServiceScopeFactory scopeFactory)
{
    _scopeFactory = scopeFactory;
}

protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        
        // Scope disposed after batch run → DbContext disposed → memory freed
    }
}
```

**Rule:**
- `BackgroundService` = **singleton** (1 instance per pod lifetime)
- `WovenDbContext` = **scoped** (1 instance per request/batch run)
- Create **new scope** for each batch run (via `IServiceScopeFactory`)

---

### 2. Distributed Locking (Redis)

**Why Locks?**
- Prevent duplicate work if multiple pods run the same worker
- Example: API pod misconfigured (`WOVEN_DISABLE_BATCH_WORKERS=false`) → both API + workers run batches

**Implementation (CacheService):**

```csharp
public interface ICacheService
{
    Task<bool> AcquireLockAsync(string key, TimeSpan expiry, CancellationToken ct);
    Task ReleaseLockAsync(string key, CancellationToken ct);
}

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _redis;

    public async Task<bool> AcquireLockAsync(string key, TimeSpan expiry, CancellationToken ct)
    {
        // SET key value NX EX expiry → only succeeds if key doesn't exist
        return await _redis.StringSetAsync(
            key,
            Environment.MachineName,  // Value = pod hostname (for debugging)
            expiry,
            when: When.NotExists);
    }

    public async Task ReleaseLockAsync(string key, CancellationToken ct)
    {
        await _redis.KeyDeleteAsync(key);
    }
}
```

**Lock Expiry Strategy:**

| Worker Runtime | Lock Expiry | Reasoning |
|----------------|-------------|-----------|
| < 5 min | 30 min | Fast worker → short expiry (quick recovery if crash) |
| 10–20 min | 2h | Medium worker → medium expiry |
| 30+ min | 6h | Slow worker → long expiry (prevent premature expiry) |

**Lock Renewal (Future):**

Currently, locks are **set once** and never renewed. If a worker runs longer than lock expiry, another pod can steal the lock.

**Future improvement:**
```csharp
// Start background renewal task
var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
_ = Task.Run(async () =>
{
    while (!renewalCts.Token.IsCancellationRequested)
    {
        await Task.Delay(LockExpiry / 2, renewalCts.Token);
        await _cache.ExtendLockAsync(LockKey, LockExpiry, renewalCts.Token);
    }
}, renewalCts.Token);

try
{
    await RunBatchAsync(ct);
}
finally
{
    renewalCts.Cancel();  // Stop renewal
    await _cache.ReleaseLockAsync(LockKey, ct);
}
```

---

### 3. Schedule Calculation (Wait Until Next Run)

**Pattern 1: Daily at Fixed Time (e.g., 03:00 UTC)**

```csharp
private static async Task WaitForNextRunAsync(CancellationToken ct)
{
    var now  = DateTime.UtcNow;
    var next = now.Date.AddHours(3);  // 03:00 UTC today
    if (now >= next) next = next.AddDays(1);  // Already passed → tomorrow
    await Task.Delay(next - now, ct);
}
```

**Pattern 2: Weekly on Specific Day (e.g., Sunday 04:00 UTC)**

```csharp
private static async Task SleepUntilSunday4AmUtcAsync(CancellationToken ct)
{
    var now = DateTime.UtcNow;
    int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
    
    // If today is Sunday and we've already passed 04:00, target next Sunday
    if (daysUntilSunday == 0 && now.Hour >= 4) daysUntilSunday = 7;
    
    var next = now.Date.AddDays(daysUntilSunday).AddHours(4);
    var delay = next - now;
    
    if (delay > TimeSpan.Zero)
        await Task.Delay(delay, ct);
}
```

**Pattern 3: Polling (e.g., Every 5 Minutes)**

```csharp
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await ProcessAsync(ct);
        await Task.Delay(TimeSpan.FromMinutes(5), ct);
    }
}
```

**Why Not Cron Libraries (Quartz.NET, Hangfire)?**
- Simpler — no external dependencies
- Transparent — schedule logic visible in code
- Works with distributed locking (cron libraries don't handle multi-pod)

---

### 4. Cancellation Handling

**Why `CancellationToken`?**
- Graceful shutdown when pod terminates (SIGTERM from Azure Container Apps)
- Current limitation: batch runs to completion (ignores cancellation mid-loop)

**Current Pattern:**

```csharp
foreach (var userId in userIds)
{
    if (ct.IsCancellationRequested) break;  // ← Check before each iteration
    await ProcessUserAsync(userId, ct);
}
```

**Future: Chunked Processing with Checkpointing**

```csharp
const int chunkSize = 100;
int offset = await _cache.GetAsync<int>("example-batch:offset") ?? 0;

while (offset < userIds.Count)
{
    if (ct.IsCancellationRequested)
    {
        // Save progress for next run
        await _cache.SetAsync("example-batch:offset", offset, TimeSpan.FromHours(1));
        _logger.LogInformation("[ExampleBatch] Interrupted — saved offset={Offset}", offset);
        break;
    }

    var chunk = userIds.Skip(offset).Take(chunkSize).ToList();
    foreach (var userId in chunk)
        await ProcessUserAsync(userId, ct);

    offset += chunkSize;
}

// Clear checkpoint on completion
await _cache.DeleteAsync("example-batch:offset");
```

---

### 5. Error Handling (Per-User vs Batch-Level)

**Per-User Errors (Log + Continue):**

```csharp
foreach (var userId in userIds)
{
    try
    {
        await ProcessUserAsync(userId, ct);
        processed++;
    }
    catch (Exception ex)
    {
        errors++;
        _logger.LogWarning(ex, "[ExampleBatch] Failed for user {UserId}", userId);
        // ← Continue to next user (don't fail entire batch)
    }
}
```

**Batch-Level Errors (Fail Fast):**

```csharp
try
{
    await RunBatchAsync(ct);
}
catch (OperationCanceledException)
{
    // Expected — pod shutting down
    break;
}
catch (Exception ex)
{
    // Unexpected — log and continue to next scheduled run
    _logger.LogError(ex, "[ExampleBatch] Batch failed");
}
```

**When to Fail Fast vs Continue?**

| Error Type | Strategy | Example |
|------------|----------|---------|
| **User-level** | Log + Continue | OpenAI API 429 for 1 user → skip that user |
| **Batch-level** | Fail Fast | PostgreSQL connection lost → stop batch |
| **Transient** | Retry (exponential backoff) | Network timeout → retry 3x |
| **Permanent** | Skip | Invalid user ID → log warning, skip |

---

### 6. Logging (Structured + Correlation IDs)

**Pattern:**

```csharp
_logger.LogInformation(
    "[ExampleBatch] Processing user {UserId} | CorrelationId={Cid}",
    userId,
    _correlation.CorrelationId);  // ← From CorrelationIdMiddleware
```

**Key Metrics to Log:**

1. **Start:**
   ```
   [ExampleBatch] Starting batch at {Time}
   ```

2. **Progress (every N items):**
   ```
   [ExampleBatch] Processed {Count}/{Total} users
   ```

3. **Errors:**
   ```
   [ExampleBatch] Failed for user {UserId} | Error={Error}
   ```

4. **Completion:**
   ```
   [ExampleBatch] Done in {Ms}ms — processed={P} errors={E}
   ```

**Log Levels:**

| Level | Use Case |
|-------|----------|
| `LogInformation` | Start, completion, progress |
| `LogWarning` | Per-user errors, lock acquisition failures |
| `LogError` | Batch-level failures, unexpected exceptions |
| `LogDebug` | Verbose details (disabled in production) |

**Never Log:**
- PII (user email, phone, messages)
- Secrets (API keys, connection strings)
- Full stack traces (logged automatically by exception handler)

---

### 7. Memory Management (Avoid OOM)

**Problem:**
Loading all users into memory → OutOfMemoryException for large datasets.

**Bad:**
```csharp
// ❌ Loads 10M users into memory
var users = await db.Users.ToListAsync(ct);
foreach (var user in users)
    await ProcessUserAsync(user, ct);
```

**Good (Streaming):**
```csharp
// ✅ Streams 1000 users at a time
await foreach (var user in db.Users.AsAsyncEnumerable().WithCancellation(ct))
{
    await ProcessUserAsync(user, ct);
}
```

**Good (Chunked):**
```csharp
// ✅ Processes 1000 users per chunk
const int chunkSize = 1000;
int offset = 0;

while (true)
{
    var chunk = await db.Users
        .OrderBy(u => u.Id)
        .Skip(offset)
        .Take(chunkSize)
        .ToListAsync(ct);

    if (chunk.Count == 0) break;

    foreach (var user in chunk)
        await ProcessUserAsync(user, ct);

    offset += chunkSize;
}
```

**Current Workers:**
- ✅ `ConnectionScoreBatchWorker` — chunked upsert (500 rows/chunk)
- ❌ `WeightLearningBatchWorker` — loads all eligible users (OK for now, < 10K users)
- ❌ `EmbeddingBatchWorker` — loads all users (14-hour runtime, high memory)

---

### 8. Database Transactions (When to Use)

**Rule:** Use transactions when batch writes must be **atomic** (all-or-nothing).

**Example 1: Atomic Upsert (Connection Scores)**

```csharp
// ✅ Raw SQL upsert (atomic per chunk)
await db.Database.ExecuteSqlRawAsync(@"
    INSERT INTO ""ConnectionScores"" (""ViewerId"", ""CandidateId"", ""Score"", ""ComputedAt"")
    VALUES {0}
    ON CONFLICT (""ViewerId"", ""CandidateId"") 
    DO UPDATE SET ""Score""=EXCLUDED.""Score"", ""ComputedAt""=EXCLUDED.""ComputedAt""
", scores);
```

**Example 2: Multi-Table Write (Needs Transaction)**

```csharp
using var transaction = await db.Database.BeginTransactionAsync(ct);

try
{
    // Write to UserVectors
    db.UserVectors.Update(userVector);
    await db.SaveChangesAsync(ct);

    // Write to MatchSignalLogs
    db.MatchSignalLogs.Add(signal);
    await db.SaveChangesAsync(ct);

    await transaction.CommitAsync(ct);
}
catch
{
    await transaction.RollbackAsync(ct);
    throw;
}
```

**When NOT to Use Transactions:**
- Per-user processing (isolation not needed)
- Read-only queries
- Bulk inserts with `ON CONFLICT` (already atomic)

---

## Service Bus Pattern (Real-Time Workers)

**Use Case:** Async message processing (e.g., tile embeddings)

### Queue Sender

```csharp
public interface IEmbeddingQueue
{
    ValueTask EnqueueAsync(Guid tileId, CancellationToken ct = default);
}

public class ServiceBusEmbeddingQueue : IEmbeddingQueue, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public ServiceBusEmbeddingQueue(ServiceBusClient client)
        => _sender = client.CreateSender("tile-embedding");

    public async ValueTask EnqueueAsync(Guid tileId, CancellationToken ct = default)
    {
        var msg = new ServiceBusMessage(tileId.ToString())
        {
            MessageId  = tileId.ToString(),  // Deduplication
            TimeToLive = TimeSpan.FromDays(2)
        };
        await _sender.SendMessageAsync(msg, ct);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
```

### Queue Processor

```csharp
public class ServiceBusEmbeddingWorker : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly TileEmbeddingService _embeddings;

    public ServiceBusEmbeddingWorker(
        ServiceBusClient client,
        TileEmbeddingService embeddings)
    {
        _embeddings = embeddings;
        _processor  = client.CreateProcessor("tile-embedding", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls   = 4,       // Process 4 messages in parallel
            AutoCompleteMessages = false    // Manual ACK (Complete/Abandon)
        });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync   += OnErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _processor.StartProcessingAsync(ct);
        
        // Block until cancellation (processor runs on background threads)
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        
        await _processor.StopProcessingAsync();
        await _processor.DisposeAsync();
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var tileId = Guid.Parse(args.Message.Body.ToString());
            await _embeddings.EmbedTileAsync(tileId, args.CancellationToken);
            
            await args.CompleteMessageAsync(args.Message);  // ✅ Success → remove from queue
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message");
            await args.AbandonMessageAsync(args.Message);   // ❌ Failure → retry with backoff
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Processor error");
        return Task.CompletedTask;
    }
}
```

**Key Differences vs Batch Workers:**
- No schedule (message-driven)
- No distributed locking (Service Bus handles deduplication)
- Manual ACK (`Complete` vs `Abandon`)
- Concurrency (`MaxConcurrentCalls = 4`)

---

## Testing

### Unit Tests (Mock Dependencies)

```csharp
public class ConnectionScoreBatchWorkerTests
{
    [Fact]
    public async Task RunBatchAsync_ComputesScoresCorrectly()
    {
        // Arrange
        var mockCache = new Mock<ICacheService>();
        mockCache.Setup(c => c.AcquireLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var options = new DbContextOptionsBuilder<WovenDbContext>()
            .UseInMemoryDatabase("test_db")
            .Options;
        var db = new WovenDbContext(options);

        // Seed test data
        db.MatchSignalLogs.Add(new MatchSignalLog
        {
            ViewerId    = 1,
            CandidateId = 2,
            EventType   = MatchSignalEventTypes.TrialAccepted,
            EventValue  = 1.0f,
            OccurredAt  = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var worker = new ConnectionScoreBatchWorker(
            new MockServiceScopeFactory(db),
            mockCache.Object,
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<ConnectionScoreBatchWorker>>());

        // Act
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);  // Let worker run once
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var score = await db.ConnectionScores.FirstOrDefaultAsync(c => c.ViewerId == 1 && c.CandidateId == 2);
        Assert.NotNull(score);
        Assert.InRange(score.Score, 0.2f, 0.3f);  // TrialAccepted weight = 0.22
    }
}
```

### Integration Tests (Real DB + Redis)

```csharp
[Trait("Category", "Integration")]
public class ConnectionScoreBatchWorkerIntegrationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ConnectionScoreBatchWorkerIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Skip = "Integration test — requires PostgreSQL + Redis")]
    public async Task EndToEnd_ProcessesAllSignals()
    {
        // Arrange
        var db = _fixture.CreateDbContext();
        var redis = _fixture.CreateRedis();

        // Seed 1000 signals
        for (int i = 0; i < 1000; i++)
        {
            db.MatchSignalLogs.Add(new MatchSignalLog { ... });
        }
        await db.SaveChangesAsync();

        var worker = _fixture.CreateWorker<ConnectionScoreBatchWorker>();

        // Act
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMinutes(1));  // Wait for completion
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var scoreCount = await db.ConnectionScores.CountAsync();
        Assert.InRange(scoreCount, 900, 1000);  // Allow some skipped (< 4 signals)
    }
}
```

### Load Tests (Simulate Production Volume)

```csharp
[Trait("Category", "Load")]
public class ConnectionScoreBatchWorkerLoadTests
{
    [Fact(Skip = "Load test — run manually")]
    public async Task LoadTest_10MillionSignals()
    {
        var db = CreateProductionDb();  // Real Azure PostgreSQL

        // Seed 10M signals (takes ~10 minutes)
        for (int i = 0; i < 10_000_000; i++)
        {
            db.MatchSignalLogs.Add(new MatchSignalLog { ... });
            if (i % 10000 == 0) await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();

        var worker = CreateWorker<ConnectionScoreBatchWorker>();

        // Measure runtime
        var sw = Stopwatch.StartNew();
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromHours(1));  // Max 1h timeout
        await worker.StopAsync(CancellationToken.None);
        sw.Stop();

        // Assert
        Assert.InRange(sw.Elapsed.TotalMinutes, 5, 15);  // Target: 10 min
    }
}
```

---

## Performance Optimization

### 1. Batch SQL Operations (Avoid N+1 Queries)

**Bad (N+1):**
```csharp
// ❌ 10K queries (1 per user)
foreach (var userId in userIds)
{
    var user = await db.Users.FindAsync(userId);
    await ProcessUserAsync(user);
}
```

**Good (Single Query):**
```csharp
// ✅ 1 query (bulk load)
var users = await db.Users
    .Where(u => userIds.Contains(u.Id))
    .ToListAsync();

foreach (var user in users)
    await ProcessUserAsync(user);
```

### 2. Raw SQL for Bulk Upserts

**Bad (EF Core):**
```csharp
// ❌ 10K round trips
foreach (var score in scores)
{
    var existing = await db.ConnectionScores.FindAsync(score.ViewerId, score.CandidateId);
    if (existing != null)
        existing.Score = score.Score;
    else
        db.ConnectionScores.Add(score);
}
await db.SaveChangesAsync();
```

**Good (Raw SQL):**
```csharp
// ✅ 1 query (bulk upsert)
await db.Database.ExecuteSqlRawAsync(@"
    INSERT INTO ""ConnectionScores"" (""ViewerId"", ""CandidateId"", ""Score"", ""ComputedAt"")
    VALUES {0}
    ON CONFLICT (""ViewerId"", ""CandidateId"")
    DO UPDATE SET ""Score""=EXCLUDED.""Score"", ""ComputedAt""=EXCLUDED.""ComputedAt""
", scores);
```

### 3. Parallel Processing (Use with Caution)

**Bad (Sequential):**
```csharp
// ❌ 10K users × 5 sec = 14 hours
foreach (var userId in userIds)
    await EmbedUserAsync(userId);
```

**Good (Parallel):**
```csharp
// ✅ 10K users ÷ 10 workers = 1.4 hours
await Parallel.ForEachAsync(userIds, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (userId, ct) =>
{
    await EmbedUserAsync(userId, ct);
});
```

**Caution:**
- Don't exceed DB connection pool (default: 100 connections)
- Don't exceed OpenAI rate limits (tier 3: 10K RPM)
- Monitor CPU usage (parallel = higher CPU)

### 4. Redis Pipelining (Batch Cache Writes)

**Bad:**
```csharp
// ❌ 10K round trips to Redis
foreach (var score in scores)
    await _cache.SetAsync($"score:{score.ViewerId}:{score.CandidateId}", score.Score);
```

**Good (Pipeline):**
```csharp
// ✅ 1 round trip (Redis pipeline)
var batch = _redis.CreateBatch();
foreach (var score in scores)
    batch.StringSetAsync($"score:{score.ViewerId}:{score.CandidateId}", score.Score);
await batch.ExecuteAsync();
```

---

## Common Pitfalls

### 1. Memory Leaks (Long-Running Workers)

**Problem:**
```csharp
// ❌ DbContext never disposed → memory leak
private readonly WovenDbContext _db;

public ExampleBatchWorker(WovenDbContext db)
{
    _db = db;  // Singleton worker holds scoped DbContext → leak
}
```

**Fix:**
```csharp
// ✅ Create new scope per batch run
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        await RunBatchAsync(db, ct);
    }  // ← Scope disposed → DbContext disposed → memory freed
}
```

### 2. Lock Expiry Too Short

**Problem:**
```csharp
// ❌ Lock expires after 30 min, but worker runs for 1 hour
private static readonly TimeSpan LockExpiry = TimeSpan.FromMinutes(30);
```

**Symptom:**
- Lock expires mid-run → another pod acquires lock → duplicate work

**Fix:**
```csharp
// ✅ Lock expiry > max expected runtime
private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(2);
```

### 3. Ignoring CancellationToken

**Problem:**
```csharp
// ❌ Worker doesn't check CancellationToken → runs to completion on shutdown
foreach (var userId in userIds)
    await ProcessUserAsync(userId);
```

**Symptom:**
- Pod shutdown takes 5+ minutes (waits for batch to complete)
- Azure kills pod (SIGKILL) → batch interrupted mid-run

**Fix:**
```csharp
// ✅ Check token before each iteration
foreach (var userId in userIds)
{
    if (ct.IsCancellationRequested) break;
    await ProcessUserAsync(userId, ct);
}
```

### 4. Logging PII (GDPR Violation)

**Problem:**
```csharp
// ❌ Logs user email (PII)
_logger.LogInformation("Processing user {Email}", user.Email);
```

**Fix:**
```csharp
// ✅ Log user ID only (non-PII)
_logger.LogInformation("Processing user {UserId}", user.Id);
```

---

## Deployment Checklist

**Before Deploying New Worker:**

- [ ] Lock key unique (no conflicts with other workers)
- [ ] Lock expiry > max expected runtime
- [ ] CancellationToken handled (graceful shutdown)
- [ ] Logging includes start/end times + error counts
- [ ] Memory tested (no leaks on long runs)
- [ ] Schedule doesn't conflict with other workers (stagger times)
- [ ] Dependencies registered in `Program.cs`
- [ ] Worker disabled on API pods (`if (!batchWorkersDisabled)`)
- [ ] Integration test passes (real DB + Redis)
- [ ] Alert configured (runtime > 2x expected)

---

**Next:** [README.md](README.md) — Queue system overview
