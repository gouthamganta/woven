# Product Philosophy

**Last Updated:** 2026-10-07

This document captures Woven's product philosophy — the "why" behind design decisions, feature priorities, and user experience choices.

---

## Core Principles

### 1. Intentionality Over Engagement

**Philosophy:**  
We optimize for **outcomes** (people leaving together) over **engagement** (time spent in app).

**Manifestations:**
- Daily deck limits (20 swipes/day) — no endless scrolling
- Spark economy soft-gates Drawn tab — prevents impulsive behavior
- Trial period (3 min) — forces deliberate connection decisions
- No addictive patterns (no streaks, no gamification beyond games)

**Anti-pattern:**  
Most dating apps maximize swiping → ad revenue. We limit swiping → force quality over quantity.

---

### 2. Invisible AI

**Philosophy:**  
ECHO (AI matching) is **invisible UX**, not a marketing feature.

**What we never show:**
- Compatibility scores
- "AI picked this match for you" messaging
- Match percentages
- Community ratings
- "Based on your behavior" explanations

**What we do show:**
- Match explanations (human-readable, no scores)
- Bridge questions (conversation starters)

**Why:**  
Users should feel they're choosing, not being manipulated. AI is infrastructure, not a feature.

---

### 3. Behavioral Truth Over Stated Preferences

**Philosophy:**  
What people **do** matters more than what they **say** they want.

**ECHO pipeline:**
- Learns from swipes, messages, voice notes, game answers
- Ignores self-reported "ideal partner" traits
- Weights recent behavior > old behavior
- Updates weights weekly based on match outcomes

**Rationale:**  
People are bad at predicting attraction. Revealed preferences (actions) beat stated preferences (words).

---

### 4. Women-First Design

**Philosophy:**  
If a feature feels predatory or exploitative toward women, we don't build it.

**Design decisions:**
- No unsolicited messages (must match first)
- Trial period (gives women exit before commitment)
- Block immediately closes chat (no "unmatch" delay)
- No "online now" indicator (prevents stalking)
- No read receipts (reduces pressure)

**Non-negotiable:**  
Women's safety > male engagement metrics.

---

### 5. Scarcity Creates Value

**Philosophy:**  
Limits create intentionality. Abundance creates noise.

**Implementations:**
- 5 sparks/day (forces selective balloon pops)
- One deck/day (prevents swipe fatigue)
- 72h balloon window (creates urgency)
- 3min trial period (forces quick decision)

**Anti-pattern:**  
Unlimited likes, unlimited swipes → paradox of choice → decision paralysis.

---

### 6. No Paywalls, No Pay-to-Win

**Philosophy:**  
Monetization should never compromise matching quality.

**Rules:**
- Spark economy is the gate (daily limits), not payment
- No "boost" or "super like" paid features
- No premium tier with better matches
- Future monetization: date planning services, not matching advantages

**Why:**  
Dating is already unequal (looks, location, demographics). Adding payment inequality makes it worse.

---

## Design Philosophy

### Minimalism

**Rule:** Every UI element must justify its existence.

**Manifestations:**
- No age on Moments cards (irrelevant after preferences filter)
- No last-seen timestamps (creates anxiety)
- No typing indicators (reduces performance pressure)
- No "X people viewed your profile" (vanity metric)

**Test:** If removing it doesn't confuse users, it shouldn't be there.

---

### Calm Technology

**Philosophy:**  
The app should fade into the background, not demand attention.

**Implementations:**
- No streak mechanics (no FOMO)
- No push spam (only meaningful notifications)
- No badge counts for Drawn tab (reduces compulsion)
- Background drift animation (calming, not distracting)

**Rationale:**  
Dating apps cause anxiety. We reduce it, not amplify it.

---

### Beauty in Simplicity

**Design principles:**
- Flat hierarchy (no nested tabs)
- One action per screen (no "swipe AND super like")
- CSS variables (consistent tokens)
- No hover translateY lifts (calm, not jumpy)

**Evidence:** [styles.scss](../../frontend/woven-frontend/src/styles.scss), design system docs

---

## Feature Decisions

### Why ◈ (Magical) and ◇ (Resonant)?

**Philosophy:**  
Binary choice (like/dislike) is too reductive. Ternary choice (◈/◇/SKIP) captures nuance.

**Meaning:**
- ◈ (Magical) — intuition, gut feeling, "can't explain it"
- ◇ (Resonant) — logic, alignment, "I see why this works"
- SKIP — neutral (no signal)

