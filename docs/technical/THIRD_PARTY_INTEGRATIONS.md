# Woven — Third-Party Integrations

All external service dependencies, how they are used, how they are configured, and any known gaps.

---

## 1. OpenAI (gpt-4.1-mini)

| Field | Detail |
|---|---|
| **What it does in Woven** | Pillar scoring (8 pillars via `AiProfileService`), profile tagging (`OpenAiTaggingService`), match explanation + date idea generation (`MatchExplanationService`), dynamic intake rewrite (`OpenAiDynamicIntakeRewriteService`), Know Me game prompts (`KnowMeAgent`), Red/Green Flag game prompts (`RedGreenFlagAgent`) |
| **Model** | `gpt-4.1-mini` (configured in appsettings.json) |
| **Calling services** | `AiProfileService`, `OpenAiTaggingService`, `MatchExplanationService`, `KnowMeAgent`, `RedGreenFlagAgent`, embedding services (embeddings endpoint) |
| **Cost control** | `OpenAiCostTracker` enforces `DailyBudgetUsd=50`; `CircuitBreakerService` opens when the daily cap is reached — no further calls are made until the next UTC day |
| **Prompt safety** | `PiiSanitizer` strips email/phone patterns and truncates input at 200 chars before every OpenAI call; `AiProfileService` applies 8 regex patterns to detect prompt injection in profile text |
| **Config key** | `OpenAI:ApiKey` (appsettings.json / Azure Container Apps secret) |
| **Local dev substitute** | No emulator — real API key required in dev. `NullAnalyticsService` pattern (no-op) does not apply here; circuit breaker prevents runaway spend. |
| **Known gaps** | None beyond the daily budget cap. |

---

## 2. Google OAuth

| Field | Detail |
|---|---|
| **What it does in Woven** | Primary user authentication. Users sign in with Google; the backend validates the Google ID token before issuing a Woven JWT. |
| **How it's called** | Backend validates Google ID token server-side. JWT is then issued to the client and stored in localStorage. |
| **Calling service** | Auth middleware / login endpoint in `Program.cs` |
| **Config key** | `GoogleAuth:ClientId = 211033152902-umjjk9n5mqd02s97skerf9sn383m0v00.apps.googleusercontent.com` |
| **Local dev substitute** | Same Google OAuth client ID used in dev (no emulator). |
| **Known gaps** | JWT stored in localStorage is a documented security gap (should be httpOnly cookie before prod). See `TECHNICAL_DEBT_AND_IMPROVEMENTS.md`. |

---

## 3. Azure Blob Storage

| Field | Detail |
|---|---|
| **What it does in Woven** | Stores all user-generated media: profile photos (`profile-photos` container), Commons tile media (`tile-media` container), voice notes (`voice-notes` container). All containers are private. |
| **How it's called** | `MediaService` generates short-lived SAS tokens (via `POST /media/upload-token`). Frontend uploads directly to Azure Blob using the SAS URL (no media goes through the backend at upload time). After upload, frontend calls `POST /media/confirm` to register the asset. |
| **Calling service** | `MediaService`, `VoiceEmbeddingService`, `AttachmentProxyService`, `MediaLifecycleWorker` |
| **Config key** | `Azure:BlobStorage:ConnectionString` (or managed identity in prod) |
| **Local dev substitute** | Azurite emulator (local Azure Blob emulator) |
| **Known gaps** | None documented. `MediaLifecycleWorker` handles blob cleanup for expired assets. |

---

## 4. Azure Service Bus Standard

| Field | Detail |
|---|---|
| **What it does in Woven** | Durable embedding task queue. When a tile or voice note is created, an embedding task is enqueued; `EmbeddingBatchWorker` processes messages off the queue. |
| **Queue name** | `tile-embedding` |
| **Queue configuration** | Max 5 retries, P2D (2-day) TTL, 5-minute message lock. Failed messages go to the dead-letter queue. |
| **Calling service** | Enqueued by media/tile creation endpoints; consumed by `EmbeddingBatchWorker` |
| **Config key** | `Azure:ServiceBus:ConnectionString` |
| **Local dev substitute** | No emulator documented — dev may use direct method calls or skip the queue. |
| **Known gaps** | None documented. Dead-letter queue exists but no documented alerting on DLQ depth. |

---

## 5. Azure Redis Cache (Standard C1)

| Field | Detail |
|---|---|
| **What it does in Woven** | Hot-path read cache. All high-frequency reads (candidate pools, scored decks, user vectors, OpenAI cost tracking) are served from Redis before hitting PostgreSQL. |
| **Tier** | Standard C1 (1 GB, 99.9% SLA) |
| **Calling service** | `CacheService` — all other services call `CacheService` rather than Redis directly |
| **Config key** | `Redis:ConnectionString` |
| **Local dev substitute** | Local Redis instance (or Redis via Docker). |
| **Known gaps** | None documented. |

