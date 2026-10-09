# Woven Business Rules

**Last Updated:** 2026-10-07

---

## Core Business Rules

### 1. No Community Ratings Shown to Users

**Rule:** Community ratings are NEVER displayed to users.

**Why:** Reduces anxiety, prevents score-chasing, focuses on genuine connection.

**Implementation:**
- Ratings collected (post-date feedback)
- Used for trust scoring (platform-only)
- Zero UI components render rating data to users

**Source of Truth:** `CLAUDE.md` (hard design rules)

---

### 2. Spark Economy

**Daily Allocation:** 5 sparks/day  
**Wallet Maximum:** 10 sparks  
**Cost per Drawn Action:** 1 spark

**Ghost Refund:** 0.5 sparks to BOTH users if match closes with no messages

**Why:**
- Soft gate (no paywall)
- Encourages thoughtful responses
- Fair refund for ghosted matches

---

### 3. Trial Period Mechanics

**Duration:** 3 minutes  
**Starts When:** BOTH users open the chat thread  
**Decisions:** CONTINUE / END / BLOCK

**Why:**
- Quick filter for instant chemistry
- Both parties must engage to start timer
- Fair exit without guilt

---

### 4. Balloon Lifetime

**Duration:** 36 hours  
**States:** ACTIVE → CLOSED (POP or EXPIRE)

**Closure Paths:**
1. POP → Trial → Decision
2. EXPIRE (36h timeout, no pop)
3. UNMATCH (user-initiated)
4. BLOCK (safety)

---

### 5. Match Types

**Pure Match:** Both users chose same option (◈ or ◇)  
**Edge Match:** Different choices, one "owns the edge"

**Edge Owner:**
- If A chose ◈ and B chose ◇ → B owns edge
- Edge owner sees notification "You're their edge case!"

---

### 6. Daily Deck Size

**Cards:** 5 per day  
**Budget:** 5 actions (Magical/Resonant/Pass counts)  
**Refresh:** Midnight UTC

**Why:**
- Quality over quantity
- Reduces decision fatigue
- Creates anticipation

---

### 7. Age Never Shown

**Rule:** Age is NEVER displayed on Moments cards or profiles.

**Why:** Reduces age-based filtering, encourages reading explanations.

**Data:** Age IS collected (onboarding) for filtering (18+ only, age-appropriate matching).

---

### 8. ChatNote Privacy

**Rule:** ChatNotes are NEVER shown to the other user.

**Purpose:** Signal for ECHO learning only.

**Why:**
- Captures intent without pressure
- Users write honestly (not performatively)
- ECHO learns what catches attention

---

### 9. Find Love Unlock

**Trigger:** Match reaches mutual engagement threshold

**Criteria:**
- 5+ minutes in conversation
- Both users sent messages
- Trial accepted (CONTINUE)

**What Unlocks:**
- Date venue suggestions
- Date idea prompts
- Deeper conversation topics

---

### 10. Verified Badge (Planned)

**Criteria:**
- Photo verification (selfie match)
- Phone number verification
- Account age > 30 days

**Benefits:**
- Higher trust score
- Priority in deck
- Badge on profile

**Status:** Not yet implemented

---

## State Machines

### Balloon State

```
ACTIVE ──┬─→ POP ──→ TRIAL ──┬─→ CONTINUE ──→ CLOSED
         │                    ├─→ END ──────→ CLOSED
         │                    └─→ BLOCK ────→ CLOSED
         ├─→ EXPIRE ──────────────────────→ CLOSED
         ├─→ UNMATCH ─────────────────────→ CLOSED
         └─→ BLOCK ───────────────────────→ CLOSED
```

**CLOSED is immutable** (never transitions back).

---

### Trial Decision

```
TRIAL_PENDING ──┬─→ CONTINUE ──→ Match persists
                ├─→ END ──────→ Match closes
                └─→ BLOCK ────→ Match closes + Block created
```

---

### Profile Status

```
ONBOARDING_STARTED ──→ DETAILS_DONE ──→ COMPLETE
```

**Deck eligibility:** COMPLETE only

---

## Business Metrics

**North Star:** Successful dates arranged (Find Love unlocks)

**Leading Indicators:**
- Daily active users (DAU)
- Deck engagement rate
- Trial acceptance rate
- Message exchange rate
- Find Love unlock rate

**Lagging Indicators:**
- Date feedback (5⭐ rate)
- User retention (D1, D7, D30)
- Match-to-date conversion

---

## Monetization (Future)

**Current:** No monetization, no paywalls

**Planned (Not Implemented):**
- Premium features (maybe)
- Venue partnerships (commission on reservations)
- Coaching insights (one-time purchases)

**Never:**
- Pay-to-win (buy matches, buy sparks)
- Ads
- Selling user data

---

## Related Documentation

- [Glossary](glossary.md)
- [State Machines](state-machines.md)
- [Product Philosophy](product-philosophy.md)
- [Business Rules (Detailed)](rules.md)
