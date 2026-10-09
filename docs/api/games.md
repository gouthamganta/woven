# Games API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

In-match games help users learn about each other. Two game types: **Know Me** (guess their preferences) and **Red/Green Flag** (react to scenarios).

**Key Concepts:**
- **Game Session** — Active game instance for a match
- **Round** — Single question/scenario in a game
- **Know Me** — Multiple-choice guessing game
- **Red/Green Flag** — React to dating scenarios

**Rate Limit:** AI-powered games limited to 10 requests per 10 minutes per user

**Source:** [`backend/WovenBackend/Endpoints/GameEndpoints.cs`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

## Endpoints

### GET /games/matches/{matchId}/availability

**Description:** Check game availability for a match.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "available": true,
  "gamesRemaining": 2,
  "reason": null,
  "games": [
    {
      "type": "KNOW_ME",
      "name": "Know Me",
      "description": "Guess what they picked",
      "duration": "2 min",
      "icon": "🎯"
    },
    {
      "type": "RED_GREEN_FLAG",
      "name": "Red / Green Flag",
      "description": "React to scenarios and learn each other fast",
      "duration": "2 min",
      "icon": "🚦"
    }
  ]
}
```

**Availability Rules:**
- Match must be ACTIVE
- Both users must have messaged
- Limit: 3 games per match

**Source:** [`GameEndpoints.cs:22-56`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

### POST /games/matches/{matchId}/sessions

**Description:** Create a game session (invite sent to other user).

**Authentication:** Required (JWT)

**Request:**
```json
{
  "gameType": "KNOW_ME"
}
```

**Game Types:**
- `KNOW_ME`
- `RED_GREEN_FLAG`

**Response (200 OK):**
```json
{
  "sessionId": "uuid",
  "gameType": "KNOW_ME",
  "status": "PENDING",
  "initiatorUserId": 123,
  "createdAt": "2026-10-07T15:00:00Z"
}
```

**Errors:**
- `400` — Invalid game type
- `403 Forbidden` — User not part of match
- `400` — Game limit reached for this match

**Source:** [`GameEndpoints.cs:62-87`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

### POST /games/sessions/{sessionId}/accept

**Description:** Accept a game invitation (starts the game).

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "status": "ACTIVE",
  "message": "Game started!"
}
```

**Side Effects:**
- Sets session status to `ACTIVE`
- Generates first round

**Errors:**
- `400` — Cannot accept (wrong user or session not pending)

**Source:** [`GameEndpoints.cs:92-105`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

### POST /games/sessions/{sessionId}/reject

**Description:** Decline a game invitation.

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "status": "REJECTED"
}
```

**Source:** [`GameEndpoints.cs:110-123`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

### GET /games/sessions/{sessionId}/round

**Description:** Get current round (question/scenario).

**Authentication:** Required (JWT)

**Response (200 OK) — Know Me:**
```json
{
  "sessionId": "uuid",
  "roundNumber": 1,
  "question": "Which would they choose for a first date?",
  "options": [
    {"id": "A", "text": "Coffee shop"},
    {"id": "B", "text": "Hiking trail"},
    {"id": "C", "text": "Art gallery"},
    {"id": "D", "text": "Live music venue"}
  ],
  "timeLimit": 30
}
```

**Response (200 OK) — Red/Green Flag:**
```json
{
  "sessionId": "uuid",
  "roundNumber": 1,
  "scenario": "They cancel plans 30 minutes before to hang out with an ex",
  "options": [
    {"id": "RED", "text": "Red Flag 🚩"},
    {"id": "YELLOW", "text": "Yellow Flag ⚠️"},
    {"id": "GREEN", "text": "Green Flag ✅"}
  ],
  "timeLimit": 20
}
```

**Errors:**
- `404` — Round not found or game not active

**Source:** [`GameEndpoints.cs:128-141`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

### POST /games/sessions/{sessionId}/answers

**Description:** Submit answers for current round (both users submit, then results revealed).

**Authentication:** Required (JWT)

**Rate Limit:** 10 requests per 10 minutes per user (AI generation limit)

**Request:**
```json
{
  "answers": {
    "q1": "B",
    "q2": "A"
  }
}
```

**Response (200 OK) — Waiting for Other User:**
```json
{
  "status": "WAITING",
  "message": "Waiting for Alex to answer..."
}
```

**Response (200 OK) — Both Answered:**
```json
{
  "status": "ROUND_COMPLETE",
  "results": {
    "yourAnswer": "B",
    "theirAnswer": "B",
    "correct": true,
    "score": 1
  },
  "nextRound": 2,
  "gameComplete": false
}
```

**Response (200 OK) — Game Complete:**
```json
{
  "status": "GAME_COMPLETE",
  "finalScore": {
    "you": 8,
    "them": 7
  },
  "totalRounds": 10
}
```

**Errors:**
- `400` — Invalid answers
- `429` — Rate limit exceeded (AI generation)

**Source:** [`GameEndpoints.cs:146-200`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs)

---

## Game Mechanics

### Know Me

**Format:** Multiple-choice preference questions

**Rounds:** 5-10 questions

**Scoring:**
- Correct guess = 1 point
- Both see results after each round

**Example Questions:**
- "Which would they choose for a first date?"
- "What's their ideal weekend morning?"
- "How do they handle conflict?"

---

### Red/Green Flag

**Format:** Dating scenarios with flag reactions

**Rounds:** 8 scenarios

**Scoring:**
- Matching reactions = compatibility point
- Results shown after all rounds

**Example Scenarios:**
- "They cancel plans 30 minutes before to hang out with an ex"
- "They still have their ex's photos on social media"
- "They offer to cook you dinner on the second date"

**Reactions:**
- 🚩 Red Flag — Deal-breaker
- ⚠️ Yellow Flag — Concern but not deal-breaker
- ✅ Green Flag — Positive sign

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Endpoints/GameEndpoints.cs`
- `backend/WovenBackend/Services/Games/IGameService.cs`
