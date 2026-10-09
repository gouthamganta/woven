# Azure Service Bus Integration

**Last Updated:** 2026-10-07  
**Component:** Tile Embedding Queue

---

## What This Is

Azure Service Bus is used for **asynchronous tile embedding** — when a user posts a new tile (content post in Commons), the backend:

1. Saves the tile to DB
2. **Enqueues tile ID** to Azure Service Bus (`tile-embedding` queue)
3. Returns 201 Created immediately (no blocking on AI calls)

A background worker (`ServiceBusEmbeddingWorker`) pulls messages from the queue and calls `TileEmbeddingService.EmbedTileAsync()`.

---

## Why Service Bus (Not Redis)?

| Feature | Azure Service Bus | Redis Pub/Sub |
|---------|-------------------|---------------|
| **Message persistence** | ✅ Durable (survives pod restarts) | ❌ In-memory only |
| **Dead-letter queue** | ✅ Built-in DLQ for failed messages | ❌ Manual retry logic needed |
| **Retry with backoff** | ✅ Automatic (delivery count, TTL) | ❌ Manual implementation |
| **Azure-native** | ✅ Integrated with App Insights, RBAC | ⚠️ Separate infra |
| **Cost** | ~$0.05/million operations | ~$15/month (Azure Cache for Redis) |

Service Bus is **overkill for lightweight jobs** (balloon expiry, moderation), but ideal for **heavyweight AI jobs** with:
- Long processing time (5–15 seconds per tile)
- High failure rate (OpenAI rate limits, network errors)
- Need for retries and DLQ

---

## Queue Configuration

**Queue Name:** `tile-embedding`

**Settings (Azure Portal):**
```
Max Delivery Count: 10        # → DLQ after 10 failed attempts
TTL: 2 days                   # Abandon stale embeddings
Lock Duration: 5 minutes      # Worker has 5 min to complete
Max Size: 1 GB
Duplicate Detection: Off      # Idempotency handled in worker
```

**Connection String:**
- **Local dev:** User Secrets (`ServiceBus:ConnectionString`)
- **Production:** Azure Key Vault (`woven-servicebus-connection`)

---

## Code Architecture

### 1. Queue Sender (`ServiceBusEmbeddingQueue`)

**File:** `backend/WovenBackend/Services/Queue/ServiceBusEmbeddingQueue.cs`

**Interface:**
```csharp
public interface IEmbeddingQueue
{
    ValueTask EnqueueAsync(Guid tileId, CancellationToken ct = default);
}
```

**Implementation:**
```csharp
public sealed class ServiceBusEmbeddingQueue : IEmbeddingQueue, IAsyncDisposable
{
    internal const string QueueName = "tile-embedding";
    private readonly ServiceBusSender _sender;

    public ServiceBusEmbeddingQueue(ServiceBusClient client)
        => _sender = client.CreateSender(QueueName);

    public async ValueTask EnqueueAsync(Guid tileId, CancellationToken ct = default)
    {
        var msg = new ServiceBusMessage(tileId.ToString())
        {
            MessageId    = tileId.ToString(),  // Deduplication key
            TimeToLive   = TimeSpan.FromDays(2)
        };
        await _sender.SendMessageAsync(msg, ct);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
```

**Usage (from TileEndpoints.cs):**
```csharp
// POST /tiles
var tile = new Tile { ... };
db.Tiles.Add(tile);
await db.SaveChangesAsync(ct);

// Enqueue for background embedding (non-blocking)
await _embeddingQueue.EnqueueAsync(tile.Id, ct);

return Results.Created($"/tiles/{tile.Id}", tile);
```

---

### 2. Queue Processor (`ServiceBusEmbeddingWorker`)

**File:** `backend/WovenBackend/Services/Queue/ServiceBusEmbeddingQueue.cs` (same file)

**Implementation:**
```csharp
public sealed class ServiceBusEmbeddingWorker : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly TileEmbeddingService _embeddings;
    private readonly ILogger<ServiceBusEmbeddingWorker> _logger;

    public ServiceBusEmbeddingWorker(
        ServiceBusClient client,
        TileEmbeddingService embeddings,
        ILogger<ServiceBusEmbeddingWorker> logger)
    {
        _embeddings = embeddings;
        _logger     = logger;
        _processor  = client.CreateProcessor(
            ServiceBusEmbeddingQueue.QueueName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls   = 4,  // Process 4 tiles in parallel
                AutoCompleteMessages = false // Manual Complete/Abandon
            });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync   += OnErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _processor.StartProcessingAsync(ct);
        // Block until cancellation — processor runs on its own threads
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        await _processor.StopProcessingAsync();
        await _processor.DisposeAsync();
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        if (!Guid.TryParse(body, out var tileId))
        {
            _logger.LogWarning("[ServiceBusEmbeddingWorker] Invalid tileId: {Body}", body);
            await args.DeadLetterMessageAsync(args.Message, "invalid-tile-id");
            return;
        }

        try
        {
            await _embeddings.EmbedTileAsync(tileId, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);  // ✅ Success
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ServiceBusEmbeddingWorker] Failed tile {TileId}", tileId);
            // Abandon → Service Bus retries with exponential backoff
            // DLQ after 10 failures (Max Delivery Count)
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "[ServiceBusEmbeddingWorker] Processor error source={Source}",
            args.ErrorSource);
        return Task.CompletedTask;
    }
}
```

