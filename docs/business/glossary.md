# Glossary — Feature Vocabulary

**Last Updated:** 2026-10-07

This glossary defines Woven's feature names, UI labels, and internal terminology. When the user-facing name differs from the internal ID, both are listed.

---

## Core Features

| User-Facing Term | Internal ID | Definition |
|------------------|-------------|------------|
| **Deck** | `'today'` | Daily discovery tab — curated list of 20 potential matches refreshed daily |
| **Drawn** | `'liked-you'` | Tab showing users who mutually liked each other (pending balloon pop) |
| **Moments** | — | Container feature: Deck + Drawn tabs |
| **Commons** | — | User-generated content feed (Tiles + Orbit interactions) |
| **You** | — | User's own profile section (settings, tiles, profile) |

---

## Match Types

| User-Facing Term | Internal ID | Definition |
|------------------|-------------|------------|
| **Magical** | `MAGICAL` | Choice represented by ◈ symbol (intuition/feeling) |
| **Resonant** | `LOGICAL` | Choice represented by ◇ symbol (logic/reason) |
| **PURE match** | `edge_owner_id = null` | Both users made the same choice (◈◈ or ◇◇) |
| **EDGE match** | `edge_owner_id != null` | Users made different choices (◈◇) — `edge_owner_id` points to winner |
| **Balloon** | `BalloonState` | 72-hour connection window after mutual like (before trial starts) |

---

## Balloon States

| Internal State | Definition |
|----------------|------------|
| `ACTIVE` | Balloon window open (72h) — either user can pop it |
| `CLOSED` | Balloon popped or expired (immutable, cannot reopen) |

---

## Trial Period

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Trial** | `IsTrial` | 3-minute connection window after both users open the chat |
| **Trial ends at** | `TrialEndsAt` | Timestamp when trial period expires |
| **Find Love** | `findLoveAt` | Final unlock stage (trial completed → full chat access) |

**Trial decisions:**
- `CONTINUE` — both users want to continue chatting (unlocks full chat)
- `END` — one or both users choose not to continue
- `BLOCK` — block user (prevents future matches)

**End reasons** (when `END` chosen):
- `no_spark` — Didn't feel a connection
- `wrong_timing` — Not the right time
- `not_my_type` — Preferences mismatch

---

## Sparks Economy

| User-Facing Term | Internal Field | Definition |
|------------------|-------------|------------|
| **Sparks** | `SparkWallet.balance` | In-app currency used for Drawn tab actions |
| **Daily refill** | — | 5 sparks/day (wallet max 10) |
| **Ghost refund** | — | 0.5 sparks back if match ends with no messages |

**Spark costs:**
- **Pop balloon** — 1 spark
- **Send first message in Drawn** — 1 spark (if not already popped)

---

## Content Tiles

| User-Facing Term | Internal Field | Definition |
|------------------|-------------|------------|
| **Tile** | `Tile` | User-generated content post (photo + caption) |
| **Orbit** | `TileOrbit` | ◈ interaction on a tile (similar to "like" but different semantics) |
| **Orbit Gravity** | `OrbitGravity` | Strength score based on dwell time + view count + recency |
| **Highlight** | `Highlight` | Featured tile (platform-selected) |

---

## ECHO Pipeline

| User-Facing Term | Internal ID | Definition |
|------------------|-------------|------------|
| **ECHO** | — | AI matchmaking pipeline (learns from behavioral signals) |
| **AI Profile** | — | 8-pillar embedding (Lifestyle, Energy, Communication, Affection, Stability, Values, Curiosity, Emotional Rhythm) |
| **Behavioral Fingerprint** | `UserBehavioralFingerprint` | 16-dim behavioral embedding (no OpenAI, platform-generated) |
| **Connection Score** | `ConnectionScore` | Aggregated match outcome score (success prediction) |

**Deck buckets:**
- `AFFINITY` — Top 20 by personalized score (cosine similarity + CF + ...) |
- `SPARK` — Top 20 by recency (new users prioritized)
- `EXPLORER` — Lowest foundational score in top-20 (diversity/serendipity)

---

## Chat Features

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Chat Thread** | `ChatThread` | Conversation between two matched users |
| **Voice Note** | `MessageType = VOICE` | Audio message (stored in Azure Blob) |
| **Love Reaction** | `MessageLoveReaction` | ❤️ reaction on a message |
| **Chat Note** | `ChatNote` | Background note (never shown to users, platform-only signal) |
| **Your Turn** | — | Indicator showing whose turn it is to respond (designed, not built) |

---

## Games

