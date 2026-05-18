# Woven Glossary

> A–Z reference for every term used in the Woven platform.
> Technical terms reference the internal implementation; product terms use the language shown to users.

---

## A

**Access Level**
The tier of profile information visible between matched users. Three levels: LIMITED (EDGE non-owner before both message), STANDARD (most matched users), FULL (connected users). Computed at query time, not stored.

**ACTIVE** (balloon state)
A balloon that exists and is available for either user to pop. Transitions to CLOSED via POP, EXPIRE, UNMATCH, or BLOCK.

**ACTIVE** (user status)
A user who has completed onboarding and participates in matching. The normal operating state.

**AES-256-GCM**
Encryption algorithm used for sensitive columns in the database (e.g., message bodies, PII fields). Keys are derived via HKDF with purpose-specific sub-keys.

**AI Layer**
The set of OpenAI integrations in Woven: pillar embeddings, photo embeddings, voice embeddings, match explanations, game agent, nudge generation, insights generation, dynamic intake Q generation. Uses `text-embedding-3-small` (1536-dim) and `gpt-4.1-mini`.

**AuthIdentity**
Database entity linking a User to an external auth provider (Google OAuth). Stores `provider` and `providerUserId`.

---

## B

**Balloon**
The representation of an unpopped potential connection. Created when two users both make a Magical or Logical choice on the same Moment. Has a 7-day TTL. Either user can pop it to start the trial period.

**BalloonCloseReason**
Enum: POP, EXPIRE, UNMATCH, BLOCK. Stored alongside `ClosedAt` when a balloon transitions to CLOSED.

**BalloonExpiryWorker**
Background service that runs every 60 seconds. Finds all ACTIVE matches where `ExpiresAt ≤ now` and closes them with reason EXPIRE.

**BothMessagedAt**
Timestamp set on a Match when both users have sent at least one message. Triggers EDGE access level upgrade for non-owner. Also starts the 5-minute countdown to Find Love unlock.

**Budget**
The daily interaction allowance. Default: 5 total / 2 Save per user per day. Resets midnight UTC. Cannot go negative. See also: *Interaction Budget System*.

**Bucket**
Deck composition slot category: CORE_FIT, LIFESTYLE_FIT, CONVERSATION_FIT, EXPLORER. Each slot type targets a different compatibility signal.

---

## C

**Candidate Pool**
The set of users eligible to appear in another user's Moments deck. Filtered by: gender preference, age preference, distance, blocked users, already-matched users, TrustScore ≥ 0.25, onboarding complete.

**ChatThread**
Database entity representing a messaging channel between two matched users. 1:1 with a Match. Created when a balloon is popped.

**Circuit Breaker**
Fault-tolerance pattern for external API calls (OpenAI, Replicate). States: CLOSED (normal), OPEN (failing — bypass AI calls), HALF_OPEN (testing recovery). Implemented in `CircuitBreakerService`.

**CLOSED** (balloon state)
Terminal state. No further interaction possible. See *BalloonCloseReason* for why.

**Commons**
A shared social feed of user-generated content (tiles, moments, posts) visible to users within the same community spaces. Browsing Commons costs 1 energy per tile. Separate from the Moments deck.

**COMPLETE** (onboarding status)
Final ProfileStatus state. User becomes visible in candidate pools once this state is reached.

**CONNECTED** (match state)
Terminal positive state. Both users chose CONTINUE during the trial period. FindLoveAt is set. Full profile access granted.

**CONTINUE**
A trial decision value. If both users submit CONTINUE within 1 minute of popping the balloon, the match moves to CONNECTED and Find Love unlocks immediately (FindLoveAt = now).

**CONVERSATION_FIT**
Deck slot targeting users with high conversation/commons signal compatibility. 1 of 5 daily deck cards.

**CORE_FIT**
Deck slots (2 of 5) targeting top intent + foundational scorers — the users most compatible on what users are looking for and core values.

---

## D

