# Architecture Diagrams

**Last Updated:** 2026-10-07

This document contains ASCII diagrams illustrating Woven's architecture.

---

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          WOVEN ARCHITECTURE                             │
└─────────────────────────────────────────────────────────────────────────┘

┌──────────────┐
│   User       │
│   (Browser)  │
└──────┬───────┘
       │
       │ HTTPS
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│  Azure Front Door (future)                                           │
│  - Global CDN                                                         │
│  - WAF (Web Application Firewall)                                    │
└──────────────────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│  Static Assets (Azure CDN)                                           │
│  - Angular frontend bundle (wooven.me)                               │
│  - Landing page videos                                               │
└──────────────────────────────────────────────────────────────────────┘
       │
       │ API calls
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│  Azure Container Apps (API Pods)                                     │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │ .NET 10 Backend (scale 0-10)                                   │  │
│  │  - Minimal API endpoints                                       │  │
│  │  - SignalR hub (real-time chat)                               │  │
│  │  - Rate limiting (120 req/min per user)                       │  │
│  └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
       │
       ├────────────────────────────────────┬───────────────┐
       │                                    │               │
       ▼                                    ▼               ▼
┌─────────────────┐            ┌─────────────────┐  ┌────────────────┐
│ PostgreSQL 16   │            │ Redis (cache)   │  │ Blob Storage   │
│ (pgvector)      │            │ - User profiles │  │ - Photos       │
│ - User data     │            │ - Daily decks   │  │ - Voice notes  │
│ - Matches       │            │ - Rate limits   │  │ - Tiles        │
│ - Chats         │            └─────────────────┘  └────────────────┘
│ - ECHO signals  │
└─────────────────┘

       │
       │ Batch workers (separate pod, min=max=1)
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│  Background Workers (Container App)                                  │
│  - ConnectionScoreBatchWorker (daily 03:50 UTC)                     │
│  - CfScoreBatchWorker (daily 05:00 UTC)                             │
│  - WeightLearningBatchWorker (weekly Sun 04:00 UTC)                 │
│  - CoachingSummaryWorker (weekly Sun 02:00 UTC)                     │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Data Flow: User Swipe → Match

```
┌─────────────────────────────────────────────────────────────────────────┐
│                  SWIPE → MATCH FLOW                                     │
└─────────────────────────────────────────────────────────────────────────┘

   User A                   Frontend                 Backend               Database
     │                         │                        │                     │
     │  Swipe MAGICAL         │                        │                     │
     │──────────────────────>│                        │                     │
     │                         │  POST /moments/respond│                     │
     │                         │──────────────────────>│                     │
     │                         │                        │  Check existing    │
     │                         │                        │  MomentResponse    │
     │                         │                        │──────────────────>│
     │                         │                        │<──────────────────│
     │                         │                        │  (none found)      │
     │                         │                        │                     │
     │                         │                        │  Create            │
     │                         │                        │  MomentResponse    │
     │                         │                        │──────────────────>│
     │                         │                        │                     │
     │                         │                        │  Check User B's    │
     │                         │                        │  response          │
     │                         │                        │──────────────────>│
     │                         │                        │<──────────────────│
     │                         │                        │  (MAGICAL found)   │
     │                         │                        │                     │
     │                         │                        │  Create Match      │
     │                         │                        │  (PURE, both ◈)    │
     │                         │                        │──────────────────>│
     │                         │                        │                     │
     │                         │                        │  Deduct 1 spark    │
     │                         │                        │  from User A       │
     │                         │                        │──────────────────>│
     │                         │                        │                     │
     │                         │     { matchId: 123 }   │                     │
     │                         │<──────────────────────│                     │
     │   Match notification   │                        │                     │
     │<──────────────────────│                        │                     │
     │                         │                        │                     │
```

---

## ECHO Pipeline

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    ECHO MATCHMAKING PIPELINE                            │
└─────────────────────────────────────────────────────────────────────────┘

