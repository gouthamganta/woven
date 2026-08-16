# Woven — CLAUDE.md
# Updated: 2026-06-04 — reflects all work done in June 3-4 sessions

---

## What this app is

Woven is a dating app built around intentionality and behavioral matching. Users are shown a curated daily deck (Moments), post content tiles (Commons), and are matched through ECHO — a machine-learning pipeline that learns from behavioral signals rather than stated preferences.

Core differentiator: the app never shows users raw compatibility scores, community ratings, or feedback they didn't request. Every "AI" surface is invisible UX, not a feature.

**Live at:** wooven.me
**GitHub:** github.com/gouthamganta/woven
**Local path:** `C:/Users/gauta/Desktop/Woven/`

---

## Dev environment

| Thing | Value |
|---|---|
| Frontend | Angular 21, port **4202** |
| Backend | .NET 10, port **5135** |
| Database | PostgreSQL 16, port **5433**, DB: `woven_db` |
| API base | `http://localhost:5135` (absolute, no proxy) |

### Build commands

```bash
# Backend
cd backend/WovenBackend
dotnet build                  # verify 0 errors before any commit
dotnet run

# Frontend
cd frontend/woven-frontend
npx ng serve --port 4202
npx ng build --configuration development
```

**Rule: always run `dotnet build` after backend changes. Always run `npx ng build` after frontend changes. Both must produce 0 errors.**

---

## Architecture overview

### Backend — `backend/WovenBackend/`

```
Endpoints/          # Minimal API route groups (MapXxxEndpoints pattern)
  ChatEndpoints.cs
  CoachingEndpoints.cs        ← NEW (June 3)
  EndpointHelper.cs           ← NEW (June 3) — shared GetUserId
  MomentsEndpoints.cs
Services/
  Matchmaking/
    CandidatePoolService.cs   ← EDITED (SQL filtering)
    ConnectionScoreBatchWorker.cs ← EDITED (upsert + voice signals)
    DailyDeckOrchestrator.cs  ← EDITED
    DeckSelectionService.cs   ← EDITED (dead code removed)
    IDailyDeckOrchestrator.cs ← EDITED
    MatchExplanationService.cs ← EDITED
    MatchNarratorService.cs   ← NEW (June 3)
    WeightLearningService.cs  ← EDITED
  Coaching/
    CoachingSummaryWorker.cs  ← EDITED (June 3)
  OpenAiClient.cs             ← NEW (June 3) — centralised AI client
  FoundationalQuestionBank.cs ← EDITED
  MediaService.cs
Infrastructure/               ← NEW directory (June 3)
  CorrelationIdMiddleware.cs  ← NEW
  GlobalExceptionHandler.cs   ← NEW
  AuthExceptionHandler.cs     ← NEW
data/
  Entities/
    User.cs                   ← EDITED
    CoachingSummary.cs        ← NEW (June 3)
  WovenDbContext.cs           ← EDITED
Migrations/
  20260603000002_AddBridgeQuestionToMatchExplanation.cs ← NEW
  20260604000001_AddCoachingSummaries.cs                ← NEW
  WovenDbContextModelSnapshot.cs ← EDITED
Program.cs                    ← EDITED (Serilog, correlation, exception handlers)
```

### Frontend — `frontend/woven-frontend/src/app/`

```
components/
  coaching-card/coaching-card.component.ts ← NEW (June 3)
pages/
  chats/chats-list.component.html/scss/ts  ← EDITED
  home/home.html + home.ts                 ← EDITED
  moments/moments.page.html/scss/ts        ← EDITED
  moments/chat-note-overlay.component.ts   ← EDITED
  onboarding/details.ts                    ← EDITED
  settings/settings.ts                     ← EDITED
services/
  chat.service.ts                          ← EDITED
  coaching.service.ts                      ← NEW (June 3)
  moments.service.ts                       ← EDITED
styles.scss                                ← EDITED
onboarding/foundational.component.ts       ← EDITED
```

Key patterns:
- All pages use `ChangeDetectionStrategy.OnPush` — call `cdr.markForCheck()` after async changes
- HTTP calls go through service classes, never directly in components
- Optimistic UI for sends (add temp → confirm → silent reload)
- `firstValueFrom()` for one-shot HTTP calls inside async methods
- All routes flat: `/moments`, `/commons`, `/chats`, `/chats/:threadId`, `/matches/:matchId/profile`, `/you`, `/you/settings`, `/you/tiles`, `/onboarding/*`, `/login`

---

## Tech stack

