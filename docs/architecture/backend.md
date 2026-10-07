# Backend Architecture

**Platform:** .NET 10 / ASP.NET Core 10  
**API Style:** Minimal API  
**Port:** 5135 (development), 8080 (production)  
**Language:** C# 13

---

## Overview

Woven's backend is built on ASP.NET Core 10 using the **Minimal API** pattern — no controllers, no heavyweight MVC stack. Everything is route groups mapped in dedicated endpoint files.

**Design principles:**
- **Minimal API over MVC** — lean, explicit routing
- **Service layer isolation** — business logic never in endpoints
- **Infrastructure as a layer** — cross-cutting concerns (correlation, errors, observability) wired before app logic
- **Background workers** — batch processing split from real-time API pods
- **Evidence-based logging** — structured logs with correlation IDs, no silent failures

---

## Project Structure

```
backend/WovenBackend/
├── Program.cs               ← App bootstrap, DI, middleware pipeline
├── Auth/                    ← JWT, Google OAuth, cookie helpers
├── Endpoints/               ← Minimal API route groups (MapXxxEndpoints pattern)
├── Services/                ← Business logic, divided by domain
│   ├── Matchmaking/         ← ECHO pipeline services
│   ├── Coaching/            ← Weekly coaching summaries
│   ├── Games/               ← KnowMe, RedGreenFlag game logic
│   ├── PushNotifications/   ← Web Push (VAPID)
│   ├── Queue/               ← Background workers (hosted services)
│   ├── Security/            ← Encryption, moderation, trust
│   ├── Embeddings/          ← Multi-modal embedding generation
│   ├── Media/               ← Azure Blob upload/delivery
│   ├── Commons/             ← Tile feed, Orbit interactions
│   └── ...                  ← Other domain services
├── Infrastructure/          ← Cross-cutting concerns (correlation, exceptions)
├── data/                    ← EF Core entities, DbContext, converters
│   ├── Entities/            ← Database models
│   ├── Converters/          ← Encrypted field converters
│   └── WovenDbContext.cs    ← Main DbContext
├── Migrations/              ← EF Core migrations
├── Hubs/                    ← SignalR hubs (real-time chat)
└── scripts/                 ← Utility scripts
```

---

## Layers

### 1. Infrastructure Layer

**Cross-cutting concerns wired FIRST in the pipeline** (before app logic):

| Component | Purpose | File |
|-----------|---------|------|
| **CorrelationIdMiddleware** | X-Correlation-ID on every request/response, injected into Serilog LogContext | [Infrastructure/CorrelationIdMiddleware.cs](../../backend/WovenBackend/Infrastructure/CorrelationIdMiddleware.cs) |
| **GlobalExceptionHandler** | Catches all unhandled exceptions → `{ error, correlationId, timestamp }` | [Infrastructure/GlobalExceptionHandler.cs](../../backend/WovenBackend/Infrastructure/GlobalExceptionHandler.cs) |
| **AuthExceptionHandler** | Maps `UnauthorizedAccessException` → HTTP 401 | [Infrastructure/AuthExceptionHandler.cs](../../backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs) |
| **DomainExceptionHandler** | Maps `DomainException` → HTTP 422 | Defined in GlobalExceptionHandler.cs |

**Order matters.** Correlation middleware runs first, exception handlers registered in order: `DomainExceptionHandler` → `AuthExceptionHandler` → `GlobalExceptionHandler`.

---

### 2. Endpoints Layer

**Pattern:** Each feature gets its own endpoint file. Endpoints are thin — they validate, delegate to services, return responses. No business logic.

**Shared helper:**  
[`EndpointHelper.GetUserId(ClaimsPrincipal)`](../../backend/WovenBackend/Endpoints/EndpointHelper.cs) — extracts user ID from JWT claims, throws `UnauthorizedAccessException` if invalid. Never returns 0.

**Example endpoints:**

