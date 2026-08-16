# Engineer Onboarding

## What Woven is

Woven is a dating app built around intentionality and behavioral matching. Users receive a curated daily deck of candidate cards (called the **Deck**), post content tiles to a shared feed (**Commons**), and are matched through **ECHO** — a machine-learning pipeline that learns from behavioral signals (what users actually do) rather than stated preferences (what users say they want). The app never surfaces raw compatibility scores, match ratings, or AI confidence values to users. Every AI surface is ambient UX — it shapes the experience invisibly.

---

## Four services you will touch most

### `MatchScoringService`
Scores a (viewer, candidate) pair across 16 components using weighted factors. These weights are seeded from `appsettings.json` (the `Echo` section) and updated weekly by `WeightLearningBatchWorker`. This is the entry point for any change to how matches are ranked.

### `MatchExplanationService`
Generates the human-readable explanation shown on a match card — the tone, the date style hints, and the 3 date idea cards that appear at the Find Love stage. Works via `gpt-4.1-mini`. Do not generate explanations inline in endpoints; always go through this service.

### `ChatEndpoints`
The Minimal API route group for all chat operations: send message, send voice note, voice-listened confirmation, trial decisions (CONTINUE/END/BLOCK), and the Find Love date idea selection. Every action in this file that involves two matched users touching each other's data is a behavioral signal — check whether it needs a `RecordAsync` call before editing.

### `MomentsEndpoints`
Handles deck delivery (`GET /moments`), card response (`POST /moments/respond`), and choose + note submission (`POST /moments/choose`). This is where match creation fires. The Drawn tab (mutual-like queue) also routes through here with the `'liked-you'` deck type. Spark deductions for Drawn responses happen here.

---

## The ECHO signal pipeline — read this before touching matchmaking

ECHO is the matching AI. It learns from what users do, not what they say. Here is the full data flow:

1. **Signal ingestion (continuous):** Every meaningful user action writes a row to `MatchSignalLogs` via `IMatchSignalService.RecordAsync(viewerId, candidateId, eventType, eventValue, metadataJson, ct)`. This is append-only — rows are never updated or deleted.

2. **ConnectionScoreBatchWorker (runs 03:50 UTC daily):** Reads `MatchSignalLogs` and aggregates a composite `ConnectionScore` for each (viewer, candidate) pair using a weighted sum of 7 signal types:

   | Signal | Weight |
   |---|---|
   | BalloonPopped | 0.05 |
   | TrialRequested | 0.10 |
   | TrialAccepted | 0.25 |
   | ConversationDepth | 0.20 |
   | DateAccepted | 0.15 |
   | ExplicitFeedback | 0.15 |
   | LoveReactions | 0.10 |

3. **WeightLearningBatchWorker (runs Sunday 04:00 UTC):** Runs logistic regression on the accumulated ConnectionScores, then writes updated per-user scoring weights to `UserMatchingWeights`. The deck a user sees next week is shaped by this week's behavior.

4. **DailyDeckOrchestrator:** Generates each user's daily deck using the latest learned weights, filtered through candidate pool logic and delivery boost rules.

5. **BehavioralFingerprintService:** Builds a 16-dimensional fingerprint from a 180-day signal window. Used in similarity scoring between users' behavioral patterns.

6. **DeliveryBoostService:** Applies a 12-step boost pipeline to deck ordering (recency, activity, photo completeness, etc.) before the deck is served.

If you add a new user action that should feed ECHO, you must: (a) add a constant to `MatchSignalEventTypes`, (b) call `RecordAsync` in the relevant endpoint or service, (c) document the signal. Skipping `RecordAsync` is a data integrity bug — ECHO silently never learns from that event.

---

## How the trial period works

The trial is a 3-minute window that starts when two matched users are both in the chat thread. Understanding this is essential before touching `ChatEndpoints` or `MatchesEndpoints`.

1. Match is created → both users are notified.
2. User A opens the chat thread → `TrialUserAOpenedAt` is set on the `Match` record.
3. User B opens the chat thread → `TrialUserBOpenedAt` is set → `TrialEndsAt = now + 3 minutes`.
4. During the trial window, users can exchange messages and voice notes.
5. Either user can make a trial decision:
   - **CONTINUE** — marks the trial as continued, match stays open.
   - **END** — closes the match. Requires an end reason: `no_spark`, `wrong_timing`, or `not_my_type`. This reason is stored as `TrialEndReason` and feeds ECHO. If neither user sent a message, a ghost refund (0.5 sparks) is applied.
   - **BLOCK** — immediately closes the match and creates a `Block` record. No ghost refund logic applies.
6. If the trial expires without a CONTINUE decision, an auto-close fires and ghost refund logic runs.

The trial is tracked on the `Match` entity: `IsTrial`, `TrialEndsAt`, `TrialUserAOpenedAt`, `TrialUserBOpenedAt`, `TrialEndReason`.

---

## What behavioral signals are and why they cannot be skipped

A behavioral signal is a timestamped record of one user's action in the context of another user. Examples: a voice note played to completion, a trial decision of CONTINUE, a date idea accepted, a conversation that reached 20 messages.

These signals are the training data for ECHO's weight learning. Without them:
- `ConnectionScoreBatchWorker` has no data to aggregate.
- `WeightLearningBatchWorker` has no scores to regress on.
- The deck degrades to unlearned default weights.
- Two users' compatibility can never improve beyond initial scoring.

Every behavioral event that involves a (viewer, candidate) pair must call `IMatchSignalService.RecordAsync`. The `MatchSignalLogs` table is append-only — it is a ledger, not a state store.

---

## The invisible AI principle

Woven's design rule is that AI is infrastructure, not a feature. This means:

- Compatibility scores are never shown to users.
- Match explanations are human-language prose — they do not say "87% compatible."
- ECHO's weights and fingerprints are never surfaced in the UI.
- Community signal aggregations (orbit gravity, flag scores) are platform-internal only.

Any PR that renders an AI confidence value, a score, or a rating to a user will be requested changes. If you're unsure whether something crosses this line, look at how `MatchExplanationService` structures its output — prose only, no numbers.

---

## Feature vocabulary

Use these names consistently across code, comments, and PRs. Inconsistent naming is a review comment.

| Feature | UI name | Internal identifier |
|---|---|---|
| Daily discovery tab | **Deck** | `'today'` |
| Mutual-like tab | **Drawn** | `'liked-you'` |
| ◈ choice | **Magical** | `MAGICAL` |
| ◇ choice | **Resonant** | `LOGICAL` |
| Trial period | **Trial** | `IsTrial`, `TrialEndsAt` |
| Content feed | **Commons** | — |
| Content post | **Tile** | — |
| Explicit ◈ on a tile | **Orbit** | `OrbitGravity` |
| Matching AI pipeline | **ECHO** | — |
| Match connection window | **Balloon** | `BalloonState` |
| Final unlock stage | **Find Love** | `findLoveAt` |

---

## Where to go from here

- `docs/contributing/LOCAL_SETUP.md` — get your local stack running
- `docs/contributing/CONTRIBUTING.md` — code conventions, PR workflow, design rules
- `docs/signals/SIGNALS_VECTORS_SCORING.md` — full signal inventory and scoring formulas
- `docs/technical/BACKEND_DESIGN.md` — full backend architecture
- `docs/flowcharts/FLOWCHARTS.md` — visual walkthroughs of all major flows