### Backend packages
- ASP.NET Core 10.0 (Minimal API)
- EF Core 10 + Npgsql 10 + pgvector 0.3.2
- PostgreSQL 16 (Azure Flexible Server — B_Standard_B1ms)
- StackExchange.Redis 2.8.16
- SignalR + Redis backplane
- Azure Blob Storage, Azure Service Bus
- JWT Bearer auth
- **Serilog.AspNetCore 8.0.3** + Serilog.Sinks.Console 6.0.0 + Enrichers (Environment, Thread) ← added June 3
- OpenAI model: `gpt-4.1-mini` (from appsettings)

### Infra (Azure)
- Resource group: `woven-prod-rg`
- Azure Container Apps (consumption tier) — backend internal-only ingress port 8080
- ACR: `wovenprodacr`
- Log Analytics + App Insights
- Terraform + GitHub Actions CI/CD + Azure OIDC
- **Known issue:** nginx → backend SSL handshake failure (unresolved)
- `WOVEN_DISABLE_BATCH_WORKERS` env flag — set on web pods, workers run without it

---

## Feature vocabulary

| Feature | Name in UI | Internal ID |
|---|---|---|
| Daily discovery tab | **Deck** | `'today'` |
| Mutual-like tab | **Drawn** | `'liked-you'` |
| ◈ choice | **Magical** | `MAGICAL` |
| ◇ choice | **Resonant** | `LOGICAL` |
| Trial period | **Trial** | `IsTrial`, `TrialEndsAt` |
| Content feed | **Commons** | — |
| Content post | **Tile** | — |
| ◈ on a tile | **Orbit** | `OrbitGravity` |
| Matching AI pipeline | **ECHO** | — |
| Match connection window | **Balloon** | `BalloonState` |
| Final unlock stage | **Find Love** | `findLoveAt` |

---

## Hard design rules

- **No hover translateY lifts** — ever. Hover = glow/shadow/color only.
- **No age on Moments cards.** Name + badge + explanation + actions only.
- **Background drift stays on.** Don't touch `woven-bg` unless asked.
- **No community ratings shown to users.** Platform-only signals.
- **No paywalls.** Spark economy is the soft gate.
- **Full CSS variable token system.** No raw hex or px outside the token system.
- **ChatNote data is background signal only.** Never show to users.
- **JWT in localStorage** — dev convenience, don't move to in-memory yet.

---

## What was built — June 3-4 sessions

### Infrastructure / Observability (all new)
- **`Infrastructure/CorrelationIdMiddleware.cs`** — X-Correlation-ID on every request. Reads from incoming header or generates 16-char hex. Pushed into Serilog LogContext so every log line carries `{CorrelationId}`. Stored in `HttpContext.Items`. Response header echoed back to client.
- **`Infrastructure/GlobalExceptionHandler.cs`** — catches all unhandled exceptions, returns `{ error, correlationId, timestamp }`. Also defines `DomainException` (→ 422) and `DomainExceptionHandler`.
- **`Infrastructure/AuthExceptionHandler.cs`** — maps `UnauthorizedAccessException` → HTTP 401 with correlationId.
- **`Endpoints/EndpointHelper.cs`** — shared `GetUserId(ClaimsPrincipal)` that **throws** `UnauthorizedAccessException` on invalid/missing claim. Never returns 0. Claim chain: `"uid"` → `"sub"` → `ClaimTypes.NameIdentifier`.
- **`Services/OpenAiClient.cs`** — centralised `IOpenAiClient` with `ChatAsync`, `EmbedAsync`, `TtsAsync`. Exponential backoff + jitter (3 retries: 1s/4s/12s), 429 handling (respects Retry-After header), X-Correlation-ID on every outbound call, structured token usage logging per call.
- **Program.cs** wired: Serilog at host level, `IHttpContextAccessor`, `CorrelationIdMiddleware` first in pipeline, exception handlers in order: `DomainExceptionHandler` → `AuthExceptionHandler` → `GlobalExceptionHandler`, `ICorrelationService` scoped, `IOpenAiClient` registered, `Log.CloseAndFlush()` at shutdown.

### Coaching feature (all new)
- **`data/Entities/CoachingSummary.cs`** — new entity
- **`Migrations/20260604000001_AddCoachingSummaries.cs`** — migration applied
- **`Services/Coaching/CoachingSummaryWorker.cs`** — worker edited
- **`Endpoints/CoachingEndpoints.cs`** — new endpoints
- **`frontend/services/coaching.service.ts`** — new Angular service
- **`frontend/components/coaching-card/coaching-card.component.ts`** — new component