**ECHO uses this:**  
EDGE matches (◈◇) surface different explanations than PURE matches (◈◈).

---

### Why Trial Period?

**Philosophy:**  
3 minutes forces quick connection assessment. Too short to overthink, too long to ghost.

**Design decision:**
- **Not 5 min** — too long, people get bored
- **Not 1 min** — too short, feels rushed
- **3 min** — Goldilocks zone (proven in dating speed-dating studies)

**Side benefit:**  
Reduces ghosting (decision required).

---

### Why No Undo?

**Philosophy:**  
Commitment to choices creates intentionality.

**Implementations:**
- No undo swipe (you chose, live with it)
- No edit/delete messages (preserves context for ECHO)
- No unsend (reduces regret-driven behavior)

**Exception:**  
Block/report (safety overrides intent).

---

### Why No "Seen" Indicator?

**Philosophy:**  
Read receipts create anxiety and obligation.

**Decision:**  
We don't show "seen" or "typing" indicators.

**Rationale:**  
Dating should feel casual, not performative.

---

### Why Voice Notes?

**Philosophy:**  
Voice conveys emotion/personality that text cannot.

**ECHO benefit:**  
Voice listening completion is a strong signal (0.05 weight in ConnectionScore).

**Design:**
- Max 60s (keeps it casual, not monologue)
- Listen tracking (backend signal, not shown to sender)
- No transcription (preserves intimacy)

---

### Why Games?

**Philosophy:**  
Shared activities reduce first-message pressure.

**Games as icebreakers:**
- KnowMe — learn about each other (low stakes)
- RedGreenFlag — debate hypotheticals (fun disagreement)

**Side benefit:**  
Game answers feed ECHO (behavioral signals).

---

### Why Commons (Tiles)?

**Philosophy:**  
Self-expression beyond profile photos.

**Orbit (◈ on tiles):**  
Not a "like" — it's curiosity, interest, "I want to know more."

**ECHO benefit:**  
SharedTileAffinity component (collaborative filtering via Orbit interactions).

---

## What We Don't Build

### No "Hot or Not" Mechanics

**Why not:**  
Reduces people to attractiveness scores. We care about compatibility, not popularity.

**Alternative:**  
Match explanations focus on alignment (values, lifestyle, communication), not looks.

---

### No "Boost" or "Super Like"

**Why not:**  
Pay-to-win breaks fairness. Everyone gets equal visibility (modulo ECHO scoring).

**Future monetization:**  
Date planning services (restaurant recs, activity booking), not matching advantages.

---

### No "See Who Liked You" Paywall

**Why not:**  
Common dark pattern (Tinder, Bumble). Creates FOMO, drives premium subscriptions.

**Alternative:**  
Drawn tab shows mutual likes (no payment required).

---

### No Endless Scrolling

**Why not:**  
Addictive pattern. Creates decision fatigue, reduces intentionality.

**Alternative:**  
One deck/day (60 candidates). Forces selective swiping.

---

### No Age Display

**Why not:**  
Age is already filtered (preferences). Showing it adds no value, only bias.

**Evidence:**  
Moments cards show name + badge + explanation only.

---

### No "Active Now" Indicator

**Why not:**  
Creates pressure ("they're online but not replying to me").

**Side benefit:**  
Reduces stalking behavior.

---

## Future Directions

### Date Planning Services

**Philosophy:**  
Help users **after** matching (date ideas, venue recs, reservation booking).

**Monetization:**  
Restaurant partnerships, activity booking fees.

**Why:**  
Aligns incentives (we win when users go on dates).

---

### Coaching as a Service

**Philosophy:**  
Weekly coaching summaries help users improve, not just match more.

**Current:**  
AI-generated summaries (free).

**Future:**  
Human coaches (paid) for personalized feedback.

---

### Friend Bridge Expansion

**Philosophy:**  
Mutual friends increase trust + safety.

**Current:**  
`FriendBridge` table tracks mutual connections.

**Future:**  
"You both know Alex" shown on Moments cards.

---

## References

- **Glossary:** [glossary.md](glossary.md)
- **Business rules:** [rules.md](rules.md)
- **State machines:** [state-machines.md](state-machines.md)
- **CLAUDE.md:** [../../CLAUDE.md](../../CLAUDE.md) (hard design rules)

---

**Last Updated:** 2026-10-07  
**Evidence:** Product decisions documented in [CLAUDE.md](../../CLAUDE.md), feature docs in [docs/features/](../features/)
