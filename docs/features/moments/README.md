# Moments — Daily Discovery System

**Last Updated:** 2026-08-17  
**Status:** Production

## What is Moments

Moments is Woven's daily discovery system. Every day at midnight UTC, users receive a curated deck of 5 candidates selected by ECHO (the matching algorithm). Users respond with one of three choices: Magical ◈ (emotional yes), Resonant ◇ (logical yes), or Pass (soft skip).

When both users choose each other with a positive choice (Magical or Resonant), a match is created and they enter a 36-hour balloon window to start a conversation.

## Why it exists

Moments replaces the infinite-swipe model with intentional, curated discovery:

- **Small batches** — 5 candidates per day (not 500)
- **Forced choice** — Daily budget prevents endless browsing
- **ECHO-driven** — Candidates selected by behavioral learning, not demographics
- **Two-choice system** — Magical (felt) vs Resonant (logical) captures *how* you're drawn to someone, feeding preference learning
- **No age shown** — Name, photo, ECHO explanation, and tiles only

## How it works

### Two tabs

1. **Deck** (`'today'`) — Daily 5 candidates from ECHO
2. **Drawn** (`'liked-you'`) — People who chose you in the last 7 days (spark-gated)

### Daily budget

- **5 total actions/day** — Includes Magical, Resonant, and Pass choices
- Tracked in `daily_interactions` table (`total_used` / `total_cap`)
- Redis cache fast-gates to prevent DB load on cap check
- Budget resets at midnight UTC

### Spark economy (Drawn tab only)

- **5 sparks earned/day** (max wallet: 10 sparks)
- **1 spark spent** per Drawn tab action (Magical/Resonant only — no Pass on Drawn)
- **0.5 spark refund** if match ends with no messages exchanged (ghost refund)

### Match creation flow

1. User A chooses User B with **Magical** or **Resonant**
2. ChatNote overlay appears (20-150 chars required)
3. User A submits note → stored in `chat_notes` table, marked `WAITING_FOR_OTHER_NOTE`
4. User B chooses User A (same day or later, depending on source)
5. User B submits note → match created immediately
6. Both users receive match notification → chat thread created → balloon state = `ACTIVE`

**Match types:**
- **PURE** — Both users chose the same type (both Magical or both Resonant)
- **EDGE** — Different types (one Magical, one Resonant) — `edge_owner_id` assigned randomly

**Balloon expiry:** 36 hours from match creation (`expires_at`). If neither user messages before expiry, balloon closes (`BalloonState.CLOSED`, `ClosedReason.EXPIRE`).

## Key concepts

### MomentChoice enum

```csharp
MAGICAL = 4  // felt/emotional yes — ◈
LOGICAL = 5  // reasoned/compatibility yes — ◇
PASS = 6     // soft skip — never triggers match
YES = 1      // legacy — treated as MAGICAL
NO = 2       // legacy — treated as LOGICAL
```

### BalloonState enum

```csharp
ACTIVE = 1   // Match exists, connection window open
CLOSED = 2   // Match ended (popped, expired, unmatched, or blocked)
```

### Source tracking

Responses track where the choice was made:
- `"TODAY"` — Deck tab (daily 5)
- `"LIKED_YOU"` — Drawn tab (7-day visibility)

Used for analytics and budget enforcement (Deck = daily cap, Drawn = spark wallet).

## User flow examples

### Scenario 1: Pure match from Deck

1. User A sees User B in today's Deck
2. A taps Magical ◈ → ChatNote overlay opens
3. A writes "Your photo made me want to say… I love your style" → submits
4. Response recorded: `status = "WAITING_FOR_OTHER_NOTE"`
5. User B sees User A the same day (or later via Drawn tab)
6. B taps Magical ◈ → ChatNote overlay opens
7. B writes note → submits
8. **PURE match created** (both Magical) → `match_type = PURE`, `edge_owner_id = NULL`
9. Both users navigate to chat thread → balloon countdown starts (36h)

### Scenario 2: Edge match from Drawn tab

1. User A chose User B yesterday (Magical)
2. User B sees A in Drawn tab today (7-day window)
3. B has 3.5 sparks in wallet
4. B taps Resonant ◇ → ChatNote overlay opens (no bridge question on Drawn)
5. B writes note → submits → 1 spark spent, balance now 2.5
6. **EDGE match created** (Magical ◈ + Resonant ◇) → `match_type = EDGE`, `edge_owner_id` = randomly A or B
7. Both users receive notification → chat thread created

### Scenario 3: Pass (no match)

1. User A sees User B in Deck
2. A taps Pass — → no overlay, no note required
3. Response recorded immediately: `choice = PASS`, `source = "TODAY"`
4. Card disappears from Deck (added to `respondedUserIds` set)
5. No match created, no budget spent (Pass is free but counts toward daily 5 cap)

## File organization

```
docs/features/moments/
├── README.md              ← You are here
├── deck-tab.md            ← Deck tab mechanics
├── drawn-tab.md           ← Drawn tab mechanics
├── chat-note.md           ← ChatNote overlay system
├── match-creation.md      ← Match creation logic
├── frontend.md            ← Angular implementation
├── backend.md             ← .NET implementation
└── api.md                 ← API reference
```

## Related systems

- **ECHO** — `docs/features/echo/` — Matching algorithm that builds daily decks
- **Balloons** — `docs/features/balloons/` — 36-hour connection window after match
- **Commons** — `docs/features/commons/` — Content feed (tiles shown on Moments cards)
- **Chat** — `docs/features/chat/` — Messaging system (created after balloon pop)

## Design principles

1. **No infinite scroll** — Daily cap forces intentional choice
2. **No age** — ECHO explanation replaces demographic data
3. **Choice captures intent** — Magical vs Resonant feeds preference learning
4. **Spark economy is soft gate** — Not a paywall, just a patience mechanism
5. **ChatNote is background signal** — Never shown to the other user, only fed to ECHO for preference learning
6. **7-day Drawn window** — Prevents ghost town effect while maintaining scarcity
