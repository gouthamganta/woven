# Woven Architecture Guide

This document provides a deep dive into Woven's architecture for code reviewers and contributors.

## System Overview

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│                 │     │                 │     │                 │
│  Angular SPA    │────▶│  ASP.NET API    │────▶│  PostgreSQL     │
│  (Frontend)     │     │  (Backend)      │     │  (Database)     │
│                 │     │                 │     │                 │
└─────────────────┘     └────────┬────────┘     └─────────────────┘
                                 │
                                 ▼
                        ┌─────────────────┐
                        │                 │
                        │  OpenAI API     │
                        │  (AI Services)  │
                        │                 │
                        └─────────────────┘
```

## Domain Model

### Core Entities

```
User (1) ─────────── (1) UserProfile
  │                       │
  │                       └── age, gender, city, state, bio
  │
  ├── (1) UserIntent ─── primaryIntent, openness[]
  │
  ├── (1) UserPreference ─── ageMin, ageMax, genders[], distance
  │
  ├── (N) UserPhoto ─── url, sortOrder, caption
  │
  └── (1) AuthIdentity ─── provider (google), providerUserId

Match ─────────────────────────────────────────────────────────
  │
  ├── userAId, userBId (normalized: A < B)
  ├── matchType: PURE | EDGE
  ├── balloonState: ACTIVE | CLOSED
  ├── closedReason: POP | EXPIRE | UNMATCH | BLOCK
  ├── expiresAt (7 days from creation)
  ├── Trial fields: isTrial, trialStartedAt, trialEndsAt (1-minute window)
  ├── Decisions: userADecision, userBDecision (CONTINUE | END)
  ├── edgeOwnerId (EDGE matches only — gets full profile access immediately)
  └── bothMessagedAt, findLoveAt (Find Love unlock tracking)

ChatThread (1:1 with Match) ───── ChatMessage[]
```

### Matchmaking Flow

```
1. UserVector Built
   └── Tags extracted via AI
   └── Embedding generated

2. CandidatePool Filtered
   └── Gender preferences
   └── Age preferences
   └── Location (optional)
   └── Blocked users excluded
   └── Already matched excluded

3. MatchScoring Applied
   └── Tag overlap scoring
   └── Compatibility calculation

4. DeckSelection
   └── Top candidates selected
   └── Diversity ensured

5. DeliveryBoost
   └── New users boosted
   └── Under-exposed boosted

6. MatchExplanation Generated (AI)
   └── Headline
   └── Bullets
   └── Date idea
```

## Backend Architecture

### Layer Structure

```
Endpoints (HTTP layer)
    │
    ▼
Services (Business logic)
    │
    ▼
Data (EF Core + Entities)
    │
    ▼
PostgreSQL
```

### Service Categories

| Category | Services | Purpose |
|----------|----------|---------|
| **Auth** | `JwtTokenService`, `GoogleTokenVerifier` | Authentication |
| **Moments** | `MomentsMatchService`, `InteractionBudgetService` | Match creation |
| **Matchmaking** | `DailyDeckOrchestrator`, `MatchScoringService` | AI-powered matching |
| **Games** | `GameService`, `KnowMeAgent`, `RedGreenFlagAgent` | Interactive games |
| **Background** | `BalloonExpiryWorker` | Scheduled tasks |

### Dependency Injection

```csharp
// Singleton (one instance for app lifetime)
builder.Services.AddSingleton<JwtTokenService>();

// Scoped (one instance per HTTP request)
builder.Services.AddScoped<IMatchScoringService, MatchScoringService>();

