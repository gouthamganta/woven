# Woven — Backend Services Documentation

Comprehensive reference for all backend services, organized by domain. All services are registered in `Program.cs` and follow the standard .NET 10 Minimal API DI pattern.

---

## Matchmaking

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `MatchScoringService` | Computes the 16-component match score for a (viewer, candidate) pair | `VectorSearchService`, `AiProfileService`, `CacheService` | No — called by `DailyDeckOrchestrator` |
| `MatchExplanationService` | Generates 3 date ideas and a headline/bullet explanation per match; reads tone bias and date style hint from behavioral signals | OpenAI gpt-4.1-mini, `CacheService`, `MatchSignalService` | No — called on match reveal |
| `DailyDeckOrchestrator` | Orchestrates the full daily deck generation pipeline per user: candidate pool → scoring → selection → boost | `CandidatePoolService`, `MatchScoringService`, `DeckSelectionService`, `DeliveryBoostService` | No — triggered by scheduler in `Program.cs` |
| `CandidatePoolService` | Builds the candidate pool eligible for scoring for a given viewer | `VectorSearchService`, `CacheService`, block/filter lists | No — called by `DailyDeckOrchestrator` |
| `DeckSelectionService` | Performs final deck selection from the scored candidate list | `MatchScoringService`, `CollaborativeFilteringService` | No — called by `DailyDeckOrchestrator` |
| `DeliveryBoostService` | 12-step boost pipeline that ensures the deck is filled when the primary pool is insufficient | `CandidatePoolService`, `MatchScoringService` | No — called by `DailyDeckOrchestrator` as fallback |
| `WeightLearningService` | Mini-batch logistic regression that updates per-user scoring weights from `ConnectionScoreLog` samples | `MatchSignalService`, `CacheService` | Yes — `WeightLearningBatchWorker`, Sunday 04:00 UTC |
| `WeightLearningBatchWorker` | Iterates all eligible users and calls `WeightLearningService` per user | `WeightLearningService` | **Is** the worker — Sunday 04:00 UTC |
| `VectorSearchService` | pgvector HNSW similarity search against user embedding columns | PostgreSQL / Npgsql pgvector | No |
| `UserVectorBuilder` | Assembles the composite `UserVector` from all component embeddings | All embedding services | No — called during embedding pipeline |
| `MatchOutcomeService` | Records match outcomes to the signal ledger | `IMatchSignalService`, `WovenDbContext` | No |
| `OpenAiTaggingService` | Tags user profiles via OpenAI for pillar / intent / style / humor / lifestyle dimensions | OpenAI gpt-4.1-mini, `PiiSanitizer` | No — called on profile creation/update |
| `MatchScore` | Data class holding all 16 score component values for a single (viewer, candidate) pair | — | No |

---

## Embeddings

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `EmbeddingBatchWorker` | Processes the Azure Service Bus `tile-embedding` queue; dispatches embedding tasks to the appropriate service | Azure Service Bus, all embedding services | **Is** the worker — continuous, Service Bus trigger |
| `AiProfileService` | Scores user profiles across 8 pillars (OpenAI); generates `PairContext`; applies 8 regex prompt-injection patterns | OpenAI gpt-4.1-mini, `PiiSanitizer`, `CircuitBreakerService` | No |
| `AttachmentProxyService` | Embedding service for attachment proxies | `EmbeddingBatchWorker`, Azure Blob Storage | No |
| `EmotionalRhythmService` | Computes emotional rhythm embedding from user text/behavioral data | OpenAI (embeddings endpoint) | No |
| `HumorEmbeddingService` | Produces 64-dim humor style embedding | OpenAI (embeddings endpoint) | No |
| `LifestyleEmbeddingService` | Produces 128-dim lifestyle embedding | OpenAI (embeddings endpoint) | No |
| `PhotoEmbeddingService` | Produces 512-dim CLIP photo embedding | Azure Blob Storage, CLIP model endpoint | No |
| `StyleEmbeddingService` | Produces 128-dim communication style embedding | OpenAI (embeddings endpoint) | No |
| `VoiceEmbeddingService` | Calls the SpeechBrain ECAPA-TDNN sidecar Container App (port 8000) to produce a 192-dim voice embedding | SpeechBrain sidecar HTTP, Azure Blob Storage | No |
| `VisualPreferenceService` / `IVisualPreferenceService` | Visual preference embedding service (interface + implementation) | Azure Blob Storage, embedding model | No |

---