---

## Message Flow

```
┌─────────────────────────────────────────────────────────────┐
│  API Pod: POST /tiles                                       │
│  ─────────────────────                                      │
│  1. Validate + save tile to PostgreSQL                     │
│  2. EnqueueAsync(tileId) → Azure Service Bus                │
│  3. Return 201 Created (< 50ms)                             │
└─────────────────────────────────────────────────────────────┘
                            ↓
                  [Azure Service Bus]
                  Queue: tile-embedding
                  TTL: 2 days, Max Delivery: 10
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  Workers Pod: ServiceBusEmbeddingWorker                     │
│  ───────────────────────────────────────                    │
│  1. Pull message (lock duration: 5 min)                     │
│  2. EmbedTileAsync(tileId):                                 │
│     → OpenAI text-embedding-ada-002 (caption + hashtags)    │
│     → Save embedding to tile_embeddings table               │
│  3. CompleteMessageAsync() → remove from queue              │
│                                                              │
│  On failure:                                                │
│  → AbandonMessageAsync() → retry with backoff               │
│  → After 10 failures → Dead-Letter Queue                    │
└─────────────────────────────────────────────────────────────┘
                            ↓
              [Dead-Letter Queue (DLQ)]
              Manual investigation needed:
              → OpenAI API down?
              → Malformed tile data?
              → Rate limit exhausted?
```

---

## Error Handling

### 1. Transient Errors (Network, Rate Limits)

**Behavior:** `AbandonMessageAsync()` → Service Bus retries with **exponential backoff**

**Examples:**
- HTTP 429 (OpenAI rate limit)
- HTTP 503 (OpenAI service unavailable)
- Network timeout

**Retry Schedule (approximated):**
```
Attempt 1: Immediate
Attempt 2: +10s
Attempt 3: +30s
Attempt 4: +1m
Attempt 5: +5m
...
Attempt 10: +30m → DLQ
```

### 2. Permanent Errors (Invalid Data)

**Behavior:** `DeadLetterMessageAsync(reason)` → skip retries, send to DLQ

**Examples:**
- Invalid tile ID (malformed GUID)
- Tile deleted before embedding completed
- Tile contains PII/unsafe content (OpenAI moderation flag)

### 3. Processor Errors (Connection Lost)

**Behavior:** `OnErrorAsync()` logs error, processor auto-reconnects

**Examples:**
- Service Bus connection dropped
- Network partition
- Pod crash → messages return to queue (lock expires after 5 min)

---

## Monitoring & Observability

### Logs (Serilog → Azure Log Analytics)

**Enqueue:**
```
[TileEndpoints] Tile {TileId} created, queued for embedding
```

**Processing:**
```
[ServiceBusEmbeddingWorker] Processing tile {TileId}
[TileEmbeddingService] Embedded tile {TileId} | Dim=1536 Cost=$0.0001
[ServiceBusEmbeddingWorker] Completed tile {TileId}
```

**Errors:**
```
[ServiceBusEmbeddingWorker] Failed tile {TileId} | Attempt=3/10
[ServiceBusEmbeddingWorker] Tile {TileId} moved to DLQ | Reason=invalid-tile-id
```

### Azure Metrics (Service Bus Namespace)

| Metric | Alert Threshold |
|--------|-----------------|
| **Active Message Count** | > 1000 (backlog growing) |
| **Dead-Letter Message Count** | > 10 (persistent failures) |
| **Incoming Messages/sec** | > 100 (traffic spike) |
| **Successful Requests %** | < 95% (high failure rate) |
| **Throttled Requests** | > 0 (quota exceeded) |

### Application Insights

**Custom Events:**
- `TileEmbedding.Enqueued` (tile ID, user ID)
- `TileEmbedding.Completed` (tile ID, duration, cost)
- `TileEmbedding.Failed` (tile ID, error, attempt count)

---

## DLQ Investigation Runbook

**When DLQ count > 10:**

1. **View DLQ messages (Azure Portal):**
   ```
   Service Bus Namespace → Queues → tile-embedding → Dead-letter queue
   ```