---

## 6. SpeechBrain ECAPA-TDNN (Voice Embedding Sidecar)

| Field | Detail |
|---|---|
| **What it does in Woven** | Generates 192-dimensional voice embeddings from voice note audio files. ECAPA-TDNN captures prosodic features (speaking rate, pitch, rhythm) beyond text content. |
| **Deployment** | Dedicated Azure Container App (port 8000, 2 uvicorn workers). Model weights pre-downloaded at Docker build time (~200 MB). Fixed at 1 replica (no autoscaling — known gap). |
| **How it's called** | `VoiceEmbeddingService` sends the audio file (fetched from Azure Blob Storage via SAS URL) to the sidecar over HTTP and receives the 192-dim vector. |
| **Config key** | `SpeechBrain:SidecarUrl` (internal Container App URL) |
| **Local dev substitute** | No documented local substitute. The sidecar container must be running for voice embedding to work. |
| **Known gaps** | Fixed at 1 replica — no autoscaling configured. High voice note volume could create a bottleneck. |

---

## 7. Replicate API

| Field | Detail |
|---|---|
| **What it does in Woven** | Not confirmed from reviewed source files. The Terraform variable `replicate_api_token` is defined and provisioned, indicating an integration is planned or partially implemented. |
| **Calling service** | Unknown — no service file explicitly using the Replicate API was identified in reviewed source. |
| **Config key** | `replicate_api_token` (Terraform variable, passed to Container Apps as a secret) |
| **Local dev substitute** | Unknown. |
| **Known gaps** | **Usage not confirmed.** The token is provisioned in infrastructure but the corresponding service usage has not been verified from reviewed source files. This should be audited before production deployment. |

---

## 8. Google Places API

| Field | Detail |
|---|---|
| **What it does in Woven** | Provides venue suggestions for date ideas generated by `MatchExplanationService`. |
| **How it's called** | `VenueService` calls the Google Places API using the configured key. |
| **Config key** | `google_places_api_key` (Terraform variable, passed to Container Apps as a secret) |
| **Local dev substitute** | No documented local substitute. |
| **Known gaps** | None documented beyond the fact that the Places API has per-request billing. |

---

## 9. VAPID / Web Push (Push Notifications)

| Field | Detail |
|---|---|
| **What it does in Woven** | Intended to deliver push notifications to users (nudges, new matches, trial alerts). |
| **Current state** | VAPID public/private keys are configured in `appsettings.json`. `NotificationService` exists and has the dispatch logic. **The service worker is not yet deployed** — push notifications cannot fire in the current build. |
| **Calling service** | `NotificationService` / `INudgeService` / `FeedbackTriggerWorker` / `WeeklyDigestWorker` |
| **Config key** | `Vapid:PublicKey`, `Vapid:PrivateKey`, `Vapid:Subject` (appsettings.json) |
| **Local dev substitute** | N/A — service worker not deployed in any environment yet. |
| **Known gaps** | **Service worker not deployed.** No Web Push endpoint wired. Push notifications are entirely non-functional until the service worker is shipped. |

---

## 10. pgvector (PostgreSQL Extension)

| Field | Detail |
|---|---|
| **What it does in Woven** | Enables vector similarity search directly in PostgreSQL. All user embedding columns are `vector(N)` types. HNSW indexes accelerate approximate nearest-neighbor search for candidate pool generation. |
| **How it's called** | `VectorSearchService` executes HNSW similarity queries via Npgsql's pgvector integration. `WovenDbContext` registers the extension via `modelBuilder.HasPostgresExtension("vector")`. |
| **Config key** | Part of the standard `ConnectionStrings:DefaultConnection` PostgreSQL connection string. |
| **Local dev substitute** | pgvector must be installed in the local PostgreSQL instance. Note: **pgvector is not installed locally** in the dev environment — any migration that adds a `vector` column must be applied manually via psql, not via `dotnet ef database update`. |
| **Known gaps** | Not installed in local dev environment. Vector column migrations require manual psql application. |

---

## Integration Health Summary

| Integration | Status | Local Substitute |
|---|---|---|
| OpenAI gpt-4.1-mini | Fully operational | None (real key required) |
| Google OAuth | Fully operational | Same client ID in dev |
| Azure Blob Storage | Fully operational | Azurite emulator |
| Azure Service Bus | Fully operational | None documented |
| Azure Redis Cache | Fully operational | Local Redis / Docker |
| SpeechBrain ECAPA-TDNN | Fully operational | None documented |
| Replicate API | **Not confirmed** | Unknown |
| Google Places API | Fully operational | None documented |
| VAPID / Web Push | **Partially configured — service worker not deployed** | N/A |
| pgvector | Fully operational in cloud | Requires manual setup locally |