| Endpoint File | Routes | Purpose |
|---------------|--------|---------|
| [AuthEndpoints.cs](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs) | `/auth/google`, `/auth/refresh` | Google OAuth, JWT refresh, cookie auth |
| [MomentsEndpoints.cs](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs) | `/moments/deck`, `/moments/respond` | Daily deck retrieval, swipe responses |
| [ChatEndpoints.cs](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs) | `/chats/{threadId}/messages`, `/chats/{threadId}/trial-decision` | Messaging, trial decisions, balloon state |
| [GameEndpoints.cs](../../backend/WovenBackend/Endpoints/GameEndpoints.cs) | `/games/sessions`, `/games/sessions/{sessionId}/submit` | KnowMe, RedGreenFlag game sessions |
| [CoachingEndpoints.cs](../../backend/WovenBackend/Endpoints/CoachingEndpoints.cs) | `/coaching/summary` | Weekly coaching summary retrieval |
| [OnboardingEndpoints.cs](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs) | `/onboarding/complete`, `/onboarding/upload-photos` | Profile setup, photo uploads, vector bootstrap |
| [MatchesEndpoints.cs](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs) | `/matches/{matchId}/pop`, `/matches/{matchId}/profile` | Balloon pop (trial start), match profile retrieval |
| [PushNotificationEndpoints.cs](../../backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs) | `/push-notifications/subscribe`, `/vapid-public-key` | Web Push subscriptions |

**Registration:**  
All endpoint groups registered in [Program.cs](../../backend/WovenBackend/Program.cs) via `.MapXxxEndpoints()` extension methods.

---

### 3. Services Layer

**Business logic lives here.** Services are registered in DI, injected into endpoints and workers.

**Key services:**

| Service | Responsibility | Location |
|---------|----------------|----------|
| **DailyDeckOrchestrator** | Builds daily decks (Deck + Drawn tabs), dual-writes to ItemsJson + DailyDeckItems table | [Services/Matchmaking/DailyDeckOrchestrator.cs](../../backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs) |
| **DeckSelectionService** | Selects 20 candidates per bucket (AFFINITY, SPARK, EXPLORER) from pool | [Services/Matchmaking/DeckSelectionService.cs](../../backend/WovenBackend/Services/Matchmaking/DeckSelectionService.cs) |
| **CandidatePoolService** | Filters candidate pool (gender reciprocity, trust, already-seen) — SQL-optimized | [Services/Matchmaking/CandidatePoolService.cs](../../backend/WovenBackend/Services/Matchmaking/CandidatePoolService.cs) |
| **MatchScoringService** | 14-component match scoring (cosine, cf, recency, mutual friends, ...) | [Services/Matchmaking/MatchScoringService.cs](../../backend/WovenBackend/Services/Matchmaking/MatchScoringService.cs) |
| **MatchExplanationService** | Generates match explanations + bridge questions via OpenAI | [Services/Matchmaking/MatchExplanationService.cs](../../backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs) |
| **WeightLearningService** | Learns component weights from match outcomes (chunked processing) | [Services/Matchmaking/WeightLearningService.cs](../../backend/WovenBackend/Services/Matchmaking/WeightLearningService.cs) |
| **ConnectionScoreBatchWorker** | Nightly upsert of ConnectionScores from MatchSignalLogs (raw SQL, no EF load-all) | [Services/Matchmaking/ConnectionScoreBatchWorker.cs](../../backend/WovenBackend/Services/Matchmaking/ConnectionScoreBatchWorker.cs) |
| **CfScoreBatchWorker** | Daily collaborative filtering from orbit + dwell interactions → CfScores table | [Services/Matchmaking/CfScoreBatchWorker.cs](../../backend/WovenBackend/Services/Matchmaking/CfScoreBatchWorker.cs) |
| **OpenAiClient** | Centralized OpenAI client (exponential backoff, 429 handling, correlation IDs, token logging) | [Services/OpenAiClient.cs](../../backend/WovenBackend/Services/OpenAiClient.cs) |
| **NotificationService** | Sends push notifications (Web Push via VAPID) | [Services/NotificationService.cs](../../backend/WovenBackend/Services/NotificationService.cs) |
| **WebPushService** | Web Push subscription management + VAPID signing | [Services/PushNotifications/WebPushService.cs](../../backend/WovenBackend/Services/PushNotifications/WebPushService.cs) |
| **IdempotencyService** | Prevents duplicate mutations via X-Idempotency-Key header | [Services/IdempotencyService.cs](../../backend/WovenBackend/Services/IdempotencyService.cs) |
| **MediaService** | Azure Blob SAS token generation, upload confirmation | [Services/MediaService.cs](../../backend/WovenBackend/Services/MediaService.cs) |
| **EncryptionService** | AES-256-GCM encryption for PII fields | [Services/Security/EncryptionService.cs](../../backend/WovenBackend/Services/Security/EncryptionService.cs) |
| **ModerationService** | Content moderation queue processing | [Services/Moderation/ModerationService.cs](../../backend/WovenBackend/Services/Moderation/ModerationService.cs) |