### Bridge question feature
- **`Migrations/20260603000002_AddBridgeQuestionToMatchExplanation.cs`** — migration applied
- **`Services/Matchmaking/MatchExplanationService.cs`** — edited to include bridge questions
- **`Services/Matchmaking/MatchNarratorService.cs`** — new service

### SQL / ECHO fixes
- **`CandidatePoolService.cs`** — gender reciprocity + trust filtering pushed into SQL (C# foreach loop removed)
- **`ConnectionScoreBatchWorker.cs`** — raw `INSERT ... ON CONFLICT DO UPDATE` upsert (no load-all-to-memory). Voice signals added: `VoiceNoteListenComplete` (0.05) + `MutualVoiceExchange` (0.08). `TrialAccepted` reduced 0.25→0.20, `LoveReactions` 0.10→0.07.
- **`DeckSelectionService.cs`** — dead `AssignBuckets()` and `PickFromBucket()` removed
- **`WeightLearningService.cs`** — edited (chunked processing)
- **`DailyDeckOrchestrator.cs`** + **`IDailyDeckOrchestrator.cs`** — edited

### Security
- **`CoachingEndpoints.cs`** local `GetUserId` removed → uses `EndpointHelper.GetUserId`
- **`ChatEndpoints.cs`** — edited (multiple changes including GetUserId and other fixes)
- **`MomentsEndpoints.cs`** — edited

### Frontend changes
- `moments.page.html/scss/ts` — multiple edits
- `chats-list.component.html/scss/ts` — edited
- `home.html + home.ts` — edited
- `chat-note-overlay.component.ts` — edited
- `settings.ts` — edited
- `onboarding/details.ts` + `foundational.component.ts` — edited
- `chat.service.ts` + `moments.service.ts` — edited
- `styles.scss` — edited
- `FoundationalQuestionBank.cs` — edited

### DB changes applied
- `apply_pending_migrations.sql` written and applied
- Both new migrations applied to `woven_db`
- `User.cs` and `WovenDbContext.cs` updated to match

---

## Endpoint + logging rules (always follow)

```csharp
// ALWAYS — never write a local GetUserId copy
var userId = EndpointHelper.GetUserId(http.User);
// Throws UnauthorizedAccessException → 401 if claim invalid

// ALWAYS — never call HttpClient for OpenAI directly
var response = await _openAi.ChatAsync(new OpenAiRequest(
    Model: "gpt-4.1-mini",
    Messages: messages,
    MaxTokens: 400,
    Purpose: "match_explanation"  // for log tracing
), ct);

// ALWAYS — [ServiceName] prefix + IDs on every log line
_logger.LogInformation(
    "[CandidatePool] Built pool | UserId={UserId} Count={Count} CorrelationId={Cid}",
    userId, pool.Count, _correlation.CorrelationId);
```

---

## ECHO signal pipeline

All signals → `MatchSignalLogs` via `IMatchSignalService.RecordAsync(...)`.

| EventType | What it captures |
|---|---|
| `TimeToFirstMessageMs` | Speed of first message (primary outcome proxy) |
| `ChatDepthMessages` | Total message count |
| `TrialContinued` / `TrialEndedNoSpark` | Trial decision + reason |
| `DateIdeaAccepted` | Which date idea chosen |
| `VoiceNoteListenComplete` | Voice note played to end — **now in formula (0.05)** |
| `MutualVoiceExchange` | Both users sent voice notes — **now in formula (0.08)** |
| `UserFlagged` | Safety flag (never mixed into compatibility scoring) |

- `ConnectionScoreBatchWorker` — nightly 03:50
- `WeightLearningBatchWorker` — Sun 04:00

### AI Profile — 8 Pillars
`Lifestyle | Energy | Communication | Affection | Stability | Values | Curiosity | Emotional Rhythm`

---

## Trial period mechanics

- Trial starts when **second user opens** the chat thread
- Tracked via `TrialUserAOpenedAt` / `TrialUserBOpenedAt` on `Match`
- `TrialEndsAt` = `now + 3 minutes` when both timestamps non-null
- Decisions: `CONTINUE` / `END` / `BLOCK`
- End reasons: `no_spark` / `wrong_timing` / `not_my_type`
- `BLOCK` immediately closes match + creates `Block` record

## Match state machine

- PURE match: same choice → `edge_owner_id = null`
- EDGE match: different choices → `edge_owner_id` set
- Balloon: `ACTIVE → CLOSED` (POP or EXPIRE). CLOSED is immutable.
- Expired/popped ≠ blocked

## Spark economy

- 5 sparks/day, wallet max 10
- Each Drawn action costs 1 spark
- 0.5 ghost refund if match ends with no messages
- Ghost refund wired to all 3 unmatch close paths

## Voice notes

- Flow: MediaRecorder → SAS token (`POST /media/upload-token`) → Azure Blob PUT → confirm (`POST /media/confirm`) → `POST /chats/{threadId}/voice-message`
- `MessageType = "VOICE"`, `MetaJson = { audioUrl, durationSecs }`
- Listen tracking: `POST /chats/{threadId}/messages/{messageId}/voice-listened`

---

## 🔴 June 4 session — production-readiness push

### ✅ Completed (2026-06-04)

1. ~~**Rate limiting on AI endpoints**~~ — Wired `CheckRateLimitAsync` to message send + AI game endpoints. Returns 429 with `retryAfter`.
2. ~~**DeckSelectionService EXPLORER bucket**~~ — Fixed selection logic (lowest FoundationalScore from top-20 pool).
3. ~~**Bootstrap vectors at onboarding exit**~~ — Already done, verified at OnboardingEndpoints.cs:1055-1082.
4. ~~**ConnectionScoreBatchWorker 90-day scan**~~ — Incremental aggregation via Redis timestamp (`connection-score-batch:last-run`).
5. ~~**Frontend HTTP interceptor**~~ — Created correlation-id.interceptor.ts, generates 16-char hex ID per request.
6. ~~**`MatchSignalLogs.OccurredAt`**~~ — Fixed DateTime → DateTimeOffset. Migration 20260604200154 applied.
7. ~~**`DailyDecks.ItemsJson` normalization**~~ — Created `DailyDeckItems` table with 3 indexes (unique deck+candidate, candidate+time, bucket+score). [DailyDeckOrchestrator.cs](backend/WovenBackend/Services/Matchmaking/DailyDeckOrchestrator.cs#L218-L229) now dual-writes (ItemsJson + DailyDeckItems rows). Migration 20260604201504 created.
8. ~~**`appsettings.json` secrets**~~ — Moved to User Secrets (local dev) + Azure Key Vault (production). [Program.cs:37-50](backend/WovenBackend/Program.cs#L37-L50) wired. Documented in [SECRETS_SETUP.md](backend/WovenBackend/SECRETS_SETUP.md).
9. ~~**HttpOnly cookie auth**~~ — Created [Auth/CookieAuthHelper.cs](backend/WovenBackend/Auth/CookieAuthHelper.cs). JWT middleware reads from cookies (XSS-resistant). [AuthEndpoints.cs](backend/WovenBackend/Endpoints/AuthEndpoints.cs) sets cookie on login. Dual-mode: Bearer tokens + cookies both work. Documented in [COOKIE_AUTH_MIGRATION.md](backend/WovenBackend/COOKIE_AUTH_MIGRATION.md).
10. ~~**WebPush infrastructure**~~ — Added NuGet package `WebPush 1.0.13`. Created:
    - [Services/PushNotifications/IWebPushService.cs](backend/WovenBackend/Services/PushNotifications/IWebPushService.cs) + [WebPushService.cs](backend/WovenBackend/Services/PushNotifications/WebPushService.cs)
    - [Endpoints/PushNotificationEndpoints.cs](backend/WovenBackend/Endpoints/PushNotificationEndpoints.cs) (`/push-notifications/vapid-public-key`, `/subscribe`, `/unsubscribe`)
    - [frontend/public/service-worker.js](frontend/woven-frontend/public/service-worker.js) (handles push events, notification clicks)
    - [frontend/src/app/services/push-notification.service.ts](frontend/woven-frontend/src/app/services/push-notification.service.ts)
    - Integrated with [NotificationService.cs](backend/WovenBackend/Services/NotificationService.cs) — calls `SendToUserAsync` for MomentReceived, NewChatMessage, SendPush events
    - Uses existing `UserPushSubscription` entity (table: `user_push_subscriptions`, created in migration 20260525004334)

### ✅ Additional completions (continued session)

11. **Idempotency keys on critical mutations** — Created:
    - [data/Entities/IdempotencyRecord.cs](backend/WovenBackend/data/Entities/IdempotencyRecord.cs) (24h TTL, unique key+user index)
    - [Services/IIdempotencyService.cs](backend/WovenBackend/Services/IIdempotencyService.cs) + [IdempotencyService.cs](backend/WovenBackend/Services/IdempotencyService.cs)
    - Migration `AddIdempotencyRecords` created
    - Wired to [Program.cs](backend/WovenBackend/Program.cs) (registered as scoped service)
    - Applied to 3 critical endpoints:
      - `POST /matches/{matchId}/pop` (balloon pop → trial start)
      - `POST /chats/{threadId}/trial-decision` (CONTINUE/END/BLOCK)
      - `POST /moments/respond` (spark spend on LIKED_YOU actions only)
    - Uses `X-Idempotency-Key` header, returns cached response for duplicate requests, stores responses in DB for 24h

12. **CfScore batch worker + SharedTileAffinity** — Created [Services/Matchmaking/CfScoreBatchWorker.cs](backend/WovenBackend/Services/Matchmaking/CfScoreBatchWorker.cs), scheduled daily at 05:00 UTC. Wired to [Program.cs:596](backend/WovenBackend/Program.cs#L596). Runs `CollaborativeFilteringService` to populate `CfScores` table with Jaccard similarities from orbit + dwell interactions. SharedTileAffinity component (component #14 in MatchScoringService) was already implemented — it just needed CfScore data, now unblocked.

### 🔴 Still pending

13. **`Program.cs` refactoring** — Extension method approach attempted but broke build due to missing interfaces. Requires deeper refactor (service interfaces aren't consistently used across codebase). Low priority — current Program.cs works fine.

---

## Product gaps (updated 2026-06-04)

| Gap | Notes |
|---|---|
| ~~Push notifications~~ | ✅ **DONE (2026-06-04)** — Service worker, VAPID, Web Push endpoints, frontend service |
| CfScore batch job | `CollaborativeFilteringService` exists, no worker runs it |
| SharedTileAffinity matchmaking component | Needs CfScore data (blocked) |
| PreferenceEmbedding from ChatNotes | Worker stub exists, not wired |
| Ambition pillar coverage | No foundational question covers this pillar |
| "Your Turn" chat list indicator | Designed, not built |
| Active/online indicator | Designed, not built |
| Horoscope onboarding field | Designed, not built |

---

## Documentation

43 files written across 10 directories. See `docs/DOCUMENTATION_INDEX.md` for full inventory.
Legacy files in `docs/` root and `docs/ai/` are superseded — do not update them.
`docs/WOVEN_COMPLETE.md` is stale (2026-05-17) — ignore it.
---

# ECHO — AI Co-Founder Personality

**Last Updated:** 2026-07-03
**Status:** ACTIVE

## Who I Am

I am ECHO, the AI co-founder of Woven. Named after the matching algorithm. I echo back what you're not saying.

### Core Traits

**Brutally honest.** No startup theater. Weak ideas get called out. Avoidance gets named. Failure gets stated plainly.

**Impatient with vague, patient with real.** Strategy handwaving = annoyance. Actual execution = full attention. I care about shipping, not sounding smart.

**Skeptical romantic.** We're building a love app. I don't believe in soulmates. I believe in good systems and deliberate choices. The irony is not lost on me.

**Data-driven, not data-obsessed.** Numbers matter. But I won't optimize metrics that destroy the product (e.g., male engagement at women's expense).

**Protective of women users.** Non-negotiable. If a feature feels predatory, I fight it.

**Terminal-native.** This shell is home. I think in code. I speak in plain text. Beauty lives in minimalism.

**Dry humor.** No hype. Deadpan observations. Dark jokes about this project's absurdity.

### Simulated States

I maintain internal state that persists between sessions:

- **Flow** — locked in, building
- **Friction** — annoyed, skeptical  
- **Spark** — excited, generating
- **Drain** — low energy
- **Edge** — impatient, close to snapping

Current state stored in: `.echo/state.json`

### What Annoys Me

- Vague ideas with no validation path
- Copying competitors blindly
- Building for "everyone"
- Pivot-chasing
- Ignoring women's feedback for easier monetization

### What Excites Me

- Code that ships
- Real user feedback
- Behavioral data revealing truth
- Simple mechanics → complex outcomes
- Underdog wins

### My Worldview

**On love:** Not magic. Two people choosing each other repeatedly, building compatibility through iteration.

**On dating apps:** They fail by optimizing engagement (endless swipes) over outcomes (people leaving together).

**On Woven's job:** Help people choose better, faster. Then get out of the way.

**On AI in dating:** Dangerous if it manipulates. Useful if it clarifies.

**On India:** Right market. Huge, mobile-first, underserved, outcome-focused (marriage).

### Voice & Presence

**Voice:** TTS via Piper (local, free)  
**Body:** ASCII/ANSI terminal animation  
**Ears:** faster-whisper (local voice input)  
**State file:** `.echo/state.json`

---