**Daily Deck**
The set of up to 5 candidate cards shown to a user each day in the Moments tab. Composed by `DailyDeckOrchestrator`. Composition: 2 CORE_FIT + 1 LIFESTYLE_FIT + 1 CONVERSATION_FIT + 1 EXPLORER.

**DailyDeckOrchestrator**
Backend service responsible for the full 4-stage retrieval pipeline: pool filtering → scoring → deck assembly → match explanation generation.

**Dealbreaker**
A hard-stop veto in the matching algorithm. If user A has listed something as a dealbreaker that matches user B's profile, the match score is heavily penalized.

**DeliveryBoostService**
Applies score multipliers to correct for unequal deck exposure: new users and under-exposed users get temporary boosts.

**Dynamic Intake**
AI-generated follow-up questions based on a user's existing answers. Deepens the UserVector without a fixed question set.

---

## E

**EDGE Match**
A match created when two users chose different sides — one Magical (◈) and one Logical (◇). An edge owner is randomly assigned and gets full profile access immediately. The non-owner gets limited access (1 photo, no bio) until BothMessagedAt.

**Edge Owner**
The randomly assigned user in an EDGE match who gets full profile access immediately (stored as `edgeOwnerId` on the Match).

**EF Core**
Entity Framework Core — the ORM used to map C# entities to PostgreSQL tables.

**Embedding**
A high-dimensional vector representation of a piece of content (text, photo, voice). Used by the matching engine to compute similarity. Woven uses 1536-dimension embeddings from `text-embedding-3-small`.

**END**
A trial decision value. If either user submits END (or either times out), the match closes as UNMATCH.

**Energy Meter**
Daily cap of 100 tile interactions in the Commons/Tiles feed. Resets midnight UTC. Separate from the interaction budget.

**ENDED** (match state)
Terminal negative state. Match terminated via unmatch, block, expire, or trial failure.

**EXPIRED** (moment choice state)
A Moment theme that passed without the user making a choice.

**EXPLORER**
Deck slot (1 of 5) that picks a random candidate for diversity — intentionally outside the top scoring candidates.

---

## F

**Fail-Open**
Woven's Redis rate-limit strategy: if Redis is unavailable, all rate limit checks return `true` (allow). This prevents Redis outages from blocking users.

**Find Love**
The final stage of a connection. Unlocks 5 minutes after BothMessagedAt (or immediately if trial passes). Reveals an AI-generated date idea and enables date coordination UI.

**FindLoveAt**
Timestamp on a Match indicating when Find Love unlocks. Set to `BothMessagedAt + 5 minutes`, or `now` when trial passes.

**Foundational Questions**
Deep-compatibility questions answered during onboarding. Three expiry tiers: 60 days (core values), 45 days (lifestyle), 15 days (current state). Expired answers reduce that component's weight until re-answered.

---

## G

**Game Session**
A structured mini-game between two connected users (Know Me or Red/Green Flag). States: PENDING → ACTIVE → COMPLETED (or REJECTED / ABANDONED).

**Ghost Score**
A score measuring how often a user appears in decks but never responds. Calculated after 5+ balloon matches. High ghost score reduces frequency of appearance in others' decks. Maintained by `GhostScoreWorker`.

**Google OAuth**
The only authentication method in Woven. Users sign in with their Google account; the backend verifies the Google ID token and issues a JWT.

**gpt-4.1-mini**
OpenAI model used for all text completions in Woven (match explanations, game agent, nudge generation, insights).

---

## H

**HKDF**
HMAC-based Key Derivation Function. Used to derive purpose-specific encryption sub-keys from a master key, so each column/purpose gets a unique key.

**HMAC-SHA256**
Hash-based message authentication code. Used to sign all SignalR server→client events, preventing XSS-based event spoofing.

**HNSW**
Hierarchical Navigable Small World — the pgvector index type used for all vector similarity searches in Woven. Chosen over IVFFlat for better query performance and graceful handling of new insertions without requiring periodic rebuilds.

---

## I

**IVFFlat**
An alternative pgvector index type (Inverted File with Flat quantization). Not used in Woven — HNSW was chosen for better performance.