**Service registration:**  
All services registered in [Program.cs](../../backend/WovenBackend/Program.cs) with appropriate lifetimes (scoped, singleton, transient).

---

### 4. Data Layer

**EF Core 10 + Npgsql 10 + pgvector 0.3.2**

**DbContext:** [WovenDbContext.cs](../../backend/WovenBackend/data/WovenDbContext.cs)

**Key entities:**

| Entity | Purpose | Table |
|--------|---------|-------|
| [User.cs](../../backend/WovenBackend/data/Entities/User.cs) | User profiles, encrypted PII | `users` |
| [Match.cs](../../backend/WovenBackend/data/Entities/Moments/Match.cs) | Matches (balloon state, trial timestamps) | `matches` |
| [ChatThread.cs](../../backend/WovenBackend/data/Entities/ChatThread.cs) | Chat threads between matched users | `chat_threads` |
| [Message.cs](../../backend/WovenBackend/data/Entities/Message.cs) | Chat messages (text, voice, game invites) | `messages` |
| [DailyDeck.cs](../../backend/WovenBackend/data/Entities/DailyDeck.cs) | Daily deck snapshots (ItemsJson + DailyDeckItems rows) | `daily_decks` |
| [DailyDeckItem.cs](../../backend/WovenBackend/data/Entities/DailyDeckItem.cs) | Normalized deck items (candidate + bucket + score) | `daily_deck_items` |
| [ConnectionScore.cs](../../backend/WovenBackend/data/Entities/ConnectionScore.cs) | Aggregated behavioral signals per match | `connection_scores` |
| [CfScore.cs](../../backend/WovenBackend/data/Entities/CfScore.cs) | Collaborative filtering scores (Jaccard similarity) | `cf_scores` |
| [MatchSignalLog.cs](../../backend/WovenBackend/data/Entities/MatchSignalLog.cs) | Raw behavioral signals (message speed, voice exchanges, trial decisions) | `match_signal_logs` |
| [CoachingSummary.cs](../../backend/WovenBackend/data/Entities/CoachingSummary.cs) | Weekly coaching summaries | `coaching_summaries` |
| [GameSession.cs](../../backend/WovenBackend/data/Entities/Games/GameSession.cs) | Game sessions (KnowMe, RedGreenFlag) | `game_sessions` |
| [IdempotencyRecord.cs](../../backend/WovenBackend/data/Entities/IdempotencyRecord.cs) | Idempotency keys (24h TTL) | `idempotency_records` |

**Encrypted fields:**  
Fields marked with `[EncryptedString]` or `[EncryptedBytes]` custom attributes are encrypted at rest using [EncryptedStringConverter](../../backend/WovenBackend/data/Converters/EncryptedStringConverter.cs) / [EncryptedBytesConverter](../../backend/WovenBackend/data/Converters/EncryptedBytesConverter.cs).

**Connection pooling:**  
- `MaxPoolSize`: 50  
- `MinPoolSize`: 2  
- `ConnectionIdleLifetime`: 300s  

Prevents pool saturation from 14+ background workers + real-time API traffic. PostgreSQL max_connections = 100 (we stay under it).

---

## Authentication & Authorization

### JWT Bearer Tokens

**Flow:**
1. User signs in with Google → backend verifies Google ID token via `GoogleTokenVerifier`
2. Backend generates JWT (HS256) with claims: `uid`, `sub`, `email`
3. JWT returned to client → stored in `localStorage` (dev convenience)
4. All protected endpoints require `Authorization: Bearer <token>` header

