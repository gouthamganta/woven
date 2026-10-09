# Worker Scheduling & Topology

**Last Updated:** 2026-10-07  
**Environment:** Azure Container Apps (production)

---

## Pod Topology

Woven runs two types of pods:

### 1. API Pods (2+ replicas)

**Purpose:** Serve HTTP requests (low-latency, high-throughput)

**Configuration:**
```bash
WOVEN_DISABLE_BATCH_WORKERS=true  # Heavy workers disabled
```

**Workers Running:**
- `BalloonExpiryWorker` (real-time — closes expired balloons)
- `ModerationWorker` (real-time — content flagging)
- `TileExpiryWorker` (real-time — deletes old tiles)
- `TileViewProcessorWorker` (real-time — aggregates views every 5 min)

**Scaling:**
- Min: 2 replicas
- Max: 10 replicas
- Rule: CPU > 70% OR HTTP queue depth > 100

**Why Disable Batch Workers?**
- Batch workers consume **CPU + memory** for 10–30 minutes
- During batch runs, API latency spikes (P95: 200ms → 800ms)
- Solution: offload to dedicated workers pod

---

### 2. Workers Pod (exactly 1 replica)

**Purpose:** Run nightly batch jobs (ECHO learning, embeddings, trust scoring)

**Configuration:**
```bash
WOVEN_DISABLE_BATCH_WORKERS=false  # Default (env var omitted)
```

**Workers Running:**
- All 12 batch workers (see [workers.md](workers.md))
- `ServiceBusEmbeddingWorker` (real-time — tile embeddings)

**Scaling:**
- Min: 1 replica
- Max: 1 replica (hard limit)

**Why Exactly 1 Replica?**
- **Redis distributed locks** prevent duplicate work across pods
- But locks add overhead (lock acquisition, renewal, expiry checks)
- Simpler to run **1 pod with all workers** than N pods competing for locks
- If pod crashes, Azure Container Apps auto-restarts (< 30s downtime)

**Failure Mode:**
- Pod crashes at 03:55 UTC (mid-ConnectionScoreBatch)
- Lock held for 2 hours → expires at 05:55 UTC
- Azure restarts pod at 04:00 UTC
- Next run: ConnectionScoreBatch runs at 03:50 UTC next day

**Trade-off:**
- ❌ Single point of failure (no redundancy)
- ✅ Simple architecture (no lock contention)
- ✅ Workers complete faster (no lock overhead)

---

## Schedule (All Times UTC)

### Daily Workers (Every Night)

| Time | Worker | Duration | Dependencies |
|------|--------|----------|--------------|
| **02:00** | TrustBatchWorker | 5–10 min | None |
| **02:30** | EmbeddingBatchWorker | **14 hours** | None (overlaps with later workers) |
| **03:00** | CfBatchWorker | 10 min | None |
| **03:45** | SelfDisclosureBatchWorker | 2 min | ChatThreads, ChatMessages |
| **03:50** | ConnectionScoreBatchWorker | 5–10 min | MatchSignalLogs |
| **04:15** | PreferenceDriftBatchWorker | 8–12 min | ConnectionScores |
| **04:20** | LinUcbBatchWorker | 15–20 min | ConnectionScores, BehavioralFingerprints |
| **04:30** | InsightBatchWorker | 10–15 min | ConnectionScores, MatchSignalLogs |
| **05:00** | CfScoreBatchWorker | 10 min | ⚠️ **Duplicate** — remove this |

**Total Runtime:** 02:00–18:30 UTC (16.5 hours, but most run in parallel)

**Peak Load:**
- 02:30–18:30 UTC: `EmbeddingBatchWorker` running (long-running OpenAI calls)
- Other workers interleave: 03:00, 03:45, 03:50, 04:15, 04:20, 04:30

---

### Weekly Workers

| Day | Time | Worker | Duration | Frequency |
|-----|------|--------|----------|-----------|
| **Sunday** | 04:00 | WeightLearningBatchWorker | 10–15 min | Weekly |
| **Wednesday** | 18:00 | CoachingSummaryWorker | 15–20 min | Weekly |

**Why These Days/Times?**

**Sunday 04:00:**
- Weekend activity analyzed → Friday/Saturday data included
- Runs **after** Saturday night's ConnectionScoreBatch (03:50)
- Users idle (low API traffic)

**Wednesday 18:00:**
- Mid-week reflection ("How's your week going?")
- Delivered at 6pm UTC = 11:30pm IST (prime evening time in India)
- Users likely to read push notification before bed