// HttpClient (typed clients for external APIs)
builder.Services.AddHttpClient<OpenAiRewriteService>();
```

## Database Design

### Key Constraints

| Table | Constraint | Purpose |
|-------|------------|---------|
| `matches` | `user_a_id < user_b_id` | Normalized pair ordering |
| `matches` | `EDGE requires edge_owner_id` | Edge match ownership |
| `daily_interactions` | `pending_used <= total_used` | Budget invariant |
| `user_ratings` | `rating_value BETWEEN -100 AND 100` | Valid rating range |

### Indexes

```sql
-- Fast lookups for active matches by user
CREATE INDEX ix_matches_user_a_active ON matches(user_a_id) WHERE balloon_state = 'ACTIVE';
CREATE INDEX ix_matches_user_b_active ON matches(user_b_id) WHERE balloon_state = 'ACTIVE';

-- Rating aggregation
CREATE INDEX ix_user_ratings_rated_user ON user_ratings(rated_user_id);
```

## Frontend Architecture

### Module Structure

```
app/
├── core/                  # Singleton services
│   └── auth/
│       └── auth.interceptor.ts
├── services/              # API services
│   ├── chat.service.ts
│   ├── matches.service.ts
│   └── moments.service.ts
├── pages/                 # Route components
│   ├── chats/
│   ├── moments/
│   └── onboarding/
└── components/            # Reusable UI
    ├── game-message-card/
    └── trial-decision/
```

### State Management

Woven uses a simple service-based state approach:
- Services hold data as class properties
- RxJS Observables for async operations
- Component-level state for UI concerns

### HTTP Interceptor Flow

```
Component
    │
    ▼ HttpClient.get()
    │
AuthInterceptor
    │
    ├── Check localStorage for token
    ├── Attach Authorization header
    │
    ▼
API Request
```

## Security Considerations

### Authentication

1. Google OAuth for initial sign-in
2. Backend verifies Google token
3. Backend issues JWT (60 min expiry)
4. Frontend stores JWT in localStorage
5. All API calls include Bearer token

### Authorization

- All endpoints require authentication (`RequireAuthorization()`)
- Match/Chat endpoints verify user is participant
- Dev endpoints only available in Development mode

### Data Protection

- Passwords never stored (OAuth only)
- JWT secret in configuration (not code)
- Database credentials via connection string
- CORS configured per environment

## Performance Considerations

### Database

- Connection pooling via Npgsql
- Async queries throughout
- Appropriate indexes on frequent queries
- `AsNoTracking()` for read-only queries

### API

- Response caching headers for static content
- Gzip compression enabled
- Minimal payload sizes

### Frontend

- Lazy loading for routes
- OnPush change detection where appropriate
- Optimistic UI updates

## Testing Strategy

### Backend

```bash
# Unit tests
dotnet test

# Integration tests (requires test database)
dotnet test --filter Category=Integration
```

### Frontend

```bash
# Unit tests with Vitest
npm test

# E2E tests (if configured)
npm run e2e
```

## Monitoring & Observability

### Health Check

```
GET /health
Response: { "status": "ok", "database": "connected" }
```

### Logging

- Structured logging via `ILogger<T>`
- Log levels: Debug, Information, Warning, Error
- Request/response logging in development

## Common Patterns

### Error Handling (Backend)

```csharp
// Return typed error responses
return Results.BadRequest(new { error = "ERROR_CODE" });
return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
return Results.Forbid(); // 403 for unauthorized access
```

### API Response Pattern

```csharp
// Success with data
return Results.Ok(new { status = "SUCCESS", data = ... });

// Error with code
return Results.BadRequest(new { error = "VALIDATION_ERROR" });
```

### Frontend Service Pattern

```typescript
@Injectable({ providedIn: 'root' })
export class SomeService {
  constructor(private http: HttpClient) {}

  getData(): Observable<DataType> {
    return this.http.get<DataType>(`${environment.apiUrl}/endpoint`);
  }
}
```

## Deployment Checklist

- [ ] Update JWT secret (min 32 chars, random)
- [ ] Configure Google OAuth for production domain
- [ ] Set up PostgreSQL with SSL
- [ ] Configure CORS for production domains
- [ ] Enable HTTPS redirect
- [ ] Set up monitoring/alerting
- [ ] Configure backup strategy
- [ ] Review rate limiting
