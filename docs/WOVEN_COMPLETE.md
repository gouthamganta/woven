# Woven — Complete Product Reference

> This document is generated from the live codebase. Every endpoint, table, worker, and rule described here reflects what is actually implemented. Last updated: 2026-05-17.

---

## Table of Contents

1. [What Woven Is](#1-what-woven-is)
2. [Core Concepts](#2-core-concepts)
3. [The Matching Engine](#3-the-matching-engine)
4. [User Journey](#4-user-journey)
5. [Every Feature — Complete Reference](#5-every-feature--complete-reference)
6. [Complete API Reference](#6-complete-api-reference)
7. [Database Reference](#7-database-reference)
8. [Background Workers Reference](#8-background-workers-reference)
9. [The AI Layer](#9-the-ai-layer)
10. [Security and Privacy](#10-security-and-privacy)
11. [Infrastructure](#11-infrastructure)
12. [Glossary](#12-glossary)

---

## 1. What Woven Is

Woven is a relationship-intent dating platform designed to create genuine connection between people who are actually compatible — not just nearby and photogenic.

Most dating apps are attention machines. They are built to keep you swiping, not to help you meet someone. Woven is built for the opposite: every design decision is oriented toward getting two people to a real date with someone they will actually like.

The core philosophy:

- **Slow down the surface, accelerate the depth.** You get five moments per day, not five hundred. Scarcity forces you to be intentional.
- **Words precede photos.** The full profile unlocks progressively; you cannot judge someone entirely on looks before the system has established some depth.
- **Compatibility is computed, not declared.** You do not fill out a checklist of what you want. The system learns what you actually respond to through your choices, your conversations, and your answers to weekly questions.
- **The goal is a date.** The app guides every match toward a specific real-world meeting — with AI-generated date ideas, venue suggestions, and availability signaling.

Technically, Woven is a .NET 10 minimal-API backend, an Angular 17 SSR frontend, PostgreSQL with pgvector for vector similarity search, Redis for caching, and Azure Container Apps for deployment.

---

## 2. Core Concepts

### 2.1 Balloons

A **balloon** is a match. The name reflects impermanence: every connection has a seven-day window. If neither person acts within that window, the balloon pops and the match closes.

A balloon has two states: `ACTIVE` and `CLOSED`. When closed, it carries a reason: `POP` (manual), `EXPIRE` (timeout), `UNMATCH` (mutual walk-away or one-sided end), or `BLOCK`.

The seven-day clock creates urgency without creating anxiety. You do not need to message immediately, but you cannot ignore someone indefinitely.

### 2.2 PURE and EDGE Matches

When two people respond to each other in the Moments deck, the match type depends on how their choices aligned:

**PURE match**: Both people chose the same side — both Magical (◈) or both Logical (◇). This is the ideal signal: they think alike. Both users see each other's full profile immediately, and the path to Find Love unlocks without restriction.

**EDGE match**: The choices differed — one chose Magical, the other chose Logical (or one was a Save that the other responded to). One person is designated the "edge owner" at random. The edge owner gets full profile access immediately. The non-owner gets limited access (one photo, no bio) until both people have sent at least one message.

This asymmetry is intentional: it rewards the person who showed interest first while ensuring the other person has enough to go on before committing.

### 2.3 Find Love

**Find Love** is the milestone that unlocks the AI-generated date idea and enables date coordination features. It is not a button — it happens automatically.

When both users in a match have sent at least one message (`BothMessagedAt` is set), a five-minute reflection window starts. After five minutes, `FindLoveAt` is set. The moment Find Love unlocks:

- The match explanation (AI-generated headline, bullets, and date idea) appears in the chat
- The date interest feature becomes available
- Venue suggestions can be requested

The five-minute delay is the "reflection window" — it gives both people a moment before the date-coordination push begins.

### 2.4 Moments

**Moments** is the daily discovery surface. Each day, the system builds a personalized deck of candidate profiles. You have five responses per day (Magical ◈, Logical ◇, or Save ⏳) and two Save picks per day.

The deck is built by `IDailyDeckOrchestrator`, which draws from a scored candidate pool, filters out blocked users and existing matches, and orders candidates by compatibility score. Each card shows the candidate's first name, age, location, one photo, a headline, and a score bucket (STRONG, GOOD, WEAK).

Moments is not a swipe stack. You see one person at a time and respond deliberately.

### 2.5 Save (Hold)

Save is a special response in Moments. When you Save someone (⏳ Hold), you bookmark them to a private list for later. They count against your daily Save budget (cap: 2). Saves are private — the other person does not know.

When you revisit a Save and choose Magical or Logical, the system looks for any Magical (◈) response from that person on any day. If found, it creates a match normally (PURE or EDGE). Saves expire as part of routine cleanup.

### 2.6 Tiles

**Tiles** are micro-content pieces — a photo with an optional caption (max 40 characters). They live in the Commons feed and on your profile. Tiles are independent of your profile photos; they are more like posts.

You can highlight up to one tile per slot. Tiles can be orbited (liked) by anyone in the Commons feed.

### 2.7 Orbit

**Orbit** is the action of appreciating a tile in the Commons feed. When you orbit a tile, that signal is recorded. Orbit rate affects tile ranking in the feed. Rate limit: 50 orbits per user per day.

### 2.8 Commons

**Commons** is the public content feed — a chronological/ranked feed of tiles from all users. It is distinct from Moments. Commons is for discovery through content; Moments is for discovery through profiles.

The feed is paginated with a session ID to maintain consistency across pages. Daily energy budget controls how many tiles you can view (rate limited).

### 2.9 Seasons

**Seasons** are time-bounded content events. Each season runs for a defined period (start and end date stored in the `Season` table) and presents a thematic prompt to all users. You respond with seasonal answers and receive a "season signature" visible on your profile during that season.

Seasons give users something to respond to collectively — shared context that creates conversation starters.

### 2.10 Foundational Questions

**Foundational questions** are a rotating set of five deep questions asked during onboarding and periodically thereafter. They are the backbone of the embedding system — the text you write here is embedded into your 1536-dimensional compatibility vector.

Version 1 questions expire after 15 days. Version 2 expires after 45 days. Later versions expire after 60 days. You can defer a v2+ cycle by 24 hours, but v1 cannot be deferred.

### 2.11 Weekly Pulse

**Weekly Pulse** is the recurring dynamic intake system. After onboarding, the system periodically presents a new set of questions (a "cycle"). These update your compatibility vector over time as you change and grow. Submitting a weekly pulse tracks `WeeklyPulseSubmitted` in analytics.

### 2.12 Games

**Games** are in-chat mini-experiences available after a match is made. Two games exist:

- **KNOW_ME**: A question-and-guess game where each player answers questions and guesses what the other person answered. An AI evaluates the answers and generates a score and insight.
- **RED_GREEN_FLAG**: A compatibility game based on preferences and dealbreakers.

Each match gets a limited number of games per day (tracked via `DailyInteraction.GamesInitiated`, cap: 2). Games have a session lifecycle: PENDING → ACTIVE → COMPLETED.

### 2.13 The Trial Period

When a user chooses to "pop" their own balloon (signal that they are uncertain), a one-minute trial period begins. During the trial:

- The pop initiator (User A) must eventually provide a rating (-100 to 100)
- User B cannot rate
- Both users choose CONTINUE or END
- If both choose CONTINUE: Find Love unlocks immediately (bypassing the normal BothMessagedAt → 5min window)
- If either chooses END: the match closes as UNMATCH

If the trial expires with no messages sent, the system auto-closes the match.

---

## 3. The Matching Engine

The matching engine is the core of Woven. It produces the ranked candidate pool that feeds the daily Moments deck.

### 3.1 Pipeline Overview

```
User's foundational answers + intake + intent + photos + voice + style
    ↓
Pillar embeddings (1536-dim vector per user, stored in UserVector)
    ↓
Candidate pool filtering (distance, age, gender, intent, blocks, existing matches)
    ↓
Multi-component scoring (13+ scoring signals)
    ↓
Dynamic per-user weight adjustment (UserMatchingWeight, updated weekly)
    ↓
Ranked deck with explanations
```

### 3.2 Candidate Pool Filtering

Before scoring, candidates are filtered by hard constraints:

- Geographic distance (user's `DistanceMiles` preference, max 100 miles)
- Age range (user's `AgeMin`/`AgeMax` preferences)
- Gender preference (`InterestedInJson`)
- Not already matched (no active balloon between the pair)
- Not blocked (neither direction in the `Block` table)
- Profile complete (ProfileStatus = COMPLETE)

### 3.3 Scoring Components

The matching score (0–100) is composed of multiple signals:

1. **Pillar cosine similarity** — The primary signal. The 1536-dim embeddings built from foundational answers and intake responses are compared using cosine similarity via pgvector. High similarity → high score.

2. **Intent alignment** — Whether the two users' stated primary intents and openness overlap.

3. **Collaborative filtering (CF) score** — Computed by `CfBatchWorker` nightly. Based on behavioral signals: which candidates similar users engaged positively with. Stored in `CfScore`.

4. **Visual preference compatibility** — Trained from the user's Magical/Logical decisions in Moments. The `UserVisualPreference` table stores aggregated preference vectors. `PhotoEmbedding` vectors are compared.

5. **Voice tone compatibility** — If both users have submitted voice notes, `UserVoicePreference` vectors are compared.

6. **Tag overlap** — `UserVectorTag` stores discrete tags (habits, lifestyle, values) extracted from text. Tag intersection contributes a bonus.

7. **Trust score** — High-trust users are ranked higher. Low-trust users are suppressed. `User.TrustScore` (0.0–1.0, default 0.5) is updated nightly by `TrustBatchWorker`.

8. **Ghost score penalty** — Users with high ghost scores (history of ignoring conversations) are suppressed in other users' decks. `User.GhostScore` is updated by `GhostDetectionWorker`.

9. **Candidate exposure deduplication** — `CandidateExposure` tracks who has already been shown to whom, preventing repeat exposure on the same day.

10. **Outcome signals** — `CandidateSignal` stores recent positive signals (e.g., `MATCH_CREATED`, `CHAT_STARTED`) between users. These decay over time and boost relevance.

11. **Season response overlap** — Thematic alignment from current season responses.

12. **Style embedding similarity** — Extracted from profile text (bio, tile captions).

13. **Humor pattern alignment** — Extracted from chat and tile content.

### 3.4 Score Buckets

The raw score (0–100) is mapped to a display bucket:

- **STRONG** (≥ 80): Shown first, with a "strong match" indicator
- **GOOD** (60–79): Standard display
- **WEAK** (< 60): May be shown at reduced priority
- **NO_MATCH**: Filtered before the deck reaches the user

### 3.5 Match Explanations

For every deck entry, `IMatchExplanationService` generates:

- **Headline**: One sentence about why these two people fit
- **Bullets**: 3–5 specific reasons (from pillar alignment, tag overlap, intent)
- **Tone**: Characterizes the match (encouraging, curious, playful, etc.)
- **Date idea**: A specific, AI-generated first date suggestion (visible after Find Love unlocks)

All four fields are stored in `MatchExplanation` per user+candidate+date.

### 3.6 Dynamic Weight Learning

The weight of each scoring component is not fixed globally — it is personalized. `WeightLearningBatchWorker` runs weekly (Sunday, 04:00 UTC) and processes match outcomes:

- Which candidates did you match with?
- Which matches led to conversations?
- Which conversations lasted?
- Which resulted in dates?

Based on this, per-user weights in `UserMatchingWeight` are adjusted. If visual compatibility is always predictive for you, its weight increases. If CF score has not been predictive, it decreases.

This means the engine gets more accurate for each user over time.

---

## 4. User Journey

### 4.1 First Contact

1. User opens the app and taps "Continue with Google"
2. Frontend sends Google ID token to `POST /auth/google`
3. Backend verifies token with Google, creates `User` and `AuthIdentity` records (or logs in existing user)
4. JWT access token returned
5. `UserRegistered` event tracked (or `AppOpened` if returning user)

### 4.2 Onboarding

The onboarding flow is gated — each step must complete before the next is available. `GET /onboarding/state` returns the current `ProfileStatus` and the `nextRoute` to render.

**Step 1 — Welcome** (`POST /onboarding/welcome`)
User acknowledges the platform. Status advances to `WELCOME_DONE`. Tracks `OnboardingStepCompleted` with `step = "welcome"`.

**Step 2 — Basics** (`PUT /onboarding/basics`)
Collects: full name, age (must be ≥ 18), gender, who they are interested in, distance preference (15–100 miles), age range, location (city, state, lat/lng). Optional: relationship structure (OPEN/CLOSED/HIERARCHICAL/OTHER). Validates coordinates are not 0,0. Creates `UserProfile` and `UserPreference`. Tracks `step = "basics"`.

**Step 3 — Photos** (`PUT /onboarding/photos`)
3–6 photos required. Each has a URL (must be non-empty) and optional caption (max 40 chars). Photos are replaced entirely on each call. Tracks `step = "photos"`.

**Step 4 — Intent** (`PUT /onboarding/intent`)
Collects: primary intent (required), openness array (what they are open to), reflection sentence (max 200 chars). Creates/updates `UserIntent`. Tracks `step = "intent"`.

**Step 5 — Foundational** (`PUT /onboarding/foundational`)
Five questions, each answered with up to 400 characters. Must answer exactly 5 questions. Stores in `UserFoundationalQuestionSet`. Sets expiry (15 days for v1). Tracks `step = "foundational"`.

**Step 6 — Details** (`PUT /onboarding/details`)
Bio (required, max 200 chars), optional fields (job, education, school, pets, habits, hobbies, children, languages, zodiac, diet, hometown — each with Public or MatchingOnly visibility), optional preference fields (ethnicity, religion, height, work style, smoking, drinking, workout), weekly vibe text, display pronouns, accessibility preferences (reduce motion, high contrast). Tracks `step = "details"`.

**Step 7 — Review** (`GET /onboarding/review`)
Read-only review of all collected data, showing both the self view and how their public profile appears.

**Step 8 — Complete** (`POST /onboarding/complete`)
Validates all required fields are present. Sets `ProfileStatus = COMPLETE`. Triggers async vector build (v1 embedding generation). Tracks `step = "complete"`.

### 4.3 Daily Loop

After onboarding, the core daily experience:

1. Open app → LastActiveAt middleware fires, tracks `AppOpened` if new session
2. Navigate to Moments → `GET /moments` returns today's deck (up to 5 cards)
3. Respond to cards (Magical ◈ / Logical ◇ / Save ⏳) via `POST /moments/respond`
4. Check matches in Balloons tab → `GET /matches`
5. Open a chat → `POST /chats/start`, then `GET /chats/{threadId}`
6. Send messages → `POST /chats/{threadId}/messages`
7. Find Love unlocks → date idea appears
8. Express date interest → `POST /chats/{threadId}/date-interest`
9. If mutual: get venue suggestions → `GET /chats/{threadId}/venue-suggestions`

### 4.4 Parallel Journeys

**The Content Creator path**: Post tiles → `POST /tiles`. Tiles appear in Commons. Other users orbit them. High-orbit tiles increase visibility.

**The Season Participant path**: Respond to season prompts → `PUT /seasons/current/responses`. Season signature appears on profile.

**The Games path**: After a match, initiate a game → `POST /games/matches/{matchId}/sessions`. Partner accepts → game begins.

**The Recurring Learner path**: Complete weekly pulse cycles (dynamic intake) to keep the vector fresh → `PUT /dynamic-intake/{cycleId}`.

### 4.5 Exiting

- **Unmatch**: `POST /matches/{matchId}/unmatch` — closes balloon, optional -100 to +100 rating
- **Block**: `POST /matches/{matchId}/block` — closes balloon and prevents future exposure
- **Walk away gracefully**: `POST /chats/{threadId}/close-gracefully` — no ghosting penalty
- **Delete account**: `DELETE /me/account` — hard delete with full anonymization

---

## 5. Every Feature — Complete Reference

### 5.1 Google Authentication

Single sign-on via Google. The backend verifies the Google ID token, extracts the user's email and name, and either creates a new user or links to an existing one. Device fingerprint (optional) and velocity checks run at login to populate trust signals.

**Rate limit**: 20 attempts per IP per day, keyed by SHA-256 hash of IP address.

**New user**: Creates `User`, `AuthIdentity`, `UserProfile`, `UserPreference`. ProfileStatus = INCOMPLETE.

**Returning user**: Updates `LastActiveAt`. Issues new JWT.

### 5.2 JWT Authentication

Every protected endpoint requires a `Bearer` token in the Authorization header. Tokens are signed with a 64-character random key (auto-generated by Terraform, stored as a Container Apps secret). Standard claims: `uid` (user ID), `sub`, `email`. Admin tokens also carry `role = admin` with 1-hour expiry.

Clock skew tolerance: zero (configured). Standard token TTL: defined in `JwtTokenService`.

### 5.3 Onboarding State Machine

`ProfileStatus` is a linear state machine: INCOMPLETE → WELCOME_DONE → BASICS_DONE → INTENT_DONE → FOUNDATION_DONE → DETAILS_DONE → COMPLETE.

`GET /onboarding/state` returns `nextRoute` — a string the frontend uses to direct to the right screen. After COMPLETE, the endpoint also checks if a foundational cycle is due and signals `FOUNDATIONAL_DUE` if so.

### 5.4 Profile Photos

Between 3 and 6 photos per user, stored in Azure Blob Storage and referenced by URL. `PUT /onboarding/photos` replaces the full photo set. Each photo has a sort order and optional caption (max 40 characters).

### 5.5 Optional Profile Fields

Users can disclose optional information with granular visibility:

**Lifestyle fields** (Public or MatchingOnly): `job`, `education`, `school`, `pets`, `habits`, `hobbies`, `children`, `languages`, `zodiac`, `diet`, `hometown`

**Preference fields** (MatchingOnly only): `pref_ethnicity`, `pref_religion`, `pref_height`, `pref_work`, `pref_smoking`, `pref_drinking`, `pref_workout`

MatchingOnly fields are used by the engine but never shown to potential matches. Public fields appear on the profile card.

### 5.6 Weekly Vibe

A short, expiring text that appears on your profile for the current week. Set via `PUT /onboarding/details` (field: `weeklyVibe`). Stored in `UserWeeklyVibe` with an expiry timestamp.

### 5.7 Moments Deck

Built daily by `IDailyDeckOrchestrator`. Each day, the orchestrator:

1. Checks `DailyDeck` for an existing deck for today's UTC date
2. If none: builds from candidate pool + scoring + explanation generation
3. Caches result in `DailyDeck`

Each card in the deck carries: userId, score, bucket, reason (headline + bullets + tone). The deck is filtered at request time to remove candidates already responded to today. Theme is fixed: "Magical (◈) vs Logical (◇)" — heart leads vs head leads.

Budget tracking: `DailyInteraction` records `TotalUsed` (cap 5) and `PendingUsed` (cap 2) per user per UTC date.

### 5.8 Save (Hold)

Choosing Save (⏳) adds the candidate to `PendingMatch`. The daily Save budget (2 per day) decrements. Saves are private. Viewing them via `GET /moments/pending` shows up to 50 saved candidates. When you respond to a saved card, the system checks for a counterpart Magical (◈) response from any day (not just today).

### 5.9 Match Creation

`POST /moments/respond` with choice Magical (◈) or Logical (◇):

1. Validates target is not blocked, not already matched
2. Spends moment budget
3. Creates `MomentResponse` (one per day per pair, enforced by unique index)
4. Looks for counterpart response: if from a Save, checks any date; otherwise today only
5. Same choice on both sides → PURE match. Different choices → EDGE match (edge owner chosen randomly)
6. Creates `Match` with 7-day expiry, creates `ChatThread`
7. Cleans up Save rows if applicable
8. Records visual decision (fire-and-forget)

### 5.10 Match Profile Access

Access level is computed by `GET /matches/{matchId}/profile-access`:

- **PURE match**: Both users get FULL immediately
- **EDGE match, edge owner**: FULL immediately
- **EDGE match, non-owner**: LIMITED until both users have sent a message (`BothMessagedAt` is set)

**FULL access**: All photos, bio, all public optional fields.
**LIMITED access**: One photo only, no bio, no optional fields.

### 5.11 Balloon Expiry

`BalloonExpiryWorker` runs every minute. It finds all ACTIVE matches where `ExpiresAt <= now` and closes them with `ClosedReason = EXPIRE`. Sends a `MomentExpired` push notification to both users.

### 5.12 Chat Threads

A `ChatThread` is created automatically when a match is created. The first user to call `POST /chats/start` for a match gets the existing thread (or triggers creation if somehow missing). The thread tracks `MessageCount` and `AvgResponseTimeMs` (a running average of how long each person takes to reply).

### 5.13 Messaging

`POST /chats/{threadId}/messages` validates:
- Thread exists and belongs to an active match
- Sender is a participant
- Body is 1–1000 characters

After saving the message, the system:
1. Updates `ChatThread.UpdatedAt`, `LastMessageAt`
2. Increments `MessageCount`
3. Updates `AvgResponseTimeMs` (relative to the other user's last message)
4. Checks if `BothMessagedAt` should be set (first time both users have messaged)
5. If BothMessagedAt just set: sets `FindLoveAt = BothMessagedAt + 5 minutes`
6. Tracks `MessageSent` analytics

### 5.14 Find Love Unlock

`FindLoveAt` is computed automatically five minutes after `BothMessagedAt`. It is never set manually. Once set, the chat thread response includes `showFindLove: true` and the date idea from `MatchExplanation` appears.

### 5.15 Trial Period

Initiated by `POST /matches/{matchId}/pop`:
- Sets `IsTrial = true`, `TrialStartedAt = now`, `TrialEndsAt = now + 1 minute`
- Cannot be initiated on a match already in trial or with `FindLoveAt` already set

Resolved by `POST /chats/{threadId}/trial-decision`:
- Request: `{ decision: "CONTINUE"|"END", rating?: int }`
- User A (pop initiator) must provide a rating
- If both CONTINUE → FindLoveAt set to now (no 5-minute wait)
- If any END → BalloonState = CLOSED, ClosedReason = UNMATCH

If trial expires with no messages: `GET /chats/{threadId}` auto-closes the match.

### 5.16 Nudges

`GET /chats/{threadId}/nudge` returns a conversation prompt from `INudgeService`. Nudges are contextual suggestions (e.g., "Ask them about their favorite weekend activity").

Dismissing a nudge (`POST /chats/{threadId}/nudge/dismiss`) caches the dismissal for 48 hours and tracks `NudgeDismissed` analytics.

### 5.17 Date Interest

`POST /chats/{threadId}/date-interest` marks the calling user as interested in the AI-generated date idea. When both users express interest:
- `DateIdeaInterestedAt` is set
- Push notification sent: "You both want to meet up! Check out some nearby spots"
- `DateInterestMutual` tracked for both users

### 5.18 Venue Suggestions

`GET /chats/{threadId}/venue-suggestions` requires mutual date interest. Calls `IVenueService.GetVenueSuggestionsAsync` using both users' locations to return nearby venue recommendations. Tracks `VenueSuggestionsViewed` analytics with `venueCount`.

### 5.19 Availability Signals

`POST /chats/{threadId}/availability` lets you share a short, freeform availability note (max 200 chars). Saves to `ChatAvailabilitySignal` and sends a push notification to the partner: "{firstName} is free: {text}".

### 5.20 Unmatch

`POST /matches/{matchId}/unmatch`:
- Accepts optional rating (-100 to 100)
- Saves rating to `UserRating` if provided
- Sets `BalloonState = CLOSED`, `ClosedReason = UNMATCH`
- Fires outcome tracking async

### 5.21 Block

`POST /matches/{matchId}/block`:
- Idempotent (safe to call multiple times)
- Creates/updates `Block` record (blocker → blocked)
- Closes match
- Records outcome async
- Blocked users are permanently excluded from each other's decks and pending lists

### 5.22 Date Feedback

`POST /matches/{matchId}/feedback` collects post-date reflection:
- `metInPerson`: boolean (required)
- `stars`: 1–5 rating (optional)
- `feltRightText`: what felt right (optional, max 300 chars)
- `feltOffText`: what felt off (optional, max 300 chars)
- `meetAgain`: "yes", "no", or "maybe" (optional)

Feedback is stored in `DateFeedback` and feeds the weight learning system.

`FeedbackTriggerWorker` runs daily at 08:00 UTC to send prompts to users who had recent dates. The pending prompt is surfaced via `GET /me/feedback-prompt`.

### 5.23 Tiles

Tiles are media posts. `POST /tiles` creates a tile via `ITileService`. `GET /tiles/mine` lists the calling user's tiles. Tiles can be highlighted in numbered slots (`POST /tiles/{tileId}/highlight` with `slotNumber`). Only one highlight per slot.

Deleting a tile (`DELETE /tiles/{tileId}`) removes it from the Commons feed and any highlights.

### 5.24 Commons Feed

`GET /commons?page=1&sessionId={uuid}` returns a paginated tile feed. Session ID maintains feed consistency across page loads. Energy budget controls daily view limit (returns 429 `ENERGY_DEPLETED` when exhausted).

`POST /commons/{tileId}/view` records a view event with optional `durationMs`. `POST /commons/refresh` clears and rebuilds the feed cache.

### 5.25 Orbit

`POST /orbit/{tileId}` via `IOrbitService`. Orbit is the equivalent of a like on a tile — it signals appreciation without creating a match. Rate limit: 50 per user per day. Orbit count influences tile ranking in the Commons feed.

### 5.26 Media Upload

Media upload is a two-step process:

**Step 1**: `POST /media/upload-token` — Returns a SAS URL for direct upload to Azure Blob Storage. Rate limit: 20 tokens per user per day. Containers: `profile-photo`, `tile-media`, `voice-note`.

**Step 2**: `POST /media/confirm` — Confirms the blob was actually uploaded. For `tile-media` and `voice-note`, this enqueues async processing (moderation, embedding generation).

`DELETE /media/{container}/{blobPath}` deletes a blob with ownership validation.

### 5.27 Selfie Verification

`POST /verification/selfie` submits a photo for identity verification. The system compares the selfie against the profile photo using `ReferencePhotoEmbedding`. Rate limit: 5 attempts per user per day.

`GET /verification/status` returns current verification status and most recent attempt.

Verified users display a verified badge (`User.IsVerified = true`, `User.VerifiedAt` set). Verification is surfaced on match cards (`isVerified` field).

### 5.28 Games

Available after a match is created. Each match can initiate up to 2 games per day (`DailyInteraction.GamesInitiated`).

**Check availability**: `GET /games/matches/{matchId}/availability` — Returns available game types with name, description, estimated duration, and icon.

**Create session**: `POST /games/matches/{matchId}/sessions` with `{ gameType: string }` — Creates a `GameSession` in PENDING state.

**Accept/Reject**: Other user calls `POST /games/sessions/{sessionId}/accept` (→ ACTIVE) or `POST /games/sessions/{sessionId}/reject`.

**Gameplay**: `GET /games/sessions/{sessionId}/round` → `POST /games/sessions/{sessionId}/answers` → `POST /games/sessions/{sessionId}/target-answers`

**Results**: `GET /games/sessions/{sessionId}/result` returns scores, winner, and AI-generated insight about the pair.

**KNOW_ME**: Each player answers questions and guesses what the other answered. Score based on accuracy. AI evaluates answer quality and generates a compatibility insight.

**RED_GREEN_FLAG**: Preference and dealbreaker alignment game.

### 5.29 Seasons

`GET /seasons/current` returns the active season with its prompts, the user's response count, and their signature prompt.

`PUT /seasons/current/responses` accepts an array of `{ pillarId, questionId, response }` objects.

Season responses contribute to the pillar embedding system.

### 5.30 Insights

The insights system generates personalized observations about a user's patterns on the platform.

`GET /me/insights` returns:
- `insights`: array of insight strings
- `shouldAskOpinion`: whether to show the feedback form
- `opinionTrigger`: which trigger is active ("no_dates_yet", "pattern_shift", "high_rejection", "low_depth")
- `opinionPrompt`: the specific question to show

`InsightBatchWorker` runs nightly (04:30 UTC) to generate new insights. Insights are stored as a JSON array in `UserInsight`.

### 5.31 Opinion Feedback

When `shouldAskOpinion` is true, the user can submit feedback: `POST /me/insights/opinion` with `{ text: string, trigger: string }`.

- Text must be 1–300 characters
- Trigger must be one of: "no_dates_yet", "pattern_shift", "high_rejection", "low_depth"
- Rate limit: 1 per user per calendar month (Redis key keyed by `yyyy-MM`)
- `Retry-After` header set to seconds until next month

Opinion feedback feeds the weight learning system.

### 5.32 Accessibility

`GET /me/accessibility` and `PUT /me/accessibility` manage two preferences:
- `reduceMotion`: disables animations on the frontend
- `highContrast`: enables high-contrast mode
- `displayPronouns`: appears on the profile card (max 50 chars)

Preferences are stored in `UserPreference` (motion/contrast) and `UserProfile` (pronouns).

### 5.33 Weekly Pulse (Dynamic Intake)

After onboarding, `DynamicIntakeCycleService` periodically generates a new question cycle. The frontend checks for a due cycle on the onboarding state response.

Questions are submitted via `PUT /dynamic-intake/{cycleId}` (alias: `PUT /me/weekly-pulse` — mapped to `DynamicIntakeEndpoints`). Answers are stored in `UserDynamicIntakeSet` and trigger an async re-embedding of the user's vector.

### 5.34 Data Export

`GET /me/data-export` returns a comprehensive dump of all data stored for the calling user:
- Profile, photos, intent, foundational answers
- Tiles, chat messages
- Visual preferences
- Third-party processor disclosure (OpenAI, Replicate)

Rate limited to 1 export per user per 30 days. The endpoint is audited (logged to `SecurityAuditLog`).

### 5.35 Account Deletion

`DELETE /me/account` performs a hard delete:
1. Deletes all `UserPhoto` records
2. Deletes all tiles, highlights, views, orbits
3. Deletes all `ChatMessage` records sent by the user
4. Anonymizes match records (sets participant IDs to a tombstone value, preserving the record for the other participant)
5. Deletes `UserVector`, `PhotoEmbedding`, `UserVisualPreference`, `UserVoicePreference`, `UserVisualDecision`
6. Deletes all media from Azure Blob Storage (profile-photo, tile-media, voice-note containers)
7. Deletes the `User` record (cascades to all remaining related records)

The deletion is audited before execution.

### 5.36 Legal Endpoints

Three public (no auth required) endpoints:

- `GET /legal/privacy` — Privacy policy summary
- `GET /legal/terms` — Terms of service summary
- `GET /legal/data-practices` — Detailed JSON breakdown of data collected, external processors used, retention policy, and user rights (access, export, deletion, correction)

---

## 6. Complete API Reference

All endpoints use JSON. All authenticated endpoints require `Authorization: Bearer {token}`.

### Authentication

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| POST | `/auth/google` | None | 20/IP/day | Google OAuth login or registration |

### Me

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/me/insights` | Required | — | Insights + opinion prompt |
| POST | `/me/insights/opinion` | Required | 1/user/month | Submit opinion feedback |
| GET | `/me/accessibility` | Required | — | Get accessibility preferences |
| PUT | `/me/accessibility` | Required | — | Update accessibility preferences |
| GET | `/me/data-summary` | Required | — | Lightweight data summary |
| GET | `/me/data-export` | Required | 1/user/30 days | Full data export |
| POST | `/me/visual-preference/reset` | Required | — | Reset visual preference data |
| POST | `/me/voice-preference/reset` | Required | — | Reset voice preference data |
| DELETE | `/me/account` | Required | — | Delete account (hard delete) |
| GET | `/me/feedback-prompt` | Required | — | Pending date feedback prompt |

### Onboarding

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/onboarding/state` | Required | Current state + next route |
| POST | `/onboarding/welcome` | Required | Complete welcome step |
| PUT | `/onboarding/basics` | Required | Set age, gender, location, preferences |
| PUT | `/onboarding/photos` | Required | Set profile photos (3–6) |
| PUT | `/onboarding/intent` | Required | Set relationship intent |
| GET | `/onboarding/foundational/questions` | Required | Get current question set |
| PUT | `/onboarding/foundational` | Required | Submit foundational answers |
| POST | `/onboarding/foundational/defer` | Required | Defer v2+ cycle by 24h |
| PUT | `/onboarding/details` | Required | Set bio, optional fields, accessibility |
| GET | `/onboarding/review` | Required | Review profile before submit |
| POST | `/onboarding/complete` | Required | Finalize profile + trigger vector build |

### Moments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/moments` | Required | Today's scored deck (up to 5) |
| GET | `/moments/pending` | Required | Pending saves (up to 50) |
| POST | `/moments/respond` | Required | YES/NO/PENDING response to candidate |

### Matches

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/matches` | Required | List active balloons |
| GET | `/matches/{matchId}/profile-access` | Required | Access level for match profile |
| GET | `/matches/{matchId}/profile` | Required | Match partner's profile |
| POST | `/matches/{matchId}/pop` | Required | Start trial period |
| POST | `/matches/{matchId}/unmatch` | Required | Unmatch (optional rating) |
| POST | `/matches/{matchId}/block` | Required | Block and close |
| POST | `/matches/{matchId}/feedback` | Required | Submit date feedback |

### Chats

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/chats` | Required | List active chat threads |
| POST | `/chats/start` | Required | Get or create thread for match |
| GET | `/chats/{threadId}` | Required | Thread detail + messages |
| POST | `/chats/{threadId}/messages` | Required | Send message |
| POST | `/chats/{threadId}/close-gracefully` | Required | Mutual walk-away |
| POST | `/chats/{threadId}/trial-decision` | Required | CONTINUE or END trial |
| GET | `/chats/{threadId}/nudge` | Required | Get conversation nudge |
| POST | `/chats/{threadId}/nudge/dismiss` | Required | Dismiss nudge (48h) |
| POST | `/chats/{threadId}/date-interest` | Required | Express date interest |
| GET | `/chats/{threadId}/venue-suggestions` | Required | Get nearby venue suggestions |
| POST | `/chats/{threadId}/availability` | Required | Share availability signal |

### Tiles

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/tiles` | Required | Create tile |
| GET | `/tiles/mine` | Required | Your tiles |
| POST | `/tiles/{tileId}/highlight` | Required | Highlight tile in slot |
| DELETE | `/tiles/{tileId}/highlight` | Required | Remove highlight |
| DELETE | `/tiles/{tileId}` | Required | Delete tile |

### Commons

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/commons` | Required | Paginated tile feed |
| POST | `/commons/refresh` | Required | Rebuild feed cache |
| POST | `/commons/{tileId}/view` | Required | Record tile view |

### Orbit

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| POST | `/orbit/{tileId}` | Required | 50/user/day | Orbit (like) a tile |

### Games

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/games/matches/{matchId}/availability` | Required | Check game availability |
| POST | `/games/matches/{matchId}/sessions` | Required | Create game session |
| POST | `/games/sessions/{sessionId}/accept` | Required | Accept pending game |
| POST | `/games/sessions/{sessionId}/reject` | Required | Reject pending game |
| GET | `/games/sessions/{sessionId}/round` | Required | Get current round |
| POST | `/games/sessions/{sessionId}/answers` | Required | Submit your answers |
| POST | `/games/sessions/{sessionId}/target-answers` | Required | Submit guesses about partner |
| GET | `/games/sessions/{sessionId}/result` | Required | Final result + AI insight |
| GET | `/games/matches/{matchId}/active` | Required | Check for active session |

### Seasons

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/seasons/current` | Required | Current season + user status |
| PUT | `/seasons/current/responses` | Required | Submit season responses |

### Media

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| POST | `/media/upload-token` | Required | 20/user/day | Get SAS upload URL |
| POST | `/media/confirm` | Required | — | Confirm upload, enqueue processing |
| DELETE | `/media/{container}/{**blobPath}` | Required | — | Delete blob |

### Verification

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| POST | `/verification/selfie` | Required | 5/user/day | Submit selfie for verification |
| GET | `/verification/status` | Required | — | Current verification status |

### Dynamic Intake

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| PUT | `/dynamic-intake/{cycleId}` | Required | Submit weekly pulse answers |

### Legal (public)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/legal/privacy` | None | Privacy policy summary |
| GET | `/legal/terms` | None | Terms of service summary |
| GET | `/legal/data-practices` | None | Data practices detail (JSON) |

### Admin Analytics (Admin role required)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/admin/analytics/overview` | Admin | DAU/WAU/MAU, registrations, matches |
| GET | `/admin/analytics/funnel` | Admin | Registration → onboarding funnel |
| GET | `/admin/analytics/content` | Admin | Content engagement (30 days) |
| GET | `/admin/analytics/ab/{experimentId}` | Admin | A/B experiment results |
| GET | `/admin/analytics/retention` | Admin | Daily retention (30 days) |
| GET | `/admin/analytics/scoring` | Admin | Scoring system activity |

### Dev Only (Development environment only)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/debug/token` | Get JWT for any user ID |
| POST | `/debug/admin-token` | Get admin JWT for any user ID |

---

## 7. Database Reference

All tables are in PostgreSQL with the pgvector extension. Vector columns use HNSW indexes for approximate nearest-neighbor search.

### Core User Tables

#### `Users`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, auto-increment |
| Email | string | Required, unique index |
| PasswordHash | string? | OAuth-first; nullable |
| FullName | string? | Set during onboarding |
| ProfilePhoto | string? | URL of primary photo |
| ProfileStatus | enum | INCOMPLETE → COMPLETE state machine |
| CreatedAt | DateTime | UTC |
| UpdatedAt | DateTime | UTC |
| TrustScore | float | 0.0–1.0, default 0.5 |
| TrustUpdatedAt | DateTime? | — |
| GhostScore | float | 0.0–1.0, default 0.5 |
| LastActiveAt | DateTimeOffset? | Updated by middleware |
| IsVerified | bool | Default false |
| VerifiedAt | DateTimeOffset? | — |
| VerificationType | string? | e.g., "SELFIE" |

#### `AuthIdentities`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| Provider | string | e.g., "google" |
| ProviderSubject | string | Google sub claim |
| Email | string | — |

Unique constraint: (Provider, ProviderSubject)

#### `UserProfiles`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users, 1:1, unique |
| Age | int? | — |
| Gender | string? | — |
| City | string? | — |
| State | string? | — |
| Lat | double? | — |
| Lng | double? | — |
| DisplayPronouns | string? | Max 50 chars |
| CreatedAt | DateTime | — |
| UpdatedAt | DateTime | — |

#### `UserPreferences`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users, 1:1, unique |
| DistanceMiles | int | 15–100 |
| AgeMin | int | Default 18 |
| AgeMax | int | Default 99 |
| InterestedInJson | string | JSON string array |
| RelationshipStructure | enum | OPEN/CLOSED/HIERARCHICAL/OTHER |
| ReduceMotion | bool? | — |
| HighContrast | bool? | — |
| CreatedAt | DateTime | — |
| UpdatedAt | DateTime | — |

#### `UserPhotos`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| Url | string | Azure Blob URL |
| Caption | string? | Max 40 chars |
| SortOrder | int | Display ordering |
| CreatedAt | DateTime | — |

Index: (UserId, SortOrder)

#### `UserIntents`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users, 1:1, unique |
| PrimaryIntent | string | Required |
| OpennessJson | string | JSON string array |
| ReflectionSentence | string | Max 200 chars |
| CreatedAt | DateTime | — |
| UpdatedAt | DateTime | — |

#### `UserOptionalFields`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| Key | string | e.g., "job", "pref_ethnicity" |
| Value | string | — |
| Visibility | enum | Public, MatchingOnly |
| CreatedAt | DateTime | — |
| UpdatedAt | DateTime | — |

Unique index: (UserId, Key)

#### `UserWeeklyVibes`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users, 1:1, unique |
| Text | string | — |
| ExpiresAt | DateTime | — |
| CreatedAt | DateTime | — |

### Onboarding / Intake Tables

#### `UserFoundationalQuestionSets`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| Version | int | 1, 2, 3… |
| QuestionsJson | string | 5 questions [{id, text}] |
| AnswersJson | string? | After submission |
| AnsweredAt | DateTime? | — |
| ExpiresAt | DateTime | Version-dependent |
| DeferredUntil | DateTime? | +24h if deferred |
| CreatedAt | DateTime | — |
| UpdatedAt | DateTime | — |

Unique index: (UserId, Version). Unique partial index: (UserId) WHERE AnsweredAt IS NULL.

#### `UserDynamicIntakeSets`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| CycleId | int | Auto-increment per user |
| QuestionsJson | string | — |
| AnswersJson | string? | — |
| AnsweredAt | DateTime? | — |
| ExpiresAt | DateTime | — |
| DeferredUntil | DateTime? | — |
| CreatedAt | DateTime | — |
| UpdatedAt | DateTime | — |

Unique index: (UserId, CycleId)

### Matching & Discovery Tables

#### `Matches`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| UserAId | int | FK → Users (restrict) |
| UserBId | int | FK → Users (restrict) |
| MatchType | enum | PURE, EDGE |
| EdgeOwnerId | int? | FK → Users (restrict); required if EDGE |
| BalloonState | enum | ACTIVE, CLOSED |
| ClosedReason | enum? | POP, EXPIRE, UNMATCH, BLOCK |
| CreatedAt | DateTimeOffset | — |
| ExpiresAt | DateTimeOffset | +7 days from creation |
| ClosedAt | DateTimeOffset? | — |
| BothMessagedAt | DateTimeOffset? | When both sent ≥1 message |
| FindLoveAt | DateTimeOffset? | BothMessagedAt + 5 min |
| IsTrial | bool | Default false |
| TrialStartedAt | DateTimeOffset? | — |
| TrialEndsAt | DateTimeOffset? | TrialStartedAt + 1 min |
| UserADecision | string? | CONTINUE or END |
| UserBDecision | string? | CONTINUE or END |
| DateIdeaInterestedA | bool | Default false |
| DateIdeaInterestedB | bool | Default false |
| DateIdeaInterestedAt | DateTimeOffset? | When both interested |
| DateAgreedAt | DateTimeOffset? | — |

Check constraints: no self-matches; EDGE must have EdgeOwnerId; CLOSED must have ClosedReason; ExpiresAt > CreatedAt.
Unique partial index: (UserAId, UserBId) WHERE BalloonState = 'ACTIVE'.

#### `DailyInteractions`
| Column | Type | Notes |
|--------|------|-------|
| UserId | int | PK composite |
| DateUtc | DateOnly | PK composite |
| TotalUsed | int | 0–5 |
| PendingUsed | int | 0–2 |
| GamesInitiated | int | 0–2 |

#### `PendingMatches`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| TargetUserId | int | FK → Users (cascade) |
| CreatedAt | DateTimeOffset | — |

Unique index: (UserId, TargetUserId)

#### `Blocks`
| Column | Type | Notes |
|--------|------|-------|
| BlockerId | int | PK composite, FK → Users (cascade) |
| BlockedId | int | PK composite, FK → Users (cascade) |
| CreatedAt | DateTimeOffset | — |

#### `MomentResponses`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| DateUtc | DateOnly | — |
| FromUserId | int | FK → Users (cascade) |
| ToUserId | int | FK → Users (cascade) |
| Choice | enum | YES (Magical ◈), NO (Logical ◇), PENDING (Save ⏳) |
| CreatedAt | DateTimeOffset | — |

Unique index: (DateUtc, FromUserId, ToUserId)

#### `DailyDecks`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| DateUtc | DateOnly | — |
| DeckJson | string | JSON array of deck items |

Unique index: (UserId, DateUtc)

#### `MatchExplanations`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| CandidateId | int | — |
| DateUtc | DateOnly | — |
| Headline | string | — |
| BulletsJson | string | JSON string array |
| Tone | string | — |
| DateIdea | string? | AI-generated date suggestion |
| CreatedAt | DateTimeOffset | — |

Index: (UserId, CandidateId, DateUtc)

#### `MatchOutcomes`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| MatchId | Guid | — |
| UserId | int | FK → Users (cascade) |
| CandidateId | int | — |
| DateUtc | DateOnly | — |
| Outcome | string | CHAT_STARTED, UNMATCH, etc. |
| CreatedAt | DateTimeOffset | — |

#### `CandidateExposures`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| ViewerUserId | int | — |
| ShownUserId | int | — |
| DateUtc | DateOnly | — |
| Surface | string | e.g., MOMENTS_DECK |
| CreatedAt | DateTimeOffset | — |

Unique index: (ViewerUserId, ShownUserId, DateUtc, Surface)

#### `CandidateSignals`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| FromUserId | int | — |
| ToUserId | int | — |
| Type | string | MATCH_CREATED, CHAT_STARTED, etc. |
| ExpiresAt | DateTimeOffset | — |
| CreatedAt | DateTimeOffset | — |

### Chat Tables

#### `ChatThreads`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| MatchId | Guid | FK → Matches (cascade), unique |
| LastMessageAt | DateTimeOffset? | — |
| MessageCount | int | Running counter |
| AvgResponseTimeMs | long? | Running average |
| CreatedAt | DateTimeOffset | — |
| UpdatedAt | DateTimeOffset | — |

#### `ChatMessages`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| ThreadId | Guid | FK → ChatThreads (cascade) |
| SenderUserId | int | FK → Users (cascade) |
| Body | string | 1–1000 chars (check constraint) |
| MessageType | string? | System message type |
| MetaJson | string? | System message metadata |
| CreatedAt | DateTimeOffset | — |

#### `ChatAvailabilitySignals`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| ThreadId | Guid | FK → ChatThreads |
| UserId | int | FK → Users |
| SignalText | string | Max 200 chars |
| CreatedAt | DateTimeOffset | — |

#### `UserRatings`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| RatedUserId | int | FK → Users |
| RaterUserId | int | FK → Users |
| MatchId | Guid | FK → Matches |
| RatingValue | int | -100 to 100 |
| CreatedAt | DateTimeOffset | — |

### Games Tables

#### `GameSessions`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| MatchId | Guid | FK → Matches (cascade) |
| InitiatorUserId | int | FK → Users (cascade) |
| GameType | string | KNOW_ME, RED_GREEN_FLAG |
| Status | string | PENDING, ACTIVE, COMPLETED |
| ExpiresAt | DateTimeOffset | — |
| CreatedAt | DateTimeOffset | — |

#### `GameRounds`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| SessionId | Guid | FK → GameSessions (cascade) |
| RoundNumber | int | — |
| GuesserUserId | int | FK → Users (cascade) |
| TargetUserId | int | FK → Users (cascade) |
| QuestionsJson | string | — |
| GuesserAnswersJson | string? | — |
| TargetAnswersJson | string? | — |

#### `GameResults`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| SessionId | Guid | FK → GameSessions (cascade) |
| MatchId | Guid | FK → Matches (cascade) |
| GameType | string | — |
| UserAId | int | — |
| UserBId | int | — |
| UserAScore | int | — |
| UserBScore | int | — |
| WinnerUserId | int? | Null if tie |
| AiInsight | string? | GPT-generated insight |
| CreatedAt | DateTimeOffset | — |

### Tiles & Commons Tables

#### `Tiles`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| UserId | int | FK → Users (cascade) |
| MediaUrl | string | Azure Blob URL |
| Caption | string? | — |
| CreatedAt | DateTimeOffset | — |

#### `Highlights`
| Column | Type | Notes |
|--------|------|-------|
| Id | Guid | PK |
| TileId | Guid | FK → Tiles (cascade) |
| HighlightedAt | DateTimeOffset | — |

#### `TileViews`, `TileOrbits`, `TileEngagements`, `TileReports`
Standard engagement tracking tables (TileId, UserId/ViewerId, optional duration, timestamps).

#### `UserEnergyMeters`
| Column | Type | Notes |
|--------|------|-------|
| UserId | int | PK composite |
| DateUtc | DateOnly | PK composite |
| EnergyBudget | int | Daily remaining |

### Vector & Embedding Tables

#### `UserVectors`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| Version | int | Increments on rebuild |
| PillarEmbedding | vector(1536) | pgvector, HNSW indexed |
| CreatedAt | DateTimeOffset | — |

Unique index: (UserId, Version)

#### `UserVectorTags`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| Version | int | — |
| TagType | string | Category of tag |
| Tag | string | Tag value |

#### `PhotoEmbeddings`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| EmbeddingVector | vector? | pgvector |
| EmbeddedAt | DateTimeOffset | — |

#### `UserVisualDecisions`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| ViewerUserId | int | FK → Users (cascade) |
| TargetUserId | int | — |
| PhotoEmbeddingId | int? | — |
| Choice | string | YES, NO |
| DecidedAt | DateTimeOffset | — |

#### `UserVisualPreferences`
| Column | Type | Notes |
|--------|------|-------|
| UserId | int | PK |
| YesSampleCount | int | — |
| NoSampleCount | int | — |
| UpdatedAt | DateTimeOffset | — |

#### `UserVoicePreferences`
| Column | Type | Notes |
|--------|------|-------|
| UserId | int | PK |
| PreferenceVector | vector? | pgvector |
| UpdatedAt | DateTimeOffset | — |

#### `UserMatchingWeights`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| WeightJson | string | Per-pillar weight map |

### Learning & Feedback Tables

#### `CfScores`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | — |
| CandidateId | int | — |
| Score | float | Collaborative filtering score |

#### `UserInsights`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | — |
| InsightsJson | string | JSON string array |
| UpdatedAt | DateTimeOffset | — |

#### `DateFeedbacks`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| MatchId | Guid | — |
| MetInPerson | bool | — |
| Stars | int? | 1–5 |
| FeltRightText | string? | — |
| FeltOffText | string? | — |
| MeetAgain | string? | yes/no/maybe |
| SubmittedAt | DateTimeOffset | — |

#### `DateFeedbackPrompts`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users |
| MatchId | Guid | — |
| TriggerType | string | — |
| SentAt | DateTimeOffset? | — |
| RespondedAt | DateTimeOffset? | — |

### Season Tables

#### `Seasons`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| Name | string | — |
| StartDate | DateOnly | — |
| EndDate | DateOnly | — |

#### `UserSeasonResponses`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | — |
| SeasonId | int | — |
| Response | string | — |

### Security & Audit Tables

#### `SecurityAuditLogs`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| Action | string | account_deletion, data_export, etc. |
| UserId | int? | Null for anonymous actions |
| ResourceType | string? | — |
| ResourceId | string? | — |
| Timestamp | DateTimeOffset | — |
| IpAddress | string? | — |

#### `UserVerifications`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | FK → Users (cascade) |
| Type | string | SELFIE |
| Status | string | PENDING, VERIFIED, FAILED |
| SubmittedAt | DateTimeOffset? | — |
| VerifiedAt | DateTimeOffset? | — |
| FailureReason | string? | — |

#### `ReferencePhotoEmbeddings`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| UserId | int | — |
| EmbeddingVector | vector? | Used for selfie matching |

### Analytics Tables

#### `AnalyticsEvents`
| Column | Type | Notes |
|--------|------|-------|
| Id | long | PK |
| UserId | int? | Nullable (anonymized after 12 months) |
| UserIdHash | string? | Anonymized after 12 months |
| SessionId | Guid? | — |
| EventType | string | Event name constant |
| Properties | string? | JSON metadata |
| CreatedAt | DateTimeOffset | — |

#### `AbExperiments`
| Column | Type | Notes |
|--------|------|-------|
| Id | string | PK (experiment name) |
| IsActive | bool | — |
| CreatedAt | DateTimeOffset | — |

#### `AbAssignments`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| ExperimentId | string | — |
| UserId | int | — |
| Variant | string | — |
| AssignedAt | DateTimeOffset | — |

#### `AbConversions`
| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK |
| ExperimentId | string | — |
| UserId | int | — |
| ConversionType | string | — |
| ConvertedAt | DateTimeOffset | — |

---

## 8. Background Workers Reference

All workers are registered as `IHostedService` and run as long-lived background services. They use `IServiceScopeFactory` for all database access (scoped services within a singleton worker).

### Worker Schedule (all times UTC)

| Time | Worker | Frequency | Description |
|------|--------|-----------|-------------|
| Every 1 min | BalloonExpiryWorker | Continuous | Close expired ACTIVE matches |
| Every 6h | GhostDetectionWorker | Continuous | Detect silent threads, update ghost scores |
| 01:00 | SeasonTransitionWorker | Nightly | Transition seasons on boundary dates |
| 02:00 | TrustBatchWorker | Nightly | Recompute trust scores from signals |
| 02:15 | AnalyticsRetentionWorker | 1st of month | Anonymize events older than 12 months |
| 02:30 | EmbeddingBatchWorker | Nightly | Generate photo/voice/style embeddings |
| 03:00 | CfBatchWorker | Nightly | Compute collaborative filtering scores |
| 03:30 | GhostDetectionWorker | Nightly pass | Full ghost detection pass |
| 04:00 | WeightLearningBatchWorker | Weekly (Sun) | Optimize per-user matching weights |
| 04:30 | InsightBatchWorker | Nightly | Generate user insights |
| 05:00 | SecurityAuditCleanupWorker | Weekly (Sun) | Clean old audit log entries |
| 06:00 | WeeklyDigestWorker | Weekly (Sun) | Send weekly digest emails |
| 08:00 | FeedbackTriggerWorker | Daily | Send date feedback prompts |

### Worker Details

**BalloonExpiryWorker** — Runs every 60 seconds. Queries for ACTIVE matches where `ExpiresAt <= now`. For each found match, sets `BalloonState = CLOSED`, `ClosedReason = EXPIRE`, `ClosedAt = now`. Sends `MomentExpired` push to both participants.

**TrustBatchWorker** — Nightly at 02:00 UTC. Aggregates trust signals (device fingerprints, velocity, ratings received, ghost behavior, verification status) into a 0.0–1.0 score per user. Updates `User.TrustScore` and `User.TrustUpdatedAt`.

**AnalyticsRetentionWorker** — Runs on the 1st of each month at 02:15 UTC (15 minutes offset from TrustBatchWorker to avoid contention). Finds `AnalyticsEvents` where `CreatedAt < now - 12 months` and `UserIdHash != null`. Executes a bulk update: sets `UserIdHash = null` and `SessionId = null`, effectively anonymizing the user link while preserving the event data for aggregate analysis.

**EmbeddingBatchWorker** — Nightly at 02:30 UTC. Processes users whose embeddings are stale or missing. Calls photo embedding service (Replicate API), voice embedding service, and style embedding service. Stores results in `PhotoEmbeddings` and `UserVoicePreferences`.

**CfBatchWorker** — Nightly at 03:00 UTC. Runs collaborative filtering across the user base. Uses match outcomes and engagement signals to compute `CfScore` pairs: for user A, which candidates scored high from the "people like A" cluster.

**GhostDetectionWorker** — Two modes: nightly full pass (03:30 UTC) and every-6h silent-thread scan. Identifies users who consistently receive messages and never reply, or who initiate matches and disappear. Updates `User.GhostScore` (0.0 = no ghost signals, 1.0 = chronic ghoster). High ghost-score users are suppressed in decks.

**WeightLearningBatchWorker** — Weekly, Sunday 04:00 UTC. For each user with sufficient outcome data, regresses the weight of each scoring pillar against observed outcomes (matches → chats → dates → feedback stars). Writes updated weights to `UserMatchingWeights`. The next day's deck uses these new weights.

**InsightBatchWorker** — Nightly at 04:30 UTC. For each user, examines match history, conversation depth, response rates, and outcome patterns. Generates a list of plain-English observations (e.g., "You tend to match well with people who value outdoor activities"). Replaces `UserInsight.InsightsJson`.

**SecurityAuditCleanupWorker** — Weekly, Sunday 05:00 UTC. Deletes `SecurityAuditLog` entries older than the retention window. Anonymizes any remaining PII in older entries.

**WeeklyDigestWorker** — Weekly, Sunday 06:00 UTC. Sends a digest email/notification to users who have not been active in the past week, summarizing activity (new Saves, expiring balloons, etc.).

**FeedbackTriggerWorker** — Daily at 08:00 UTC. Checks for matches that recently reached `DateIdeaInterestedAt` (i.e., both expressed date interest) and have not yet received a feedback prompt. Creates `DateFeedbackPrompt` records and sends push notifications.

---

## 9. The AI Layer

Woven uses AI at multiple points in the system. All AI calls are asynchronous and fail gracefully — if an AI service is unavailable, the system returns a degraded but functional result.

### 9.1 Pillar Embeddings (OpenAI)

**What**: The primary matching signal. Text from foundational answers, dynamic intake responses, and seasonal responses is embedded into a 1536-dimensional vector using OpenAI's embedding API.

**When**: On `POST /onboarding/complete` (v1 build, async) and after each intake cycle completion.

**Model**: OpenAI text-embedding model (1536-dim output).

**Storage**: `UserVectors.PillarEmbedding` (pgvector column).

**Used by**: `IVectorSearchService` for candidate pool generation and `IMatchScoringService` for pillar cosine similarity.

### 9.2 Photo Embeddings (Replicate)

**What**: Visual feature vectors extracted from profile photos. These represent visual aesthetics, not identity.

**When**: Nightly by `EmbeddingBatchWorker` for users with new or missing embeddings.

**Model**: Replicate-hosted vision model (configurable via `Replicate__ApiToken`).

**Storage**: `PhotoEmbeddings.EmbeddingVector`.

**Used by**: `IVisualPreferenceService` — compared against `UserVisualPreference` to score visual compatibility. Also used for selfie verification (`ReferencePhotoEmbedding`).

### 9.3 Voice Embeddings (SpeechBrain)

**What**: Tone and prosody features extracted from voice notes. These capture speaking style, not content.

**When**: After `POST /media/confirm` for `voice-note` container (enqueued, processed async).

**Model**: SpeechBrain (Python script: `scripts/speechbrain_embed.py`). Called via `Process.Start` in the dev smoke test.

**Storage**: `UserVoicePreferences.PreferenceVector`.

**Used by**: Voice tone compatibility scoring in `IMatchScoringService`.

### 9.4 Match Explanations (OpenAI)

**What**: Human-readable explanations of why two people are compatible, including a date idea.

**When**: During daily deck build by `IMatchExplanationService`. One explanation per (userId, candidateId, date) triple.

**Model**: OpenAI chat completion.

**Output**:
- `Headline`: one sentence
- `Bullets`: 3–5 specific reasons
- `Tone`: characterization word
- `DateIdea`: a concrete, specific first date suggestion

**Storage**: `MatchExplanations`.

**Used by**: Displayed in the Moments deck card and unlocked in chat after FindLoveAt.

### 9.5 Game Agent (OpenAI)

**What**: Evaluates game answers, computes scores, and generates an AI insight about the match.

**When**: After both players have submitted answers to a game session.

**Interface**: `IGameAgent.EvaluateAsync(userAAnswers, userBAnswers, userBGuesses)`.

**Output**: `EvaluationResult` with scores and `AiInsight` string.

**Storage**: `GameResults.AiInsight`.

**Used by**: `GET /games/sessions/{sessionId}/result`.

### 9.6 Nudge Generation (OpenAI)

**What**: Contextual conversation prompts generated based on the current chat thread state.

**When**: On `GET /chats/{threadId}/nudge` call.

**Interface**: `INudgeService.GetConversationNudgeAsync(userId, threadId, ct)`.

**Used by**: The nudge card shown in the chat thread.

### 9.7 Insights Generation (OpenAI)

**What**: Personalized observations about a user's patterns and behaviors on the platform.

**When**: Nightly by `InsightBatchWorker`.

**Interface**: `IInsightService` (batch generation path).

**Output**: Plain English strings stored in `UserInsights.InsightsJson`.

**Used by**: `GET /me/insights`.

### 9.8 Opinion Analysis

**What**: When a user submits a feedback opinion, it feeds the weight learning system indirectly. The free-text opinion is associated with a specific trigger and used in aggregate analysis.

**Storage**: Linked to `AnalyticsEvent` with type `OpinionSubmitted` and stored text in properties.

### 9.9 Cost Controls

- All AI calls are async and non-blocking
- Deck explanations are cached in `MatchExplanations` — never re-generated for the same (user, candidate, date)
- Embeddings are batch-processed nightly, not inline
- Voice/photo embeddings are processed after upload confirmation, not during the upload
- SpeechBrain smoke test runs dev-only and never blocks startup

---

## 10. Security and Privacy

### 10.1 Authentication

All protected endpoints validate a JWT Bearer token. Tokens are signed with a 64-character random key stored in Azure Key Vault (injected as a Container Apps secret). The key is auto-generated by Terraform's `random_password` resource and never stored in code.

Standard JWT claims: `uid` (integer user ID), `sub`, `email`. Admin tokens additionally carry `role = admin` and expire in 1 hour.

### 10.2 Authorization

Standard user endpoints use `RequireAuthorization()` with the default policy (valid JWT). Admin analytics endpoints require the custom `"Admin"` policy which requires a `role` claim equal to `"admin"`. No user can self-elevate to admin.

### 10.3 Rate Limiting

Rate limits are enforced via Redis using `CheckRateLimitAsync`. On Redis failure, the method returns `true` (allow) — Redis outage never blocks users. All rate-limited endpoints return `HTTP 429` with a `Retry-After` header in seconds.

| Endpoint | Limit | Window | Key |
|----------|-------|--------|-----|
| POST /auth/google | 20 | Per day | SHA-256(IP) + date |
| POST /verification/selfie | 5 | Per day | userId + date |
| POST /media/upload-token | 20 | Per day | userId + date |
| POST /orbit/{tileId} | 50 | Per day | userId + date |
| POST /me/insights/opinion | 1 | Per month | userId + yyyy-MM |
| GET /me/data-export | 1 | 30 days | Redis flag |

### 10.4 Encryption at Rest

Sensitive data is encrypted using `IEncryptionService`. The master key is loaded from the `Encryption__MasterKey` environment variable (stored as a Container Apps secret, never in code).

### 10.5 PII Handling

- Email addresses are never logged in plaintext
- `PiiSanitizer.HashForAudit(value, salt)` produces a SHA-256 hash for audit keys
- IP addresses in rate-limit keys are hashed: `SHA-256(ip + "rl-auth-v1")`
- Analytics events are linked to `UserIdHash` (hashed) rather than raw user IDs for the first 12 months
- After 12 months, `AnalyticsRetentionWorker` nulls out both `UserIdHash` and `SessionId`

### 10.6 Security Audit Logging

`ISecurityAuditService.Log()` records high-sensitivity actions to `SecurityAuditLogs`:

| Action | Trigger |
|--------|---------|
| account_deletion | DELETE /me/account |
| data_export | GET /me/data-export |
| bulk_data_export | Admin data exports |
| preference_reset | POST /me/visual-preference/reset |
| voice_preference_reset | POST /me/voice-preference/reset |
| pii_access | AnalyticsRetentionWorker anonymization run |

`SecurityAuditCleanupWorker` purges old entries weekly.

### 10.7 Data Matching Safety

- Users cannot match with themselves (database check constraint)
- A user cannot see their own profile in the Moments deck
- Blocks are enforced at the query level — blocked users never appear in decks or Save lists
- Existing active matches are excluded from deck generation

### 10.8 Trust and Ghost Scoring

**Trust score** (0.0–1.0): Aggregated from device fingerprint consistency, login velocity, received ratings, verification status, and behavioral signals. Updated nightly. High-trust users rank higher in decks. Low-trust users are suppressed.

**Ghost score** (0.0–1.0): Aggregated from silence signals — receiving messages without replying, initiating matches and never messaging, trial periods abandoned without decision. Updated nightly and every 6 hours. High ghost-score users are suppressed in other users' decks.

### 10.9 User Data Rights

Enforced via endpoints (not just policy):

- **Access**: `GET /me/data-summary` and `GET /me/data-export`
- **Export**: Full export once per 30 days
- **Deletion**: `DELETE /me/account` — hard delete with full anonymization
- **Correction**: Update profile fields via onboarding/details endpoints
- **Visual data reset**: `POST /me/visual-preference/reset`
- **Voice data reset**: `POST /me/voice-preference/reset`

Third-party data disclosures are returned by `GET /legal/data-practices` — OpenAI (embeddings, completions), Replicate (photo embeddings), Azure (storage, compute).

### 10.10 Transport Security

All communication is HTTPS. The frontend Container App is external (public). The backend Container App is internal-only (`external_enabled = false`). The frontend proxies all API calls through nginx, which routes to the backend's internal FQDN via HTTPS with SNI configuration. The backend never receives direct public traffic.

---

## 11. Infrastructure

### 11.1 Azure Resources

All resources are provisioned via Terraform. The plan at last review: **34 to add, 0 to change, 0 to destroy**.

**Container Apps Environment**
- Hosts both frontend and backend Container Apps
- Internal load balancer enabled
- VNet-integrated (infrastructure subnet)
- Connected to Log Analytics workspace
- Infrastructure resource group managed by Azure (drift ignored via `lifecycle.ignore_changes`)

**Backend Container App** (`woven-backend`)
- Internal only (`external_enabled = false`)
- 0.5 CPU, 1Gi memory
- 1–5 replicas
- User-assigned managed identity for ACR pulls
- System-assigned identity for Azure service access
- All secrets injected via Container Apps secrets (never plain env vars)
- Health probes: startup (100s budget), liveness (every 30s), readiness (every 10s)
- Port: 8080

**Frontend Container App** (`woven-frontend`)
- External (`external_enabled = true`)
- 0.5 CPU, 1Gi memory
- 1–3 replicas
- nginx serving Angular SSR build
- BACKEND_URL injected at startup via envsubst from template
- Health probes on port 80

**Azure Container Registry (ACR)**
- Hosts `woven-backend` and `woven-frontend` images
- Images tagged by version (`var.backend_image_tag`, `var.frontend_image_tag`)
- AcrPull role assigned to the shared user-assigned managed identity

**PostgreSQL Flexible Server**
- pgvector extension enabled
- Connection string injected as secret (`ConnectionStrings__DefaultConnection`)

**Redis Cache**
- Used for session caching, rate limiting, feed caching, real-time backplane
- Connection string injected as secret (`Redis__ConnectionString`)

**Azure Blob Storage**
- Containers: `profile-photo`, `tile-media`, `voice-note`
- Direct upload via SAS tokens (no backend intermediary for the binary data)
- Connection string injected as secret (`Azure__Storage__ConnectionString`)

**Application Insights**
- Full telemetry for the backend
- Connection string injected as secret (`APPLICATIONINSIGHTS_CONNECTION_STRING`)

### 11.2 Secrets (Container Apps)

All secrets are injected as Container Apps secrets and mapped to environment variables. None are committed to code or Terraform state in plaintext.

| Secret Name | Environment Variable | Source |
|-------------|---------------------|--------|
| db-conn | ConnectionStrings__DefaultConnection | Terraform variable |
| appinsights-conn | APPLICATIONINSIGHTS_CONNECTION_STRING | Terraform variable |
| jwt-key | Jwt__Key | Auto-generated by `random_password` |
| redis-conn | Redis__ConnectionString | Terraform variable |
| storage-conn | Azure__Storage__ConnectionString | Terraform variable |
| google-client-id | GoogleAuth__ClientId | Terraform variable |
| moderation-enabled | IsModerationEnabled | Hardcoded `"true"` |
| encryption-master-key | Encryption__MasterKey | Terraform variable |
| replicate-api-token | Replicate__ApiToken | Terraform variable |
| openai-api-key | OpenAI__ApiKey | Terraform variable |
| google-places-key | Google__PlacesApiKey | Terraform variable |

### 11.3 Networking

The frontend and backend share a Container Apps Environment but have different ingress configurations:
- Frontend: external, port 80, nginx
- Backend: internal, port 8080, .NET

Frontend nginx configuration uses `BACKEND_URL` (set to the backend's internal FQDN with HTTPS) via `envsubst` at container startup. The backend FQDN is derived from `azurerm_container_app.backend.ingress[0].fqdn` — the internal-only endpoint assigned by the Container Apps environment.

CORS on the backend allows the frontend's Container Apps domain and an optional custom domain (`var.custom_domain`). The backend cannot reference the frontend resource directly (circular dependency) so the CORS origin is constructed from `azurerm_container_app_environment.main.default_domain`.

### 11.4 Managed Identity for ACR

A user-assigned managed identity (`woven-backend-acr-identity`) is created before the Container Apps. It receives the `AcrPull` role on the ACR. Both Container Apps reference this identity for image pulls. This pattern solves the chicken-and-egg problem inherent to system-assigned identities: the identity must exist before the app, but the app must exist to have a system-assigned identity.

### 11.5 Local Development

The development environment uses:
- `appsettings.Development.json` for local configuration
- `DevAuthEndpoints.cs` provides `/debug/token` and `/debug/admin-token` for testing any user's JWT
- SpeechBrain smoke test runs at startup (non-blocking, logs available/unavailable)
- Docker Compose for local service dependencies (PostgreSQL, Redis)

---

## 12. Glossary

**A/B Experiment**: A controlled test comparing two or more variants of a feature. Assignments tracked in `AbAssignments`, conversions in `AbConversions`. Results visible via admin analytics.

**AnalyticsRetentionWorker**: Background worker that runs on the 1st of each month to anonymize analytics events older than 12 months.

**Balloon**: A match. The name reflects its finite lifespan (7 days). Has states ACTIVE and CLOSED.

**BalloonExpiryWorker**: Runs every minute. Closes ACTIVE matches past their `ExpiresAt`.

**BothMessagedAt**: The timestamp when both participants in a match have sent at least one message. Triggers the 5-minute reflection window leading to Find Love.

**Budget (Moments)**: Daily response allocation. 5 Magical/Logical responses per day, 2 Save picks per day. Tracked in `DailyInteraction`.

**CF (Collaborative Filtering)**: Behavioral similarity scoring. Users who behave like you tend to find the same candidates appealing. Computed nightly by `CfBatchWorker`.

**Commons**: The public tile feed. Distinct from the Moments deck. Anyone can view tiles from any user.

**DailyDeck**: Cached candidate list for a user on a given UTC date. Built by `IDailyDeckOrchestrator`.

**Date Idea**: AI-generated specific date suggestion included in `MatchExplanation`. Visible after Find Love unlocks.

**DateFeedbackPrompt**: Automated prompt sent after both users express date interest. Collects post-date reflection.

**Dynamic Intake**: Recurring question cycles that update the user's compatibility vector after onboarding. Also called "Weekly Pulse".

**EDGE match**: A match where the two users' Moments choices differed (e.g., one Magical ◈, one Logical ◇ — or one responded to the other's Save). One user is randomly designated the edge owner and gets full profile access; the other gets limited access until both have messaged.

**EmbeddingBatchWorker**: Nightly worker that generates photo and voice embeddings for users missing them.

**Energy**: Daily budget for Commons feed browsing. Tracked in `UserEnergyMeters`.

**Find Love**: The milestone that unlocks the date idea and date coordination features. Automatically set 5 minutes after `BothMessagedAt`.

**FindLoveAt**: Timestamp field on `Match`. Set to `BothMessagedAt + 5 minutes` (or immediately if trial CONTINUE/CONTINUE).

**Foundational Questions**: 5 deep open-ended questions asked during onboarding and in recurring cycles. Core input to the pillar embedding vector.

**Ghost Score**: A 0.0–1.0 score indicating how often a user ignores messages or disappears from conversations. High scores suppress the user in other users' decks.

**GhostDetectionWorker**: Runs nightly and every 6 hours. Detects ghosting patterns and updates `User.GhostScore`.

**Highlight**: Pinning a tile to a numbered slot on your profile.

**InsightBatchWorker**: Nightly worker that generates personalized observations about each user's patterns.

**IsModerationEnabled**: Boolean flag (currently `"true"`) that controls whether content moderation runs on submitted media.

**JWT**: JSON Web Token. Used for all authentication. Signed with an auto-generated 64-char key.

**KNOW_ME**: A game type where players answer questions and guess what their match answered.

**LastActiveAt**: Timestamp updated by middleware on every authenticated request. Used for re-engagement detection.

**Logical (◇)**: The left-side Moments choice. Represents a head-led, reason-first approach. Stored internally as `MomentChoice.NO`. Same-side match with another Logical → PURE. Opposite to Magical → EDGE.

**Magical (◈)**: The right-side Moments choice. Represents a heart-led, intuition-first approach. Stored internally as `MomentChoice.YES`. Same-side match with another Magical → PURE. Opposite to Logical → EDGE.

**Match Explanation**: AI-generated (headline + bullets + tone + date idea) for a (user, candidate) pair. Cached in `MatchExplanations`.

**Moments**: The daily discovery surface. Shows a scored deck of up to 5 candidate profiles.

**MomentResponse**: A record of one user's Magical/Logical/Save response to another on a given date. Stored internally as `YES`/`NO`/`PENDING` in the database enum.

**Orbit**: Appreciating a tile in the Commons feed. Equivalent of a like. Rate limited to 50/day.

**Pillar Embedding**: 1536-dimensional vector representing a user's personality, values, and compatibility profile. Built from foundational answers and intake responses via OpenAI's embedding API.

**ProfileStatus**: State machine tracking onboarding completion. Values: INCOMPLETE → WELCOME_DONE → BASICS_DONE → INTENT_DONE → FOUNDATION_DONE → DETAILS_DONE → COMPLETE.

**PURE match**: A match where both users chose the same side in Moments — both Magical (◈) or both Logical (◇). Highest-quality signal: they think alike. Both get full profile access immediately.

**Reflection Window**: The 5-minute gap between `BothMessagedAt` and `FindLoveAt`. Gives users a moment before the date-coordination features unlock.

**Replicate**: Third-party API used for photo embedding generation (visual feature extraction).

**Save (⏳ Hold)**: The middle Moments choice. Bookmarks a candidate to a private list without committing. Stored internally as `MomentChoice.PENDING`. Budget: 2 Saves per day. No notification to the other person.

**Season**: A time-bounded thematic prompt event. Users respond to season pillars; responses contribute to the embedding vector and appear as a season signature on the profile.

**SpeechBrain**: Python-based voice embedding library. Called via subprocess in the embedding pipeline for voice note processing.

**Tile**: A micro-content post — a photo with an optional short caption. Lives in the Commons feed.

**Trial Period**: A 1-minute window initiated when a user "pops" their own balloon. Both users decide CONTINUE or END; both CONTINUE unlocks Find Love immediately.

**Trust Score**: A 0.0–1.0 score aggregating behavioral trust signals (device consistency, ratings, velocity, verification). High-trust users rank higher in decks.

**TrustBatchWorker**: Nightly worker that recomputes trust scores.

**UserVector**: The stored pillar embedding (1536-dim pgvector) for a user. Versioned; rebuilt when foundational answers are updated.

**Weekly Pulse**: See "Dynamic Intake".

**WeightLearningBatchWorker**: Weekly worker that personalizes each user's matching pillar weights based on their outcome history.