---

### Real-Time Workers (No Schedule)

| Worker | Trigger | Concurrency |
|--------|---------|-------------|
| **ServiceBusEmbeddingWorker** | Message in `tile-embedding` queue | Max 4 concurrent |
| **TileViewProcessorWorker** | Every 5 minutes (polling) | Single-threaded |
| **BalloonExpiryWorker** | Every 1 minute (polling) | Single-threaded |
| **ModerationWorker** | Message in moderation queue | Max 2 concurrent |
| **TileExpiryWorker** | Every 1 hour (polling) | Single-threaded |

**Runs On:** All pods (API + workers)

---

## Distributed Locking (Redis)

All batch workers (except real-time workers) use **Redis distributed locks** to prevent duplicate work.

### Lock Pattern

```csharp
private const string LockKey = "lock:connection-score-batch";
private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(2);

// Before batch run
if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
{
    _logger.LogInformation("[Worker] Skipping — another pod holds the lock");
    return;
}

try
{
    await RunBatchAsync(ct);
}
finally
{
    await _cache.ReleaseLockAsync(LockKey, ct);
}
```

### Lock Keys

| Worker | Lock Key | Expiry |
|--------|----------|--------|
| TrustBatchWorker | `lock:trust-batch` | 4h |
| EmbeddingBatchWorker | `lock:embedding-batch` | 4h |
| SelfDisclosureBatchWorker | `lock:self-disclosure-batch` | 2h |
| ConnectionScoreBatchWorker | `lock:connection-score-batch` | 2h |
| WeightLearningBatchWorker | `lock:weight-learning-batch` | 6h |
| PreferenceDriftBatchWorker | `lock:preference-drift-batch` | 3h |
| LinUcbBatchWorker | `lock:linucb-batch` | 30 min |
| CoachingSummaryWorker | `lock:coaching-summary-batch` | 4h |

### Lock Expiry Strategy

**Why Different Expiries?**
- Short expiry (30 min): Fast workers → lock released quickly
- Long expiry (6h): Slow workers → prevent premature lock expiry

**Failure Scenarios:**

1. **Pod crashes mid-batch:**
   - Lock held in Redis (no automatic release)
   - Lock expires after TTL → next run acquires lock
   - Example: `ConnectionScoreBatch` crashes at 03:55, lock expires at 05:55, next run at 03:50 tomorrow

2. **Worker hangs (infinite loop):**
   - Lock held indefinitely (renewal loop keeps extending TTL)
   - Manual intervention needed: `redis-cli DEL lock:connection-score-batch`
   - Alert: Worker runtime > 2x expected duration

3. **Multiple pods try to run:**
   - Only first pod acquires lock (Redis `SET NX`)
   - Others log "Skipping — another pod holds the lock"
   - No duplicate work, no errors

---

## Incremental Processing (Optimization)

Some workers use **incremental processing** to avoid full scans every night.

### Example: ConnectionScoreBatchWorker

**Before (full scan):**
```sql
-- Every night, re-score ALL pairs with signals in 90-day window
-- Runtime: 30 minutes, 500K pairs
SELECT ViewerId, CandidateId, EventType, COUNT(*), MAX(EventValue)
FROM MatchSignalLogs
WHERE OccurredAt >= NOW() - INTERVAL '90 days'
GROUP BY ViewerId, CandidateId, EventType
```

**After (incremental):**
```sql
-- Only re-score pairs with NEW signals since last run
-- Runtime: 5 minutes, 10K pairs
SELECT ViewerId, CandidateId, EventType, COUNT(*), MAX(EventValue)
FROM MatchSignalLogs
WHERE OccurredAt >= :last_run AND OccurredAt >= NOW() - INTERVAL '90 days'
GROUP BY ViewerId, CandidateId, EventType
```

**Redis State:**
```bash
redis-cli GET connection-score-batch:last-run
# → "2026-10-06T03:50:15.123Z"
```

**Trade-offs:**
- ✅ 6x faster (5 min vs 30 min)
- ✅ Lower DB load (10K vs 500K rows)
- ❌ Dependency on Redis (cache clear = full scan)
- ❌ Complexity (two code paths: incremental vs full)

**Other Workers Using Incremental:**
- None yet (only `ConnectionScoreBatchWorker` implemented)

**Future:**
- `EmbeddingBatchWorker` — only re-embed users with profile changes
- `CfBatchWorker` — only re-score pairs with new tile interactions

---

## WOVEN_DISABLE_BATCH_WORKERS Flag

