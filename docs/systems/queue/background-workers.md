# Background Workers

**System:** Queue / Scheduling  
**Related:** [Workers](./workers.md) | [Scheduling](./scheduling.md)

---

## Overview

Background workers run scheduled jobs (batch processing, cleanup, aggregations) outside the request/response cycle.

---

## Worker Types

**Batch aggregation:**
- `ConnectionScoreBatchWorker` — daily 03:50, aggregates match signals
- `WeightLearningBatchWorker` — weekly Sun 04:00, trains ECHO weights
- `CfScoreBatchWorker` — daily 05:00, collaborative filtering

**Cleanup:**
- `BalloonExpiryWorker` — every 60s, closes expired balloons
- `DataRetentionWorker` — daily cleanup of old records

**Generation:**
- `CoachingSummaryWorker` — weekly, generates coaching summaries

---

## Scheduling

**Framework:** Hangfire (recurring jobs)

**Timezone:** All schedules in UTC

**Disable flag:** `WOVEN_DISABLE_BATCH_WORKERS` env var (set on web pods)

**Evidence:** [Program.cs](../../../backend/WovenBackend/Program.cs) — worker registration

---

## Related

- [Queue System](./README.md)
- [Worker Details](./workers.md)
- [Scheduling](./scheduling.md)
