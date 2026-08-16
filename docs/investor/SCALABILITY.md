# Scalability

## Overview

Woven's infrastructure is production-deployed on Azure and managed via Terraform. The architecture was designed with horizontal scalability in mind from the beginning: the API layer, background workers, and AI inference are separated into distinct container pods with independent scaling characteristics. This document describes the technical scalability story as currently built, with honest identification of scaling assumptions and limits.

---

## Compute: Azure Container Apps

The production deployment runs on Azure Container Apps with four pods:

| Pod | Contents | Scaling behavior |
|---|---|---|
| frontend | Angular static serving | Horizontal (request-based) |
| backend | .NET 10 minimal API | Horizontal (request-based) |
| workers | All batch workers | Fixed (batch isolation) |
| speechbrain | SpeechBrain ECAPA-TDNN inference | Fixed (min=max=1, 2 uvicorn workers) |

**API pod isolation:** The API pod runs with `WOVEN_DISABLE_BATCH_WORKERS=true`, ensuring that scheduled batch jobs (weight learning, embedding generation, connection score aggregation) do not compete with request-serving for CPU and memory. This is a critical architectural decision: batch workloads and API latency have fundamentally different resource profiles and should not share a process.

**Batch worker isolation:** The workers pod runs all scheduled jobs in isolation. `WeightLearningBatchWorker` (Sun 04:00 UTC), `ConnectionScoreBatchWorker` (nightly 03:50 UTC), `EmbeddingBatchWorker`, `ModerationWorker`, and `WeightLearningBatchWorker` are all registered in this pod. Off-peak scheduling (03:50-04:00 UTC window) minimizes resource contention with user-facing traffic.

**SpeechBrain pod:** The voice embedding service runs in a dedicated pod with 2 uvicorn workers. The ECAPA-TDNN model is pre-downloaded at build time (not fetched at runtime), ensuring cold-start latency is bounded. The fixed scale (min=max=1) reflects current traffic assumptions; this is the pod most likely to require scaling attention as voice note volume grows.

---

## Database: PostgreSQL Flexible Server + pgvector

The database layer uses Azure PostgreSQL Flexible Server with the pgvector extension installed.

**Vector search performance:** All embedding columns use HNSW (Hierarchical Navigable Small World) indexes for approximate nearest-neighbor search. HNSW provides sub-linear query time for cosine similarity search across high-dimensional vectors. The 9 embedding modalities (up to 1536-dim) are indexed separately, allowing the query planner to use the appropriate index per scoring component.

**Optional high availability:** Azure PostgreSQL Flexible Server supports zone-redundant high availability (standby replica with automatic failover). This is available in the current Terraform configuration as an opt-in setting, not yet enabled by default.

**PII encryption at rest:** AES-256-GCM encryption is applied to email, full_name, city, state, and reflection sentences at the application layer before storage. This is independent of database-level encryption and ensures that a database-level breach does not expose unencrypted PII.

**Scaling path:** PostgreSQL Flexible Server supports vertical scaling (compute tier upgrades) and read replicas. As the embedding search workload grows, read replicas dedicated to ECHO scoring queries are the natural next step.

---

## Cache: Redis Standard C1

All hot-path reads go through `CacheService`, which wraps Redis Standard C1 (1 GB, 99.9% SLA). The `ICacheService` interface abstracts the cache layer, allowing the implementation to be swapped without changing calling code.

**Cached data classes:**
- User profile reads (frequently accessed by ECHO scoring)
- Daily deck compositions (expensive to recompute)
- Match state (BalloonState, TrialState — high read frequency during active sessions)

**Cache miss behavior:** All cache misses fall through to PostgreSQL reads. The cache layer is a read-through optimization, not a primary data store. Cache invalidation is event-driven: writes to the database trigger cache key invalidation for affected entities.

**Scaling path:** Redis Standard C1 (1 GB) is appropriate for early-scale usage. Redis Standard C2 (2 GB) or Cluster tier are the natural upgrade paths as active user count grows. The `ICacheService` abstraction means the upgrade requires no application-level changes.

---

## Async Processing: Azure Service Bus Standard