### Implementation (Program.cs)

```csharp
// Line 82–83
var batchWorkersDisabled = builder.Configuration.GetValue<bool>(
    "WOVEN_DISABLE_BATCH_WORKERS", false);

// Worker registration (scattered throughout Program.cs)
if (!batchWorkersDisabled) 
    builder.Services.AddHostedService<ConnectionScoreBatchWorker>();

if (!batchWorkersDisabled) 
    builder.Services.AddHostedService<WeightLearningBatchWorker>();

// Real-time workers ALWAYS run (no flag check)
builder.Services.AddHostedService<TileViewProcessorWorker>();
```

### Environment Variables (Azure Container Apps)

**API Pods:**
```bash
WOVEN_DISABLE_BATCH_WORKERS=true
```

**Workers Pod:**
```bash
# No env var → default=false → workers enabled
```

### Local Development

**Default (run all workers):**
```bash
cd backend/WovenBackend
dotnet run
```

**Simulate API pod (workers disabled):**
```bash
export WOVEN_DISABLE_BATCH_WORKERS=true
dotnet run
```

**Force workers enabled:**
```bash
export WOVEN_DISABLE_BATCH_WORKERS=false
dotnet run
```

---

## Terraform Configuration (Azure Container Apps)

**File:** `infrastructure/terraform/container_apps.tf`

### API Container App

```hcl
resource "azurerm_container_app" "api" {
  name                = "woven-api"
  resource_group_name = azurerm_resource_group.main.name
  
  template {
    min_replicas = 2
    max_replicas = 10
    
    container {
      name   = "api"
      image  = "${var.acr_login_server}/woven-backend:${var.image_tag}"
      cpu    = 0.5
      memory = "1Gi"
      
      env {
        name  = "WOVEN_DISABLE_BATCH_WORKERS"
        value = "true"
      }
      
      # Other env vars (Redis, PostgreSQL, etc.)
    }
  }
  
  ingress {
    external_enabled = true
    target_port      = 8080
  }
}
```

### Workers Container App

```hcl
resource "azurerm_container_app" "workers" {
  name                = "woven-workers"
  resource_group_name = azurerm_resource_group.main.name
  
  template {
    min_replicas = 1
    max_replicas = 1  # ← Exactly 1 replica
    
    container {
      name   = "workers"
      image  = "${var.acr_login_server}/woven-backend:${var.image_tag}"
      cpu    = 1.0
      memory = "2Gi"
      
      # No WOVEN_DISABLE_BATCH_WORKERS → defaults to false
      
      # Other env vars (same as API pod)
    }
  }
  
  ingress {
    external_enabled = false  # ← Internal-only (no public access)
  }
}
```

**Key Differences:**
- API: `min_replicas=2`, `max_replicas=10`, `WOVEN_DISABLE_BATCH_WORKERS=true`
- Workers: `min_replicas=1`, `max_replicas=1`, no flag (workers enabled)

---

## Observability

### Logs (Azure Log Analytics)

**Query:** All worker completions (last 24h)
```kusto
ContainerAppConsoleLogs_CL
| where Log_s contains "Done in" or Log_s contains "Completed in"
| where TimeGenerated > ago(24h)
| project TimeGenerated, Log_s
| order by TimeGenerated desc
```

**Query:** Worker failures (errors)
```kusto
ContainerAppConsoleLogs_CL
| where Log_s contains "[Error]" and Log_s contains "Batch"
| where TimeGenerated > ago(24h)
| project TimeGenerated, Log_s
```

**Query:** Lock acquisition failures
```kusto
ContainerAppConsoleLogs_CL
| where Log_s contains "Skipping — another pod holds the lock"
| where TimeGenerated > ago(24h)
| summarize count() by bin(TimeGenerated, 1h)
```

### Metrics (Application Insights)

**Custom Metrics (Not Yet Implemented):**
- `batch_worker.duration_seconds` (per worker)
- `batch_worker.rows_processed` (per worker)
- `batch_worker.errors_total` (per worker)

**Existing Metrics:**
- CPU usage (per pod)
- Memory usage (per pod)
- Request count (API pod only)
- OpenAI API cost (tracked via `IOpenAiCostTracker`, not logged)

### Alerts (Azure Monitor)

**Configured:**
- Worker pod CPU > 90% for 10 minutes → page on-call
- Worker pod memory > 1.8 GB for 5 minutes → page on-call
- Any worker error log → Slack #woven-alerts

