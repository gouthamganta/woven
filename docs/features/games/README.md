# Games — Interactive Chat Mini-Games

**Status:** SHIPPED  
**Last Updated:** 2026-08-17

---

## What Are Games?

Games are short, AI-powered interactive activities users can play with their matches inside chat threads. They serve dual purposes:

1. **Engagement:** Break the ice, learn about each other, have fun
2. **Signal Generation:** Feed behavioral data into ECHO's matchmaking pipeline

Games are **not** advertised as ECHO training mechanisms. From the user's perspective, they're just fun ways to connect.

---

## Available Games

| Game | Icon | Duration | What It Does |
|---|---|---|---|
| **Know Me** | 🎯 | 2 min | Guess what your match picked for personality/lifestyle questions |
| **Red/Green Flag** | 🚦 | 2 min | React to scenarios about each other (green/yellow/red/depends) |

---

## When Games Appear

Games are available when:
- Match balloon is **ACTIVE**
- Daily limit not reached (2 games initiated per user per day)
- No pending game invite in this match

A user **initiates** a game by:
1. Tapping the game icon in the chat input toolbar
2. Choosing a game type from the list
3. Sending the invite

The other user sees:
- A system message card with Accept/Reject buttons
- 10-minute expiry timer (extended from 3min as of June)

Once accepted:
- Game session becomes ACTIVE (30-minute expiry)
- Both users play 2 rounds (role swap)
- AI generates personalized questions based on their ECHO profiles

---

## Game Flow

```
INITIATE → PENDING (10min) → ACTIVE (30min) → COMPLETED
                ↓                 ↓
            REJECTED         EXPIRED
```

### Round Structure

Each game has **2 rounds** (users take turns):

**Round 1:**  
- Initiator = Guesser (predicts partner's answers)  
- Partner = Target (answers about themselves)

**Round 2:**  
- Roles flip

### Scoring

- **Know Me:** Points = number of correct guesses (max 3 per round)
- **Red/Green Flag:** Points = alignment count (how many flags matched)

### Final Result

- Winner determined (or tie)
- AI-generated insight sentence (playful, non-judgmental)
- Results stored → feed ECHO signals

---

## Daily Limits

- **2 game initiations per user per day** (tracked in `DailyInteraction.GamesInitiated`)
- No limit on accepting games
- Limit resets at UTC midnight

---

## ECHO Signal Integration

Games feed 3 signal types into `MatchSignalLogs`:

| Signal | Game | What It Measures |
|---|---|---|
| `KnowMeDisclosureDepth` | Know Me | Completion rate (rounds finished / total) |
| `RedFlagGameDepth` | Red/Green Flag | Completion rate |
| `FlagAgreementRate` | Red/Green Flag | Alignment score (0-1) across all rounds |

These signals influence future match scoring and deck composition.

**Critical:** Users are never shown these signals. Games feel like entertainment, not data collection.

---

## AI Personalization

Games use the same ECHO AI Profile data that drives matchmaking:

- **8 Pillars** (Lifestyle, Energy, Communication, etc.)
- **Pulse metrics** (socialCapacity, energyLevel)
- **Tags** (interests, hobbies)
- **Lifestyle fields** (diet, workout)
- **Pair Context** (shared tags, aligned pillars, intent alignment)

AI agents adapt:
- **Difficulty:** EASY/MEDIUM/HARD based on pillar alignment
- **Tone:** PLAYFUL/BALANCED/THOUGHTFUL based on conversation preferences
- **Question personalization:** References actual interests, not generic templates

---

## Tech Stack

| Layer | Technology |
|---|---|
| AI Model | GPT-4.1-mini (via OpenAiClient) |
| Backend | GameService, GameAgents (KnowMe, RedGreenFlag) |
| Frontend | inline-game-player.component.ts (modal overlay) |
| Storage | GameSessions, GameRounds, GameResults, GameOutcomes |
| Signals | MatchSignalLogs via IGameOutcomeService |

---

## Related Docs

- [know-me.md](./know-me.md) — KnowMe game mechanics
- [red-green-flag.md](./red-green-flag.md) — Red/Green Flag game mechanics
- [game-agents.md](./game-agents.md) — AI agent implementation
- [outcomes.md](./outcomes.md) — ECHO signal tracking
- [frontend.md](./frontend.md) — Frontend components
- [backend.md](./backend.md) — Backend API + services

---

## Key Files

**Backend:**
- `Services/Games/GameService.cs` — orchestration
- `Services/Games/KnowMeAgent.cs` — question generation
- `Services/Games/RedGreenFlagAgent.cs` — scenario generation
- `Services/Games/GameOutcomeService.cs` — ECHO signal recording
- `Endpoints/GameEndpoints.cs` — API routes

**Frontend:**
- `components/inline-game-player/` — game modal UI
- `components/game-message-card/` — chat invite cards
- `services/games.service.ts` — HTTP client

**Data:**
- `data/Entities/Games/GameSession.cs`
- `data/Entities/Games/GameRound.cs`
- `data/Entities/Games/GameResult.cs`
- `data/Entities/Games/GameOutcome.cs`