2. **Common DLQ Reasons:**
   | Reason | Fix |
   |--------|-----|
   | `invalid-tile-id` | Data corruption — investigate caller |
   | `MaxDeliveryCountExceeded` | OpenAI API down — replay messages after recovery |
   | `TTLExpiredException` | Message sat in queue >2 days — safe to delete |

3. **Replay DLQ messages (Service Bus Explorer):**
   - Select messages
   - **Resubmit** → sends back to main queue
   - Worker retries immediately

4. **Purge DLQ (if safe):**
   ```bash
   az servicebus queue purge \
     --namespace-name woven-prod-sb \
     --name tile-embedding/$deadletterqueue \
     --resource-group woven-prod-rg
   ```

---

## Configuration (Program.cs)

**Registration:**
```csharp
// Service Bus client (singleton)
var serviceBusConnStr = builder.Configuration["ServiceBus:ConnectionString"]
    ?? throw new InvalidOperationException("ServiceBus:ConnectionString not configured");
var sbClient = new ServiceBusClient(serviceBusConnStr);
builder.Services.AddSingleton(sbClient);

// Queue sender (scoped — used by API endpoints)
builder.Services.AddScoped<IEmbeddingQueue, ServiceBusEmbeddingQueue>();

// Queue processor (hosted service — runs on workers pod only)
if (!batchWorkersDisabled)
    builder.Services.AddHostedService<ServiceBusEmbeddingWorker>();
```

**Why scoped for sender?**
- `ServiceBusSender` is **thread-safe** but **not disposable per-request**
- Scoped registration ensures sender is disposed at end of request scope
- Singleton `ServiceBusClient` handles connection pooling

---

## Cost Analysis

**Azure Service Bus (Basic tier):**
- $0.05 per million operations
- 1 operation = 1 enqueue OR 1 process message
- 10,000 tiles/day = 20,000 operations/day = **$0.001/day = $0.36/month**

**OpenAI API (called by worker):**
- text-embedding-ada-002: $0.0001 / 1K tokens
- Average tile: 50 tokens → **$0.000005/tile**
- 10,000 tiles/day = **$0.05/day = $1.50/month**

**Total:** ~$2/month (Azure Service Bus is negligible)

---

## Testing

### Unit Tests
```csharp
// Test queue sender (mocked ServiceBusClient)
[Fact]
public async Task EnqueueAsync_SendsMessageWithCorrectId()
{
    var mockClient = new Mock<ServiceBusClient>();
    var mockSender = new Mock<ServiceBusSender>();
    mockClient.Setup(c => c.CreateSender("tile-embedding")).Returns(mockSender.Object);

    var queue = new ServiceBusEmbeddingQueue(mockClient.Object);
    var tileId = Guid.NewGuid();

    await queue.EnqueueAsync(tileId);

    mockSender.Verify(s => s.SendMessageAsync(
        It.Is<ServiceBusMessage>(m => m.MessageId == tileId.ToString()),
        It.IsAny<CancellationToken>()));
}
```

### Integration Tests
```csharp
// Test end-to-end (requires real Service Bus connection)
[Fact(Skip = "Integration test — requires Azure Service Bus")]
public async Task EmbedTileAsync_CompletesSuccessfully()
{
    var connStr = Environment.GetEnvironmentVariable("SERVICEBUS_CONN");
    var client = new ServiceBusClient(connStr);
    var queue = new ServiceBusEmbeddingQueue(client);

    var tileId = Guid.NewGuid();
    await queue.EnqueueAsync(tileId);

    // Wait for worker to process
    await Task.Delay(TimeSpan.FromSeconds(10));

    // Assert tile has embedding in DB
    var tile = await db.TileEmbeddings.FindAsync(tileId);
    Assert.NotNull(tile);
    Assert.Equal(1536, tile.Embedding.Length);
}
```

---

## Migration Path (If Moving Away from Service Bus)

**Alternative: Redis Streams**

```csharp
// Sender
await redis.XAddAsync("tile-embedding", [("tileId", tileId.ToString())]);

// Consumer
while (true)
{
    var entries = await redis.XReadAsync("tile-embedding", ">");
    foreach (var entry in entries)
    {
        var tileId = entry.Values.First(v => v.Name == "tileId").Value;
        await EmbedTileAsync(Guid.Parse(tileId));
        await redis.XAckAsync("tile-embedding", "worker-group", entry.Id);
    }
}
```

**Trade-offs:**
- ✅ Lower cost (~$0 vs $0.36/month)
- ✅ Lower latency (no network hop to Service Bus)
- ❌ Manual DLQ logic
- ❌ Manual retry with backoff
- ❌ No Azure-native monitoring (need custom metrics)

**Recommendation:** Keep Service Bus. Cost difference is negligible ($0.36/month), and DLQ + retry logic are production-critical.

---

**Next:** [workers.md](workers.md) — All batch workers (schedules, what they do)