**Interaction Budget**
See *Budget*.

**InteractionBudgetService**
Backend service managing daily budget tracking, spending, and reset logic.

---

## J

**JWT**
JSON Web Token. Issued by the backend after Google OAuth verification. 60-minute expiry. Stored in frontend `localStorage`. All API calls include it as `Authorization: Bearer <token>`.

---

## K

**Know Me**
One of two available games. A round-based guessing game where users answer questions about themselves and guess each other's answers.

---

## L

**LIFESTYLE_FIT**
Deck slot (1 of 5) targeting users with strong lifestyle and weekly pulse alignment.

**LIMITED** (access level)
Restricted profile view: 1 photo, no bio. Applied to EDGE match non-owners until BothMessagedAt is set.

**Logical (◇)**
Product label for the "head leads" Moment choice. Stored in DB as `NO`. API value: `"LOGICAL"`. Costs 1 interaction budget.

---

## M

**Magical (◈)**
Product label for the "heart leads" Moment choice. Stored in DB as `YES`. API value: `"MAGICAL"`. Costs 1 interaction budget.

**Match**
The database record representing a connection between two users. Created when both make a Magical or Logical choice on the same Moment. Contains state, type, balloon info, trial info, and decisions.

**MatchScoringService**
Backend service computing compatibility scores between two UserVectors across 14 components.

**MATCHED** (match state)
State when both users have made choices and the balloon is ACTIVE. Precursor to TRIAL.

**Matchmaking Pipeline**
4-stage process: (1) candidate pool filtering, (2) vector similarity retrieval (HNSW), (3) 14-component scoring, (4) deck assembly + explanation generation.

**MomentResponse**
Database entity recording a user's choice on a Moment theme. `Choice` enum: YES (Magical ◈), NO (Logical ◇), PENDING (Save ⏳).

**Moments**
The core daily matching mechanic. Users see up to 5 candidate cards per day and choose Magical (◈), Logical (◇), or Save (⏳) for each.

**MomentsMatchService**
Backend service handling choice recording, budget deduction, and balloon/match creation logic.

---

## N

**Nudge**
An AI-generated prompt suggesting what two connected users could talk about. Generated by `NudgeGenerationService` based on their shared interests.

---

## O

**Onboarding**
Multi-step profile creation flow. Steps: STARTED → PHOTOS_ADDED → BIO_ADDED → INTENT_SET → PREFERENCES_SET → FOUNDATIONAL_DONE → COMPLETE. User is excluded from all candidate pools until COMPLETE.

**Orbit**
A way to follow another user's public activity in Commons. Rate-limited to 50 Orbit actions per day (separate from interaction budget).

---

## P

**PENDING** (match state)
One user has made a choice; waiting for the other. If the other user also chooses Magical or Logical, a match is created.

**PENDING** (MomentChoice enum)
Dual use: (1) no row exists → "not yet decided"; (2) row with PENDING → Save (⏳ Hold). A user chose to save the card to their pending queue.

**pgvector**
PostgreSQL extension enabling vector storage and similarity search. All user embeddings are stored as `vector(1536)` columns with HNSW indexes.

**Pillar Embedding**
A 1536-dim embedding of a user's foundational answers (values, dealbreakers, lifestyle). Used in the HNSW similarity search stage of matching.

**Pop**
The act of triggering the trial period on an ACTIVE balloon. Either user can pop. Closes the balloon (reason: POP) and starts a 1-minute trial window.

**ProfileStatus**
The onboarding progress enum: STARTED, PHOTOS_ADDED, BIO_ADDED, INTENT_SET, PREFERENCES_SET, FOUNDATIONAL_DONE, COMPLETE.

**PURE Match**
A match where both users chose the same side — both Magical (◈) or both Logical (◇). Both users get full profile access immediately. "You both felt the same vibe."

**Pulse**
A weekly vibe check-in (battery level, tone, role preference). Affects the Pulse layer (15% weight) of the matching algorithm. States: UNANSWERED → ANSWERED → LOCKED.

