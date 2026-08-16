# Woven — High Level Design

This document describes Woven's product architecture from a surface and flow perspective. For technical architecture (services, database, infrastructure), see the technical documentation.

---

## Three Surfaces

Woven is organized around three primary surfaces. Every user-facing feature lives in one of these three tabs.

```
┌──────────────────────────────────────────────────────────┐
│                         WOVEN                            │
├────────────┬──────────────────┬──────────────────────────┤
│  MOMENTS   │     COMMONS      │         CHATS            │
│            │                  │                          │
│  Deck tab  │  Content tiles   │  Active match threads    │
│  Drawn tab │  posted by users │  Trial decisions         │
│            │  Orbit (◈) on    │  Find Love date ideas    │
│  ECHO      │  tiles feeds     │  Games, voice notes      │
│  curates   │  ECHO matching   │  Close / Block / Extend  │
└────────────┴──────────────────┴──────────────────────────┘
```

---

## Surface 1: Moments

Moments is the discovery engine. It has two tabs.

### Deck Tab

The Deck is a user's daily curated profile stack. ECHO generates it fresh each day. The cap is 5 responses per day — once a user has responded to 5 profiles (any combination of Magical, Resonant, or Pass), the day's deck is exhausted.

```
ECHO scoring pipeline
       │
       ▼
12-step delivery boost pipeline
  (reciprocal exposure, orbit gravity, fatigue penalties, etc.)
       │
       ▼
Daily Deck: up to N candidates ranked for this viewer
       │
       ▼
User sees cards one at a time:
  [Name] [Verification badge]
  [What caught our eye — match explanation headline + 2-3 bullets]
  [◈ Magical] [◇ Resonant] [— Pass]
```

**Pass** is recorded silently. No cost. No match check.

**Magical or Resonant** opens the ChatNote overlay. The user writes a 20–150 character opening note (or selects a starter phrase). On submission, the choice + note are recorded. If the other user has also submitted a positive choice + note, a Balloon (match) is created immediately. If not, status is set to `RECORDED_WAITING`.

**What the user never sees:**
- Raw ECHO score
- Whether the other person has seen their card
- Whether the other person has responded

**Card anatomy:**

| Element | Shown |
|---|---|
| Name | Yes |
| Verification badge | Yes (if isVerified) |
| Age | No — never |
| Match explanation (headline + bullets) | Yes |
| ◈ Magical / ◇ Resonant / — Pass | Yes |
| ECHO score | No |

### Drawn Tab

Drawn shows people who have already sent a positive choice toward the viewer in the last 7 days. These are real, stored interest signals — not suggestions.

**Rules:**
- Excluded: already matched, blocked, people in today's Deck
- Sorted: most recent first
- Deduplicated per user (one entry per person)
- Entries expire after 7 days, with a countdown ("Xh left" or "Xd left") shown

**Drawn actions:**
- ◈ Magical or ◇ Resonant (Pass is not available in Drawn)
- Each action costs **1 spark** from the viewer's SparkWallet
- A ChatNote is still required before the choice is submitted
- If the other person's original choice + the viewer's response create a mutual pair, a Balloon is created

---

## Surface 2: Commons

Commons is the content layer. Users post tiles; other users browse them. Commons is not a social network — there are no follower relationships, no public like counts, no public comment threads.

**Tile types:** text, photo, video, voice
**Tile lifespan:** configurable expiry (typically 7 days)
**Moderation:** tiles enter a queue before appearing in feeds

**The Orbit (◈):**
A viewer can ◈ (Orbit) any tile. This is an explicit romantic interest signal sent to the tile owner. It generates OrbitGravity, a score that feeds directly into ECHO's candidate pool scoring for (viewer, tile owner) pairs. Orbit is the highest-value signal a user can send without entering the match flow.

**Energy meter:**
Users have a daily energy budget for Commons browsing. The meter tracks how many tiles have been viewed per day. This limits passive consumption and keeps signal data meaningful — dwell on a tile has higher signal weight when the user's total session is bounded.

