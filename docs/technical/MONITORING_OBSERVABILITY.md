# Woven — Monitoring & Observability

---

## Azure Application Insights

All Azure Container Apps (backend API, SpeechBrain sidecar, frontend nginx) send telemetry to a single Application Insights resource.

- **Wired via Terraform**: the `app_insights_connection_string` Terraform variable is passed to each Container App as an environment variable at deploy time. No code change is needed to toggle it — it is always active in cloud environments.
- **Log Analytics workspace**: backs the Application Insights resource. Retention days are configurable via Terraform (`log_analytics_retention_days` variable).
- **What is captured automatically**: HTTP request traces (method, path, status, duration), dependency traces (PostgreSQL queries via Npgsql, Redis calls, Service Bus messages, outbound HTTP to OpenAI / Google Places / SpeechBrain), exceptions, and container-level metrics (CPU, memory, replica count).

Application Insights is not configured in local dev — logs go to the console only.

---

## Structured Logging via ILogger\<T\>

All services and background workers use `ILogger<T>` for structured logging. Log messages are emitted to the console in dev and to Application Insights in cloud environments.

### Log Level Configuration (from appsettings.json)

| Scope | Level |
|---|---|
| Default (all services) | `Information` |
| `Microsoft.AspNetCore` (in dev) | `Warning` |

### Batch Worker Log Conventions

Background workers use a consistent `[WorkerName]` prefix in all log messages, enabling easy filtering in Application Insights or log analytics queries.

Example patterns:

```
[WeightLearning] User {UserId}: learned weights from {N} samples, top={Top} ({Val:F3})
[ConnectionScore] Processed {N} pairs for user {UserId}
[EmbeddingBatch] Dispatched embedding task {TaskId} for tile {TileId}
[GhostDetection] Found {N} ghosted matches, issued {R} refunds
```

Structured log parameters (user IDs, counts, component names) are always passed as named properties, not concatenated strings, so they are queryable in Application Insights.

---

## AnalyticsService — User Behavior Event Tracking

`AnalyticsService` records discrete user behavior events for product analytics. It is separate from Application Insights telemetry (which is infrastructure-level) — `AnalyticsService` tracks product-level events like "user completed profile", "match revealed", "trial continued".

- **Event type constants**: all valid event type strings are defined in `AnalyticsEvents.cs`. New event types must be added there first.
- **`NullAnalyticsService`**: a no-op implementation of the analytics interface used in test environments. It satisfies the DI registration without writing any records, so tests do not produce real analytics events.
- **`AnalyticsRetentionWorker`**: a scheduled worker that prunes old analytics event records per a configured retention window, preventing unbounded table growth.

---

## OpenAI Cost Control

Two components enforce the `DailyBudgetUsd = 50` cap on OpenAI spend:

### OpenAiCostTracker

Tracks cumulative OpenAI spend for the current UTC day. Uses `CacheService` (Redis) as its backing store so the count is shared across all backend replicas. Every OpenAI call reports its token usage to `OpenAiCostTracker` after completion.

### CircuitBreakerService

When `OpenAiCostTracker` determines that the daily cap has been reached, `CircuitBreakerService` opens the circuit. All subsequent OpenAI calls are rejected immediately (without hitting the API) until the next UTC day resets the counter. Services that call OpenAI handle the open-circuit state gracefully — they fall back to cached results or return a degraded response rather than returning an error to the user.

---

## SecurityAuditLog

`SecurityAuditService` records structured entries for 7 security event types (e.g., key rotation, failed auth, PII access, moderation action — exact names are defined in the service). All records include a timestamp, user ID, event type, and metadata.

`SecurityAuditCleanupWorker` prunes records older than the configured retention window on a scheduled basis.

Security audit logs are queryable in Application Insights (they are written via `ILogger`) and in the `SecurityAuditLog` database table (for compliance queries).

---

## Health Endpoints

| Service | Path | Used by |
|---|---|---|
| Backend (.NET 10) | `/health` | Docker HEALTHCHECK, CD smoke check (30×10s poll), Azure Container App liveness probe |
| Frontend (nginx) | `/health` | CD smoke check (15×10s poll via curl) |

The `/health` endpoint on the backend returns HTTP 200 when the app is running. It does not currently perform deep dependency checks (e.g., PostgreSQL connectivity) — it is a liveness signal only.

---

## CD Smoke Checks

After each deployment, the CD pipeline performs these checks before marking the deployment successful:

| Target | Method | Attempts | Interval | Failure action |
|---|---|---|---|---|
| Backend | Azure CLI revision health query | 30 | 10 seconds | Fail deployment |
| Frontend | HTTP GET, expect 200 | 15 | 10 seconds | Fail deployment |

These are post-deploy readiness checks, not pre-merge tests.

---

## Batch Worker Observability

All background workers emit structured log entries at key milestones:

- **Start**: worker iteration begins, number of users/records to process.
- **Per-item**: result for each user or record (e.g., weights learned, scores computed, embeddings dispatched).
- **End**: total records processed, elapsed time, any errors or skips.

This pattern means a single Application Insights query on `[WorkerName]` prefix gives a complete picture of each worker run.

Workers that process per-user data always include `UserId` as a structured property so logs can be filtered to a single user for debugging.

---

## Known Gaps

| Gap | Notes |
|---|---|
| No Grafana dashboards | Application Insights is the only visualization layer. No custom dashboards or alerting rules are documented. |
| No alerting rules | No Azure Monitor alert rules or Action Groups are documented in Terraform or source. |
| No DLQ alerting | The Service Bus dead-letter queue exists but no alert fires when DLQ depth exceeds a threshold. |
| /health is liveness only | The backend health endpoint does not check PostgreSQL, Redis, or Service Bus connectivity. |