**Cookie-based auth (XSS-resistant):**  
As of June 4, 2026, backend supports **dual-mode auth** — Bearer tokens + HttpOnly cookies. JWT middleware reads from cookies if Bearer header absent. See [COOKIE_AUTH_MIGRATION.md](../../backend/WovenBackend/COOKIE_AUTH_MIGRATION.md).

**Helpers:**
- [JwtTokenService.cs](../../backend/WovenBackend/Auth/JwtTokenService.cs) — JWT generation/validation
- [CookieAuthHelper.cs](../../backend/WovenBackend/Auth/CookieAuthHelper.cs) — Sets HttpOnly cookies with JWT
- [GoogleTokenVerifier.cs](../../backend/WovenBackend/Auth/GoogleTokenVerifier.cs) — Verifies Google ID tokens

**User ID extraction:**  
[`EndpointHelper.GetUserId(ClaimsPrincipal)`](../../backend/WovenBackend/Endpoints/EndpointHelper.cs) — throws `UnauthorizedAccessException` if invalid claim. Never returns 0.

---

## Rate Limiting

**Global policies** (configured in [Program.cs](../../backend/WovenBackend/Program.cs)):

| Policy | Limit | Algorithm | Applied to |
|--------|-------|-----------|------------|
| `user` | 120 req/60s per user | Sliding window | All authenticated endpoints |
| `ai-heavy` | 10 req/60s per user | Token bucket | Deck generation, games, match explanations |
| `openai-global` | 5 concurrent calls | Concurrency limiter | OpenAI outbound requests (prevents quota exhaustion) |

**429 response:**
```json
{
  "error": "Rate limit exceeded. Please slow down.",
  "correlationId": "abc123...",
  "retryAfterSeconds": 60
}
```

**Headers:**
- `Retry-After: 60`
- `X-Correlation-ID: <correlation-id>`

---

## Observability

### Structured Logging (Serilog)

**Configuration:** [Program.cs](../../backend/WovenBackend/Program.cs)

**Enrichers:**
- `FromLogContext` — correlation IDs, custom properties
- `WithEnvironmentName` — Production/Development
- `WithThreadId` — async debugging

**Output:**
- **Development:** Human-readable console
- **Production:** Compact JSON (for Azure Log Analytics)

**Log format:**
```csharp
_logger.LogInformation(
    "[ServiceName] Action | UserId={UserId} Count={Count} CorrelationId={Cid}",
    userId, pool.Count, _correlation.CorrelationId);
```

**Correlation IDs:**  
Every request gets a `X-Correlation-ID` header (client-supplied or server-generated 16-char hex). Pushed into Serilog LogContext so every log line carries `{CorrelationId}`. Echoed back in response headers.

### Exception Handling

**3-tier exception pipeline:**
1. **DomainExceptionHandler** — catches `DomainException` → HTTP 422 (Unprocessable Entity)
2. **AuthExceptionHandler** — catches `UnauthorizedAccessException` → HTTP 401
3. **GlobalExceptionHandler** — catches all others → HTTP 500