Daily Deck Generation (02:00 UTC)
│
├─ 1. CandidatePoolService
│     └─ SQL filtering: gender reciprocity, trust, already-seen
│        → Candidate pool (500-1000 users)
│
├─ 2. MatchScoringService (14 components)
│     ├─ Cosine similarity (text embeddings, 1536-dim)
│     ├─ AI Profile similarity (8-dim)
│     ├─ CF Score (collaborative filtering via CfScores table)
│     ├─ Recency boost (new users)
│     ├─ Mutual friends (FriendBridge)
│     ├─ SharedTileAffinity (Orbit interactions)
│     ├─ ... (8 more components)
│     └─ Weighted sum → final score per candidate
│
├─ 3. DeckSelectionService
│     ├─ AFFINITY bucket: top 20 by score (personalized matches)
│     ├─ SPARK bucket: top 20 by recency (new users)
│     └─ EXPLORER bucket: lowest foundational score in top-20 (diversity)
│        → 60 candidates total (3 buckets × 20)
│
├─ 4. MatchExplanationService (OpenAI)
│     └─ Generate explanations + bridge questions for top matches
│        → MatchExplanation records
│
└─ 5. DailyDeckOrchestrator
      ├─ Dual-write: ItemsJson (legacy) + DailyDeckItems (normalized)
      └─ Cache in Redis (24h TTL)

───────────────────────────────────────────────────────────────────────────

Behavioral Signal Collection (real-time)
│
├─ User swipes, sends message, listens to voice note, etc.
│  └─ IMatchSignalService.RecordAsync(eventType, value)
│     └─ MatchSignalLogs table (raw events)
│
└─ ConnectionScoreBatchWorker (nightly 03:50 UTC)
   └─ Aggregate signals → ConnectionScores table (upsert via raw SQL)
      - TimeToFirstMessageMs: 0.20 weight
      - ChatDepthMessages: 0.15 weight
      - VoiceNoteListenComplete: 0.05 weight
      - MutualVoiceExchange: 0.08 weight
      - ...

───────────────────────────────────────────────────────────────────────────

Weight Learning (weekly Sun 04:00 UTC)
│
└─ WeightLearningBatchWorker
   ├─ Load ConnectionScores + MatchOutcomes (training data)
   ├─ Gradient descent: minimize prediction error
   └─ Update UserMatchingWeights (per-user learned weights)
      → Next deck generation uses learned weights

───────────────────────────────────────────────────────────────────────────

Collaborative Filtering (daily 05:00 UTC)
│
└─ CfScoreBatchWorker
   ├─ Load Orbit + dwell interactions (TileOrbits, TileViews)
   ├─ Jaccard similarity: shared tiles / total tiles
   └─ Upsert CfScores table
      → MatchScoringService reads CfScore component