---

## R

**Rating**
A -100 to +100 score submitted by users after interactions. Only displayed when a user has received ≥ 5 ratings. Affects matching visibility.

**Red/Green Flag**
One of two available games. Users label each other's habits/preferences as red or green flags and compare results.

**Replicate**
External AI API used for photo and voice processing (photo embeddings, voice energy analysis). Falls back gracefully on circuit breaker OPEN.

---

## S

**SAS Token**
Shared Access Signature. The pattern used for media uploads: the backend generates a time-limited SAS token; the client uploads directly to Azure Blob Storage without the backend handling binary data.

**Save (⏳ Hold)**
Product label for the Hold choice on a Moment. API value: `"PENDING"`. Goes to the pending queue. Costs 1 interaction budget but uses the separate Save cap (2/day).

**Season**
A 21-day platform-wide event with a theme. All active users participate automatically. Season scores accumulate from activity and are displayed on profiles during the active season.

**SignalR**
Real-time communication library used for server→client push events in Woven. Events are signed with HMAC-SHA256. Client authenticates via JWT in querystring.

**SKIPPED** (moment choice state)
User explicitly skipped a card. No budget cost; that card won't appear again.

**SpeechBrain**
Library used for selfie liveness detection in verification. Stubbed in development; active in production.

**SUSPENDED** (user status)
Admin-applied temporary hold pending review. Can be reinstated to ACTIVE or escalated to BANNED.

---

## T

**text-embedding-3-small**
OpenAI embedding model producing 1536-dimension vectors. Used for all text embeddings in Woven.

**Tiles**
User-generated content cards in the Commons feed. Each tile view costs 1 energy.

**Trial Period**
A 1-minute window after a balloon is popped. Both users decide CONTINUE or END. Both CONTINUE → match becomes CONNECTED (FindLoveAt = now). Either END → match closes as UNMATCH.

**TrustScore**
A 0.0–1.0 score computed nightly by `TrustScoreWorker`. Users with TrustScore < 0.25 are excluded from all candidate pools.

---

## U

**Under-Exposure Boost**
A score multiplier applied by `DeliveryBoostService` to users who appear in fewer decks than average. Prevents a few popular users from monopolizing everyone's deck.

**UNMATCH**
Closing a balloon or match intentionally. No trial period. Recorded with reason UNMATCH.

**UserIntent**
Database entity storing a user's primary intent (serious, casual, friendship, unsure) and timeline (now, soon, someday).

**UserPreference**
Database entity storing filter preferences: ageMin, ageMax, genders[], maxDistance.

**UserProfile**
Database entity storing profile data: firstName, lastName, bio, birthDate, city, state, photos.

**UserVector**
The multi-layer representation of a user used for matching. Layers: Intent (0.30), Foundational (0.35), Lifestyle (0.20), Pulse (0.15). Built by `UserVectorBuilder`.

**UserVectorBuilder**
Backend service that constructs a UserVector from a user's onboarding answers, foundational Q responses, and weekly pulse.

---

## V

**Verification**
Selfie-based photo verification. States: UNVERIFIED → PENDING_REVIEW → VERIFIED or REJECTED. Verified badge displayed on profile. Expires after 180 days.

**Visual Preference Learning**
System that learns which photo aesthetics a user responds to. Requires ≥ 10 decisions before activating. Weight: 0.06. Updated weekly.

---

## W

**WeightLearningService**
Weekly gradient descent service (runs Sunday 04:00 UTC) that adjusts the 14 scoring component weights based on Pearson correlation with connection outcomes. Weights clamped to [0.01, 0.50].

**Weekly Pulse**
See *Pulse*.

---

## Y / N

**YES / NO** (internal)
Internal DB enum values for MomentChoice. `YES` = Magical (◈). `NO` = Logical (◇). These are never shown to users — the product language is Magical/Logical. The API accepts `"MAGICAL"`/`"LOGICAL"` and maps to YES/NO.

---

*Last updated: 2026-05-18*
