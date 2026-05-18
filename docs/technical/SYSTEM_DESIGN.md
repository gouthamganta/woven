# Woven — System Design

> Definitive end-to-end technical reference. Written for a new senior engineer joining the team or an investor doing technical due diligence. Every architectural decision is explained and justified. Last updated: 2026-05-18.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [High-Level Architecture](#2-high-level-architecture)
3. [Frontend Architecture](#3-frontend-architecture)
4. [Backend Architecture](#4-backend-architecture)
5. [Database Design](#5-database-design)
6. [The Matching Engine](#6-the-matching-engine)
7. [Real-Time System](#7-real-time-system)
8. [AI Integration Layer](#8-ai-integration-layer)
9. [Caching Strategy](#9-caching-strategy)
10. [Security Architecture](#10-security-architecture)
11. [Background Worker System](#11-background-worker-system)
12. [Media Pipeline](#12-media-pipeline)
13. [Infrastructure and Deployment](#13-infrastructure-and-deployment)
14. [Scalability Considerations](#14-scalability-considerations)
15. [Monitoring and Observability](#15-monitoring-and-observability)
16. [Known Issues and Technical Debt](#16-known-issues-and-technical-debt)

---

## 1. Executive Summary

Woven is a relationship-intent matchmaking platform built for people who are serious about finding genuine compatibility — not just proximity and photos. The core technical problem Woven solves is this: most dating platforms are engagement-maximization engines. Woven is a compatibility-maximization engine. Every architectural decision flows from that distinction.

Technically, Woven is a .NET 10 minimal-API monolith backed by PostgreSQL with the pgvector extension for semantic similarity search, Redis for distributed caching and real-time coordination, and Azure Blob Storage for media. The frontend is an Angular 17 server-side-rendered single-page application. All compute runs on Azure Container Apps in Central India. The matching engine uses a 14-component weighted scoring system where the weights themselves are personalized per-user through a weekly gradient descent pass over outcome data. AI is integrated at nine distinct touchpoints: pillar embeddings, photo embeddings, voice embeddings, match explanations, game evaluation, nudge generation, insight generation, season prompt generation, and content moderation.

**Key numbers:**
- **47 API endpoints** across 14 domain groups
- **14 background workers** on non-conflicting UTC schedules
- **~60 database tables** organized across 9 domain clusters
- **9 AI integrations** (OpenAI embeddings, OpenAI completions, Replicate CLIP, SpeechBrain, Azure Content Moderator, Azure Speech, Google OAuth, Google Places, Google JWKS)
- **34 Azure resources** provisioned via Terraform
- **14 scoring components** in the matching engine
- **9 embedding types** across 4 modalities (text, visual, voice, behavioral)
- **AES-256-GCM** column encryption on all PII fields
- **HNSW indexes** on all pgvector columns for sub-10ms ANN retrieval

---

## 2. High-Level Architecture

### 2.1 The Full Mental Model

```
┌─────────────────────────────────────────────────────────────────────┐
│  User Device (Mobile Browser)                                        │
│  Angular 17 SSR — rendered server-side on first load, hydrated SPA  │
└──────────────────────┬──────────────────────────────────────────────┘
                       │ HTTPS
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Azure Container Apps — Frontend (External Ingress, Port 80)         │
│  nginx 1.25 — static asset serving + reverse proxy                  │
│  envsubst at startup: BACKEND_URL → nginx.conf                       │
└──────────────────────┬──────────────────────────────────────────────┘
                       │ HTTPS (internal FQDN, SNI)
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Azure Container Apps — Backend (Internal Ingress Only, Port 8080)  │
│  ASP.NET Core 10 Minimal APIs                                        │
│  47 endpoints · 14 background workers · SignalR hub                  │
└──────┬──────────────┬──────────────┬──────────────┬─────────────────┘
       │              │              │              │
       ▼              ▼              ▼              ▼
  PostgreSQL 16   Redis Cache    Azure Blob     SignalR
  + pgvector       Basic C0       Storage       Backplane
  (Private DNS)   (TLS, no VNet) (3 containers) (same Redis)
                                                
External services (HTTPS, outbound only):
  OpenAI API · Replicate API · Google OAuth JWKS ·
  Google Places API · Azure Content Moderator · Azure Speech
  SpeechBrain (subprocess, same pod)
```

### 2.2 Every Architectural Decision Justified

**Why Azure Container Apps, not App Service?**

Container Apps gives us auto-scaling (1–5 replicas without manual configuration), revision-based deployments with zero-downtime rollouts, and a serverless billing model where we pay for consumption not allocation. App Service is an always-on PaaS that would cost 3–4× more at our scale without providing meaningful operational benefits. Container Apps also integrates naturally with managed identity for ACR pulls, eliminating stored credentials entirely.

**Why PostgreSQL, not Cosmos DB?**

Three reasons. First, the matching engine requires complex multi-join queries with aggregations across UserVectors, CandidateExposures, Blocks, DailyInteractions, and Matches simultaneously — this is deeply relational work that Cosmos DB would handle with fan-out reads and eventual consistency guarantees that are wrong for this use case. Second, pgvector gives us ANN search in the same database as our relational data, eliminating a cross-service round trip on every deck build. Third, EF Core's migration tooling against PostgreSQL is mature and well-understood; the equivalent for Cosmos DB requires substantially more ceremony for schema evolution.

**Why Redis, not in-memory or distributed SQL cache?**

The backend runs 1–5 replicas behind the Container Apps load balancer. Rate limiters, session state, and daily deck caches must be consistent across all replicas. An in-process cache would create split-brain state (user A might hit replica 1 for their rate limit increment and replica 2 for the check). Redis provides a single consistent source of truth across replicas. We also use Redis as the SignalR backplane — without it, a WebSocket message sent to a connection on replica 2 would fail silently if the hub client for that user is on replica 1.

**Why pgvector, not Pinecone or Weaviate?**

The primary reason is colocation. Every deck build requires filtering candidates by distance, age, block status, existing matches, and gender preference before scoring. Running that filter in a dedicated vector DB would require: (1) fetching all plausible candidates from Postgres, (2) sending the list to the vector DB for nearest-neighbor search, (3) merging results. With pgvector, the entire operation is a single SQL query with `ORDER BY embedding <=> $1 LIMIT 200` after the WHERE clause applies relational filters. This eliminates the round trip, reduces latency, and keeps transaction boundaries clean. The tradeoff is that pgvector's HNSW implementation is not as tunable as Pinecone's, but at our scale (< 100k users) the default HNSW parameters perform well.

**Why Angular 17 SSR, not Next.js or a native app?**

The founding constraint is a single developer building the full product. Angular's strongly-typed, module-based structure is more maintainable under that constraint than React's flexibility. SSR gives us fast first-load (critical for app store discoverability if we wrap in Capacitor later) and correct meta tags for social sharing. We chose Angular over Next.js primarily because the backend team has existing TypeScript and Angular familiarity, and the Angular CLI's `ng generate` significantly reduces boilerplate.

---

## 3. Frontend Architecture

### 3.1 Stack

- **Framework**: Angular 17 with standalone components (no NgModules)
- **Rendering**: Server-side rendering via `@angular/ssr` (main.server.ts entry point)
- **HTTP**: Angular `HttpClient` with auth interceptor injecting JWT
- **Real-time**: `@microsoft/signalr` client, JWT passed via query string
- **Routing**: `app.routes.ts` with lazy-loaded route groups
- **Build**: Angular CLI 17 with esbuild bundler

### 3.2 Navigation Structure

Four primary tabs, each a lazy-loaded route group:

```
/home
  /home/moments          → Daily deck (Magical ◈ / Logical ◇ / Save ⏳)
  /home/moments/pending  → Saved candidates
  /home/commons          → Public tile feed
  /home/chats            → Chat thread list
  /home/chats/:threadId  → Individual chat
  /home/you              → Profile and settings
  /home/you/insights     → Personalized insights
  /home/you/settings     → Accessibility, account

/onboarding (gates profile completion)
  /onboarding/welcome
  /onboarding/basics
  /onboarding/photos
  /onboarding/intent
  /onboarding/foundational
  /onboarding/details
  /onboarding/review

/auth/login
```

### 3.3 State Management

No NgRx. State lives in:

1. **Services** (singleton, injected): `MomentsService`, `ChatService`, `MatchService`, `AuthService` hold observable state as `BehaviorSubject`
2. **Route parameters**: Thread IDs, match IDs passed via router
3. **Local component state**: Deck card swipe state, input values, loading flags

This is appropriate for the current scale. The app is mostly read-heavy with point mutations (respond to a card, send a message). NgRx would add substantial boilerplate without meaningful benefit until we have complex cross-component sync requirements.

### 3.4 How it Communicates with the Backend

All API calls go through `HttpClient` with the `AuthInterceptor` which:
1. Reads the JWT from `localStorage`
2. Injects `Authorization: Bearer {token}` on every outbound request
3. Intercepts 401 responses and redirects to `/auth/login`

The frontend never constructs backend URLs directly — all are routed through nginx (`/api/**` → backend FQDN). This means the frontend bundle contains no backend hostname, which is correct for multi-environment deployment.

### 3.5 SignalR Real-Time Connection

The SignalR client connects to `/hubs` with the JWT in the query string (required because WebSocket handshakes cannot carry custom headers). Connection lifecycle:

```typescript
HubConnectionBuilder
  .withUrl('/hubs', { accessTokenFactory: () => this.auth.token })
  .withAutomaticReconnect([0, 2000, 10000, 30000])
  .build()
```

The reconnect schedule (0ms, 2s, 10s, 30s) is intentional: immediate retry for transient network blips, then exponential backoff to avoid thundering-herd on pod restarts.

### 3.6 Accessibility

- `reduceMotion` preference: stored in `UserPreference`, loaded on app init, applied via CSS `@media (prefers-reduced-motion)` override
- `highContrast` preference: toggles a CSS class on the root element
- `displayPronouns`: shown on all profile cards throughout the app
- All interactive elements have ARIA labels
- Angular's OnPush change detection reduces unnecessary repaints

---

## 4. Backend Architecture

### 4.1 Stack

- **Runtime**: .NET 10 on Linux (mcr.microsoft.com/dotnet/aspnet:10.0)
- **Framework**: ASP.NET Core 10 Minimal APIs
- **ORM**: EF Core 10 with Npgsql provider + pgvector extension
- **Auth**: JWT Bearer validation + custom Google token verifier
- **Real-time**: SignalR with Redis backplane (StackExchange.Redis 2.8.16)
- **Cache**: StackExchange.Redis (same connection, different logical DB)
- **Blob**: Azure.Storage.Blobs 12.21.0

### 4.2 Why Minimal APIs, Not Controllers

Controller-based ASP.NET MVC adds: `[ApiController]` attribute, `ControllerBase` inheritance, model binding with `[FromBody]`/`[FromRoute]`, and automatic 400 responses from `ModelState`. For a project with a small, disciplined team, this indirection slows reading the code. Minimal APIs in .NET 8+ support parameter injection from DI (services declared as handler parameters are resolved automatically), route group authorization (`group.RequireAuthorization()`), and endpoint filters for cross-cutting concerns — all without the controller ceremony. The result is handlers that are literally the function that runs when the route is hit, readable top-to-bottom without framework magic.

### 4.3 Service Layer Organization

```
Services/
  Matching/
    CandidatePoolService     — Eligibility filtering
    MatchScoringService      — 14-component weighted scoring
    DeliveryBoostService     — Delivery signals and penalties
    DeckSelectionService     — Slot-based deck composition
    DailyDeckOrchestrator    — Cache check + full pipeline orchestration
    MatchExplanationService  — AI explanation generation + caching
    UserVectorBuilder        — Embedding aggregation into UserVector
    WeightLearningService    — Weekly gradient descent on outcome data
    IVectorSearchService     — pgvector ANN queries

  Embeddings/
    PillarEmbeddingService   — OpenAI text-embedding-3-small (1536-dim)
    PhotoEmbeddingService    — Replicate CLIP (512-dim)
    VoiceEmbeddingService    — SpeechBrain ECAPA-TDNN (192-dim)
    StyleEmbeddingService    — In-process feature extraction (128-dim)
    HumorEmbeddingService    — Game behavior analysis (64-dim)
    LifestyleEmbeddingService — Behavioral signals (128-dim)
    EmotionalRhythmService   — Temporal interaction patterns (48-dim)
    AttachmentProxyService   — Chat behavior proxy (4-dim)
    ExpressionEmbeddingService — Tile content mean embedding (1536-dim)

  AI/
    AiProfileService         — Aggregates user context for AI prompts
    OpenAiResilientClient    — HTTP client with circuit breaker + retry
    OpenAiCostTracker        — Daily budget cap enforcement
    CircuitBreakerService    — State machine: CLOSED/OPEN/HALF_OPEN
    OpenAiTaggingService     — Intent and tag extraction

  Security/
    EncryptionService        — AES-256-GCM column encryption
    SecurityAuditService     — Append-only audit log
    PiiSanitizer             — Regex scrubbing + audit hashing
    TrustService             — Trust score updates
    GhostDetectionService    — Ghost score updates
    KeyRotationWorker        — 90-day key rotation check

  Analytics/
    AnalyticsService         — Fire-and-forget event tracking
    AnalyticsRetentionWorker — 12-month PII anonymization

  Insights/
    InsightService           — User pattern analysis + delivery
    InsightBatchWorker       — Nightly batch generation

  Games/
    GameService              — Session lifecycle management
    GameAgentFactory         — Per-game-type AI agent selection
    KnowMeAgent              — KNOW_ME game logic + AI evaluation
    RedGreenFlagAgent        — RED_GREEN_FLAG logic

  Media/
    MediaService             — SAS generation + blob operations
    ModerationService        — Azure Content Moderator integration
    ModerationWorker         — Async moderation queue processor

  Chat/
    NudgeService             — Context-aware conversation prompts
    VenueService             — Google Places integration
    DateFeedbackService      — Post-date reflection collection

  Seasons/
    SeasonService            — Season state + response collection
    SeasonTransitionWorker   — Nightly season boundary checks

  Trust/
    TrustBatchWorker         — Nightly trust score recalculation
    GhostDetectionWorker     — Ghost score maintenance

  CacheService               — Redis abstraction with encryption
  NotificationService        — SignalR + push routing
  JwtTokenService            — Token generation (user + admin)
  GoogleTokenVerifier        — Google JWKS validation
```

### 4.4 DI Lifetime Decisions

| Lifetime | Services | Reason |
|----------|----------|--------|
| Singleton | `JwtTokenService`, `EncryptionService`, `CacheService`, `NotificationService`, `AnalyticsService`, `CircuitBreakerService`, `OpenAiCostTracker` | Shared state (circuit breaker state machine, budget counters) or expensive initialization (JWKS cache, Redis connections). Must use `IServiceScopeFactory` for any DB access. |
| Scoped | All endpoint handlers, all matching services, all embedding services, `WovenDbContext`, all repository-pattern services | One per HTTP request. Can hold DbContext safely. Disposed at request end. |
| HostedService | All 14 background workers | Long-lived singleton processes. Use `IServiceScopeFactory.CreateScope()` on every work cycle to access scoped services. |

### 4.5 All Endpoints by Domain

**Authentication (1)**
`POST /auth/google`

**Me (10)**
`GET /me/insights` · `POST /me/insights/opinion` · `GET /me/accessibility` · `PUT /me/accessibility` · `GET /me/data-summary` · `GET /me/data-export` · `POST /me/visual-preference/reset` · `POST /me/voice-preference/reset` · `DELETE /me/account` · `GET /me/feedback-prompt`

**Onboarding (11)**
`GET /onboarding/state` · `POST /onboarding/welcome` · `PUT /onboarding/basics` · `PUT /onboarding/photos` · `PUT /onboarding/intent` · `GET /onboarding/foundational/questions` · `PUT /onboarding/foundational` · `POST /onboarding/foundational/defer` · `PUT /onboarding/details` · `GET /onboarding/review` · `POST /onboarding/complete`

**Moments (3)**
`GET /moments` · `GET /moments/pending` · `POST /moments/respond`

**Matches (6)**
`GET /matches` · `GET /matches/{id}/profile-access` · `GET /matches/{id}/profile` · `POST /matches/{id}/pop` · `POST /matches/{id}/unmatch` · `POST /matches/{id}/block` · `POST /matches/{id}/feedback`

**Chats (11)**
`GET /chats` · `POST /chats/start` · `GET /chats/{id}` · `POST /chats/{id}/messages` · `POST /chats/{id}/close-gracefully` · `POST /chats/{id}/trial-decision` · `GET /chats/{id}/nudge` · `POST /chats/{id}/nudge/dismiss` · `POST /chats/{id}/date-interest` · `GET /chats/{id}/venue-suggestions` · `POST /chats/{id}/availability`

**Tiles (5)**
`POST /tiles` · `GET /tiles/mine` · `POST /tiles/{id}/highlight` · `DELETE /tiles/{id}/highlight` · `DELETE /tiles/{id}`

**Commons (3)**
`GET /commons` · `POST /commons/refresh` · `POST /commons/{id}/view`

**Orbit (1)**
`POST /orbit/{tileId}`

**Games (9)**
`GET /games/matches/{id}/availability` · `POST /games/matches/{id}/sessions` · `POST /games/sessions/{id}/accept` · `POST /games/sessions/{id}/reject` · `GET /games/sessions/{id}/round` · `POST /games/sessions/{id}/answers` · `POST /games/sessions/{id}/target-answers` · `GET /games/sessions/{id}/result` · `GET /games/matches/{id}/active`

**Seasons (2)**
`GET /seasons/current` · `PUT /seasons/current/responses`

**Media (3)**
`POST /media/upload-token` · `POST /media/confirm` · `DELETE /media/{container}/{**blobPath}`

**Verification (2)**
`POST /verification/selfie` · `GET /verification/status`

**Dynamic Intake (1)**
`PUT /dynamic-intake/{cycleId}`

**Legal (3) — public**
`GET /legal/privacy` · `GET /legal/terms` · `GET /legal/data-practices`

**Admin Analytics (6) — admin role required**
`GET /admin/analytics/overview` · `GET /admin/analytics/funnel` · `GET /admin/analytics/content` · `GET /admin/analytics/ab/{experimentId}` · `GET /admin/analytics/retention` · `GET /admin/analytics/scoring`

**Dev Only (2)**
`POST /debug/token` · `POST /debug/admin-token`

### 4.6 Middleware Pipeline

Requests pass through this pipeline in order:

```
1. CORS                    — Configured origins (frontend Container App + custom domain)
2. Exception Handler       — JSON 500 in Production, stack trace in Development
3. UseAuthentication       — JWT validation
4. UseAuthorization        — Policy enforcement
5. LastActiveAt Middleware — Updates User.LastActiveAt on any authenticated request;
                            checks Redis session key; tracks AppOpened analytics event
                            on new session (2-hour TTL); checks re-engagement insights
6. Request routing         — Minimal API route matching
7. Rate limit enforcement  — Per-endpoint via ICacheService.CheckRateLimitAsync
                            (applied as inline logic at the start of each handler)
8. Handler execution
9. Response                — JSON serialization
```

### 4.7 Error Handling Strategy

- **4xx**: Returned as `Results.BadRequest(new { error = "ERROR_CODE" })` or `Results.StatusCode(429)` with `Retry-After` header. Error codes are ALL_CAPS_SNAKE_CASE constants that the frontend maps to UI copy.
- **5xx**: Global exception handler returns `{ "error": "INTERNAL_ERROR" }` in production. Application Insights captures the full stack trace.
- **AI failures**: All OpenAI/Replicate calls are wrapped in `try/catch` and return degraded results (empty explanation, null embedding) rather than propagating. The circuit breaker prevents cascade failure.
- **Redis failures**: All cache operations catch `RedisException` and return default values. Rate limit checks return `true` (allow) on Redis failure — fail-open is the correct choice.
- **Analytics**: All `TrackAsync` calls are fire-and-forget. The analytics service catches all exceptions internally. A failed tracking call never affects the user's request.

### 4.8 Resilience Patterns

**Circuit Breaker** (`CircuitBreakerService`): Three-state machine (CLOSED → OPEN → HALF_OPEN). Applied to OpenAI and Replicate calls. When OPEN, requests return immediately with null/degraded result without hitting the external service. Resets to HALF_OPEN after a configurable timeout, allowing one probe request.

**Retry**: `OpenAiResilientClient` implements exponential backoff retry (3 attempts, 1s/2s/4s delays) on 429 (rate limit) and 5xx responses. 4xx responses (bad request, auth failure) are not retried.

**Graceful degradation**: Every AI-dependent response has a non-AI fallback. Match explanations fall back to template-based text. Deck scores fall back to pillar cosine similarity only. Nudges fall back to null (not shown).

---

## 5. Database Design

### 5.1 Why Relational, Not Document Store

The matching engine's candidate pool query joins six tables in a single operation: Users, UserProfiles, UserPreferences, Blocks, Matches, and DailyInteractions. This query must be consistent (no eventual consistency lag that would show a blocked user) and must run in under 100ms. A document store would require either denormalization so extreme it destroys write semantics, or cross-document consistency that no document store handles well. PostgreSQL handles this query with a multi-column index scan in 20–40ms at current scale.

Additionally, pgvector as a PostgreSQL extension means ANN search participates in the same transaction and isolation level as relational queries. There is no dual-write, no cache coherence problem, no distributed transaction. The vector and the relational data are always consistent with each other.

### 5.2 PostgreSQL Configuration

```
Version:           16
SKU:               B_Standard_B1ms (1 vCore, 2 GiB RAM)
Storage:           32 GiB
Backup retention:  7 days
Extensions:        pgvector (installed via Terraform)
Private DNS zone:  privatelink.postgres.database.azure.com
Access:            Only from container subnet (NSG rule)
Connection:        Npgsql with connection pooling (default pool size)
```

### 5.3 Schema Organization by Domain

**User Core** (6 tables): `Users`, `AuthIdentities`, `UserProfiles`, `UserPreferences`, `UserPhotos`, `UserIntents`, `UserOptionalFields`, `UserWeeklyVibes`

**Onboarding / Intake** (2 tables): `UserFoundationalQuestionSets`, `UserDynamicIntakeSets`

**Matching and Discovery** (8 tables): `Matches`, `DailyInteractions`, `PendingMatches`, `Blocks`, `MomentResponses`, `DailyDecks`, `MatchExplanations`, `MatchOutcomes`, `CandidateExposures`, `CandidateSignals`

**Chat** (4 tables): `ChatThreads`, `ChatMessages`, `ChatAvailabilitySignals`, `UserRatings`

**Games** (5 tables): `GameSessions`, `GameRounds`, `GameResults`, `GameAnalytics`, `GameOutcomes`

**Tiles and Commons** (6 tables): `Tiles`, `Highlights`, `TileViews`, `TileOrbits`, `TileEngagements`, `TileReports`, `UserEnergyMeters`

**Vectors and Embeddings** (9 tables): `UserVectors`, `UserVectorTags`, `PhotoEmbeddings`, `UserVisualDecisions`, `UserVisualPreferences`, `UserVoicePreferences`, `UserMatchingWeights`, `CfScores`, `ReferencePhotoEmbeddings`

**Seasons and Feedback** (5 tables): `Seasons`, `UserSeasonResponses`, `DateFeedbacks`, `DateFeedbackPrompts`, `UserInsights`

**Security and Audit** (4 tables): `SecurityAuditLogs`, `UserVerifications`, `ModerationQueues`, `TileReports`

**Analytics and Experiments** (5 tables): `AnalyticsEvents`, `AbExperiments`, `AbAssignments`, `AbConversions`, `KeyRotationLogs`

### 5.4 Key Constraints and Why They Exist

**No self-matches** (check constraint on `Matches`):
```sql
CHECK (UserAId != UserBId)
```
Prevents a user from matching with themselves due to a bug in the respond endpoint. Belt-and-suspenders — the application also prevents this, but the constraint makes it impossible at the DB level.

**EDGE requires EdgeOwnerId** (check constraint):
```sql
CHECK (MatchType != 'EDGE' OR EdgeOwnerId IS NOT NULL)
```
An EDGE match without an owner would break profile access computation. Making it impossible at constraint level means the application never has to handle a null edge owner.

**ACTIVE state consistency** (check constraint):
```sql
CHECK (BalloonState != 'ACTIVE' OR (ClosedReason IS NULL AND ClosedAt IS NULL))
```
An ACTIVE match must not have a closed reason or closed timestamp. This prevents partial-state bugs where a match is "active" but carries closure metadata.

**At most one active balloon per pair** (unique partial index):
```sql
CREATE UNIQUE INDEX uq_matches_active_pair
ON Matches (UserAId, UserBId)
WHERE BalloonState = 'ACTIVE'
```
Two users can only have one active balloon at a time. This prevents double-match bugs from race conditions in the respond endpoint. The partial index means closed matches are not subject to the constraint — the same pair can re-match after their balloon expires.

**One MomentResponse per day per directed pair** (unique index):
```sql
CREATE UNIQUE INDEX uq_moment_response_daily
ON MomentResponses (DateUtc, FromUserId, ToUserId)
```
Prevents budget circumvention (spending only 1 but getting 2 YES responses counted). Applied at DB level so a network retry cannot create a duplicate response.

**No unanswered foundational cycle per user at a time** (unique partial index):
```sql
CREATE UNIQUE INDEX uq_foundational_pending
ON UserFoundationalQuestionSets (UserId)
WHERE AnsweredAt IS NULL
```
A user can only have one outstanding foundational question cycle at a time. This prevents the frontend from generating multiple parallel cycles through retry.

### 5.5 pgvector Index Configuration

All vector columns use **HNSW** (Hierarchical Navigable Small World) indexes. The choice between HNSW and IVFFlat:

| | HNSW | IVFFlat |
|---|---|---|
| Build time | Slow | Fast |
| Query time | Fast (O(log n)) | Moderate |
| Memory | Higher | Lower |
| Index update | Good (insertions don't require rebuild) | Requires rebuild on significant changes |
| Accuracy | High (tunable via `ef_construction`) | Lower (tunable via `nlist`) |

HNSW is correct for production. We build the index once and query it continuously. The higher memory footprint is acceptable on a 32GB storage node. The critical advantage is that HNSW handles insertions (new users) gracefully without index rebuilds, which IVFFlat requires for maintaining accuracy as the dataset grows.

Default HNSW parameters (`m=16, ef_construction=64`) are used. These are appropriate for our embedding dimensions and expected dataset size (< 100k users initially).

### 5.6 EF Core Migration Strategy

All schema changes are managed via EF Core migrations (`dotnet ef migrations add`). Migrations run automatically at startup via `db.Database.MigrateAsync()` in `Program.cs`. This is appropriate at current scale but would need to change before horizontal scaling (running migrations on startup from multiple pods simultaneously can cause conflicts — we would switch to a migration-as-a-step CI/CD job before going multi-instance at the database level).

pgvector-specific DDL (HNSW index creation, extension installation) is applied via raw SQL in `MigrationBuilder.Sql()` calls within specific migrations, since EF Core does not natively understand pgvector index syntax.

---

## 6. The Matching Engine

The matching engine is Woven's core competitive advantage. Everything else is table stakes. This section explains every component in full.

### 6.1 Candidate Pool Generation

The pool is generated by `CandidatePoolService`. It applies hard eligibility constraints in SQL — candidates failing these constraints are never scored.

**Eligibility filters (all in a single query):**

1. `UserId != currentUserId` — no self-matches
2. Not in `Blocks` table (either direction — blocked or was blocked by)
3. No ACTIVE `Match` row between the pair (unique partial index makes this a fast lookup)
4. Not in `CandidateExposures` for today's UTC date + `MOMENTS_DECK` surface (prevents same-day re-show)
5. `UserProfile.Gender` is in `currentUser.InterestedInJson` AND `currentUser.Gender` is in `candidate.InterestedInJson` (reciprocal gender match)
6. `UserProfile.Age` is between `currentUser.AgeMin` and `currentUser.AgeMax` AND `currentUser.Age` is between `candidate.AgeMin` and `candidate.AgeMax` (reciprocal age range)
7. Relationship structure compatibility: `MONO_ONLY` and `NONMONO_ONLY` are mutually exclusive; `OPEN` is compatible with everything
8. `User.TrustScore >= 0.25` (trust gate — low-trust users are excluded from pool entirely)
9. `User.ProfileStatus = COMPLETE`
10. Geographic distance within the maximum of both users' `DistanceMiles` preferences (haversine calculation applied in-process after SQL fetch, since PostgreSQL doesn't have a native haversine function without PostGIS)

The query returns up to 200 candidates for scoring.

### 6.2 Multi-Stage Retrieval

After eligibility filtering, the scoring pipeline runs in four stages:

**Stage 1 — ANN Retrieval**: `IVectorSearchService` issues an HNSW nearest-neighbor query against `UserVectors.PillarEmbedding` using cosine distance (`<=>` operator). This retrieves the top 200 semantically similar candidates regardless of other signals, forming the pre-score candidate set.

**Stage 2 — Full Scoring**: `MatchScoringService` scores every candidate in the pre-score set using all 14 components. This runs in-process and is parallelized across candidates using `Task.WhenAll`.

**Stage 3 — Delivery Boost Application**: `DeliveryBoostService` applies behavioral signals on top of the base score: reciprocal exposure boosts, pending save boosts, fatigue penalties, past match penalties, orbit gravity, trust multiplier, ghost penalty, and verification bonus.

**Stage 4 — Deck Selection**: `DeckSelectionService` applies a slot-based composition algorithm to select exactly 5 candidates with diversity across match buckets.

### 6.3 Full Scoring Formula

The base score is a weighted sum across 14 components, all normalized to [0, 100]:

| Component | Default Weight | Source |
|-----------|---------------|--------|
| PillarScore | 0.20 | Cosine similarity of 1536-dim PillarEmbeddings |
| IntentScore | 0.13 | Intent type + openness alignment |
| ExpressionScore | 0.10 | Cosine similarity of tile ExpressionEmbeddings |
| StyleScore | 0.09 | StyleEmbedding alignment (128-dim) |
| VisualScore | 0.10 | PhotoEmbedding vs UserVisualPreference (512-dim) |
| VoiceScore | 0.08 | VoiceEmbedding vs UserVoicePreference (192-dim) |
| HumorScore | 0.07 | HumorEmbedding alignment (64-dim) |
| LifestyleScore | 0.08 | LifestyleEmbedding alignment (128-dim) |
| BehavioralLifestyleScore | 0.05 | Tag overlap from UserVectorTags |
| EmotionalRhythmScore | 0.04 | EmotionalRhythmEmbedding (48-dim) |
| AttachmentScore | 0.04 | AttachmentProxyEmbedding (4-dim) |
| OrbitGravityScore | 0.08 | Decayed orbit engagement |
| PulseScore | 0.06 | Current dynamic intake alignment |
| CfScore | 0.03 | Collaborative filtering from CfScores table |

**Dynamic weight redistribution**: When a component's embedding is unavailable (user has no voice note, no tiles, insufficient visual decisions), its weight is redistributed proportionally across available components. The total weight always sums to 1.0.

**Depth boost**: `min(15.0, DepthSignals × 2.5)` — adds up to 15 points based on conversation depth signals (message count, response time, session count) from prior interactions.

**Season freshness**: +5 points if the candidate has submitted responses to the current season (signals active engagement).

**Intent multiplier**: Applied after weighted sum, range [0.70, 1.05]. Higher multiplier when intent types align (both looking for long-term, both open to casual, etc.).

**Trust dampening**: `rawScore × clamp(trustScore, 0, 1)`. A trust score of 0.5 (neutral) applies no dampening. A trust score of 0.1 reduces the final score by 90%, effectively suppressing low-trust users from reaching high positions even if their compatibility score is high.

**Ghost penalty**: Final score multiplied by `ghostScore` [0.0, 1.0]. A ghost score of 0.8 means 80% of the computed score is preserved. At 0.0 (chronic ghoster), the candidate is entirely suppressed.

**Verified bonus**: `score × 1.05` — verified users get a 5% multiplier applied last.

**Final range**: [0, 100], where > 80 = STRONG, 60–79 = GOOD, 40–59 = OK, < 40 = WILDCARD.

### 6.4 Deck Composition

`DeckSelectionService` fills 5 slots using a typed-slot algorithm:

```
Slot 1-2: CORE_FIT    — Intent ≥ 70 AND PillarScore ≥ 65
Slot 3:   LIFESTYLE_FIT — LifestyleScore ≥ 70
Slot 4:   CONVERSATION_FIT — PulseScore ≥ 70 (weekly intake alignment)
Slot 5:   EXPLORER    — Total ≥ 60 with lowest PillarScore (intentional discovery)
```

If a typed slot cannot be filled (insufficient candidates meeting the threshold), it falls back to the next available candidate by total score. The EXPLORER slot deliberately picks a candidate who is different from the user's apparent "type" — this is the key mechanism preventing the filter bubble problem endemic to recommendation systems.

### 6.5 Embedding System

All embeddings are stored in `UserVectors` (versioned) or dedicated embedding tables. Every user has a `UserVector` with a Version integer — when foundational answers are re-submitted or a new intake cycle completes, a new version is written and the old one is superseded (but not deleted, for auditing).

**PillarEmbedding** (1536-dim): Generated by OpenAI `text-embedding-3-small`. Input: concatenated text from all 5 foundational answers + current dynamic intake answers + season responses. The concatenation is structured as a bulleted prompt with pillar labels to give the embedding model context for dimension alignment. Rebuilt on each foundational submission and intake cycle completion.

**ExpressionEmbedding** (1536-dim): Mean pooling of individual tile text embeddings (tile captions). Represents "what this person expresses publicly." Updated by `EmbeddingBatchWorker` when new tiles are added.

**StyleEmbedding** (128-dim): In-process feature extraction from bio, optional field values, and intent reflection sentence. Features include vocabulary richness, sentence length distribution, formality indicators, and topic presence. Implemented without external API calls.

**HumorEmbedding** (64-dim): Extracted from game answers, tile captions, and chat message patterns (punctuation, emoji, sarcasm markers). Updated by `EmbeddingBatchWorker`.

**LifestyleEmbedding** (128-dim): Extracted from optional lifestyle fields (job, diet, habits, hobbies, children, workout preference). Categorical encoding + continuous normalization.

**EmotionalRhythmEmbedding** (48-dim): Temporal interaction features — time-of-day of messages, response latency patterns, session frequency, day-of-week activity. Captures when and how quickly this person engages, not just what they say.

**AttachmentProxyEmbedding** (4-dim): Derived from chat behavior: initiation rate, avg message length, response consistency, graceful vs abrupt exit rate. A proxy for attachment style (secure/anxious/avoidant/disorganized). 4 dimensions = one per attachment style probability.

**PhotoEmbedding** (512-dim): Replicate CLIP vision model applied to profile photos. Captures visual aesthetics (color palette, composition, activity type) not facial features. EXIF data stripped before upload.

**VoiceEmbedding** (192-dim): SpeechBrain ECAPA-TDNN speaker embedding. Captures voice tone, prosody, rhythm — not content. Generated from voice note uploads.

**UserVisualPreference** (512-dim × 2): Aggregated YES-side and NO-side centroids of candidate PhotoEmbeddings. Built from `UserVisualDecisions`. Requires ≥ 10 decisions before contributing to scoring (cold start threshold). Updated by `EmbeddingBatchWorker`.

**UserVoicePreference** (192-dim): Centroid of VoiceEmbeddings of candidates the user has positively engaged with (Magical response + chat started). Updated by `EmbeddingBatchWorker`.

### 6.6 Feedback Loops

The engine learns from outcomes through four mechanisms:

**Visual decision recording**: Every `POST /moments/respond` call records a `UserVisualDecision` (fire-and-forget). These accumulate into `UserVisualPreference` vectors during the nightly `EmbeddingBatchWorker` run.

**Match outcome tracking**: `MatchOutcomeService` records outcomes at each stage (CHAT_STARTED, MESSAGES_24H, EXPIRED, UNMATCHED, BLOCKED) in `MatchOutcomes`. This data feeds `WeightLearningService`.

**Post-date feedback**: `DateFeedback` (stars, felt right, felt off, meet again) is the highest-signal outcome. When `meetAgain = "yes"` and `stars ≥ 4`, the pair's component scores become positive training examples in the next `WeightLearningService` run.

**Weekly weight learning**: `WeightLearningService` runs every Sunday at 04:00 UTC. For each user with ≥ 5 outcome records:
1. Compute Pearson correlation between each component's contribution and the outcome valence
2. Apply gradient: `learned_weight = default_weight + 0.1 × gradient`
3. Clamp to [0.01, 0.50] to prevent any component from dominating or being zeroed out
4. Write to `UserMatchingWeights`

### 6.7 Match Explanation Generation

`MatchExplanationService` generates explanations for every candidate in the deck, cached in `MatchExplanations` per (userId, candidateId, date).

**Input to OpenAI** (structured prompt via `AiProfileService`):
- Viewer's pillar tags, intent, optional fields
- Candidate's pillar tags, intent, optional fields
- Component score breakdown (which components are high)
- Season response overlap (shared themes)

**Output**:
- `headline`: one sentence explanation of the connection
- `bullets`: 3–5 specific reasons derived from score breakdown
- `tone`: characterization (curious, warm, adventurous, etc.)
- `dateIdea`: a specific, concrete first date suggestion

The date idea is re-generated when Find Love unlocks (in case it needs to be more specific given the conversation that has developed). This second generation incorporates the venue context from Google Places if venue suggestions have already been fetched.

---

## 7. Real-Time System

### 7.1 SignalR Hub Architecture

`WovenHub` (`/hubs`) is an ASP.NET Core SignalR hub with `[Authorize]` attribute. On connection, the user is added to a group named `user:{userId}`. All server-to-client messages are sent to the group, enabling multi-device support (all tabs/sessions for the same user receive the event).

Redis backplane (`AddSignalR().AddStackExchangeRedis(redisConnection)`) ensures messages sent from any backend replica reach the connection hosted on any other replica. Without this, a message sent from replica 2 to user 5 would fail if user 5's WebSocket is on replica 1.

### 7.2 Message Signing

Every SignalR message from the server is HMAC-SHA256 signed. The signature is derived from `IEncryptionService` using a purpose-specific key string `"SignalR.MessageSigning.v1"`. The frontend verifies the signature before acting on any received event. This prevents XSS-injected scripts from faking server events and triggering UI state changes.

### 7.3 Six Server Event Types

| Event | Trigger | Payload |
|-------|---------|---------|
| `MomentExpired` | BalloonExpiryWorker closes a match | `{ matchId }` |
| `MessageReceived` | `POST /chats/{id}/messages` | `{ threadId, message }` |
| `FindLoveUnlocked` | `FindLoveAt` passes | `{ matchId, threadId, dateIdea }` |
| `DateInterestMutual` | Both users express date interest | `{ matchId, threadId }` |
| `AvailabilitySignal` | `POST /chats/{id}/availability` | `{ threadId, signalText, senderName }` |
| `TrialDecisionRequired` | Partner submitted trial decision | `{ threadId, partnerDecision }` |

### 7.4 JWT Auth for WebSocket

WebSocket upgrade requests cannot carry custom headers. The SignalR client passes the JWT as `?access_token={token}` in the query string. The server's JWT validation middleware reads this query parameter for `/hubs` connections via:

```csharp
OnMessageReceived = context => {
    var token = context.Request.Query["access_token"];
    if (!string.IsNullOrEmpty(token) && context.Request.Path.StartsWithSegments("/hubs"))
        context.Token = token;
    return Task.CompletedTask;
}
```

### 7.5 Reconnection Strategy

The client uses Angular's SignalR library with automatic reconnect delays of `[0, 2000, 10000, 30000]` ms. After exhausting the retry schedule, the connection is considered failed and the user sees a "reconnecting" indicator. The connection is re-established on next app foreground event (Page Visibility API).

---

## 8. AI Integration Layer

### 8.1 OpenAI text-embedding-3-small

**When**: PillarEmbedding generation on `POST /onboarding/complete`, foundational re-submission, and dynamic intake completion. Also tile ExpressionEmbedding.

**Input**: Structured text (~500–800 tokens). Concatenated foundational answers with pillar-labeled headers.

**Output**: 1536-dim float array, normalized to unit vector.

**Cost**: ~$0.00002 per 1000 tokens. A full embedding generation is ~$0.000016 per user.

**Caching**: Embeddings are stored in `UserVectors` and never re-generated unless the source text changes. The nightly `EmbeddingBatchWorker` only processes users whose embeddings are stale.

### 8.2 OpenAI gpt-4.1-mini Completions

Used at five touchpoints via the Responses API (structured output):

| Use | Input tokens (approx) | Output tokens (approx) | When |
|-----|-----------------------|------------------------|------|
| Match explanation + date idea | 800 | 200 | Deck build (cached per day) |
| Game evaluation (KNOW_ME) | 1000 | 300 | On both players completing answers |
| Insight generation | 600 | 200 | Nightly batch per user |
| Conversation nudge | 400 | 100 | On nudge request (cached 48h) |
| Date idea regeneration | 600 | 150 | On Find Love unlock |

**Circuit breaker**: `OpenAiResilientClient` wraps all gpt-4.1-mini calls. On three consecutive failures, the circuit opens for 60 seconds. During open state, callers receive null/empty results and log a warning.

**Cost cap**: `OpenAiCostTracker` maintains a daily rolling token counter. When the daily budget is exceeded, new completion requests are rejected and callers receive fallback responses.

### 8.3 Replicate CLIP (photo-embeddings)

**When**: Nightly by `EmbeddingBatchWorker` for users with new or missing photo embeddings.

**Input**: Profile photo URLs (uploaded to Azure Blob, SAS URL passed to Replicate).

**Output**: 512-dim visual feature vector.

**Privacy**: EXIF metadata (GPS, device info) is stripped from all photos before they are uploaded to Blob Storage. The URL passed to Replicate contains no PII beyond the image content.

**Caching**: Results stored in `PhotoEmbeddings` and `ReferencePhotoEmbeddings`. Not re-processed unless the photo changes.

### 8.4 SpeechBrain ECAPA-TDNN (voice-embeddings)

**When**: After `POST /media/confirm` for voice-note container. Enqueued for async processing.

**Implementation**: `VoiceEmbeddingService` calls a Python subprocess: `python3 scripts/speechbrain_embed.py --input {blobPath}`. The script downloads the audio from Blob Storage, runs ECAPA-TDNN, and outputs the 192-dim embedding as JSON to stdout.

**Dev smoke test**: At startup in Development environment, `Program.cs` runs `python3 scripts/speechbrain_embed.py --test`. Logs "SpeechBrain: OK" or "SpeechBrain: UNAVAILABLE — voice embedding will be skipped." Failure never blocks startup.

**Production note**: In production, if SpeechBrain fails or is unavailable, the voice embedding is skipped for that upload. The VoiceScore component will be absent from scoring for that user and its weight redistributed to other components.

### 8.5 Azure Content Moderator

**When**: After every tile upload, voice note upload, and bio/optional field text submission.

**Thresholds**:
- Auto-approve: confidence < 0.4 on all categories
- Human review queue: 0.4 ≤ confidence < 0.8
- Auto-reject: confidence ≥ 0.8 on any harmful category

`ModerationWorker` processes the review queue asynchronously. The tile/content remains hidden until moderation completes (not optimistically shown).

### 8.6 Azure Speech Service

**When**: Voice note transcription, feeding into text moderation pipeline.

The transcription is used only for moderation text analysis. The text is discarded after the moderation decision. Audio is never transcribed for user-facing purposes (voice notes play as audio, not as text).

### 8.7 Google OAuth JWKS

`GoogleTokenVerifier` fetches Google's public key set from `https://www.googleapis.com/oauth2/v3/certs` and caches it for 24 hours. Token validation is performed locally against the cached JWKS — no round-trip to Google per login after the initial JWKS fetch.

### 8.8 Google Places API

**When**: `GET /chats/{threadId}/venue-suggestions` (requires mutual date interest).

**Input**: Both users' `City` and `State` from their profiles.

**Output**: Ranked list of venues with name, type, address, rating.

### 8.9 Cost Controls

```
$50/day OpenAI budget cap (OpenAiCostTracker)
Circuit breaker on all external AI calls (60s open window)
Match explanation cached per (userId, candidateId, date) — never re-generated
Embedding batch: only processes stale/missing embeddings
SpeechBrain: local process, no per-call cost
Azure Content Moderator: pay-per-call, budgeted separately
```

---

## 9. Caching Strategy

### 9.1 Redis Architecture

Single Redis instance (Azure Cache for Redis, Basic C0, 250 MB). Used for three distinct purposes:

1. **Application cache** — daily decks, sessions, nudge dismissal, date interest signals
2. **Rate limiters** — all per-endpoint counters
3. **SignalR backplane** — message routing across replicas

C0 Basic has no Redis Cluster, no geo-replication, and no persistence. A Redis restart clears all state. This is acceptable at current scale because:
- Daily decks are rebuilt from PostgreSQL if cache miss
- Rate limit counters reset on Redis restart (fail-open is the correct behavior)
- SignalR connections reconnect automatically

### 9.2 Cache Key Schema

All keys follow the pattern `{namespace}:{qualifier}`.

| Key pattern | TTL | Purpose |
|-------------|-----|---------|
| `deck:{userId}:{dateUtc}` | Until UTC midnight | Daily Moments deck |
| `session:{userId}` | 2 hours | Analytics session ID |
| `rl:auth:{ipHash}:{date}` | Until midnight | Auth rate limit (IP) |
| `rl:verify:{userId}:{date}` | Until midnight | Verification rate limit |
| `rl:upload:{userId}:{date}` | Until midnight | Upload token rate limit |
| `rl:orbit:{userId}:{date}` | Until midnight | Orbit rate limit |
| `rl:opinion:{userId}:{yyyy-MM}` | 31 days | Opinion rate limit |
| `nudge:dismiss:{userId}:{threadId}` | 48 hours | Nudge dismissal |
| `date-interest:{matchId}` | 7 days | Date interest notification dedup |
| `feed:{userId}:{sessionId}` | Session-scoped | Commons feed pagination |
| `embedding:{userId}:pillar` | 24 hours | Pillar embedding lookup cache |
| `jwks:google` | 24 hours | Google public key cache |

### 9.3 Encrypted Cache Keys

`CacheService` automatically encrypts values stored under keys matching `session:*` and `embedding:*`. The rationale:
- Session data may include user activity metadata
- Embedding vectors, if leaked, could enable partial reconstruction of user text

The encryption uses `IEncryptionService` (AES-256-GCM) with a key derived specifically for the cache purpose.

### 9.4 Cache Invalidation

| Trigger | Cache invalidated |
|---------|-------------------|
| User completes onboarding | — (deck built on first GET /moments request) |
| User responds to card | Deck is filtered at request time (DailyDeck JSON re-read, responded IDs excluded) |
| User submits new foundational answers | `embedding:{userId}:pillar` deleted (will rebuild on next deck cycle) |
| Account deletion | All `session:{userId}`, `rl:*:{userId}:*`, `embedding:{userId}:*` keys deleted |

### 9.5 Fail-Open Strategy

Every Redis operation is wrapped in try/catch. On failure:
- `GetAsync<T>`: returns `default(T)` (null)
- `SetAsync`: silently discards (writes only, never reads again)
- `CheckRateLimitAsync`: returns `true` (allow)
- `IncrementAsync`: returns `-1` (sentinel for "counter unavailable")

This ensures Redis downtime never degrades the user experience below the non-cached baseline.

---

## 10. Security Architecture

### 10.1 Defense in Depth Model

Security is applied at seven independent layers. A failure at any one layer does not compromise the system.

### 10.2 Layer 1: Network Isolation

- Backend Container App has `external_enabled = false`. It has no public IP. It is reachable only from within the Container Apps environment.
- PostgreSQL is on a private subnet with a private DNS zone. It has no public endpoint. NSG rules restrict traffic to the container subnet (`10.0.1.0/24`).
- Redis has TLS enabled. It is not VNet-integrated at the Basic C0 tier but is accessible only with the primary key (kept in Key Vault, injected as secret).
- VNet: `10.0.0.0/16` with three subnets: container (`10.0.1.0/24`), database (`10.0.2.0/24`), private endpoints (`10.0.3.0/24`).

### 10.3 Layer 2: Authentication

Google ID tokens are verified against Google's JWKS (cached 24h). On success, the backend issues its own JWT:

```
Algorithm:   HMAC-SHA256
Claims:      uid (int), sub (string), email (string), iat, exp
Admin adds:  role = "admin"
Standard TTL: 60 minutes
Admin TTL:    60 minutes
Signing key:  64-char random, generated by Terraform, stored in Container Apps secret
```

The signing key is never stored in code, git, or Terraform state in plaintext — it is written to Container Apps secrets by Terraform using the `random_password` resource.

### 10.4 Layer 3: Authorization

- Default policy: valid JWT required on all endpoints except `/auth/google`, `/legal/*`, and `/health/*`
- Admin policy: JWT must carry `role = "admin"` claim — only issuable via `POST /debug/admin-token` (dev only) or by a back-office process
- Resource ownership: every mutating operation validates that the requesting user owns or is a participant in the target resource (match, thread, tile, blob)
- Match access level: computed per-request by `GET /matches/{id}/profile-access` — FULL for PURE matches and EDGE owners, LIMITED for EDGE non-owners until `BothMessagedAt` is set

### 10.5 Layer 4: Rate Limiting

All rate limits use Redis atomic increment (`IncrementAsync` which is `INCR` + conditional `EXPIRE`). Fail-open on Redis failure.

| Endpoint | Limit | Window | Key basis |
|----------|-------|--------|-----------|
| POST /auth/google | 20 | Per day | SHA-256(IP + "rl-auth-v1") |
| POST /verification/selfie | 5 | Per day | userId |
| POST /media/upload-token | 20 | Per day | userId |
| POST /orbit/{tileId} | 50 | Per day | userId |
| POST /me/insights/opinion | 1 | Per month | userId |
| GET /me/data-export | 1 | 30 days | Redis flag (boolean key) |

All rate-limited endpoints return HTTP 429 with `Retry-After: {seconds}`.

### 10.6 Layer 5: Encryption

**Column encryption**: AES-256-GCM with 12-byte nonce and 16-byte authentication tag. Encrypted columns:
- `User.Email`
- `User.FullName`
- `UserProfile.City`, `UserProfile.State`
- `UserOptionalField.Value`
- `UserIntent.ReflectionSentence`

**Key derivation**: HKDF derives purpose-specific sub-keys from the master key (`Encryption__MasterKey`). Purpose strings: `"column-encryption-v1"`, `"signalr-signing-v1"`, `"cache-encryption-v1"`. This means a compromised column encryption key does not compromise SignalR message signing.

**Key rotation**: `KeyRotationWorker` checks every 7 days whether the master key is older than 90 days. When rotation is due, it logs a structured alert to Application Insights. Actual rotation is a manual procedure (update the Container Apps secret, restart pods, allow EF interceptors to re-encrypt on next write). A migration-based bulk re-encryption is not yet implemented.

**Redis encryption**: Values under `session:*` and `embedding:*` keys are AES-256-GCM encrypted before storage.

**SignalR signing**: Each server-to-client message carries an HMAC-SHA256 signature computed with the `"signalr-signing-v1"` derived key. The client verifies the signature before processing any event.

### 10.7 Layer 6: PII Protection

- **Email and name**: Encrypted at column level. Never logged.
- **IP addresses**: Never stored in plaintext. Rate-limit keys use `SHA-256(ip + "rl-auth-v1")`.
- **Analytics**: Events linked to `UserIdHash = SHA-256(userId + salt)` rather than raw `UserId`. Session IDs are random GUIDs with 2-hour TTL.
- **12-month retention**: `AnalyticsRetentionWorker` nulls `UserIdHash` and `SessionId` on events older than 12 months. Aggregate statistics remain. Individual linkage is severed.
- **Outbound PII filtering**: `PiiSanitizer` applies regex patterns for email addresses, phone numbers, social handles, and street addresses to any AI-bound content. Strip-before-send.
- **EXIF stripping**: All photos are processed to remove EXIF metadata (GPS coordinates, device fingerprint, timestamp) before upload to Blob Storage.

### 10.8 Layer 7: Trust Signals

**Trust score** (0.0–1.0, default 0.5): Updated nightly by `TrustBatchWorker`. Inputs: device fingerprint consistency, login velocity, verification status, ratings received from past matches, ghost signal frequency. Users below 0.25 are excluded from the candidate pool (trust gate). Users above 0.75 receive a 5% score multiplier.

**Ghost score** (0.0–1.0, default 0.5): Updated nightly and every 6 hours by `GhostDetectionWorker`. High ghost scores indicate chronic non-responsiveness. Applied as a multiplicative penalty on the matching score.

**Catfish detection**: When a new profile photo is uploaded, `PhotoEmbeddingService` checks the 512-dim CLIP embedding against `ReferencePhotoEmbeddings` for near-duplicate detection (cosine similarity > 0.95). High similarity to another user's reference photo flags for human review.

**Content moderation**: All user-generated content passes through Azure Content Moderator before being shown to other users.

---

## 11. Background Worker System

### 11.1 IHostedService Architecture

All 14 workers implement `IHostedService` via the `BackgroundService` base class. Each overrides `ExecuteAsync(CancellationToken)` with a `while (!stoppingToken.IsCancellationRequested)` loop. Workers never take `WovenDbContext` as a constructor parameter — they always receive `IServiceScopeFactory` and create a scope per work cycle.

### 11.2 Full Schedule (UTC, no conflicts)

| UTC Time | Worker | Frequency | What it reads | What it writes |
|----------|--------|-----------|---------------|----------------|
| Every 1 min | BalloonExpiryWorker | Continuous | Matches (ACTIVE, ExpiresAt <= now) | Matches (CLOSED, EXPIRE) |
| Every 6h | GhostDetectionWorker | Continuous | ChatThreads, ChatMessages, Matches | User.GhostScore |
| 01:00 | SeasonTransitionWorker | Nightly | Seasons (EndDate) | Seasons, UserSeasonResponses |
| 02:00 | TrustBatchWorker | Nightly | Users, AuthIdentities, Blocks, UserRatings | User.TrustScore |
| 02:15 | AnalyticsRetentionWorker | 1st of month | AnalyticsEvents (> 12 months) | AnalyticsEvents (anonymize) |
| 02:30 | EmbeddingBatchWorker | Nightly | UserPhotos, Tiles, UserVectors | PhotoEmbeddings, UserVectors, UserVoicePreferences |
| 03:00 | CfBatchWorker | Nightly | MatchOutcomes, MomentResponses | CfScores |
| 03:30 | GhostDetectionWorker | Nightly pass | ChatThreads (ACTIVE, silent) | User.GhostScore |
| 04:00 | WeightLearningBatchWorker | Weekly (Sun) | MatchOutcomes, DateFeedbacks, UserMatchingWeights | UserMatchingWeights |
| 04:30 | InsightBatchWorker | Nightly | MatchOutcomes, ChatMessages, UserVectors | UserInsights |
| 05:00 | SecurityAuditCleanupWorker | Weekly (Sun) | SecurityAuditLogs (> retention) | SecurityAuditLogs (delete/anonymize) |
| 06:00 | WeeklyDigestWorker | Weekly (Sun) | Users (inactive 7+ days), Matches | Notifications (SignalR + push) |
| 08:00 | FeedbackTriggerWorker | Daily | Matches (DateIdeaInterestedAt set, no prompt yet) | DateFeedbackPrompts, Notifications |
| Continuous | ModerationWorker | Queue-driven | ModerationQueues (PENDING) | ModerationQueues (APPROVED/REJECTED) |

### 11.3 Nightly Sequencing Rationale

The nightly batch runs in this order: Trust → Embedding → CF → WeightLearning → Insight.

This sequence is intentional:
1. **Trust first** — establishes which users are eligible for the next deck build. If a user's trust score drops below the gate threshold, they should be excluded before embeddings are regenerated for them.
2. **Embedding second** — produces the fresh vectors that CF and WeightLearning will consume.
3. **CF third** — uses the freshly computed embeddings and latest outcome data.
4. **WeightLearning fourth** — consumes CF scores and outcome data with fresh embeddings as input.
5. **Insight last** — runs with all fresh data available, produces the most current insights.

### 11.4 Error Handling in Workers

Every worker wraps its work cycle in try/catch:
```csharp
catch (OperationCanceledException) { break; }      // Clean shutdown
catch (Exception ex) {
    _logger.LogError(ex, "[WorkerName] cycle failed");
    await Task.Delay(TimeSpan.FromHours(1), ct);   // Back off, retry next hour
}
```

A failed cycle does not crash the worker. It logs the exception to Application Insights, waits 1 hour, and retries. This means a transient database error does not permanently break the nightly batch.

---

## 12. Media Pipeline

### 12.1 Azure Blob Storage Containers

Three private containers (no public access):

| Container | Content | Lifecycle |
|-----------|---------|-----------|
| `profile-photos` | Profile photo uploads | Deleted on account deletion |
| `tile-media` | Tile images, videos | Deleted with tile or on account deletion |
| `voice-notes` | Voice note recordings | Deleted with tile or on account deletion |

### 12.2 SAS Token Pattern (Direct Upload)

The backend never handles binary upload streams. Instead:

1. Client calls `POST /media/upload-token` with `{ container, fileName, contentType }`
2. Backend validates ownership, checks rate limit (20/day), generates a SAS token with:
   - 15-minute expiry
   - Write permission only (no read, no delete, no list)
   - Exact blob path: `{userId}/{uuid}/{fileName}`
3. Client uploads directly to Azure Blob Storage using the SAS URL
4. Client calls `POST /media/confirm` with `{ blobPath, container }`
5. Backend verifies blob exists and enqueues processing

This eliminates the backend as a binary data middleman. The backend only handles JSON. Binary payloads never enter the .NET process.

### 12.3 Processing Pipeline

After `POST /media/confirm`:

**Profile photos**: Moderation check only. No transcoding. CDN delivery via SAS URL or direct Blob URL.

**Tile images**: Moderation check → if approved, tile becomes visible. If pending review, tile shows a placeholder.

**Tile videos**: Frame extraction (every 2 seconds) → each frame sent to Azure Content Moderator → audio extracted → Azure Speech transcription → text moderation. `ModerationWorker` processes all steps asynchronously.

**Voice notes**: Azure Speech transcription → text moderation → SpeechBrain embedding generation. Three separate async steps.

### 12.4 Media Deletion on Account Delete

`DELETE /me/account` calls `IMediaService.DeleteAllForUserAsync(userId, ct)` which:
1. Lists all blobs under `profile-photos/{userId}/`
2. Lists all blobs under `tile-media/{userId}/`
3. Lists all blobs under `voice-notes/{userId}/`
4. Deletes all listed blobs via `BlobClient.DeleteIfExistsAsync`

This runs before the database deletion to ensure blobs are cleaned up even if the DB transaction fails.

---

## 13. Infrastructure and Deployment

### 13.1 All 34 Azure Resources

**Networking (7)**
- Virtual Network (`10.0.0.0/16`)
- Container subnet (`10.0.1.0/24`)
- Database subnet (`10.0.2.0/24`)
- Private endpoints subnet (`10.0.3.0/24`)
- NSG for container subnet (rules: allow HTTPS inbound from Container Apps, deny all else)
- NSG for database subnet (allow 5432 from container subnet only)
- Private DNS zone (`privatelink.postgres.database.azure.com`)

**Compute (2)**
- Container Apps Environment (VNet-integrated, internal load balancer)
- Backend Container App (internal ingress, 1–5 replicas, 0.5 CPU / 1Gi)
- Frontend Container App (external ingress, 1–3 replicas, 0.5 CPU / 1Gi)

**Data (3)**
- PostgreSQL Flexible Server (B_Standard_B1ms, 32GB, v16, pgvector extension)
- Redis Cache (Basic C0, 250MB, TLS enabled)
- Azure Storage Account (3 private containers)

**Identity (2)**
- User-assigned managed identity (`woven-backend-acr-identity`)
- AcrPull role assignment on ACR

**Registry (1)**
- Azure Container Registry (Basic SKU)

**Monitoring (2)**
- Log Analytics Workspace (30-day retention default)
- Application Insights (connected to Log Analytics)

**Security (generated by Terraform)**
- JWT signing key (`random_password`, 64 chars)
- All secrets stored as Container Apps secrets (not in Terraform state plaintext)

### 13.2 Terraform Module Organization

```
infra/
  main.tf          — Root: calls all modules
  variables.tf     — Input variable declarations with validation
  outputs.tf       — 30+ outputs (URLs, connection strings, IDs)
  locals.tf        — Name construction, resource naming map
  provider.tf      — Azure provider config
  backend.tf       — Terraform state in Azure Storage
  versions.tf      — Provider version constraints

  modules/
    container_apps/  — Frontend + backend apps, managed identity
    postgres/        — Flexible Server + private DNS + VNet integration
    networking/      — VNet, subnets, NSGs
    acr/             — Container Registry
    monitoring/      — Log Analytics + App Insights
```

### 13.3 CI/CD Pipeline

GitHub Actions with Azure OIDC federation (no stored credentials):

```
On push to main:
  1. Build backend Docker image
  2. Run dotnet test
  3. Build frontend Docker image
  4. Push both images to ACR (tagged with commit SHA)
  5. Run terraform plan (PR gate) or terraform apply (main branch)
  6. Update Container App image tags to new SHA
  7. Container Apps performs rolling revision deployment
```

Azure OIDC: the GitHub Actions workflow authenticates to Azure using a federated credential on the managed identity. No client secrets or service principal keys are stored in GitHub Secrets.

### 13.4 Local Development

```
docker-compose.yml starts:
  postgres:16 with pgvector extension (port 5432)
  redis:7 (port 6379)
  azurite (Azure Blob emulator, port 10000)

Backend: dotnet run (or Visual Studio F5)
Frontend: ng serve (port 4200)

Environment: appsettings.Development.json overrides
  ConnectionStrings__DefaultConnection → localhost postgres
  Redis__ConnectionString → localhost:6379
  Azure__Storage__ConnectionString → Azurite connection string
  IsModerationEnabled → "false" (skip content moderation locally)
  
Dev-only endpoints:
  POST /debug/token       — Issue JWT for any userId
  POST /debug/admin-token — Issue admin JWT for any userId
```

---

## 14. Scalability Considerations

### 14.1 Current Scale Design

The system is designed for 10k–50k MAU initially. At this scale:
- 1–3 backend replicas handle normal load
- PostgreSQL B_Standard_B1ms handles ~100 concurrent connections (Npgsql pool)
- Redis Basic C0 (250MB) holds all sessions, rate limiters, and deck caches comfortably
- pgvector HNSW search over 50k user vectors completes in < 20ms

### 14.2 Scaling Triggers and Responses

**10k → 100k MAU**:
- Redis: upgrade to Standard C1 (1GB, persistence, replication)
- PostgreSQL: upgrade to General Purpose D2s_v3 (2 vCores, 8 GiB), add read replica for analytics queries
- pgvector HNSW: tune `m` and `ef_construction` based on measured recall/latency
- Container Apps: increase max replicas to 10

**100k → 1M MAU**:
- Extract batch workers to separate Container App (currently co-located with API)
- Add distributed locking (Azure Blob Storage lease or Redis SETNX) to batch workers to support multiple instances
- Add message queue (Azure Service Bus) to replace fire-and-forget for embedding and moderation tasks
- Consider read replicas or sharding for UserVectors table
- Deck caching: upgrade to Redis Cluster for horizontal scaling

### 14.3 Current Bottlenecks

1. **Batch workers**: Single instance only (no distributed locking). At current scale, one instance running in 10 minutes is fine. At 500k users, the nightly embedding batch would take hours on one instance.
2. **HNSW index build time**: On a B_Standard_B1ms, building HNSW on 50k+ 1536-dim vectors takes ~2 minutes. Queries remain fast but index addition is slow during high-write periods.
3. **OpenAI embedding throughput**: The API rate limit on text-embedding-3-small is 1M tokens/minute. At 50k users, a full re-embedding run is ~25M tokens — requires ~25 minutes of streaming requests.

---

## 15. Monitoring and Observability

### 15.1 Application Insights

All backend telemetry flows to Application Insights connected to the Log Analytics workspace. Captured automatically:
- Every HTTP request (method, path, status, duration)
- Every dependency call (PostgreSQL queries, Redis ops, HTTP calls to OpenAI/Replicate/Google)
- Exceptions with full stack traces
- Custom telemetry from `_logger.LogInformation/Warning/Error`

### 15.2 Health Endpoints

```
GET /health/live   — Liveness probe. Returns 200 unconditionally.
                    Used by Container Apps startup and liveness probes.
                    No external dependencies checked.

GET /health/ready  — Readiness probe. Checks PostgreSQL connectivity.
                    Returns 200 if DB responds, 503 if not.
                    Container Apps removes pod from load balancer on 503.

GET /health        — Full health check. Returns status of:
                    PostgreSQL, Redis, Azure Blob Storage.
                    Used for manual operational checks.
```

### 15.3 Structured Logging

All workers log with structured format including worker name, cycle duration, rows affected:
```
[TrustBatchWorker] Processed 4231 users in 00:02:17.341, 89 scores updated
[EmbeddingBatchWorker] Embedded 143 photos, 12 voice notes, 891 tiles in 00:08:22
[BalloonExpiryWorker] Expired 7 matches, notifications sent
```

These appear in Log Analytics and can be queried with KQL.

### 15.4 Analytics Events

50+ event types tracked via `IAnalyticsService`. Key funnel events:
```
UserRegistered → OnboardingStepCompleted (×6) → 
MomentsDeckViewed → MomentResponded → 
MatchCreated → ChatStarted → MessageSent → 
FindLoveUnlocked → DateInterestExpressed → DateInterestMutual
```

Admin dashboard endpoints (`/admin/analytics/*`) expose these as aggregated metrics.

### 15.5 Cost Tracking

`OpenAiCostTracker` tracks daily token usage with per-model cost rates. When the daily budget cap is reached, all new completion requests are rejected until UTC midnight resets the counter.

---

## 16. Known Issues and Technical Debt

These are honest assessments of what needs work. They are not hypothetical — they are real limitations encountered during development.

### 16.1 Known Open Issues

**nginx → backend SSL handshake**: The frontend nginx configuration proxies to the backend's internal FQDN over HTTPS (because Container Apps internal communication is HTTPS). The `proxy_ssl_verify` directive is currently set to `off` to avoid certificate chain validation failures against the Container Apps-managed certificate. This is a security compromise — an ideal configuration would verify the cert. Fix requires importing the Container Apps CA certificate into the nginx container.

**SpeechBrain in production**: The Python subprocess approach works locally. In production, the Python runtime and SpeechBrain model weights (~200MB) must be present in the backend Docker image. This inflates the image size and increases cold start time. The interim production path routes through Azure Speech for transcription (no voice embedding in production yet). Voice scoring contributes 0% to production deck scores currently; its weight is redistributed.

**FFmpeg transcoding**: Tile video processing stubs call `ModerationWorker` for frame extraction, but the actual FFmpeg transcoding is not implemented. Video tiles are accepted but not moderated for content. This is a known gap.

**PillarEmbedding dimension history**: Early users onboarded with 8-dim float embeddings (raw pillar score vectors). Phase 3D upgraded to 1536-dim text embeddings. Old 8-dim vectors are stored in UserVectors v1 rows. A migration script to re-embed all v1 users with the new text-embedding-3-small approach exists but has not been run in production. Affected users are matched using only their CfScore and tag overlap (pillar similarity unavailable).

### 16.2 Technical Debt

**No distributed locking on batch workers**: If the Container App scales to 2+ replicas, `TrustBatchWorker`, `EmbeddingBatchWorker`, and `WeightLearningBatchWorker` would run simultaneously on multiple pods, creating duplicate writes. Current mitigation: Container Apps is configured with `min_replicas = 1` and the batch workers are designed for single-instance operation. Must be addressed before significant horizontal scaling.

**No message queue**: Fire-and-forget tasks (embedding generation after media confirm, moderation after tile upload) are dispatched as async tasks in-process. If the pod restarts during processing, those tasks are silently lost. The user's embedding never gets generated until the next nightly batch. Fix: Azure Service Bus queue with explicit acknowledgment.

**Redis Basic C0 has no persistence**: A Redis restart (e.g., during an Azure maintenance window) clears all rate limit counters, session state, and deck caches. Rate limit counters resetting is acceptable (fail-open). Session state loss causes all analytics sessions to reset (minor). Deck cache clearing causes all users to incur a cold deck build on their next request (expensive but correct). Fix: upgrade to Redis Standard C1 with RDB persistence.

**No automated key rotation**: `KeyRotationWorker` detects when rotation is due and logs an alert. The actual rotation (updating the Container Apps secret + pod restart) is a manual procedure. For a production system handling encrypted PII, this should be automated.

**Collaborative filtering is naive Jaccard similarity**: `CfBatchWorker` computes behavioral overlap between users rather than matrix factorization. At small scale this is fine. At 100k+ users, Jaccard similarity becomes too sparse to be useful. Fix: ALS (Alternating Least Squares) matrix factorization, or use OpenAI embeddings of interaction patterns instead.

**Visual preference cold start**: The `VisualScore` component contributes 0 until a user has made ≥ 10 Magical/Logical decisions (the minimum for a meaningful preference vector). New users see pure pillar-similarity-based matching for their first 10 sessions. This is expected behavior but should be surfaced in the UX.

**Insights use 8-dim clustering**: `InsightService` clusters users using an older 8-dim embedding representation rather than the 1536-dim PillarEmbedding. The cosine similarity threshold (0.80) was calibrated for 8-dim. After the re-embedding migration, the threshold needs recalibration for 1536-dim.