## Moments / Interactions

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `MomentsMatchService` | Core match creation logic: handles like/pass choices, creates Match rows, triggers balloon | `WovenDbContext`, `IMatchSignalService`, `SparkWalletService` | No |
| `InteractionBudgetService` | Manages daily spark budget (5 sparks/day, wallet max 10); gates Drawn (liked-you) actions | `SparkWalletService`, `WovenDbContext` | No |
| `SparkWalletService` | Spark wallet operations: credit, debit, ghost refund (0.5 sparks) | `WovenDbContext`, `CacheService` | No |

---

## Games

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `GameService` | Orchestrates in-chat game sessions; routes to the appropriate agent | `GameAgentFactory`, `WovenDbContext`, `IMatchSignalService` | No |
| `KnowMeAgent` | Preference discovery game: generates dynamic questions about the partner across 3 difficulty tiers (EASY / MEDIUM / HARD); injects `PairContext` into prompts | OpenAI gpt-4.1-mini, `AiProfileService`, `PiiSanitizer` | No |
| `RedGreenFlagAgent` | Values alignment game: presents 3 statements per round labelled GREEN / YELLOW / RED / DEPENDS; enforces 90-second limit; enforces anti-generic rules (no texting speed, ghosting, coffee dates, weekend plans) | OpenAI gpt-4.1-mini, `PiiSanitizer` | No |
| `GameAgentFactory` | Factory that instantiates the correct game agent by game type | `KnowMeAgent`, `RedGreenFlagAgent` | No |
| `GameOutcomeService` | Records game results to the signal ledger | `IMatchSignalService`, `WovenDbContext` | No |

---

## Security

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `EncryptionService` / `IEncryptionService` | AES-256-GCM encrypt and decrypt; applied to PII fields at write/read time | .NET `AesGcm` | No |
| `SecurityAuditService` / `ISecurityAuditService` | Logs 7 security event types to `SecurityAuditLog` | `WovenDbContext` | No |
| `SecurityAuditCleanupWorker` | Prunes old `SecurityAuditLog` records | `SecurityAuditService`, `WovenDbContext` | **Is** the worker — scheduled cleanup |
| `KeyRotationWorker` | Periodic encryption key rotation | `IEncryptionService` | **Is** the worker — periodic schedule |
| `PiiSanitizer` | Strips email and phone patterns via regex; truncates text at 200 chars before any OpenAI call | — (regex only) | No |

---

## Analytics

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `AnalyticsService` | Event tracking for user behavior; all event type strings defined in `AnalyticsEvents.cs` | `WovenDbContext` | No |
| `NullAnalyticsService` | No-op implementation of analytics interface; used in test environments | — | No |
| `AnalyticsRetentionWorker` | Prunes old analytics event records per retention policy | `AnalyticsService`, `WovenDbContext` | **Is** the worker — scheduled cleanup |
| `AnalyticsEvents` | Constants class defining all event type strings | — | No |

---

## Feedback / Insights

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `DateFeedbackService` | Collects and stores post-date feedback | `WovenDbContext`, `IMatchSignalService` | No |
| `FeedbackInsightService` | Generates insights from aggregated feedback data | `DateFeedbackService`, OpenAI gpt-4.1-mini | No |
| `FeedbackTriggerWorker` | Triggers post-date feedback prompts at the appropriate time | `DateFeedbackService`, `INotificationService` | **Is** the worker — scheduled trigger |
| `InsightService` | Manages user insight records | `WovenDbContext` | No |
| `InsightBatchWorker` | Batch generation of user insights | `InsightService`, `FeedbackInsightService` | **Is** the worker — scheduled batch |
| `WeeklyDigestWorker` | Sends weekly digest emails or notifications to users | `INotificationService`, `InsightService` | **Is** the worker — weekly schedule |

---

## Trust / Safety

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `CatfishDetectionService` | Detects catfishing patterns from profile and behavioral signals | `WovenDbContext`, `AiProfileService` | No |
| `TrustService` | Computes and stores trust scores per user | `CatfishDetectionService`, `WovenDbContext` | No |
| `ModerationService` | Content moderation for user-generated content; `IsModerationEnabled=false` in dev | OpenAI moderation endpoint | No |
| `ModerationWorker` | Processes the moderation queue | `ModerationService`, `WovenDbContext` | **Is** the worker — queue processor |

---

## Anti-Ghosting

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `GhostDetectionService` | Detects ghosting patterns (matches with no messages after balloon pop) | `WovenDbContext`, `IMatchSignalService` | No |
| `GhostDetectionWorker` | Periodically scans for ghosting patterns and triggers ghost refund logic | `GhostDetectionService`, `SparkWalletService` | **Is** the worker — periodic schedule |

---