Tile embedding generation and other workloads that can tolerate latency (embeddings are not needed synchronously with user actions) are processed via Azure Service Bus Standard.

**Configuration:**
- Durable message queue with max 5 retry attempts per message
- Dead-letter queue for messages that exhaust retries — allows failed embedding jobs to be inspected and reprocessed without data loss
- `EmbeddingBatchWorker` consumes from the queue on a scheduled basis

**Why Service Bus over direct async:** Service Bus provides durability (messages survive worker pod restarts), backpressure (the queue absorbs traffic spikes without dropping work), and dead-lettering (failed jobs are preserved for inspection rather than silently dropped). These properties are essential for an async pipeline where embedding generation failures would produce incomplete user profiles.

**Scaling path:** Service Bus Standard supports message volumes well beyond early-scale needs. The worker pod consuming from the queue can be scaled horizontally if embedding throughput becomes a bottleneck.

---

## AI Cost Controls

OpenAI API costs are the primary variable cost driver in Woven's current architecture. Several layers of control are in place:

**DailyBudgetUsd:** A configurable daily spending cap (`OpenAiDailyBudgetUsd=$50` in current configuration) is enforced by `OpenAiCostTracker`. When the daily budget is exhausted, `CircuitBreakerService` opens the circuit and subsequent AI calls return graceful fallbacks rather than failing hard.

**Graceful degradation:** Every AI-powered surface has a fallback path:
- Match explanation generation: falls back to a template-based explanation if OpenAI is unavailable
- AI profile tagging: falls back to existing tags if budget is exhausted
- Dynamic intake rewrite: falls back to original intake text

**Model selection:** `gpt-4.1-mini` is used for all generation tasks. This is a deliberate cost control decision — the mini model provides sufficient quality for explanation generation and tagging at significantly lower cost than full-size models.

**SpeechBrain (self-hosted):** Voice embedding generation runs on the dedicated SpeechBrain pod and does not incur per-call API costs. This is a significant long-term cost advantage: as voice note volume grows, voice embedding costs scale with compute (fixed Azure Container Apps pricing) rather than per-call API pricing.

---

## Infrastructure as Code: Terraform

All Azure resources are defined in Terraform and managed via OIDC-based CI/CD. No secrets are stored in Terraform state. The OIDC authentication approach means:

- No long-lived credentials in CI/CD secrets
- No credentials in Terraform state files
- Repeatable environment creation: running `terraform apply` creates a complete, correctly configured environment

**Multi-region path:** The current VNet and Private DNS zone structure is designed to support regional expansion. Additional regions can be provisioned by duplicating the Terraform module with a new region variable, without architectural redesign.

**What Terraform currently manages:**
- Azure Container Apps environment and all 4 pods
- PostgreSQL Flexible Server (with pgvector extension)
- Redis Standard C1
- Azure Service Bus Standard (queue + dead-letter)
- Azure Blob Storage (profile-photos, tile-media, voice-notes containers)
- Azure Container Registry
- VNet + Private DNS zones

---

## Honest Scaling Assumptions and Limits

The following are current architectural assumptions that will require attention as user scale grows:

**SpeechBrain pod is fixed at 1 instance.** As voice note volume grows, voice embedding generation throughput is bounded by 2 uvicorn workers on a single pod. This will require horizontal scaling attention before it becomes a bottleneck. The fix is straightforward (adjust min/max in Terraform) but has not been tested at scale.

**CfScore batch worker is not yet running.** `CollaborativeFilteringService` exists but no batch worker runs it. The `cf` component in ECHO scoring (base weight=0.03) currently returns a default value. As the user base grows, enabling this component will require provisioning the batch job and ensuring it completes within the off-peak window.

**Redis 1 GB limit.** Standard C1 is appropriate for early scale. If cached deck compositions become large (many users with large candidate pools), cache eviction pressure will increase. The upgrade path to C2 is straightforward.

**pgvector HNSW at scale.** HNSW indexes require memory proportional to index size. As the embedding count grows (more users, more embedding types per user), index build time and memory requirements will increase. PostgreSQL Flexible Server's compute tier may need to scale to maintain index performance. This is a known pgvector operational consideration, not a Woven-specific architectural flaw.
