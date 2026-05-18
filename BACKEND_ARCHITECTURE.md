# Woven — Backend Architecture Reference
> A complete technical reference for the server, database, endpoints, business logic, and service wiring. Use this when planning new endpoints, schema changes, or feature additions.

---

## Table of Contents
1. [Stack & Technologies](#1-stack--technologies)
2. [Project Structure](#2-project-structure)
3. [Complete API Endpoint Reference](#3-complete-api-endpoint-reference)
4. [Database Schema](#4-database-schema)
5. [Authentication & Authorization](#5-authentication--authorization)
6. [Matchmaking Engine](#6-matchmaking-engine)
7. [Moments (Interaction) System](#7-moments-interaction-system)
8. [Chat & Balloon System](#8-chat--balloon-system)
9. [Games System](#9-games-system)
10. [Onboarding Flow](#10-onboarding-flow)
11. [Background Jobs & Workers](#11-background-jobs--workers)
12. [Service Layer & Dependency Injection](#12-service-layer--dependency-injection)
13. [Configuration & Environment](#13-configuration--environment)
14. [Data Integrity & Constraints](#14-data-integrity--constraints)
15. [Error Handling & Resilience](#15-error-handling--resilience)
16. [End-to-End User Journey](#16-end-to-end-user-journey)

---

## 1. Stack & Technologies

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 10 (Minimal APIs pattern) |
| Database | PostgreSQL 16 |
| ORM | Entity Framework Core 10 (Npgsql provider) |
| Auth | Google OAuth 2.0 + JWT Bearer tokens |
| AI | OpenAI API (gpt-4.1-mini) |
| Containerization | Docker (multi-stage build) |
| Hosting | Azure Container Apps |
| API Docs | Swagger / OpenAPI |

**Architecture style:** Modular service-oriented with dependency injection. All I/O is async/await. No microservices — single deployable binary.

---

## 2. Project Structure

```
/backend/WovenBackend/
├── Program.cs                              # Startup, DI registration, middleware
│
├── Endpoints/                              # HTTP route definitions (Minimal API)
│   ├── AuthEndpoints.cs
│   ├── OnboardingEndpoints.cs
│   ├── MomentsEndpoints.cs
│   ├── ChatEndpoints.cs
│   ├── MatchesEndpoints.cs
│   ├── GameEndpoints.cs
│   ├── DynamicIntakeEndpoints.cs
│   ├── DevAuthEndpoints.cs                # Dev-only
│   ├── DevMatchmakingSmokeEndpoints.cs    # Dev-only
│   └── DevSeedEndpoints.cs               # Dev-only
│
├── Auth/
│   ├── GoogleTokenVerifier.cs             # Validates Google ID tokens
│   └── JwtTokenService.cs                # Creates + validates JWTs
│
├── Services/
│   ├── Matchmaking/
│   │   ├── DailyDeckOrchestrator.cs      # Entry point for deck generation
│   │   ├── CandidatePoolService.cs       # Eligibility filtering
│   │   ├── MatchScoringService.cs        # Vector-based scoring (4 dimensions)
│   │   ├── MatchExplanationService.cs    # OpenAI-generated match reasons
│   │   ├── UserVectorBuilder.cs          # Builds/updates user vectors
│   │   ├── DeliveryBoostService.cs       # Fatigue/diversity adjustments
│   │   ├── DeckSelectionService.cs       # Top 5 selection
│   │   └── OpenAiTaggingService.cs       # AI tag extraction
│   │
│   ├── Moments/
│   │   ├── MomentsMatchService.cs        # Creates Match records
│   │   ├── InteractionBudgetService.cs   # Daily cap enforcement
│   │   ├── BalloonExpiryWorker.cs        # Background expiry job
│   │   └── MomentsRules.cs              # Constants (caps, durations)
│   │
│   ├── Games/
│   │   ├── GameService.cs               # Session lifecycle
│   │   ├── GameAgentFactory.cs          # Creates game AI agents
│   │   ├── KnowMeAgent.cs              # "Know Me" game questions (AI)
│   │   ├── RedGreenFlagAgent.cs        # "Red/Green Flag" scenarios (AI)
│   │   └── GameOutcomeService.cs        # Scoring + analytics
│   │
│   ├── AiProfileService.cs              # Aggregates user data for AI context
│   ├── FoundationalCycleService.cs      # Manages recurring foundational Qs
│   ├── DynamicIntakeCycleService.cs     # Manages weekly pulse cycles
│   ├── FoundationalQuestionBank.cs      # Base question templates
│   ├── DynamicQuestionBank.cs          # Weekly pulse question pool
│   ├── OpenAiRewriteService.cs         # Personalizes foundational Qs
│   ├── OpenAiDynamicIntakeRewriteService.cs
│   ├── OpenAiResilientClient.cs        # OpenAI HTTP wrapper
│   ├── OpenAiCostTracker.cs            # Daily budget tracking
│   └── CircuitBreakerService.cs        # OpenAI resilience
│
├── data/
│   ├── WovenDbContext.cs               # EF Core DbContext
│   ├── WovenDbContextFactory.cs        # Design-time context factory
│   ├── Entities/                       # All database entity classes
│   │   ├── User.cs
│   │   ├── UserProfile.cs
│   │   ├── UserPreference.cs
│   │   ├── UserIntent.cs
│   │   ├── UserPhoto.cs
│   │   ├── UserOptionalField.cs
│   │   ├── UserVector.cs
│   │   ├── UserVectorTag.cs
│   │   ├── UserFoundationalQuestionSet.cs
│   │   ├── UserFoundationalV1.cs
│   │   ├── UserDynamicIntakeSet.cs
│   │   ├── UserWeeklyVibe.cs
│   │   ├── AuthIdentity.cs
│   │   ├── MatchExplanation.cs
│   │   ├── DailyDeck.cs
│   │   ├── CandidateExposure.cs
│   │   ├── CandidateSignal.cs
│   │   ├── MatchOutcome.cs
│   │   ├── Moments/
│   │   │   ├── Match.cs
│   │   │   ├── MomentResponse.cs
│   │   │   ├── PendingMatch.cs
│   │   │   ├── ChatThread.cs
│   │   │   ├── ChatMessage.cs
│   │   │   ├── Block.cs
│   │   │   ├── DailyInteraction.cs
│   │   │   └── UserRating.cs
│   │   └── Games/
│   │       ├── GameSession.cs
│   │       ├── GameRound.cs
│   │       ├── GameResult.cs
│   │       ├── GameAnalytic.cs
│   │       └── GameOutcome.cs
│   └── Enums: ProfileStatus, RelationshipStructure, VisibilityLevel
│
└── Migrations/                         # EF Core migration files
```

---

## 3. Complete API Endpoint Reference

### Auth

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/auth/google` | Public | Google OAuth login / signup |

**POST /auth/google**
```
Input:  { idToken: string }
Output: { accessToken: string, user: { id, email, fullName, profilePhoto } }
Flow:
  1. Verify Google ID token (hits Google tokeninfo endpoint)
  2. Find/create user by Google subject (merged by email to avoid duplicates)
  3. Issue JWT (60 min expiry)
```

---

### Onboarding

All endpoints require JWT. Advance `ProfileStatus` step by step.

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/onboarding/state` | Current onboarding progress + profile status |
| POST | `/onboarding/welcome` | Mark welcome seen → WELCOME_DONE |
| PUT | `/onboarding/basics` | Save age, gender, location, distance, interestedIn, relationshipStructure → BASICS_DONE |
| PUT | `/onboarding/photos` | Upload 3–6 photos with captions → part of flow |
| PUT | `/onboarding/intent` | Save primaryIntent, openness tags, reflectionSentence → INTENT_DONE |
| GET | `/onboarding/foundational/questions` | Load 5 personalized foundational questions |
| PUT | `/onboarding/foundational` | Submit answers to foundational questions → FOUNDATION_DONE |
| PUT | `/onboarding/details` | Save bio, optional fields, weekly vibe → DETAILS_DONE |
| GET | `/onboarding/review` | Full read-only profile preview |
| POST | `/onboarding/complete` | Finalize → COMPLETE, triggers async vector build |
| POST | `/onboarding/foundational/defer` | Postpone recurring foundational questions by 24h |

**Status progression:**
```
INCOMPLETE → WELCOME_DONE → BASICS_DONE → INTENT_DONE
→ FOUNDATION_DONE → DETAILS_DONE → COMPLETE
```

---

### Moments

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/moments` | Get today's daily deck of 5 candidates |
| GET | `/moments/pending` | Get saved-for-later candidates |
| POST | `/moments/respond` | React to a candidate: YES / NO / PENDING |

**POST /moments/respond**
```
Input:  { targetUserId: int, choice: "YES"|"NO"|"PENDING", source?: "PENDING" }
Output: { status, matchId?, edgeOwnerId?, totalUsed, pendingUsed }

Status values:
  BUDGET_EXHAUSTED    — daily cap hit
  PENDING_SAVED       — saved for later (PENDING choice)
  RECORDED_WAITING    — response saved, waiting for counterpart
  OTHER_PENDING       — counterpart hasn't decided yet
  MATCH_CREATED_PURE  — both said YES, balloon created
  MATCH_CREATED_EDGE  — yes/no mismatch, trial balloon created
```

---

### Chats

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/chats` | List active chat threads with match info |
| POST | `/chats/start` | Create thread for a match |
| GET | `/chats/{threadId}` | Load full chat: messages + match state |
| POST | `/chats/{threadId}/messages` | Send a message |
| POST | `/chats/{threadId}/trial-decision` | Make CONTINUE/END decision (EDGE matches) |

**GET /chats/{threadId} — Response shape:**
```json
{
  "meUserId": 42,
  "threadId": "uuid",
  "matchId": "uuid",
  "balloonState": "ACTIVE",
  "expiresAt": "2025-05-17T12:00:00Z",
  "bothMessagedAt": "2025-05-15T14:30:00Z",
  "findLoveAt": "2025-05-15T14:35:00Z",
  "showFindLove": true,
  "showBalloonTimer": false,
  "reflectionSecondsLeft": 120,
  "isTrial": false,
  "trialEndsAt": null,
  "trialSecondsLeft": null,
  "canMakeDecision": false,
  "userADecision": null,
  "userBDecision": null,
  "dateIdea": "Grab coffee at a rooftop spot",
  "messages": [...]
}
```

---

### Matches

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/matches` | List active balloons (all active matches) |
| GET | `/matches/{matchId}/profile-access` | Check access level for this match |
| GET | `/matches/{matchId}/profile` | Load other user's profile (respects access level) |
| POST | `/matches/{matchId}/unmatch` | Unmatch with optional rating |

**Access levels:**
- `FULL` — see everything (photos, bio, details, all answers)
- `LIMITED` — see name, age, photos only (EDGE non-owner before 2-way messaging)

---

### Games

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/games/matches/{matchId}/availability` | Can they play? Games remaining today? |
| POST | `/games/matches/{matchId}/sessions` | Create a game session |
| POST | `/games/sessions/{sessionId}/accept` | Accept game invite |
| POST | `/games/sessions/{sessionId}/reject` | Reject game invite |
| GET | `/games/sessions/{sessionId}/round` | Get current round questions |
| POST | `/games/sessions/{sessionId}/answers` | Submit answers for current round |
| POST | `/games/sessions/{sessionId}/target-answers` | Submit what you think they'll answer |
| GET | `/games/sessions/{sessionId}/result` | Get final scores + AI insight |
| GET | `/games/matches/{matchId}/active` | Check if active game session exists |

---

### Dynamic Intake (Weekly Pulse)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/intake/dynamic/current` | Get current week's pulse questions |
| PUT | `/intake/dynamic` | Submit pulse answers |

---

### Health

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/health/live` | Liveness (no DB, instant) |
| GET | `/health/ready` | Readiness (tests DB connection) |
| GET | `/health` | General health summary |

---

### Debug (Dev-only, disabled in production)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/debug/me/ai-profile` | Get AiProfile object for current user |
| GET | `/debug/me/vector` | Get raw UserVector JSON |
| GET | `/debug/match/{candidateId}/pair-context` | Pair context data |
| GET | `/debug/match/{candidateId}/explanation` | Explanation for a candidate |
| POST | `/debug/test/foundational-rewrite` | Test foundational Q personalization |
| POST | `/debug/test/dynamic-rewrite` | Test intake Q personalization |
| GET | `/debug/me/game-analytics` | Game performance stats |
| GET | `/debug/me/game-outcomes` | Recent game outcomes |

---

## 4. Database Schema

### Core User Tables

**users**
```
id              int          PK, auto-increment
email           string       UNIQUE, REQUIRED
full_name       string?
profile_photo   string?      URL
password_hash   string?      null for Google-only accounts
profile_status  string       enum: INCOMPLETE | WELCOME_DONE | BASICS_DONE
                                   INTENT_DONE | FOUNDATION_DONE | DETAILS_DONE | COMPLETE
created_at      datetime
updated_at      datetime
```

**user_profiles**
```
id              int          PK
user_id         int          FK → users (UNIQUE, CASCADE)
age             int          >= 18
gender          string
city            string
state           string
lat             double?
lng             double?
created_at      datetime
updated_at      datetime
```

**user_preferences**
```
id                      int      PK
user_id                 int      FK → users (UNIQUE, CASCADE)
distance_miles          int      15–100
age_min                 int      >= 18
age_max                 int      <= 99
interested_in_json      string   JSON array: ["men", "women", "non-binary"]
relationship_structure  string   enum: OPEN | PARTNERED | MARRIED
```

**user_intents**
```
id                    int      PK
user_id               int      FK → users (UNIQUE, CASCADE)
primary_intent        string   e.g. "looking for a relationship"
openness_json         string   JSON array of openness tags
reflection_sentence   string   <= 200 chars
```

**user_photos**
```
id          int      PK
user_id     int      FK → users (CASCADE)
url         string   REQUIRED
caption     string?  <= 40 chars
sort_order  int
UNIQUE: (user_id, sort_order)
```

**user_optional_fields**
```
id          int      PK
user_id     int      FK → users (CASCADE)
key         string   e.g. "bio", "job", "education", "pets", "pref_height"
value       string
visibility  string   enum: PUBLIC | MATCHING_ONLY
UNIQUE: (user_id, key)
```

**user_weekly_vibes**
```
id          int       PK
user_id     int       FK → users (UNIQUE, CASCADE)
text        string
expires_at  datetime
```

**auth_identities**
```
id                int      PK
user_id           int      FK → users (CASCADE)
provider          string   e.g. "google"
provider_subject  string   OAuth sub claim
email             string
UNIQUE: (provider, provider_subject)
```

---

### Questionnaire Tables

**user_foundational_question_sets**
```
id              int       PK
user_id         int       FK → users (CASCADE)
version         int       1 = onboarding, 2+ = recurring (every 45 days)
questions_json  string    5 personalized questions
answers_json    string    { questionId: answer } pairs
answered_at     datetime? null = unanswered / active
expires_at      datetime  when renewal is due
deferred_until  datetime? if deferred, don't show until this time
UNIQUE: (user_id, version)
UNIQUE PARTIAL: (user_id) WHERE answered_at IS NULL    ← only 1 active at a time
```

**user_dynamic_intake_sets** (weekly pulse)
```
id                int       PK
user_id           int       FK → users (CASCADE)
cycle_id          string    weekly cycle identifier (e.g. "2025-W20")
cycle_start_utc   datetime
cycle_end_utc     datetime
variant_json      string    3 personalized questions
answers_json      string    { d1_battery, d2_tone, d3_role }
features_json     string    computed pulse features (for vector)
mapping_version   int
answered_at_utc   datetime?
UNIQUE: (user_id, cycle_id)
```

---

### Matchmaking Tables

**user_vectors**
```
id                  int      PK
user_id             int      FK → users (CASCADE)
version             int      increments on each profile update
vector_json         string   JSON:
                               {
                                 "intent": { style, tags },
                                 "foundational": { pillars, tags },
                                 "lifestyle": { kids, smoking, workout, ... },
                                 "pulse": { battery, tone, role, ... }
                               }
pillar_scores_json  string   JSON: 8 pillars scored 0.0–1.0
                               Lifestyle, Energy, Values, Communication,
                               Ambition, Stability, Curiosity, Affection
UNIQUE: (user_id, version)
```

**user_vector_tags**
```
user_id     int      FK → users (CASCADE)
version     int
tag_type    string   TRAIT | HOBBY | LIFESTYLE
tag         string
INDEX: (user_id, version, tag_type)
INDEX: (tag, tag_type)
```

**daily_decks**
```
id              int      PK
user_id         int      FK → users (CASCADE)
date_utc        date
items_json      string   [{candidateId, score, bucket, explanationId}, ...]
generated_at    datetime
UNIQUE: (user_id, date_utc)    ← 1 deck per user per day
```

**match_explanations**
```
id              int      PK
user_id         int      FK → users (CASCADE)
candidate_id    int
date_utc        date
headline        string   "You both want..."
bullets_json    string   2 bullet points
tone            string   playful | calm | serious
date_idea       string?  shown after Find Love unlocks
INDEX: (user_id, candidate_id, date_utc)
```

**candidate_exposures**
```
viewer_user_id   int      FK → users (CASCADE)
shown_user_id    int      FK → users (CASCADE)
surface          string   DECK | PENDING | ...
bucket           string   STRONG | GOOD | OK
score_snapshot   double
date_utc         date
UNIQUE: (viewer_user_id, shown_user_id, date_utc, surface)
```

**candidate_signals**
```
from_user_id   int      FK → users (CASCADE)
to_user_id     int      FK → users (CASCADE)
type           string   YES | NO | PENDING
expires_at     datetime TTL for signal decay
created_at     datetime
INDEX: (from_user_id, to_user_id, type, created_at)
INDEX: (to_user_id, expires_at)
```

**match_outcomes**
```
id                int       PK
match_id          uuid      FK → matches (CASCADE)
user_id           int       FK → users (CASCADE)
candidate_id      int
date_utc          date
chat_started_at   datetime?
match_closed_at   datetime?
result            string    CHAT_STARTED | MATCH_CLOSED | CONTINUED | ...
```

---

### Moments (Match + Chat) Tables

**matches**
```
id                uuid      PK
user_a_id         int       FK → users (RESTRICT)
user_b_id         int       FK → users (RESTRICT)
match_type        string    PURE | EDGE
edge_owner_id     int?      FK → users (RESTRICT)  — NULL if PURE
balloon_state     string    ACTIVE | CLOSED
closed_reason     string?   POP | EXPIRE | UNMATCH | BLOCK  — required if CLOSED
closed_at         datetime? — required if CLOSED
created_at        datetime
expires_at        datetime  PURE=48h, EDGE=7d from creation
both_messaged_at  datetime? set when both send ≥1 message each
find_love_at      datetime? both_messaged_at + 5 min
is_trial          bool      true for EDGE matches
trial_started_at  datetime? set on first message in EDGE
trial_ends_at     datetime? trial_started_at + 48h
user_a_decision   string?   CONTINUE | END
user_b_decision   string?   CONTINUE | END

Check constraints:
  user_a_id <> user_b_id
  ACTIVE → closed_reason IS NULL AND closed_at IS NULL
  CLOSED → closed_reason IS NOT NULL AND closed_at IS NOT NULL
  PURE → edge_owner_id IS NULL
  EDGE → edge_owner_id IS NOT NULL
  expires_at > created_at

UNIQUE PARTIAL: (user_a_id, user_b_id, balloon_state) WHERE balloon_state='ACTIVE'
  ← prevents duplicate active balloons between same pair
INDEX: (user_a_id, balloon_state, expires_at)
INDEX: (user_b_id, balloon_state, expires_at)
INDEX: (balloon_state, expires_at)
```

**moment_responses**
```
id              uuid     PK
date_utc        date     REQUIRED
from_user_id    int      FK → users (CASCADE)
to_user_id      int      FK → users (CASCADE)
choice          string   YES | NO | PENDING
created_at      datetime
UNIQUE: (date_utc, from_user_id, to_user_id)
Check: from_user_id <> to_user_id
```

**pending_matches** (saved-for-later)
```
user_id         int      FK → users (CASCADE)
target_user_id  int      FK → users (CASCADE)
created_at      datetime
UNIQUE: (user_id, target_user_id)
Check: user_id <> target_user_id
INDEX: (user_id, created_at)
```

**daily_interactions** (budget tracking)
```
user_id         int      FK → users (CASCADE)
date_utc        date
total_used      int      0–5 (YES/NO responses)
pending_used    int      0–2 (PENDING choices)
games_initiated int      0–2 (games started today)
PK: (user_id, date_utc)
Check: total_used >= 0 AND <= 5
Check: pending_used >= 0 AND <= 2
Check: pending_used <= total_used
Check: games_initiated >= 0 AND <= 2
```

**blocks**
```
blocker_id   int      FK → users (CASCADE)
blocked_id   int      FK → users (CASCADE)
created_at   datetime
UNIQUE: (blocker_id, blocked_id)
Check: blocker_id <> blocked_id
INDEX: (blocker_id), INDEX: (blocked_id)
```

**chat_threads**
```
id          uuid     PK
match_id    uuid     FK → matches (UNIQUE, CASCADE)  ← 1 thread per match
created_at  datetime
updated_at  datetime
```

**chat_messages**
```
id              uuid     PK
thread_id       uuid     FK → chat_threads (CASCADE)
sender_user_id  int      FK → users (CASCADE)
body            string   1–1000 chars
message_type    string?  "user" | "system"
meta_json       string?  optional metadata
created_at      datetime
Check: length(body) >= 1 AND <= 1000
INDEX: (thread_id)
```

**user_ratings**
```
id              int      PK
rated_user_id   int      FK → users (CASCADE)
rater_user_id   int      FK → users (CASCADE)
match_id        uuid     FK → matches (SET NULL)
rating_value    int      -100 to +100
created_at      datetime
Check: rating_value >= -100 AND <= 100
UNIQUE: (rated_user_id, rater_user_id, match_id)
INDEX: (rated_user_id)
```

---

### Games Tables

**game_sessions**
```
id                  uuid     PK
match_id            uuid     FK → matches (CASCADE)
initiator_user_id   int      FK → users (CASCADE)
game_type           string   KNOW_ME | RED_GREEN_FLAG
status              string   PENDING | ACTIVE | COMPLETED | EXPIRED | REJECTED
expires_at          datetime 48h from creation
created_at          datetime
INDEX: (match_id, status)
INDEX: (expires_at, status)
```

**game_rounds**
```
id                      uuid     PK
session_id              uuid     FK → game_sessions (CASCADE)
round_number            int      1–5
guesser_user_id         int      FK → users (CASCADE)
target_user_id          int      FK → users (CASCADE)
questions_json          string
guesser_answers_json    string?
target_answers_json     string?
guesser_score           int?
target_score            int?
INDEX: (session_id, round_number)
```

**game_results**
```
id               uuid     PK
session_id       uuid     FK → game_sessions (CASCADE)
match_id         uuid     FK → matches (CASCADE)
game_type        string
user_a_id        int      FK → users
user_b_id        int      FK → users
user_a_score     int
user_b_score     int
winner_user_id   int?
ai_insight       string   OpenAI-generated one-liner about the game
INDEX: (match_id, created_at)
```

**game_outcomes**
```
id                  uuid     PK
session_id          uuid     FK → game_sessions (UNIQUE, RESTRICT)
initiator_user_id   int      FK → users (RESTRICT)
partner_user_id     int      FK → users (RESTRICT)
match_id            uuid     FK → matches (RESTRICT)
game_type           string
difficulty          string?
tone                string?
bucket              string?
intent_alignment    string?
initiator_score     int?
partner_score       int?
completion_status   string?
UNIQUE: (session_id)
INDEX: (initiator_user_id, created_at)
INDEX: (partner_user_id, created_at)
INDEX: (match_id)
```

---

## 5. Authentication & Authorization

### Google OAuth Flow

```
1. Frontend receives Google ID token (from Google Sign-In SDK)
2. POST /auth/google { idToken }
3. GoogleTokenVerifier.VerifyAsync(idToken):
   - Calls Google tokeninfo API
   - Validates signature, issuer, audience (ClientId)
   - Returns: { Subject, Email, Name, Picture }
4. Backend checks AuthIdentity(provider="google", subject=Sub):
   - Found: use existing user
   - Not found: create new User + AuthIdentity
     (check by email first to prevent duplicates if user had prior account)
5. JwtTokenService.CreateAccessToken(userId, email):
   - Claims: sub=userId, uid=userId, email
   - Issuer: "WovenBackend"
   - Audience: "WovenFrontend"
   - Expiry: 60 minutes
   - Signed: HMAC-SHA256 with Jwt:Key
6. Return { accessToken, user: { id, email, fullName, profilePhoto } }
```

### JWT Validation (all protected endpoints)

```
ValidateIssuer:           true (must be "WovenBackend")
ValidateAudience:         true (must be "WovenFrontend")
ValidateLifetime:         true
ValidateIssuerSigningKey: true
ClockSkew:                1 minute
```

### User ID Extraction in Endpoints

```csharp
// All endpoints use a helper:
var userId = user.FindFirstValue("uid") ?? user.FindFirstValue("sub");
```

---

## 6. Matchmaking Engine

### Overview: Daily Deck Generation

Called by `GET /moments`. Entry point: `DailyDeckOrchestrator`.

```
GetOrCreateDeckAsync(userId, today):
  1. Check daily_decks for (userId, today) → return cached if found
  2. CandidatePoolService.GetEligibleCandidatesAsync(userId)
  3. MatchScoringService.ScoreCandidatesAsync(userId, candidates)
  4. DeliveryBoostService.GetBoostMapAsync(userId, candidates, date)
  5. DeckSelectionService.SelectTop5(scores, boostMap)
  6. MatchExplanationService.GenerateAndSaveExplanationAsync(per candidate)
  7. Save DailyDeck to DB
  8. Return deck items with full explanation data
```

---

### Step 1: Candidate Pool Eligibility

`CandidatePoolService` filters the full user table to a ~100-candidate pool.

A candidate is **eligible** if ALL of the following are true:

1. `profile_status = COMPLETE`
2. `candidate.id != user.id`
3. Distance: `haversine(user.lat, user.lng, candidate.lat, candidate.lng) <= user.distance_miles`
4. Age: `candidate.age >= user.age_min AND candidate.age <= user.age_max`
5. Gender: `candidate.gender IN user.interested_in_json`
6. No active match: no Match where `balloon_state='ACTIVE'` includes both users
7. No block: no Block in either direction between the two
8. Not responded today: no MomentResponse where `date_utc=today AND from_user_id=userId AND to_user_id=candidate.id`
9. User has a UserVector (v1 built during onboarding)
10. Candidate has a UserVector

---

### Step 2: Scoring Algorithm

`MatchScoringService` computes a 0–100 score across 4 weighted dimensions.

**TotalScore = 0.30×Intent + 0.30×Foundational + 0.20×Lifestyle + 0.20×Pulse**

#### IntentScore (30%)
```
if user.primary_intent == candidate.primary_intent:
  base = 75.0
else:
  base = 50.0

openness_overlap = |user.openness ∩ candidate.openness| / max(|user.openness|, |candidate.openness|)
openness_bonus = openness_overlap * 25

IntentScore = base + openness_bonus
```

#### FoundationalScore (30%)
```
// Pillar similarity: 8 numeric pillars (0.0–1.0 each)
pillar_similarity = avg(1 - |user_pillar[p] - candidate_pillar[p]|) for all 8 pillars
pillar_score = pillar_similarity * 100

// Tag overlap: from foundational question answers
tag_overlap = |user_tags ∩ candidate_tags| / max(|user_tags|, |candidate_tags|)
tag_score = tag_overlap * 100

FoundationalScore = 0.60 * pillar_score + 0.40 * tag_score
```

#### LifestyleScore (20%)
```
// Load optional fields (pref_height, work, drinking, etc.)
// For each preference field:
  Missing data → 50 points (neutral)
  Compatible   → 80 points
  Incompatible → 20 points

LifestyleScore = weighted average of all preference comparisons
```

#### PulseScore (20%)
```
// From UserDynamicIntakeSet.features_json for both users
// Compare d1_battery, d2_tone, d3_role

Perfect match on all 3 → 100
No match on any       → 25
PulseScore = interpolated based on alignment count
```

---

### Step 3: Delivery Boost

`DeliveryBoostService` adjusts raw scores to prevent repetition and add diversity.

- **Fatigue penalty:** Candidate shown in last 7 days → score multiplied by < 1.0
- **Diversity boost:** Candidate not recently shown → small positive multiplier
- **Result:** `adjustedScore = rawScore * boostMultiplier`

---

### Step 4: Top 5 Selection

`DeckSelectionService` applies boost, sorts descending, takes top 5.

**Buckets:**
- Score >= 80 → `"STRONG"`
- Score >= 60 → `"GOOD"`
- Score >= 40 → `"OK"`
- Score < 40  → filtered out (not eligible for deck)

---

### Step 5: Match Explanation Generation

`MatchExplanationService` calls OpenAI (gpt-4.1-mini) per candidate:

```
Input context:
  - user's AiProfile (aggregated from all vectors + answers)
  - candidate's AiProfile
  - match score + bucket

OpenAI generates:
  - headline: string (e.g. "You both want depth over small talk")
  - bullets: [string, string] (2 specific shared qualities)
  - tone: "playful" | "calm" | "serious"
  - dateIdea: string (revealed after Find Love unlocks)

Saved to: match_explanations table
```

---

## 7. Moments (Interaction) System

### Daily Budget

Tracked in `daily_interactions` (PK: user_id, date_utc). Resets at UTC midnight.

| Budget | Cap | Action |
|--------|-----|--------|
| total_used | 5 | YES or NO response |
| pending_used | 2 | PENDING (save for later) choice |
| games_initiated | 2 | Starting a new game |

### Respond to a Candidate

```
POST /moments/respond { targetUserId, choice, source? }

1. InteractionBudgetService.TrySpendAsync(userId, spendType):
   - Get/create DailyInteraction for today
   - Check cap → increment if allowed
   - Return { Allowed, DenyReason }

2. If choice == PENDING:
   - Insert PendingMatch { userId, targetUserId }
   - Return status=PENDING_SAVED

3. If choice == YES or NO:
   - Upsert MomentResponse { date_utc=today, from=userId, to=targetUserId, choice }

4. Look up counterpart response:
   - MomentResponse where from=targetUserId AND to=userId
   - If source=PENDING: any date; else same date_utc

5. If no counterpart: return RECORDED_WAITING

6. If counterpart is PENDING: return OTHER_PENDING

7. Both responded:
   - YES + YES → PURE match
     Match { type=PURE, expiresAt=+48h, IsTrial=false, EdgeOwnerId=null }
   - YES + NO or NO + YES → EDGE match
     Match { type=EDGE, expiresAt=+7d, IsTrial=true, EdgeOwnerId=<one of them> }
   - NO + NO → no match created (neither gets a balloon)

8. If source=PENDING: delete PendingMatch rows both directions

9. Return { status=MATCH_CREATED_PURE|MATCH_CREATED_EDGE, matchId, edgeOwnerId?, ... }
```

---

## 8. Chat & Balloon System

### Match Types

| Type | Created When | Duration | Trial | Edge Owner Access | Other Access |
|------|-------------|----------|-------|-------------------|--------------|
| PURE | Both YES | 48 hours | No | N/A — both FULL | N/A — both FULL |
| EDGE | YES/NO mismatch | 7 days | Yes (48h from first msg) | FULL immediately | LIMITED until 2-way msg |

**PURE Match flow:**
1. Balloon created → both can message immediately
2. First message from each → `both_messaged_at` set → `find_love_at = +5 min`
3. After 5 min: "Find Love" unlocks, date idea shown
4. Balloon expires at 48h or user manually pops it

**EDGE Match flow:**
1. Balloon created; edge owner (who said NO) has FULL access; other has LIMITED
2. First message starts trial: `trial_started_at = now`, `trial_ends_at = +48h`
3. Once both send ≥1 message: non-owner gets FULL access
4. At `trial_ends_at`:
   - Edge owner (User A): rates partner (-100 to +100) + CONTINUE or END
   - Other (User B): CONTINUE or END only
   - Both CONTINUE → `is_trial = false`, `find_love_at = now`
   - Either END → `balloon_state = CLOSED`, `closed_reason = UNMATCH`

### Balloon States

```
ACTIVE:
  closed_reason = NULL, closed_at = NULL

CLOSED:
  closed_reason = POP     (user manually ended)
                | EXPIRE  (balloon expired, set by BalloonExpiryWorker)
                | UNMATCH (user or trial decision)
                | BLOCK   (user blocked the other)
  closed_at = now
```

### Find Love Timer

`find_love_at` = `both_messaged_at` + 5 minutes

The frontend shows a 5-minute countdown. When `find_love_at` passes:
- `showFindLove = true`
- `dateIdea` is revealed from `match_explanations`

---

## 9. Games System

### Game Types

**KNOW_ME**
- 5 rounds, 2 minutes per round
- Round: Target answers "What would I pick?" (A/B/C/D options)
- Guesser tries to pick what target picked
- Scoring: exact match = +20 points

**RED_GREEN_FLAG**
- 5 rounds, 2 minutes per round
- Both see the same scenario
- Each marks: Red Flag (−1), Neutral (0), Green Flag (+1)
- Scoring: both agree (R/R or G/G) = +20, disagree = −10

### Session Lifecycle

```
PENDING  → created, waiting for partner to accept
ACTIVE   → partner accepted, rounds in progress
COMPLETED→ all 5 rounds done, result available
EXPIRED  → partner didn't respond within 48h
REJECTED → partner declined
```

### Game Limits

- 2 games initiated per match per day (tracked in `daily_interactions.games_initiated`)
- Sessions expire in 48h if not accepted

### AI Question Generation

Both game agents call OpenAI with user + partner AiProfile context to generate:
- Personalized, specific questions (not generic)
- Appropriate difficulty based on match context
- Stored in `game_rounds.questions_json`

---

## 10. Onboarding Flow

### State Machine

```
INCOMPLETE
  ↓ POST /onboarding/welcome
WELCOME_DONE
  ↓ PUT /onboarding/basics
BASICS_DONE
  ↓ PUT /onboarding/intent
INTENT_DONE
  ↓ PUT /onboarding/foundational
FOUNDATION_DONE
  ↓ PUT /onboarding/details
DETAILS_DONE
  ↓ POST /onboarding/complete
COMPLETE  →  triggers async UserVectorBuilder.BuildAndSaveV1Async()
```

### Foundational Questions

**Version 1 (onboarding):**
- 5 questions from `FoundationalQuestionBank` (personalized by name/gender/intent)
- Stored in `user_foundational_v1` (permanent, shown in profile review)
- Required, cannot defer

**Version 2+ (recurring every 45 days):**
- 5 personalized questions via `OpenAiRewriteService`
- Can be deferred 24h once per cycle
- Updating answers triggers async vector update

### Weekly Pulse (Dynamic Intake)

3 quick questions every 7 days:
- `d1_battery`: energy level (Low / Medium / High)
- `d2_tone`: conversational tone (Playful / Calm / Serious / Thoughtful)
- `d3_role`: seeking role (Leading / Collaborating / Following / Mix)

Submission triggers async `UserVectorBuilder.UpdatePulseAsync()`.

---

## 11. Background Jobs & Workers

### BalloonExpiryWorker (Hosted Service, always running)

```csharp
// Periodic job (TimedHostedService)
// Query: matches WHERE balloon_state='ACTIVE' AND expires_at <= NOW()
// Action: SET balloon_state='CLOSED', closed_reason='EXPIRE', closed_at=NOW()
```

Runs continuously throughout app lifetime. Handles PURE (48h) and EDGE (7d) expirations.

### Fire-and-Forget Background Tasks

These run on `Task.Run` with a new DI scope (to avoid request scope pollution):

| Trigger | Background Task |
|---------|----------------|
| `POST /onboarding/complete` | `UserVectorBuilder.BuildAndSaveV1Async()` |
| `PUT /intake/dynamic` | `UserVectorBuilder.UpdatePulseAsync()` |
| `PUT /onboarding/foundational` (v2+) | Vector update |
| `POST /chats/{threadId}/messages` (first msg) | `MatchOutcomeService.RecordChatStartedAsync()` |

---

## 12. Service Layer & Dependency Injection

### Lifetimes

| Service | Lifetime | Reason |
|---------|----------|--------|
| `JwtTokenService` | Singleton | Stateless, shared signing key |
| `CircuitBreakerService` | Singleton | Global failure counter |
| `OpenAiCostTracker` | Singleton | Global daily spend counter |
| All business services | Scoped | One per HTTP request |
| `BalloonExpiryWorker` | Hosted Service | App lifetime, runs continuously |

### Service Dependency Map

```
DailyDeckOrchestrator
  ├── CandidatePoolService      → WovenDbContext
  ├── MatchScoringService       → WovenDbContext
  ├── DeliveryBoostService      → WovenDbContext
  ├── DeckSelectionService      (stateless)
  └── MatchExplanationService   → OpenAiResilientClient, WovenDbContext

MomentsMatchService
  ├── InteractionBudgetService  → WovenDbContext
  └── WovenDbContext

GameService
  ├── GameAgentFactory
  │   ├── KnowMeAgent           → OpenAiResilientClient
  │   └── RedGreenFlagAgent     → OpenAiResilientClient
  ├── GameOutcomeService        → WovenDbContext
  └── WovenDbContext

OpenAiResilientClient
  ├── ICircuitBreakerService    (Singleton)
  ├── IOpenAiCostTracker        (Singleton)
  └── HttpClient
```

---

## 13. Configuration & Environment

### appsettings.json Structure

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Port=5433;Database=woven;Username=woven;Password=woven"
  },
  "GoogleAuth": {
    "ClientId": "211033152902-..."
  },
  "Jwt": {
    "Issuer": "WovenBackend",
    "Audience": "WovenFrontend",
    "Key": "<min 32 chars secret>",
    "ExpiryMinutes": 60,
    "ClockSkewMinutes": 1
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:4200", "http://localhost:4000"]
  },
  "OpenAI": {
    "Model": "gpt-4.1-mini",
    "Endpoint": "https://api.openai.com/v1/chat/completions",
    "DailyBudgetUsd": 50.0
  }
}
```

### Docker Compose Services

| Service | Image | External Port | Internal Port |
|---------|-------|--------------|--------------|
| postgres | postgres:16 | 5433 | 5432 |
| backend | WovenBackend (custom) | 5135 | 8080 |
| frontend | nginx (custom) | 80 | 80 |
| pgadmin | pgAdmin (dev only) | 5050 | 80 |

**Backend Dockerfile (multi-stage):**
```
Stage 1 (SDK 10.0): restore + publish -c Release
Stage 2 (ASP.NET 10.0): copy publish output, non-root user (appuser)
Exposed port: 8080
ASPNETCORE_URLS=http://+:8080
ASPNETCORE_ENVIRONMENT=Production
```

### Database Resilience

```csharp
npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: 10s)
npgsqlOptions.CommandTimeout(30)
```

---

## 14. Data Integrity & Constraints

### Critical Check Constraints

```sql
-- No self-matches
CHECK user_a_id <> user_b_id

-- ACTIVE balloons cannot have closed fields
CHECK balloon_state='ACTIVE' => closed_reason IS NULL AND closed_at IS NULL

-- CLOSED balloons must have closed fields
CHECK balloon_state='CLOSED' => closed_reason IS NOT NULL AND closed_at IS NOT NULL

-- PURE means no edge owner
CHECK match_type='PURE' => edge_owner_id IS NULL
CHECK match_type='EDGE' => edge_owner_id IS NOT NULL

-- Budget bounds
CHECK total_used BETWEEN 0 AND 5
CHECK pending_used BETWEEN 0 AND 2
CHECK pending_used <= total_used
CHECK games_initiated BETWEEN 0 AND 2

-- Rating range
CHECK rating_value BETWEEN -100 AND 100

-- Message length
CHECK length(body) BETWEEN 1 AND 1000
```

### Critical Unique Indexes

```sql
-- One profile row per user
UNIQUE user_id ON user_profiles, user_preferences, user_intents, user_weekly_vibes

-- Only one response per user pair per day
UNIQUE (date_utc, from_user_id, to_user_id) ON moment_responses

-- No duplicate active balloon between same pair
UNIQUE (user_a_id, user_b_id, balloon_state) WHERE balloon_state='ACTIVE'

-- One deck per user per day
UNIQUE (user_id, date_utc) ON daily_decks

-- Only one unanswered foundational set active at a time
UNIQUE PARTIAL (user_id) WHERE answered_at IS NULL ON user_foundational_question_sets

-- One thread per match
UNIQUE match_id ON chat_threads
```

---

## 15. Error Handling & Resilience

### Circuit Breaker (OpenAI calls)

`CircuitBreakerService` (Singleton) tracks consecutive failures against OpenAI:
- **Closed:** Normal operation
- **Open:** Too many failures — block requests, return error immediately
- **Half-open:** Allow a test request to check if API recovered

### Cost Tracking

`OpenAiCostTracker` (Singleton) tracks daily spend against `DailyBudgetUsd: 50.0`.
If budget exceeded: OpenAI calls are blocked, returning an error to caller (non-fatal — deck generation falls back to unexaplained cards).

### Database Retry

EF Core configured with `EnableRetryOnFailure(5, 10s)` — handles transient Postgres connection issues automatically.

---

## 16. End-to-End User Journey

```
1. SIGN UP
   POST /auth/google → JWT token issued → profileStatus=INCOMPLETE

2. ONBOARDING (8 steps)
   POST /onboarding/welcome         → WELCOME_DONE
   PUT  /onboarding/basics          → BASICS_DONE
   PUT  /onboarding/intent          → INTENT_DONE
   GET  /onboarding/foundational/questions
   PUT  /onboarding/foundational    → FOUNDATION_DONE
   PUT  /onboarding/details         → DETAILS_DONE
   GET  /onboarding/review
   POST /onboarding/complete        → COMPLETE + async vector build (v1)

3. DAILY DECK
   GET /moments
   → DailyDeckOrchestrator: pool → score → boost → top5 → explanations (OpenAI)
   → Returns 5 candidate cards with headlines, bullets, photos

4. RESPOND TO CANDIDATES
   POST /moments/respond { choice: "YES" }
   → Budget checked (5/day max)
   → MomentResponse saved
   → Check counterpart response
   → Match created if counterpart responded

5a. PURE MATCH (both YES)
   → Both get immediate FULL profile access
   → POST /chats/start → POST /chats/{id}/messages
   → After both message: find_love_at = +5min
   → 5 min later: date idea revealed

5b. EDGE MATCH (YES/NO)
   → Edge owner: FULL access; other: LIMITED
   → First message starts 48h trial
   → After both message: non-owner gets FULL access
   → At trial end: both make CONTINUE/END decision
   → Both CONTINUE: match continues + Find Love unlocks
   → Either END: balloon closed (UNMATCH)

6. GAMES
   GET  /games/matches/{id}/availability
   POST /games/matches/{id}/sessions { gameType: "KNOW_ME" }
   POST /games/sessions/{id}/accept
   GET  /games/sessions/{id}/round       ← 5 rounds
   POST /games/sessions/{id}/answers     ← submit each round
   GET  /games/sessions/{id}/result      ← scores + AI insight

7. WEEKLY PULSE
   GET /intake/dynamic/current
   PUT /intake/dynamic { d1_battery, d2_tone, d3_role }
   → Async vector pulse update

8. RECURRING FOUNDATIONAL (every 45 days)
   GET /onboarding/foundational/questions
   PUT /onboarding/foundational
   → Async vector foundational update
```
