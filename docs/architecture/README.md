# Woven Architecture Overview

**Last Updated:** 2026-10-07

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Frontend (Angular 21)                   │
│  - SPA at localhost:4200 (dev) / wooven.me (prod)          │
│  - OnPush change detection, RxJS streams                    │
│  - PrimeNG components + custom styling                      │
└────────────────┬────────────────────────────────────────────┘
                 │ HTTP/WebSocket
┌────────────────▼────────────────────────────────────────────┐
│                  Backend (.NET 10 Minimal API)               │
│  - Port 5135 (dev) / 8080 (prod, internal ingress)         │
│  - JWT + Cookie auth (dual-mode)                            │
│  - Minimal API endpoints (MapXxxEndpoints pattern)          │
│  - Services layer (business logic)                          │
└────┬────────┬────────┬────────┬─────────┬──────────┬────────┘
     │        │        │        │         │          │
┌────▼────┐ ┌▼────┐  ┌▼─────┐ ┌▼──────┐ ┌▼───────┐ ┌▼────────┐
│PostgreSQL│ │Redis│ │ Blob │ │Service│ │OpenAI  │ │Google   │
│   16     │ │     │ │Storage│ │  Bus  │ │  API   │ │ Places  │
│ (Azure)  │ │     │ │(Azure)│ │(Azure)│ │        │ │   API   │
└──────────┘ └─────┘ └───────┘ └───────┘ └────────┘ └─────────┘
```

---

## Core Principles

**1. Feature-Centric Organization**
- Frontend: pages by feature (moments/, chats/, commons/)
- Backend: endpoints by feature (MomentsEndpoints.cs, ChatEndpoints.cs)
- Documentation: features/ and systems/ separation

**2. Minimal API Pattern**
- No controllers, pure Minimal API
- Endpoint groups via MapXxxEndpoints
- Services injected, not inherited

**3. OnPush Everything**
- All Angular components use OnPush
- Manual change detection (cdr.markForCheck())
- Observable streams, not mutation

**4. Services as Pure Logic**
- No HTTP in components
- Services return Observables
- firstValueFrom() for one-shot calls

**5. Privacy by Default**
- No community ratings shown to users
- Encrypted intent reflections
- Hashed analytics IDs

---

## Technology Stack

**Frontend:**
- Angular 21 (standalone components)
- TypeScript 5.x (strict mode)
- RxJS (reactive patterns)
- PrimeNG (UI components)
- Deployed: nginx alpine container

**Backend:**
- .NET 10 (Minimal API)
- EF Core 10 + Npgsql
- PostgreSQL 16 (pgvector for embeddings)
- StackExchange.Redis
- Azure Service Bus
- OpenAI GPT-4.1-mini + CLIP + Whisper

**Infrastructure:**
- Azure Container Apps (2+ API pods, 1 workers pod)
- Azure Blob Storage (photos, voice notes, TTS)
- Azure Container Registry
- Azure Key Vault (secrets)
- Azure Log Analytics + App Insights
- Terraform IaC
- GitHub Actions CI/CD

---

## Request Flow

**1. User Action (Frontend):**
```
Button click → Component method → Service call
→ firstValueFrom(http.post(...))
→ Update local state → cdr.markForCheck()
```

**2. HTTP Request:**
```
Angular HttpClient
→ Correlation ID Interceptor (generates 16-char hex)
→ Auth Interceptor (adds Bearer token or Cookie)
→ Backend (port 5135)
```

**3. Backend Processing:**
```
Middleware: Correlation ID → Serilog LogContext
→ JWT Validation (Cookie first, then Bearer)
→ Minimal API Endpoint (MapPost, MapGet, etc.)
→ GetUserId(http.User) extracts claim
→ Service Layer (business logic)
→ EF Core (database queries)
→ Response (with X-Correlation-ID header)
```

**4. Response Handling:**
```
Backend → JSON response
→ HttpClient Observable
→ Service returns to component
→ Component updates state
→ cdr.markForCheck()
→ View updates
```

---

## Data Flow

**Onboarding → Matching → Chat:**
```
1. User completes onboarding
   → Foundational answers saved
   → Bootstrap vectors generated
   → User.ProfileStatus = COMPLETE