```

---

## Chat Flow with Trial

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     CHAT FLOW + TRIAL PERIOD                           │
└─────────────────────────────────────────────────────────────────────────┘

User A                   User B                Backend               SignalR Hub
  │                         │                      │                       │
  │  Pop balloon           │                      │                       │
  │──────────────────────────────────────────────>│                       │
  │                         │                      │  Update Match:        │
  │                         │                      │  BalloonState=CLOSED  │
  │                         │                      │  TrialUserAOpenedAt   │
  │                         │                      │  TrialEndsAt=null     │
  │                         │                      │──────────────────────>│
  │                         │                      │                       │
  │                         │                      │  Send notification    │
  │                         │                      │──────────────────────>│
  │                         │<─────────────────────────────────────────────│
  │                         │  "User A popped the balloon!"               │
  │                         │                      │                       │
  │                         │  Open chat          │                       │
  │                         │──────────────────────────────>│               │
  │                         │                      │  Update Match:        │
  │                         │                      │  TrialUserBOpenedAt   │
  │                         │                      │  TrialEndsAt=now+3min │
  │                         │                      │                       │
  │  Send message          │                      │                       │
  │──────────────────────────────────────────────>│                       │
  │                         │                      │  Save to DB           │
  │                         │                      │                       │
  │                         │                      │  Broadcast via SignalR│
  │                         │                      │──────────────────────>│
  │                         │<─────────────────────────────────────────────│
  │                         │  (message received)  │                       │
  │                         │                      │                       │
  │   ... (3 min passes) ... │                      │                       │
  │                         │                      │                       │
  │  Trial decision UI     │                      │                       │
  │  (CONTINUE / END)      │                      │                       │
  │──────────────────────────────────────────────>│                       │
  │                         │                      │  Update Match:        │
  │                         │                      │  - If CONTINUE:       │
  │                         │                      │    TrialEndsAt=null   │
  │                         │                      │  - If END:            │
  │                         │                      │    EndedAt=now        │
  │                         │                      │    EndReason=...      │
  │                         │                      │                       │
  │                         │                      │  Record signal:       │
  │                         │                      │  TrialContinued or    │
  │                         │                      │  TrialEndedNoSpark    │
  │                         │                      │                       │
```

---

## Request Lifecycle

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    HTTP REQUEST LIFECYCLE                               │
└─────────────────────────────────────────────────────────────────────────┘

Client Request
│
├─ 1. HTTP Request
│     Headers: Authorization: Bearer <JWT>
│              X-Correlation-ID: <client-generated or empty>
│
│  ▼
├─ 2. CorrelationIdMiddleware (FIRST middleware)
│     - Read or generate X-Correlation-ID
│     - Push into HttpContext.Items
│     - Push into Serilog LogContext (all logs carry it)
│     - Echo in response header
│
│  ▼
├─ 3. JWT Authentication Middleware
│     - Validate Bearer token
│     - Extract claims (uid, sub, email)
│     - Populate User principal
│
│  ▼
├─ 4. Rate Limiting Middleware
│     - Check user's rate limit (120 req/60s)
│     - If exceeded → 429 response (with X-Correlation-ID + Retry-After)
│     - If OK → continue
│
│  ▼
├─ 5. Route Matching
│     - Match URL to endpoint (POST /moments/respond)
│     - Resolve DI dependencies (services, DbContext, logger)
│
│  ▼
├─ 6. Endpoint Handler
│     - EndpointHelper.GetUserId(http.User) → extract user ID
│     - Delegate to service (e.g., MomentsService.RespondAsync)
│     - Return Results.Ok(response)
│
│  ▼
├─ 7. Exception Handling (if error)
│     - DomainException → 422 (Unprocessable Entity)
│     - UnauthorizedAccessException → 401
│     - All others → 500 (GlobalExceptionHandler)
│     - Response: { error, correlationId, timestamp }
│
│  ▼
└─ 8. Response
      Status: 200 OK
      Headers: X-Correlation-ID: <same as request>
      Body: { deck: [...] }
```

---

## Database Relationships

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    KEY DATABASE RELATIONSHIPS                           │
└─────────────────────────────────────────────────────────────────────────┘

users (1) ──< (many) user_photos
users (1) ── (1) user_profiles
users (1) ── (1) user_preferences
users (1) ── (1) spark_wallets
users (1) ──< (many) user_vectors
users (1) ──< (many) daily_decks

daily_decks (1) ──< (many) daily_deck_items
daily_deck_items (many) ─> (1) users (candidate_id FK)

matches (1) ─> (1) users (user_a_id FK)
matches (1) ─> (1) users (user_b_id FK)
matches (1) ── (1) match_explanations
matches (1) ── (1) chat_threads
matches (1) ──< (many) match_signal_logs
matches (1) ── (1) connection_scores

chat_threads (1) ──< (many) chat_messages
chat_messages (many) ─> (1) users (sender_id FK)

tiles (many) ─> (1) users (author_id FK)
tiles (1) ──< (many) tile_orbits
tiles (1) ──< (many) tile_views

game_sessions (many) ─> (1) matches (match_id FK)
game_sessions (1) ──< (many) game_rounds
game_rounds (1) ──< (many) game_results
game_results (many) ─> (1) users (user_id FK)

coaching_summaries (many) ─> (1) users (user_id FK)
```