| User-Facing Term | Internal ID | Definition |
|------------------|-------------|------------|
| **KnowMe** | `KNOW_ME` | 10-question game: guess your match's answers |
| **RedGreenFlag** | `RED_GREEN_FLAG` | Mutual flag voting game |
| **Game Session** | `GameSession` | Instance of a game between two users |
| **Game Round** | `GameRound` | Single question within a game session |
| **Alignment Score** | `GameAnalytic.alignment_score` | Percentage of matching answers |

---

## Onboarding

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Foundational Questions** | `UserFoundationalV1` | 8 pillar questions (answered during onboarding) |
| **Dynamic Intake** | `UserDynamicIntakeSet` | Follow-up questions generated based on initial answers |
| **Weekly Vibe** | `UserWeeklyVibe` | Weekly status update (expires after 7 days) |
| **Optional Fields** | `UserOptionalField` | Extra profile fields (horoscope, etc.) — visibility controlled |

**Profile statuses:**
- `INCOMPLETE` — User hasn't finished onboarding
- `COMPLETE` — User completed onboarding (can use app)
- `SUSPENDED` — Account suspended (moderation action)

---

## Moderation & Trust

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Community Rating** | `UserRating` | Platform-only signal (never shown to users) |
| **Trust Score** | (calculated) | Composite score used for candidate filtering |
| **Block** | `Block` | User blocks another user (prevents future matches) |
| **Report** | `TileReport` | User reports a tile for violating guidelines |
| **Moderation Queue** | `ModerationQueue` | Content awaiting human review |

**Moderation statuses:**
- `PENDING` — Awaiting review
- `APPROVED` — Passed moderation
- `REJECTED` — Blocked

---

## Analytics & Insights

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Event** | `AnalyticsEvent` | User action tracking (page views, clicks, etc.) |
| **A/B Experiment** | `AbExperiment` | A/B test definition |
| **Variant** | `AbAssignment.variant` | User's assigned A/B variant |
| **Conversion** | `AbConversion` | Goal completion in A/B test |

---

## Seasons & Venues

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Season** | `Season` | Time-limited event/theme (e.g., "Summer Nights") |
| **Season Response** | `UserSeasonResponse` | User's response to seasonal prompt |
| **Venue** | (future) | Date location recommendation (not yet built) |

---

## Coaching & Feedback

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Coaching Summary** | `CoachingSummary` | Weekly AI-generated coaching summary |
| **Date Feedback** | `DateFeedback` | Post-date survey (did you go on a date? rating?) |
| **Availability Signal** | `ChatAvailabilitySignal` | Pre-date availability hints (e.g., "free this weekend") |

---

## Technical Terms

| User-Facing Term | Internal Field | Definition |
|------------------|----------------|------------|
| **Correlation ID** | `X-Correlation-ID` | 16-char hex ID for request tracing (every HTTP request) |
| **Idempotency Key** | `X-Idempotency-Key` | UUID for preventing duplicate mutations (24h TTL) |
| **Embedding** | `UserVector.embedding` | 1536-dim OpenAI text embedding |
| **pgvector** | — | PostgreSQL extension for vector similarity search |
| **HNSW** | — | Hierarchical Navigable Small World (vector index algorithm) |

---

## State Machines

See [state-machines.md](state-machines.md) for visual diagrams of:
- Balloon state transitions
- Trial period flow
- Match lifecycle
- Profile status progression

---

## Anti-Patterns (Don't Use These)

| ❌ Wrong Term | ✅ Correct Term | Why |
|--------------|----------------|-----|
| "Swipe right/left" | "Choose Magical/Resonant" | No swiping UX, we use ◈/◇ buttons |
| "Like" | "Orbit" (for tiles only) | "Like" implies social graph, we don't have that |
| "Compatibility score" | Never show | ECHO scores are invisible UX, never surfaced to users |
| "AI recommendation" | Never mention | ECHO is invisible, users don't know AI picked their deck |
| "Notification" | "Push notification" (specific) | Be precise: push vs in-app vs email |

---

## Abbreviations

| Abbreviation | Full Term |
|--------------|-----------|
| **ECHO** | Not an acronym — just the name of the matching AI |
| **CF** | Collaborative Filtering |
| **LinUCB** | Linear Upper Confidence Bound (bandit algorithm) |
| **HNSW** | Hierarchical Navigable Small World (vector index) |
| **TTL** | Time To Live (cache expiration) |
| **SAS** | Shared Access Signature (Azure Blob auth) |
| **VAPID** | Voluntary Application Server Identification (Web Push) |

---

## References

- **Product philosophy:** [product-philosophy.md](product-philosophy.md)
- **Business rules:** [rules.md](rules.md)
- **State machines:** [state-machines.md](state-machines.md)
- **API docs:** [../api/](../api/)

---

**Last Updated:** 2026-10-07  
**Evidence:** [CLAUDE.md](../../CLAUDE.md) feature vocabulary table, feature documentation in [docs/features/](../features/)
