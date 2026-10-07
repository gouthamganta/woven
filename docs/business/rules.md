# Business Rules

**Last Updated:** 2026-10-07

This document defines Woven's core business rules — constraints, validations, and behavioral policies that govern the app.

---

## Match Rules

### Mutual Like Required
**Rule:** A match is created ONLY when both users choose the same or compatible responses.

**Compatible combinations:**
- ◈ + ◈ → PURE match (`edge_owner_id = null`)
- ◇ + ◇ → PURE match (`edge_owner_id = null`)
- ◈ + ◇ → EDGE match (`edge_owner_id` set to the user who got their choice)

**Non-compatible:**
- SKIP → no match (silent rejection)

**Enforcement:** [MomentsEndpoints.cs](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

### No Self-Matches
**Rule:** Users cannot match with themselves.

**Enforcement:** Candidate pool excludes own user ID (SQL WHERE clause in `CandidatePoolService`)

---

### No Duplicate Matches
**Rule:** Once a match exists between User A and User B, no second match can be created.

**Enforcement:** Unique index on `matches.(user_a_id, user_b_id)` (database constraint)

---

### No Repeat Swipes
**Rule:** Once a user responds to a candidate (MAGICAL/LOGICAL/SKIP), they cannot respond again.

**Enforcement:**  
- Unique index on `moment_responses.(user_id, candidate_id)`
- API check before creating `MomentResponse`

**Evidence:** [MomentsEndpoints.cs:RespondToMoment](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs)

---

## Balloon Rules

### Balloon Lifetime
**Rule:** Balloon window is 72 hours from match creation.

**Calculation:**
```csharp
match.BalloonExpiresAt = match.CreatedAt.AddHours(72);
```

**Enforcement:** `BalloonExpiryWorker` scans every 60s, expires balloons past `BalloonExpiresAt`

---

### Balloon Pop (Immutable)
**Rule:** Once balloon state = `CLOSED`, it cannot reopen.

**Evidence:** `BalloonState` is immutable after close (no UPDATE back to `ACTIVE`)

---

### One Pop Per Match
**Rule:** Balloon can be popped by either user, but only once.

**Enforcement:**  
- Check `balloon_state = ACTIVE` before allowing pop
- After pop → set `balloon_state = CLOSED`
- If already `CLOSED` → reject with 400 Bad Request

**Evidence:** [MatchesEndpoints.cs:PopBalloon](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

## Trial Period Rules

### Trial Starts When Both Users Open Chat
**Rule:** Trial begins only when BOTH `TrialUserAOpenedAt` and `TrialUserBOpenedAt` are non-null.

**Calculation:**
```csharp
if (match.TrialUserAOpenedAt != null && match.TrialUserBOpenedAt != null && match.TrialEndsAt == null)
{
    match.TrialEndsAt = DateTime.UtcNow.AddMinutes(3);
}
```

**Enforcement:** [ChatEndpoints.cs:GetThread](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

### Trial Length
**Rule:** Trial period is 3 minutes.

**Evidence:** `TrialEndsAt = now + 3 minutes`

---

### Trial Decision Required
**Rule:** After trial expires, both users must make a decision (CONTINUE/END/BLOCK).

**UI Flow:**
1. Trial timer expires
2. Modal shown: "Continue chatting with [Name]?"
3. User chooses: CONTINUE, END (with reason), or BLOCK

**Backend enforcement:** None (UI-driven), but `TrialEndsAt` timestamp triggers frontend modal.

---

### Unilateral Block
**Rule:** Either user can BLOCK at any time (during trial or after).

**Effect:**
- Match immediately closed (`EndedAt = now`)
- Block record created (`blocker_id`, `blocked_id`)
- Chat thread closed (no further messages)
- Users never see each other in future decks

**Enforcement:** [ChatEndpoints.cs:TrialDecision](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs)

---

## Spark Economy Rules

### Daily Refill
**Rule:** Users receive 5 sparks/day at midnight UTC.

**Wallet cap:** 10 sparks max (excess discarded)

**Enforcement:** `SparkRefillWorker` (scheduled daily 00:00 UTC) — **not yet implemented, manual refills only**

---

### Spark Costs
**Rule:** Actions in Drawn tab cost sparks.

| Action | Cost |
|--------|------|
| Pop balloon (from Drawn) | 1 spark |
| Send first message (if balloon not popped) | 1 spark |

**Evidence:** [MomentsEndpoints.cs](../../backend/WovenBackend/Endpoints/MomentsEndpoints.cs), [MatchesEndpoints.cs](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs)

---

### Ghost Refund
**Rule:** If match ends with 0 messages sent, 0.5 sparks refunded to both users.

**Enforcement:** All 3 unmatch paths check message count → refund if zero.

**Evidence:**  
- [ChatEndpoints.cs:TrialDecision](../../backend/WovenBackend/Endpoints/ChatEndpoints.cs) (END path)
- [MatchesEndpoints.cs:CloseMatch](../../backend/WovenBackend/Endpoints/MatchesEndpoints.cs) (BLOCK path)
- `BalloonExpiryWorker` (EXPIRE path)

---

### Insufficient Sparks
**Rule:** If user has 0 sparks, Drawn actions are blocked.

**UI:** Show "Out of sparks" message, disable pop/message buttons.

**Backend:** Endpoint returns 400 Bad Request if `SparkWallet.balance < 1`

---

## Daily Deck Rules

### One Deck Per Day
**Rule:** Users get one deck per day (refreshed at 02:00 UTC).

**Enforcement:**  
- `DeckGenerationWorker` runs daily 02:00 UTC
- Unique index on `daily_decks.(user_id, generated_at)` (date only, not timestamp)

---

### Deck Size
**Rule:** Each deck contains 60 candidates (3 buckets × 20).

**Buckets:**
- **AFFINITY** — 20 candidates (top by personalized score)
- **SPARK** — 20 candidates (new users prioritized)
- **EXPLORER** — 20 candidates (diversity/serendipity)

**Enforcement:** [DeckSelectionService.cs](../../backend/WovenBackend/Services/Matchmaking/DeckSelectionService.cs)

---

### No Already-Seen Candidates
**Rule:** Candidates shown in previous decks are excluded from today's deck.

**Enforcement:** `CandidateExposure` table tracks `(user_id, candidate_id, shown_at)` → SQL WHERE clause excludes them.

**Evidence:** [CandidatePoolService.cs](../../backend/WovenBackend/Services/Matchmaking/CandidatePoolService.cs)

---

## Candidate Pool Rules

### Gender Reciprocity
**Rule:** User A sees User B ONLY IF:
- A seeks B's gender
- B seeks A's gender

**Example:**  
- Alice seeks "Men"
- Bob seeks "Women"
- Alice sees Bob ✅, Bob sees Alice ✅

**Example (non-reciprocal):**  
- Alice seeks "Men"
- Bob seeks "Men"
- Alice sees Bob ✅, Bob does NOT see Alice ❌

**Enforcement:** SQL filter in `CandidatePoolService` (reciprocity check before adding to pool)

**Evidence:** [CandidatePoolService.cs:BuildPoolAsync](../../backend/WovenBackend/Services/Matchmaking/CandidatePoolService.cs)

---

### Trust Filtering
**Rule:** Users with low trust scores (<0.5) are excluded from candidate pools.

**Enforcement:** SQL WHERE clause: `trust_score >= 0.5`

**Trust score calculation:** (not yet implemented — placeholder for future)

---

### Age Preferences
**Rule:** User A sees User B ONLY IF B's age is within A's `age_min`–`age_max` range.

**Enforcement:** SQL WHERE clause in candidate pool query.

---

### Distance Preferences
**Rule:** (Future) User A sees User B ONLY IF distance <= A's `max_distance_km`.

**Enforcement:** Not yet implemented (no location data collected).

---

## Chat Rules

### Message Order
**Rule:** Messages are ordered chronologically (`created_at ASC`).

**Enforcement:** SQL `ORDER BY created_at ASC` in `ChatEndpoints.cs:GetMessages`

---

### Voice Note Max Duration
**Rule:** Voice notes limited to 60 seconds.

**Enforcement:** Frontend MediaRecorder stops at 60s, backend validates `durationSecs <= 60`

---

### No Editing/Deleting Messages
**Rule:** Once sent, messages cannot be edited or deleted.

**Rationale:** Preserve context for behavioral signals (ECHO learning).

**Future exception:** Allow delete for PII/safety (with audit log).

---

## Content Moderation Rules

### Tile Expiry
**Rule:** Tiles expire after 7 days (default) or custom `expires_at` timestamp.

**Enforcement:** `TileExpiryWorker` scans every 60s, soft-deletes expired tiles.

---

### Moderation Queue
**Rule:** All tiles go through moderation queue before being visible.

**Enforcement:** `ModerationQueueWorker` processes queue every 30s.

**Statuses:**
- `PENDING` → awaiting review
- `APPROVED` → visible to users
- `REJECTED` → hidden, author notified

---

### Report Threshold
**Rule:** If a tile receives 5+ reports, it's auto-flagged for priority review.

**Enforcement:** `TileReport` count trigger (future automation — currently manual review only).

---

## Onboarding Rules

### Minimum Photos
**Rule:** Users must upload at least 3 photos to complete onboarding.

**Enforcement:** [OnboardingEndpoints.cs:CompleteOnboarding](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs) checks `user_photos` count >= 3.

---

### Foundational Questions Required
**Rule:** All 8 foundational questions must be answered.

**Enforcement:** Onboarding completion checks `UserFoundationalV1` exists + all pillars non-null.

---

### Profile Status Progression
**Rule:** User cannot access main app until `profile_status = COMPLETE`.

**Enforcement:** `AuthGuard` (frontend) + endpoint checks (backend) reject `INCOMPLETE` users.

**Progression:**
1. Signup → `INCOMPLETE`
2. Upload photos → still `INCOMPLETE`
3. Answer foundational → still `INCOMPLETE`
4. Complete onboarding → `COMPLETE`

---

## Rate Limiting Rules

### Global User Rate Limit
**Rule:** 120 requests per 60 seconds per user (sliding window).

**Enforcement:** ASP.NET Core rate limiter (`"user"` policy)

**Evidence:** [Program.cs:RateLimiter](../../backend/WovenBackend/Program.cs)

---

### AI-Heavy Endpoints
**Rule:** 10 requests per 60 seconds per user (token bucket).

**Applied to:**
- `POST /moments/respond` (deck generation)
- `POST /games/sessions/{id}/submit` (game AI)
- Match explanation generation (internal)

**Enforcement:** `RequireRateLimiting("ai-heavy")` on endpoints

---

### OpenAI Global Limit
**Rule:** 5 concurrent outbound OpenAI calls (globally, not per-user).

**Why:** Prevent quota exhaustion.

**Enforcement:** Concurrency limiter (`"openai-global"` policy)

---

## Privacy Rules

### No Age Display
**Rule:** Age is NEVER shown on Moments cards.

**UI:** Cards show name + badge + explanation only.

**Enforcement:** Frontend components exclude `age` field from display.

---

### No Community Ratings Shown
**Rule:** Community ratings (`UserRating`) are platform-only signals, never shown to users.

**Enforcement:** No API endpoint exposes `UserRating` to frontend.

---

### ChatNote Privacy
**Rule:** ChatNotes are NEVER shown to users (background signal only).

**Enforcement:** No API endpoint returns `ChatNote` data (platform-only table).

---

## Game Rules

### KnowMe Game
**Rule:** 10 questions, user guesses their match's answers.

**Scoring:**  
- 1 point per correct guess
- Alignment score = (correct / 10) × 100%

**Enforcement:** [GameEndpoints.cs:SubmitAnswer](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

### RedGreenFlag Game
**Rule:** Mutual flag voting on hypothetical scenarios.

**Outcome:**  
- Both vote RED → discussion prompt shown
- Both vote GREEN → continue
- Mixed votes → neutral (no action)

**Enforcement:** [GameEndpoints.cs](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

### Game Completion Bonus
**Rule:** (Future) Users who complete games get +0.5 sparks.

**Enforcement:** Not yet implemented.

---

## Security Rules

### JWT Expiry
**Rule:** JWT tokens expire after 7 days.

**Enforcement:** `JwtTokenService` sets `exp` claim = `now + 7 days`

**Evidence:** [JwtTokenService.cs](../../backend/WovenBackend/Auth/JwtTokenService.cs)

---

### Cookie Auth (HttpOnly)
**Rule:** JWT tokens stored in HttpOnly cookies (XSS-resistant).

**Enforcement:** `CookieAuthHelper.SetJwtCookie()` sets `httpOnly: true`

**Evidence:** [CookieAuthHelper.cs](../../backend/WovenBackend/Auth/CookieAuthHelper.cs)

---

### Idempotency Window
**Rule:** Idempotency keys are valid for 24 hours.

**Enforcement:** `IdempotencyRecord.created_at` + 24h = expiration (database cleanup via worker)

**Evidence:** [IdempotencyService.cs](../../backend/WovenBackend/Services/IdempotencyService.cs)

---

## Design Rules (from CLAUDE.md)

### No Hover Lift Animations
**Rule:** Hover effects = glow/shadow/color ONLY, never translateY lifts.

**Rationale:** Design philosophy (minimalism).

**Enforcement:** CSS code review.

---

### Background Drift Stays On
**Rule:** Never remove `woven-bg` background drift animation unless explicitly asked.

**Enforcement:** Code review + design approval.

---

### No Paywalls
**Rule:** Spark economy is the soft gate (daily limits), never hard paywall.

**Rationale:** Product philosophy (no pay-to-win).

**Enforcement:** No payment endpoints exist.

---

## References

- **Glossary:** [glossary.md](glossary.md)
- **State machines:** [state-machines.md](state-machines.md)
- **Product philosophy:** [product-philosophy.md](product-philosophy.md)
- **API docs:** [../api/](../api/)

---

**Last Updated:** 2026-10-07  
**Evidence:** [backend/WovenBackend/](../../backend/WovenBackend/), [CLAUDE.md](../../CLAUDE.md) hard design rules