---

## Deployment Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      CI/CD DEPLOYMENT FLOW                              │
└─────────────────────────────────────────────────────────────────────────┘

Developer
  │
  │  git push origin master
  ▼
GitHub Repository (gouthamganta/woven)
  │
  │  Trigger: push to master
  ▼
GitHub Actions Workflow
  │
  ├─ 1. Build Docker image (multi-stage)
  │     FROM dotnet/sdk:10.0 (build stage)
  │     FROM dotnet/aspnet:10.0 (runtime stage)
  │     → woven-backend:latest
  │
  ├─ 2. Push to ACR
  │     docker push wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
  │     docker push wovenprodacr.azurecr.io/woven-backend:latest
  │
  ├─ 3. Azure Login (OIDC)
  │     GitHub OIDC → Azure AD → short-lived token
  │
  ├─ 4. Update Container App (API)
  │     az containerapp update \
  │       --name woven-api \
  │       --image wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
  │     → Rolling update (brief downtime)
  │
  ├─ 5. Update Container App (Workers)
  │     az containerapp update \
  │       --name woven-workers \
  │       --image wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
  │
  └─ 6. Smoke test (future)
        curl -f https://wooven.me/health || exit 1

───────────────────────────────────────────────────────────────────────────

Container Apps Environment
  │
  ├─ API Pods (scale 0-10)
  │   └─ EF Core migrations run on startup
  │      → Database schema updated automatically
  │
  └─ Workers Pod (min=max=1)
      └─ Batch workers start immediately
         → ConnectionScore, CfScore, etc.
```

---

## Security Layers

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         SECURITY LAYERS                                 │
└─────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────────┐
│  Layer 1: Network Security                                             │
│  - VNet isolation (Container Apps + PostgreSQL)                        │
│  - Private endpoints (no public DB access)                             │
│  - SSL/TLS enforced (all connections)                                  │
└────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────────────────┐
│  Layer 2: Authentication & Authorization                               │
│  - JWT Bearer tokens (HS256)                                           │
│  - HttpOnly cookies (XSS-resistant)                                    │
│  - Managed Identity (Container Apps → Key Vault, Blob)                │
└────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────────────────┐
│  Layer 3: Application Security                                         │
│  - Rate limiting (120 req/60s per user)                               │
│  - Idempotency keys (prevent duplicate mutations)                     │
│  - Input validation (ASP.NET Core model binding)                      │
│  - Parameterized queries (EF Core, no raw SQL)                        │
└────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────────────────┐
│  Layer 4: Data Security                                                │
│  - Encrypted at rest (Azure-managed keys)                             │
│  - Encrypted in transit (SSL/TLS)                                     │
│  - PII encryption (AES-256-GCM at application level)                  │
│  - Secrets in Key Vault (never in code)                               │
└────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────────────────┐
│  Layer 5: Monitoring & Audit                                           │
│  - Correlation IDs (trace every request)                              │
│  - Security audit logs (SecurityAuditLog table)                       │
│  - Azure Monitor alerts (5xx rate, high latency)                      │
└────────────────────────────────────────────────────────────────────────┘
```

---

## Next Steps

1. **Create interactive diagrams** — diagrams.net (draw.io) exports
2. **Add sequence diagrams** — for complex flows (trial, games, etc.)
3. **Database ER diagram** — visual schema relationships
4. **Infrastructure topology** — Azure resources map

---

**Last Updated:** 2026-10-07  
**Evidence:** ASCII art derived from [backend/](../../backend/), [frontend/](../../frontend/), [CLAUDE.md](../../CLAUDE.md)
