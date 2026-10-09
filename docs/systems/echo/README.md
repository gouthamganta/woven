# ECHO — Woven's AI Matching System

**ECHO** is Woven's behavioral learning AI that powers matchmaking, coaching, and conversational games. Named after its core principle: it echoes back what you're not saying — learning from actions, not words.

---

## What ECHO Does

ECHO is not a feature. It's invisible infrastructure that:

- **Curates your daily deck** — 5 people/day selected from a pool of hundreds, ranked by 16 compatibility dimensions
- **Learns from 40+ behavioral signals** — time-to-first-message, trial decisions, voice exchanges, game depth, love reactions
- **Generates weekly coaching summaries** — warm, perceptive reflections on your week (no metrics, no comparisons)
- **Runs conversational games** — Know Me (guess their answers) and Red/Green Flag (judge statements together)
- **Explains why matches work** — 2-sentence explanations based on actual compatibility, not marketing speak
- **Personalizes over time** — per-user weight learning adjusts the 16-component formula based on your outcomes

---

## Core Philosophy

**ECHO never shows users raw scores, community ratings, or feedback they didn't request.**

Every AI surface is invisible UX:
- No "compatibility percentage" badges
- No "other users rated this profile 8.5/10"
- No "AI thinks you should message them first"
- No leaderboards, no gamification of human connection

What users see:
- **Moments**: A curated daily deck with 1-2 sentence explanations
- **Coaching**: A weekly private reflection (opt-in, never pushy)
- **Games**: Fun conversation starters that reveal compatibility
- **Match explanations**: "You both value depth over surface-level chat."

What they don't see:
- That you scored 87.3 on their profile
- That ECHO flagged low conversation risk
- That your behavioral lifestyle embedding is 0.94 similar
- That you're in the CORE_FIT bucket

ECHO's job is to **clarify choice, not make it for you.**

---

## How It Works (High-Level)

```
┌─────────────────────────────────────────────────────────────┐
│  1. USER JOINS → Foundational Questions                    │
│     ↓                                                       │
│  2. Answers → 9 Embedding Modalities (pillar, style, etc)  │
│     ↓                                                       │
│  3. Daily Deck Generation (03:00-05:00 UTC nightly)        │
│     - CandidatePoolService: who's eligible?                │
│     - MatchScoringService: score all via 16 components     │
│     - DeckSelectionService: pick top 5 with diversity      │
│     ↓                                                       │
│  4. USER INTERACTS → Behavioral Signals Logged             │
│     - BalloonPop, TrialAccepted, MessageSent, etc.         │
│     ↓                                                       │
│  5. Nightly Aggregation (03:50 UTC)                        │
│     - ConnectionScoreBatchWorker → composite [0,1] score   │
│     ↓                                                       │
│  6. Weekly Learning (Sunday 04:00 UTC)                     │
│     - WeightLearningService → personalized weights         │
│     ↓                                                       │
│  7. Coaching (Wednesday 18:00 UTC)                         │
│     - CoachingSummaryWorker → warm reflection              │
└─────────────────────────────────────────────────────────────┘
```

---

## Key Metrics

| Dimension | Value |
|---|---|
| **Behavioral signals** | 40+ event types |
| **Embedding modalities** | 9 (pillar, visual, voice, style, humor, lifestyle, emotional rhythm, attachment, behavioral lifestyle) |
| **Scoring components** | 16 (weighted, personalized per user) |
| **Daily deck size** | 5 candidates (2 CORE_FIT, 1 LIFESTYLE_FIT, 1 CONVERSATION_FIT, 1 EXPLORER) |
| **Learning threshold** | ≥10 ConnectionScore samples for weight personalization |
| **Trust gate** | TrustScore ≥ 0.25 to enter candidate pool |
| **Signal window** | 90 days (rolling) |

---

## Documentation Index

**Core Concepts:**
- [overview.md](./overview.md) — Architecture and philosophy
- [agents.md](./agents.md) — All ECHO agents (games, coaching, explanations)
- [coach.md](./coach.md) — Weekly coaching system
- [matching-pipeline.md](./matching-pipeline.md) — Daily deck orchestration

**Data & Learning:**
- [signals.md](./signals.md) — All 40+ signal types captured
- [embeddings.md](./embeddings.md) — 9 embedding modalities
- [scoring.md](./scoring.md) — 16-component MatchScoringService formula
- [learning.md](./learning.md) — Weight learning and personalization

**Operations:**
- [workers.md](./workers.md) — Batch job schedule and what each does
- [configuration.md](./configuration.md) — ECHO config in appsettings.json
- [cold-start.md](./cold-start.md) — How ECHO works on day 1 (before signals)

---

## Quick Reference

**OpenAI Models Used:**
- Chat/Explanations: `gpt-4.1-mini`
- Embeddings: `text-embedding-3-small`
- TTS (narrator): `tts-1` (voice: `nova`)

**Voice Embedding:**
- Model: ECAPA-TDNN (SpeechBrain)
- Dimensions: 192
- Endpoint: `SpeechBrain:EndpointUrl` (HTTP) or subprocess fallback (dev)

**Batch Job Schedule (UTC):**
- 02:30 — EmbeddingBatchWorker
- 03:50 — ConnectionScoreBatchWorker
- 04:00 — WeightLearningBatchWorker (Sunday only)
- 04:20 — LinUcbBatchWorker
- 05:00 — CfScoreBatchWorker
- 18:00 — CoachingSummaryWorker (Wednesday only)

**Key Constants:**
- Min samples for weight learning: 10
- Min ConnectionScore for training: 0.08
- Trust gate: 0.25
- Signal retention: 90 days
- Coaching eligibility: ≥14 days, ≥3 deck interactions/week

---

## Implementation Notes

All ECHO code lives in:
- `backend/WovenBackend/Services/Matchmaking/` — core matching
- `backend/WovenBackend/Services/Games/` — Know Me, Red/Green Flag
- `backend/WovenBackend/Services/Coaching/` — weekly summaries
- `backend/WovenBackend/Services/Embeddings/` — 9 embedding modalities
- `backend/WovenBackend/Services/OpenAiClient.cs` — centralized AI client

Signals logged via:
- `backend/WovenBackend/Data/Entities/MatchSignalLog.cs`
- `IMatchSignalService.RecordAsync(...)`

---

## See Also

- [CLAUDE.md](../../../CLAUDE.md#echo--ai-co-founder-personality) — ECHO persona and voice
- [Architecture Overview](../../architecture/README.md) — System architecture
- [Database Schema](../../architecture/database.md) — Entity schemas