**Missing (TODO):**
- Worker runtime > 2x expected duration → alert
- Lock held > 2x expiry time → alert
- ConnectionScoreBatch failed → critical alert (blocks WeightLearning)

---

## Deployment Strategy

### CI/CD (GitHub Actions)

**File:** `.github/workflows/deploy.yml`

**Build:**
```yaml
- name: Build Docker image
  run: docker build -t woven-backend:${{ github.sha }} .
  
- name: Push to ACR
  run: |
    docker tag woven-backend:${{ github.sha }} \
      wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
    docker push wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
```

**Deploy:**
```yaml
- name: Deploy API pods
  run: |
    az containerapp update \
      --name woven-api \
      --image wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
  
- name: Deploy workers pod
  run: |
    az containerapp update \
      --name woven-workers \
      --image wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
```

**Rollout:**
1. Build + push image to ACR
2. Deploy API pods first (rolling update, 2 → 4 → 2)
3. Deploy workers pod (blue-green: stop old, start new)
4. Monitor for 5 minutes (error rate, latency)
5. Rollback if error rate > 1%

**Workers Pod Deployment:**
- **Graceful shutdown:** Send SIGTERM → wait 30s for current batch to finish → SIGKILL
- **Current limitation:** Batch is interrupted mid-run (no graceful completion)
- **Future:** Implement `CancellationToken` handling (already wired, but batch logic ignores it)

---

## Disaster Recovery

### Scenario 1: Workers Pod Crashes

**Impact:**
- Current batch interrupted (e.g., `ConnectionScoreBatch` at 03:55)
- Lock held in Redis (expires after TTL)

**Recovery:**
- Azure Container Apps auto-restarts pod (< 30s)
- Next batch run completes normally (e.g., 03:50 tomorrow)

**User Impact:** None (API pods unaffected)

---

### Scenario 2: Redis Down (Lock Storage)

**Impact:**
- Workers cannot acquire locks → all batch workers skip runs
- Logs: `[Worker] Redis connection failed — skipping batch`

**Recovery:**
1. Azure Cache for Redis auto-failover (< 1 minute)
2. Workers retry at next scheduled run
3. If Redis down >24h: all batches run next day (catch-up mode)

**User Impact:**
- ECHO weights/scores become stale (1 day lag)
- Match quality degrades slightly (imperceptible to users)

---

### Scenario 3: PostgreSQL Down

**Impact:**
- All workers fail (cannot read/write data)
- API pods also fail (500 errors)

**Recovery:**
1. Azure Database for PostgreSQL auto-failover (< 2 minutes)
2. Workers retry at next scheduled run
3. API pods recover immediately

**User Impact:**
- 2-minute downtime (API + workers)
- Batches run next day (catch-up mode)

---

### Scenario 4: OpenAI API Down

**Impact:**
- `EmbeddingBatchWorker`, `CoachingSummaryWorker` fail
- Other workers unaffected (no OpenAI dependency)

**Recovery:**
1. Circuit breaker (`IOpenAiResilientClient`) opens after 3 failures
2. Workers retry at next scheduled run
3. If OpenAI down >24h: embeddings/summaries delayed 1 week

**User Impact:**
- No coaching summaries (Wednesday)
- No new embeddings (match quality degrades over 1–2 weeks)

---

## Future Improvements

### 1. Observability Enhancements

- [ ] OpenTelemetry tracing (trace_id per batch run)
- [ ] Custom metrics (duration, rows processed, errors)
- [ ] Alerts on worker failures (Slack + PagerDuty)

### 2. Graceful Shutdown

- [ ] Handle `CancellationToken` in batch loops
- [ ] Complete current chunk before shutdown (max 5 min delay)
- [ ] Save progress to Redis (`connection-score-batch:progress`)

### 3. Multi-Replica Workers Pod

- [ ] Remove Redis locks (switch to Database-level locking)
- [ ] Scale workers pod to 2–3 replicas (faster completion)
- [ ] Use PostgreSQL advisory locks (`pg_try_advisory_lock`)

### 4. Priority Queue (Azure Service Bus)

- [ ] High-priority embeddings (new users) jump queue
- [ ] Separate queue: `tile-embedding-high-priority`
- [ ] Workers process high-priority first

### 5. Cost Tracking per Worker

- [ ] Wire `IOpenAiCostTracker` to batch workers
- [ ] Log cost per run (e.g., `[CoachingSummary] Cost=$1.23`)
- [ ] Alert if weekly cost > $50

---

**Next:** [implementation.md](implementation.md) — Code patterns, error handling, testing
