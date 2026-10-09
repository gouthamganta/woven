# Queue System

**Last Updated:** 2026-10-07  
**Status:** Production (Azure Service Bus + .NET BackgroundService workers)

---

## What This Is

Woven's background job processing system — split between:

1. **Azure Service Bus** — for async message-driven work (tile embeddings)
2. **.NET BackgroundService workers** — for scheduled batch jobs (ECHO learning, trust scoring, embeddings)

All heavy batch workers are **disabled on API pods** via `WOVEN_DISABLE_BATCH_WORKERS=true`. They run only on a dedicated **workers pod** (scaled to exactly 1 replica).

Lightweight real-time workers (balloon expiry, moderation, tile expiry) run on **all pods**.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│  API Pods (2+ replicas)                                     │
│  ─────────────────────────                                  │
│  • WOVEN_DISABLE_BATCH_WORKERS=true                         │
│  • Lightweight workers only:                                │
│    - BalloonExpiryWorker                                    │
│    - ModerationWorker                                       │
│    - TileExpiryWorker                                       │
│    - TileViewProcessorWorker (real-time aggregation)        │
│                                                              │
│  • Enqueues to Azure Service Bus:                           │
│    → ServiceBusEmbeddingQueue.EnqueueAsync(tileId)          │
└─────────────────────────────────────────────────────────────┘

                    ↓ (Azure Service Bus)

┌─────────────────────────────────────────────────────────────┐
│  Workers Pod (exactly 1 replica)                            │
│  ────────────────────────────                               │
│  • WOVEN_DISABLE_BATCH_WORKERS=false (default)              │
│  • All batch workers (12 nightly/weekly jobs)               │
│  • ServiceBusEmbeddingWorker (message processor)            │
│                                                              │
│  Distributed locks (Redis):                                 │
│  → Each batch worker acquires lock before running           │
│  → Prevents duplicate work if pod restarts mid-run          │
└─────────────────────────────────────────────────────────────┘
```

---

## Files in This Directory

| File | What It Covers |
|------|----------------|
| **[service-bus.md](service-bus.md)** | Azure Service Bus integration (queue, sender, processor) |
| **[workers.md](workers.md)** | All 13 batch workers — what they do, when they run |
| **[scheduling.md](scheduling.md)** | Cron schedules, WOVEN_DISABLE_BATCH_WORKERS flag, pod topology |
| **[implementation.md](implementation.md)** | Code patterns, distributed locking, error handling |

---

## Quick Reference: Worker Schedule

All times **UTC**. See [workers.md](workers.md) for full details.

| Time | Worker | What It Does |
|------|--------|--------------|
| 02:00 | `TrustBatchWorker` | Bot detection, trust scoring |
| 02:30 | `EmbeddingBatchWorker` | Style, humor, lifestyle, emotional rhythm embeddings |
| 03:00 | `CfBatchWorker` | Collaborative filtering (Jaccard similarities) |
| 03:45 | `SelfDisclosureBatchWorker` | Self-disclosure ratios from chat threads |
| 03:50 | `ConnectionScoreBatchWorker` | ECHO connection scores (match outcome proxy) |
| 04:00 | `WeightLearningBatchWorker` | **Sunday only** — learns ECHO pillar weights |
| 04:15 | `PreferenceDriftBatchWorker` | Preference drift detection |
| 04:20 | `LinUcbBatchWorker` | LinUCB bandit model updates |
| 04:30 | `InsightBatchWorker` | Opinion prompts + insight computation |
| 05:00 | `CfScoreBatchWorker` | CF scores (same as 03:00 `CfBatchWorker`) |
| 18:00 | `CoachingSummaryWorker` | **Wednesday only** — AI coaching summaries |
| *real-time* | `ServiceBusEmbeddingWorker` | Tile embedding processor (Azure Service Bus) |
| *real-time* | `TileViewProcessorWorker` | Tile view aggregation (all pods) |

---

## Why This Topology?

**Problem:** Nightly batch workers are **heavy** — they:
- Query millions of rows (ConnectionScores, MatchSignalLogs, embeddings)
- Call OpenAI APIs hundreds of times (CoachingSummary, embeddings)
- Run for 10–30 minutes each

**Solution:**
- **API pods** focus on **low-latency request handling** (P95 < 200ms)
- **Workers pod** (1 replica) runs **all nightly batch jobs**
- Redis distributed locks ensure **no duplicate work** if workers pod restarts

**Trade-off:**
- If workers pod crashes mid-batch, batch is delayed until next cycle
- But API pods stay responsive — user-facing latency unaffected

---

## Monitoring

**Logs:**
- All workers log to structured Serilog
- Correlation IDs (`X-Correlation-ID`) on every log line
- Azure Log Analytics + App Insights

**Key Metrics:**
- Batch completion time (`[Worker] Done in {Ms}ms`)
- Lock acquisition failures (`Skipping — another pod holds the lock`)
- Error counts (`processed={P} errors={E}`)

**Alerts:**
- Worker runs taking >30 minutes
- Multiple consecutive lock acquisition failures (indicates stuck lock)
- Error rate >5% in any batch

---

## Local Development

**To run all workers locally:**

```bash
cd backend/WovenBackend
# Don't set WOVEN_DISABLE_BATCH_WORKERS — default is false
dotnet run
```

**To simulate production (API pod with workers disabled):**

```bash
export WOVEN_DISABLE_BATCH_WORKERS=true
dotnet run
```

**Azure Service Bus emulator:**

Not available. Use real Azure Service Bus connection string (from `appsettings.Development.json` or User Secrets).

---

## Related Documentation

- [ECHO pipeline](../echo/README.md) — ConnectionScores, WeightLearning, LinUCB
- [Embeddings system](../embeddings/README.md) — EmbeddingBatchWorker details
- [Trust & Safety](../trust/README.md) — TrustBatchWorker, bot detection
- [Matchmaking](../matchmaking/README.md) — CfScore, PreferenceDrift

---

## Future Work

1. **Observability** — OpenTelemetry tracing for batch workers
2. **Priority queue** — High-priority embeddings (new users) jump the queue
3. **Graceful shutdown** — Workers complete current batch before shutdown (CancellationToken already wired)
4. **Cost tracking** — OpenAI API cost per worker run (IOpenAiCostTracker exists but not wired to batch workers)

---

**Next:** [service-bus.md](service-bus.md) — Azure Service Bus integration