**What Commons feeds into ECHO:**
- Orbit actions (OrbitGravity score)
- Dwell time on tiles
- Return visits to a profile after a tile view
- Shared tile affinity signals (similar users who orbited the same tiles)

---

## Surface 3: Chats

Chats is the active match layer. Each Balloon (match) has exactly one ChatThread. The Chats tab lists all open threads.

### Chat Thread Anatomy

A chat thread has multiple distinct states and UI layers:

```
┌──────────────────────────────────────────┐
│  Chat thread header                      │
│  [Avatar] [Name] [Pronouns]              │
│  Match type: PURE / EDGE                 │
│  Balloon expires: [countdown]            │
├──────────────────────────────────────────┤
│  Message area (50 most recent, ASC)      │
│  - TEXT messages                         │
│  - VOICE messages (audio player)         │
│  - ◈ reactions on messages               │
│  - ◈ reactions on ChatNotes              │
├──────────────────────────────────────────┤
│  [Active layer — depends on state]       │
│                                          │
│  IF trial active:                        │
│    Trial countdown timer                 │
│    [CONTINUE] [END] [BLOCK]              │
│                                          │
│  IF showBalloonTimer:                    │
│    "Find Love unlocking in X:XX"         │
│                                          │
│  IF showFindLove:                        │
│    3 date idea cards with [Plan It]      │
│    "Waiting to see if they're in…"       │
│    OR both selected → venue suggestions  │
├──────────────────────────────────────────┤
│  Input bar                               │
│  [Text input] [🎤 voice note] [Send]     │
│  [Availability signal button]            │
│  [Games] [Close Gracefully]              │
└──────────────────────────────────────────┘
```

### Trial Period Flow

```
Match created (Balloon)
       │
       ▼
User A opens thread → TrialUserAOpenedAt = now
       │
       ▼
User B opens thread → TrialUserBOpenedAt = now
       │
       ▼
TrialEndsAt = now + 3 minutes
       │
  [3-minute trial window — both can message freely]
       │
       ▼
Trial ends → each user sees decision UI
       │
       ├── Both CONTINUE → match continues, FindLoveAt = now
       │
       ├── Either END → match CLOSED (UNMATCH)
       │     └── If no messages: ghost refund (0.5 sparks each)
       │
       └── BLOCK → match CLOSED (BLOCK), Block record created
```

### Find Love Flow

```
Both users have sent at least 1 message
       │
       ▼
BothMessagedAt = now
  showBalloonTimer = true (countdown visible)
       │
  [5-minute reflection window]
       │
       ▼
showFindLove = true
  3 date ideas appear in thread
       │
       ├── User A selects idea → "Waiting to see if they're in…"
       ├── User B selects idea → "Waiting to see if they're in…"
       │
       └── Both selected → push notification fires, venue suggestions unlock
```

### Match Close Paths

| Path | Trigger | Ghost Refund |
|---|---|---|
| Close Gracefully | User taps "Close Gracefully" in thread | Yes, if no messages |
| Trial END decision | User selects END after trial period | Yes, if no messages |
| Auto-close expired trial | Trial expired, no CONTINUE decision | Yes, if no messages |
| BLOCK | User taps BLOCK | No |
| Balloon expiry (7 days) | Match.ExpiresAt elapsed | Depends on message state |

---

## ECHO — The Invisible Matching Engine

ECHO is the behavioral matching pipeline. It is invisible to users — they never interact with it directly, and they never see its output as numbers.

### What ECHO Consumes

| Signal Type | Source |
|---|---|
| Pillar answers | Onboarding foundational questions (5 dimensions) |
| Response speed (TimeToFirstMessageMs) | Chat thread behavior |
| Trial decisions (CONTINUE / END / no_spark etc.) | Trial period |
| Date idea choices (DateIdeaAccepted) | Find Love flow |
| Voice note listen completion (VoiceNoteListenComplete) | Voice notes |
| Mutual voice exchange (MutualVoiceExchange) | Voice notes |
| Chat depth (ChatDepthMessages) | Message count |
| Orbit gravity (OrbitGravity) | Commons Orbit actions |
| Visual preference | Learned from MAGICAL/RESONANT choices on profile photos |
| Voice similarity | 192-dim voice embeddings (ECAPA-TDNN) |
| Collaborative filtering | Similar users' preferences (CfScore) |

