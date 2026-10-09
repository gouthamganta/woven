# Matchmaking System

**System:** ECHO Matching Engine  
**Related:** [ECHO Overview](../echo/README.md) | [Scoring](../echo/scoring.md)

---

## Overview

The matchmaking system (ECHO) handles candidate pool filtering, match scoring, deck selection, and match creation.

---

## Core Components

| Component | Purpose | File |
|-----------|---------|------|
| **CandidatePoolService** | Filter eligible candidates (gender, trust, age, blocking) | [candidate-pool.md](./candidate-pool.md) |
| **MatchScoringService** | Score candidates across 14 components | [../echo/scoring.md](../echo/scoring.md) |
| **DeckSelectionService** | Select top 5 from scored pool | [../../features/moments/backend.md](../../features/moments/backend.md) |
| **DailyDeckOrchestrator** | Orchestrate daily deck generation | [../echo/README.md](../echo/README.md) |
| **MatchExplanationService** | Generate match explanations | [match-explanation.md](./match-explanation.md) |

---

## Pipeline Flow

```
User Profile → Candidate Pool → Scoring → Deck Selection → Daily Deck
```

1. **Filter candidates** (CandidatePoolService)
2. **Score each candidate** (MatchScoringService — 14 components)
3. **Select top 5** (DeckSelectionService — bucket diversity)
4. **Generate explanations** (MatchExplanationService)
5. **Save daily deck** (DailyDecks + DailyDeckItems)

---

## Related

- [ECHO System](../echo/README.md)
- [Match Lifecycle](./match-lifecycle.md)
- [Candidate Pool](./candidate-pool.md)
- [Deck Selection](../../features/moments/backend.md#deck-selection)
