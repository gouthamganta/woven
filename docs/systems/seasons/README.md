# Seasons System

**Purpose:** Time-boxed introspective prompts that keep user profiles fresh and drive re-engagement

**Status:** ✅ Active (Phase 3B)

---

## Overview

Seasons are 21-day cycles where all active users are prompted to reflect on the same question. Responses update their AI profile embeddings, improving match quality over time and creating natural touchpoints for re-engagement.

Unlike one-time onboarding questions, Seasons capture evolution: what mattered last month vs. this month, how priorities shift, what someone's learning about themselves.

---

## Core Mechanics

### Lifecycle

| Phase | What Happens |
|---|---|
| **Creation** | SeasonTransitionWorker runs daily at 01:00 UTC. If current season expired (or none exists), creates new Season with AI-generated prompt |
| **Active** | 21-day window. Users can submit/update responses anytime during this period |
| **Submission** | User answers all 8 pillar questions. Backend invalidates cached embeddings + triggers re-embedding (fire-and-forget) |
| **Transition** | At season end, new season starts next day. All COMPLETE users notified via SignalR |

### Data Model

**Season** (`seasons` table):
- `SeasonNumber` — monotonic counter (1, 2, 3...)
- `StartDate` / `EndDate` — 21-day window
- `PromptText` — introspective question (max 200 chars), AI-generated
- Created via `SeasonTransitionWorker` at 01:00 UTC

**UserSeasonResponse** (`user_season_responses` table):
- `UserId` + `SeasonId` + `PillarId` (unique key)
- `QuestionId` — which foundational question was answered
- `Response` — free-text answer
- `RespondedAt` — timestamp
- Upsert semantics: re-answering updates existing response

### The 8 Pillars

Every season, users answer the same seasonal prompt through 8 lenses:

1. **Lifestyle** — daily rhythms, habits, structure
2. **Energy** — social patterns, recharge needs
3. **Communication** — expression, conflict, conversation
4. **Affection** — physical touch, intimacy, closeness
5. **Stability** — commitment, planning, change tolerance
6. **Values** — ethics, spirituality, worldview
7. **Curiosity** — learning, growth, exploration
8. **Emotional Rhythm** — feelings, attachment, vulnerability

Each response → `UserSeasonResponse` row → re-embedded → updated match scores.

---

## Why It Works

### For Users
- **Fresh context** — "What I need this month" vs. stale onboarding answers
- **Re-engagement hook** — built-in reason to return every 3 weeks
- **Self-reflection** — structured introspection (dating app as journaling tool)
- **Invisible AI** — no explicit "update profile" CTA, just natural questions

### For ECHO
- **Temporal signals** — capture change over time (stability seekers → explorers, etc.)
- **Recency weighting** — latest season responses matter most
- **Embedding drift detection** — if vector shifts significantly, personality likely evolved
- **Behavioral validation** — do stated changes match actual match choices?

### For Product
- **Retention mechanism** — 21-day cycle = 17 annual touchpoints
- **Content flywheel** — AI-generated prompts → no editorial bottleneck
- **Quality filter** — active responders signal serious intent
- **Data richness** — 8 answers per user per season = dense preference graph

---

## Integration Points

### Embeddings Pipeline
When a user submits season responses:
1. `SeasonService.SubmitSeasonResponsesAsync()` saves responses
2. Fire-and-forget task invalidates `CacheKeys.PillarEmbedding(userId)`
3. Calls `IUserVectorBuilder.BuildAndSaveV1Async(userId)` to re-embed
4. New embeddings flow into next day's `DailyDeckOrchestrator` run

### Notifications
- **SeasonResponseSubmitted** — SignalR event on submission (confirms save)
- **NewSeasonStarted** — SignalR event to all COMPLETE users when new season begins
- Both are fire-and-forget (failures logged, never block main flow)

### Analytics
- `AnalyticsEvents.SeasonResponseSubmitted` — tracks completion rate, seasonNumber
- Dashboard shows 30-day season response count (see `AdminAnalyticsEndpoints.cs`)

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| User submits twice in one season | **Upsert** — latest response overwrites previous |
| User skips a season | No penalty. Next season is available when it starts |
| Season expires while user is answering | Submit returns `NO_ACTIVE_SEASON` error (must wait for next) |
| Worker fails to generate prompt | Fallback: "What matters most to you in a connection right now?" |
| Re-embedding fails post-submit | Logged as warning, user not notified (best-effort) |
| SignalR notification fails | Logged, never blocks save |

---

## Future Enhancements

### Under Consideration
- **Season leaderboard** — show % of users who responded (social proof)
- **Seasonal insights** — "Your Energy pillar shifted 20% this season" (optional visibility)
- **Multi-question seasons** — instead of one prompt → 8 answers, allow 2-3 distinct questions
- **Seasonal match boost** — prioritize people who answered same season (temporal alignment signal)

### Not Planned
- ❌ **Mandatory seasons** — skipping is allowed (no FOMO pressure)
- ❌ **Season history browsing** — no "see what you answered in Season 3" (privacy)
- ❌ **Public season responses** — always private, never shown to matches

---

## Related Systems

- **Onboarding** — initial pillar answers (Foundational Questions)
- **Weekly Pulses** — lighter-weight check-ins (future Phase 3C feature)
- **User Vectors** — pillar embeddings built from season responses
- **Daily Deck** — uses updated embeddings to improve match relevance

---

## Files

| File | Purpose |
|---|---|
| `data/Entities/Season.cs` | Season entity definition |
| `data/Entities/UserSeasonResponse.cs` | User response entity |
| `Services/Seasons/ISeasonService.cs` | Service interface + DTOs |
| `Services/Seasons/SeasonService.cs` | Business logic (get current, submit) |
| `Services/Seasons/SeasonTransitionWorker.cs` | Background worker (creates seasons) |
| `Endpoints/SeasonEndpoints.cs` | HTTP API (GET current, PUT responses) |

**See:**
- [implementation.md](./implementation.md) — service internals
- [configuration.md](./configuration.md) — season lifecycle + worker schedule
- [api.md](./api.md) — endpoint contracts

---

**Last Updated:** 2026-10-07