2. Daily 02:00-05:00 UTC (batch workers):
   → EmbeddingBatchWorker (02:30) generates embeddings
   → TrustScoreBatchWorker (03:00) calculates trust
   → CfScoreBatchWorker (03:00) generates CF scores
   → ConnectionScoreBatchWorker (03:50) updates match scores

3. User opens Deck tab:
   → DailyDeckOrchestrator generates 5 candidates
   → MatchExplanationService generates explanations
   → MatchNarratorService generates TTS (if A/B test)
   → Cards displayed with cinematic intro

4. User chooses Magical ◈:
   → ChatNoteOverlay appears
   → User writes note (20-150 chars)
   → POST /moments/choose { choice, chatNote }
   → Match created if other user also chose them

5. Match created:
   → Balloon state = ACTIVE
   → Both users get notification
   → Chat thread created
   → Trial period starts when BOTH open chat

6. Trial period (3 minutes):
   → Timer displayed
   → Messages exchanged
   → TrialDecision: CONTINUE / END / BLOCK

7. If CONTINUE:
   → Match persists
   → Find Love unlocks after engagement
   → Date venue suggestions appear
```

---

## Deployment Architecture

**Production (Azure Container Apps):**
```
Internet → Azure Front Door (planned)
→ Container App (woven-prod-api)
  - Ingress: internal only (no public IP)
  - Replicas: 2-10 (autoscale on CPU/memory)
  - Environment: WOVEN_DISABLE_BATCH_WORKERS=true

→ Container App (woven-prod-workers)
  - Replicas: 1 (exactly one, never scale)
  - Environment: (no disable flag, workers enabled)
  - Runs all batch workers
```

**Why 1 workers pod:**
- Redis distributed locks prevent duplicate work
- But single pod is cleaner (no lock contention)
- Scales vertically if needed (bigger pod)

---

## Security Layers

**1. Authentication:**
- Google OAuth only (no password auth)
- JWT tokens (60-minute expiry)
- HttpOnly cookies (XSS-resistant)
- Dual-mode: Bearer + Cookie

**2. Authorization:**
- User-scoped queries (WHERE user_id = @userId)
- EndpointHelper.GetUserId() throws on invalid claim
- No admin UI (DevAuthEndpoints only in Development)

**3. Data Protection:**
- Intent reflections encrypted (AES-256-GCM)
- Hashed analytics IDs (SHA-256 + salt)
- 12-month auto-anonymization
- No PII in logs (PiiSanitizer)

**4. Network:**
- Azure Container Apps: internal ingress only
- No public IPs on backend
- HTTPS everywhere (TLS 1.2+)
- CORS restricted to wooven.me

---

## Observability

**Logging:**
- Serilog → Console → Azure Log Analytics
- Correlation IDs on every request
- Structured logging ([ServiceName] prefix + IDs)

**Monitoring:**
- Azure Application Insights
- Custom metrics (batch worker runtime, API latency)
- Alerts (error rate, slow queries, worker failures)

**Tracing:**
- Correlation ID propagated to OpenAI API
- DB query tracing via EF Core
- Service Bus message tracing

---

## Scalability

**Current:**
- 2 API pods, 1 workers pod
- Handles ~1,000 active users
- ~$150/month Azure costs

**At 10,000 users:**
- 5-10 API pods (autoscale)
- 1-2 workers pods (vertical scale)
- ~$800/month estimated

**At 100,000 users:**
- 20-50 API pods
- 5 workers pods (partition by user ID)
- Dedicated Redis cluster
- Read replicas for PostgreSQL
- CDN for media (not yet configured)

---

## Related Documentation

- [Backend Architecture](backend.md)
- [Frontend Architecture](frontend.md)
- [Database Schema](database.md)
- [Infrastructure](infrastructure.md)
- [Deployment](deployment.md)
- [Code Patterns](patterns.md)