### What ECHO Produces

- A ranked candidate list per viewer (the daily Deck)
- Match explanations (rendered to human language, shown on cards)
- Date idea generation (per match pair)
- Per-user scoring weights (updated weekly via logistic regression)

### Weight Learning

ECHO runs a weekly weight update (Sunday 04:00 UTC) using logistic regression on real connection outcomes. The outcome score for each (viewer, candidate) pair is aggregated nightly (03:50 UTC). After enough data, no two users' candidate scoring uses the same feature weights.

---

## Spark Economy — Design

The spark economy is a soft behavioral gate. It does not lock features behind payment. Its purpose is to create intentionality in the Drawn tab — responding to someone who chose you first is a deliberate act, not a reflexive tap.

| Event | Spark Change |
|---|---|
| Daily refill | +5 (up to wallet max of 10) |
| Drawn action (Magical or Resonant) | -1 |
| Ghost refund (match closes, no messages) | +0.5 to each user |

---

## Onboarding Flow

Onboarding is a sequential multi-step flow. Users cannot access the main app until all required steps are complete. The OnboardingGate at `/app` checks status and redirects.

```
welcome
  │
basics (name, gender, pronouns, birthday, city/state)
  │
intent (relationship intent, 1-sentence reflection — encrypted)
  │
foundational (AI-generated questions covering 5 pillar dimensions)
  │
photos (profile photo upload)
  │
details (hobbies, optional fields)
  │
lifestyle (diet, workout, children preference, drinking, smoking, religion)
  │
review (profile summary)
  │
start → main app
```

---

## Profile

A profile is the public representation of a user within the app. It is not a public web page — it is only shown to other users within Woven.

**Profile elements:**
- Photos (sorted by sortOrder; ECHO selects the best-matched photo per viewer using CLIP embeddings)
- Name
- Verification badge (✓ when isVerified = true)
- Pronouns
- Highlights: up to 9 tiles shown as profile highlights (My Tiles, slot 1–9)
- Weekly vibe (UserWeeklyVibe) — expires weekly
- Optional fields with visibility settings

**No age shown on Moments deck cards.** Age may appear in other profile contexts.

---

## Notification Model

Woven uses push notifications for key moments:

| Trigger | Notification |
|---|---|
| Both users select a Find Love date idea | Push to both users |
| Availability signal sent | Push to partner |

Push notifications for in-app events (new messages, new matches, trial about to expire) are part of planned infrastructure but not yet implemented (no service worker, no VAPID, no Web Push endpoint as of 2026-05-26).

---

## Games System

Two games are available inside any chat thread:

### Know Me

- One user generates 3 questions about the other from their real profile data
- Other user answers
- Guesser predicted; target self-rates
- Score = prediction / self-rating alignment
- Difficulty: EASY (80% guessable), MEDIUM (50%), HARD (30%)
- Tone: PLAYFUL, THOUGHTFUL, BALANCED

### Red Flag / Green Flag

- 3 statements generated about one user from their profile data
- Guesser rates each: GREEN / YELLOW / RED / DEPENDS
- Target self-rates
- Score = alignment count
- 90-second time limit
- AI generates 1–2 sentence post-game insight

---

## What Is Not Yet Built

| Feature | Status |
|---|---|
| Push notifications (service worker, VAPID, Web Push) | Not built |
| "Your Turn" chat list indicator | Designed, not built |
| Active / online indicator | Designed, not built |
| Horoscope onboarding field | Designed, not built |
| Ambition pillar (foundational questions) | Not covered |

---

*See also: [PRODUCT_STORY.md](PRODUCT_STORY.md) | [USER_LIFECYCLE.md](USER_LIFECYCLE.md) | [FEATURES.md](FEATURES.md)*