## Nudges

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `NudgeService` / `INudgeService` | Generates and sends nudges to re-engage dormant matches or incomplete profiles | `INotificationService`, `WovenDbContext` | No |

---

## Recommendations

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `CollaborativeFilteringService` | Collaborative filtering; computes `CfScore` for candidate ranking | `WovenDbContext`, `VectorSearchService` | **Known gap** — `CfBatchWorker` exists but is not scheduled to run |
| `CfBatchWorker` | Intended batch runner for collaborative filtering scores | `CollaborativeFilteringService` | Exists but **not scheduled** — known gap |

---

## Other / Infrastructure Services

| Service | Purpose | Key Dependencies | Batch Worker |
|---|---|---|---|
| `CacheService` | Redis cache wrapper; all hot-path reads go through this layer | Azure Redis Cache (Standard C1) | No |
| `NotificationService` | Push notification dispatch; VAPID keys configured in appsettings; service worker not yet deployed | VAPID config, `WovenDbContext` | No |
| `MediaService` | Media upload: generates SAS tokens (POST /media/upload-token), manages Azure Blob Storage containers | Azure Blob Storage | No |
| `CircuitBreakerService` | OpenAI circuit breaker; prevents overspend when `OpenAiCostTracker` limit is reached | `OpenAiCostTracker` | No |
| `OpenAiCostTracker` | Tracks OpenAI daily spend; enforces `DailyBudgetUsd=50` cap | `CacheService`, Redis | No |
| `DynamicQuestionBank` | Stores and retrieves dynamic intake questions | `WovenDbContext` | No |
| `DynamicIntakeCycleService` | Cycles users through dynamic intake questions | `DynamicQuestionBank`, `WovenDbContext` | No |
| `FoundationalQuestionBank` | Stores foundational onboarding questions | `WovenDbContext` | No |
| `FoundationalCycleService` | Cycles users through foundational questions | `FoundationalQuestionBank`, `WovenDbContext` | No |
| `SeasonService` | Manages season state | `WovenDbContext` | No |
| `SeasonTransitionWorker` | Handles season transition events | `SeasonService` | **Is** the worker — event-driven |
| `VenueService` | Venue suggestions for date ideas via Google Places API | Google Places API (`google_places_api_key`) | No |
| `VerificationService` | User identity verification | `WovenDbContext`, `SecurityAuditService` | No |
| `OrbitService` | Handles Orbit (explicit ◈ on a Commons tile) logic | `WovenDbContext`, `IMatchSignalService` | No |
| `FriendBridgeService` | Friend bridge / mutual connections feature | `WovenDbContext` | No |
| `CommonsFeedService` | Generates the Commons (content tile feed) for a user | `WovenDbContext`, `CacheService`, `OrbitService` | No |
| `MediaLifecycleWorker` | Cleans up expired media blobs from Azure Blob Storage | Azure Blob Storage, `WovenDbContext` | **Is** the worker — scheduled cleanup |
| `TileExpiryWorker` | Expires stale Commons tiles | `WovenDbContext` | **Is** the worker — scheduled cleanup |
| `BalloonExpiryWorker` | Expires stale balloons / matches that never progressed | `WovenDbContext`, `SparkWalletService` | **Is** the worker — scheduled cleanup |

---

## Scheduled Worker Summary

| Worker | Schedule | Purpose |
|---|---|---|
| `WeightLearningBatchWorker` | Sunday 04:00 UTC | Per-user weight learning from connection score samples |
| `EmbeddingBatchWorker` | Continuous (Service Bus trigger) | Embedding task queue processor |
| `SecurityAuditCleanupWorker` | Scheduled cleanup | Prunes old security audit records |
| `KeyRotationWorker` | Periodic | Encryption key rotation |
| `AnalyticsRetentionWorker` | Scheduled cleanup | Analytics event record pruning |
| `FeedbackTriggerWorker` | Scheduled | Post-date feedback prompt triggers |
| `InsightBatchWorker` | Scheduled batch | User insight generation |
| `WeeklyDigestWorker` | Weekly | Digest emails / notifications |
| `ModerationWorker` | Queue processor | Content moderation queue |
| `GhostDetectionWorker` | Periodic | Ghost pattern scanning, refund triggers |
| `SeasonTransitionWorker` | Event-driven | Season transition handling |
| `MediaLifecycleWorker` | Scheduled cleanup | Expired media blob cleanup |
| `TileExpiryWorker` | Scheduled cleanup | Stale Commons tile expiry |
| `BalloonExpiryWorker` | Scheduled cleanup | Stale balloon / match expiry |
| `CfBatchWorker` | **Not scheduled** — known gap | Collaborative filtering score computation |