**Error response format:**
```json
{
  "error": "Detailed error message",
  "correlationId": "abc123...",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

**Caller's flow:**  
Error → matched by most specific handler → generic fallback → structured JSON response + correlation ID.

---

## Background Workers

**Split deployment:**  
- **API pods** (scale 0–N) — `WOVEN_DISABLE_BATCH_WORKERS=true`, lightweight real-time workers only
- **Worker pod** (min=max=1) — `WOVEN_DISABLE_BATCH_WORKERS=false`, runs heavy nightly/weekly batch jobs

**Why:** Prevent resource contention. Batch workers (ECHO learning, scoring aggregation) are CPU/memory-intensive. API pods stay responsive.

### Real-Time Workers (run on all pods)

| Worker | Schedule | What it does |
|--------|----------|--------------|
| **BalloonExpiryWorker** | Every 60s | Expires balloons (closes matches past `BalloonExpiresAt`) |
| **TileExpiryWorker** | Every 60s | Deletes expired Commons tiles |
| **ModerationQueueWorker** | Every 30s | Processes moderation queue |

### Batch Workers (run on worker pod only)

| Worker | Schedule | What it does |
|--------|----------|--------------|
| **ConnectionScoreBatchWorker** | Daily 03:50 UTC | Upserts ConnectionScores from MatchSignalLogs (incremental via Redis timestamp) |
| **CfScoreBatchWorker** | Daily 05:00 UTC | Runs collaborative filtering → CfScores table (Jaccard similarity from orbit/dwell) |
| **WeightLearningBatchWorker** | Weekly Sun 04:00 UTC | Learns ECHO component weights from match outcomes |
| **CoachingSummaryWorker** | Weekly Sun 02:00 UTC | Generates weekly coaching summaries |
| **DeckGenerationWorker** | Daily 02:00 UTC | Pre-generates daily decks for all users |

**Registration:**  
All workers registered in [Program.cs](../../backend/WovenBackend/Program.cs) as `IHostedService` implementations (gated by `WOVEN_DISABLE_BATCH_WORKERS` flag).

---

## External Integrations

### Azure Services

| Service | Purpose | Config |
|---------|---------|--------|
| **Azure Blob Storage** | Photo/voice note storage | `BlobStorage:ConnectionString`, `BlobStorage:ContainerName` |
| **Azure Service Bus** | (Future) Event-driven workflows | Not yet wired |
| **Azure Key Vault** | Secret management (production) | `KeyVault:Name` → reads JWT secret, DB connection string, OpenAI key |

### OpenAI

**Client:** [OpenAiClient.cs](../../backend/WovenBackend/Services/OpenAiClient.cs)

**Features:**
- Exponential backoff + jitter (3 retries: 1s, 4s, 12s)
- 429 handling (respects `Retry-After` header)
- X-Correlation-ID on every outbound call
- Structured token usage logging per call

**Model:** `gpt-4.1-mini` (from appsettings)

**Endpoints using OpenAI:**
- Match explanation generation
- Bridge question generation
- Coaching summary generation
- Dynamic intake rewrites
- Foundational question rewrites

### Redis

**Purpose:** Caching layer + distributed locking

**Client:** StackExchange.Redis 2.8.16

**Cache keys:** Defined in [CacheKeys.cs](../../backend/WovenBackend/Services/CacheKeys.cs)

**Common patterns:**
```csharp
var key = CacheKeys.UserProfile(userId);
var cached = await _cache.GetAsync<UserProfile>(key, ct);
if (cached != null) return cached;

var fresh = await _db.Users.FindAsync(userId);
await _cache.SetAsync(key, fresh, TimeSpan.FromMinutes(15), ct);
return fresh;
```

**Used for:**
- User profile caching (15 min TTL)
- Daily deck caching (1 day TTL)
- Rate limit state
- Distributed locks (batch worker coordination)

---

## Secrets Management

**Development:** User Secrets (`dotnet user-secrets`)  
**Production:** Azure Key Vault

**Setup guide:** [SECRETS_SETUP.md](../../backend/WovenBackend/SECRETS_SETUP.md)

**Secrets:**
- JWT signing key (`Jwt:Secret`)
- PostgreSQL connection string (`ConnectionStrings:DefaultConnection`)
- OpenAI API key (`OpenAi:ApiKey`)
- Azure Blob connection string (`BlobStorage:ConnectionString`)
- Redis connection string (`Redis:ConnectionString`)

**Never commit secrets to Git.** `appsettings.json` contains placeholders only.

---

## Idempotency

**Service:** [IdempotencyService.cs](../../backend/WovenBackend/Services/IdempotencyService.cs)

**How it works:**
1. Client sends `X-Idempotency-Key: <uuid>` header
2. Backend checks `idempotency_records` table (unique index on `key + user_id`)
3. If key exists → return cached response (no re-execution)
4. If new → execute, store response, return fresh result

**TTL:** 24 hours (auto-expires via `created_at` timestamp)

**Applied to:**
- `POST /matches/{matchId}/pop` (balloon pop → trial start)
- `POST /chats/{threadId}/trial-decision` (CONTINUE/END/BLOCK)
- `POST /moments/respond` (spark spend on LIKED_YOU actions only)

**Why:** Prevents double-charges, duplicate trial starts, race conditions on critical mutations.

---

## Deployment Architecture

**See [deployment.md](deployment.md) for full details.**

**Quick summary:**
- Azure Container Apps (consumption tier)
- 2 container groups: **api** (scale 0–N) + **workers** (min=max=1)
- Terraform + GitHub Actions CI/CD
- Azure OIDC (no long-lived credentials)
- ACR: `wovenprodacr`

---

## Code Patterns

### Endpoint pattern

```csharp
public static class MomentsEndpoints
{
    public static void MapMomentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/moments")
            .RequireAuthorization()
            .RequireRateLimiting("user");

        group.MapGet("/deck", GetDeck);
        group.MapPost("/respond", RespondToMoment);
    }

    private static async Task<IResult> GetDeck(
        HttpContext http,
        WovenDbContext db,
        IDailyDeckOrchestrator deckService,
        ILogger<IDailyDeckOrchestrator> logger,
        CancellationToken ct)
    {
        var userId = EndpointHelper.GetUserId(http.User);
        var deck = await deckService.GetOrBuildDeckAsync(userId, ct);
        return Results.Ok(deck);
    }
}
```

**Rules:**
- Thin endpoints — validate, delegate, return
- Use `EndpointHelper.GetUserId()` — never write local copy
- Never call HttpClient for OpenAI — use `IOpenAiClient`
- Log with `[ServiceName]` prefix + correlation ID

### Service pattern

```csharp
public interface IDailyDeckOrchestrator
{
    Task<DailyDeckResponse> GetOrBuildDeckAsync(long userId, CancellationToken ct);
}

public class DailyDeckOrchestrator : IDailyDeckOrchestrator
{
    private readonly WovenDbContext _db;
    private readonly ILogger<DailyDeckOrchestrator> _logger;
    private readonly ICorrelationService _correlation;

    public DailyDeckOrchestrator(
        WovenDbContext db,
        ILogger<DailyDeckOrchestrator> logger,
        ICorrelationService correlation)
    {
        _db = db;
        _logger = logger;
        _correlation = correlation;
    }

    public async Task<DailyDeckResponse> GetOrBuildDeckAsync(long userId, CancellationToken ct)
    {
        _logger.LogInformation(
            "[DailyDeck] Building deck | UserId={UserId} CorrelationId={Cid}",
            userId, _correlation.CorrelationId);

        // Business logic here...
    }
}
```

**Rules:**
- Interface + implementation
- Constructor injection (never `new` for services)
- Structured logging with correlation IDs
- CancellationToken on all async methods

---

## Testing

**Current state:** Minimal test coverage (bootstrap phase).

**Planned:**
- Unit tests for services (xUnit)
- Integration tests for endpoints (WebApplicationFactory)
- E2E tests for critical flows (Playwright + Playwright.NUnit)

**Running tests:**
```bash
cd backend/WovenBackend.Tests
dotnet test
```

---

## Build & Run

### Development

```bash
cd backend/WovenBackend
dotnet build   # Must pass with 0 errors before committing
dotnet run     # Starts on http://localhost:5135
```

**Swagger:** http://localhost:5135/swagger

### Production

```bash
docker build -t woven-backend .
docker run -p 8080:8080 woven-backend
```

**Environment variables:**
- `ASPNETCORE_ENVIRONMENT=Production`
- `WOVEN_DISABLE_BATCH_WORKERS=true` (API pods) / `false` (worker pod)
- All secrets from Azure Key Vault

---

## Known Issues

**Issue:** nginx → backend SSL handshake failure (unresolved)  
**Impact:** External ingress fails in production  
**Workaround:** Use internal-only ingress (Azure Container Apps environment networking)

---

## Next Steps

1. **Increase test coverage** — unit + integration tests for critical flows
2. **OpenTelemetry** — distributed tracing across services
3. **Metrics export** — Prometheus + Grafana dashboards
4. **API versioning** — `/v1/moments`, `/v2/moments` when breaking changes needed
5. **GraphQL layer** — reduce over-fetching on mobile clients

---

**Last Updated:** 2026-10-07  
**Evidence:** [Program.cs](../../backend/WovenBackend/Program.cs), [Services/](../../backend/WovenBackend/Services/), [Endpoints/](../../backend/WovenBackend/Endpoints/)
