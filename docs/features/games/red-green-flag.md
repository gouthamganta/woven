# Red/Green Flag — Scenario Reaction Game

**Game Type:** `RED_GREEN_FLAG`  
**Icon:** 🚦  
**Duration:** ~2 minutes  
**Rounds:** 2 (role swap)

---

## Concept

A two-player judgment game where users react to statements about each other and see how well their perceptions align.

**Example:**
> Statement: "They'll cancel plans last minute if they're drained — and tell you honestly."  
> Both vote: GREEN ✅ | YELLOW ⚠️ | RED 🚩 | DEPENDS 🤷

The "target" (person the statement is about) votes how they see themselves. The "guesser" votes how they think the target will vote. Score = alignment count.

---

## Game Flow

### Round 1

1. **Initiator** = Guesser, **Partner** = Target
2. AI generates 3 personalized statements about the **target**
3. Both users see the same statements and vote independently
4. Guesser: "How do you think **they** will react to this?"
5. Target: "How do **you** react to this about yourself?"
6. Score = number of matching votes (0-3)

### Round 2

Roles flip. AI generates 3 **new** statements about the other person.

### Final Result

- Total alignment scores displayed
- Winner or tie declared
- AI-generated insight ("You're reading each other pretty well — keep going.")

---

## Voting Options

Every statement has 4 choices:

| Vote | Emoji | Meaning |
|---|---|---|
| GREEN | ✅ | Totally fine / healthy behavior |
| YELLOW | ⚠️ | Minor concern / situational |
| RED | 🚩 | Deal-breaker / problematic |
| DEPENDS | 🤷 | Context matters / can't generalize |

**No "correct" answer** — it's about alignment, not moral judgment.

---

## Statement Generation (AI Agent)

**Agent:** `RedGreenFlagAgent.cs`  
**Model:** GPT-4.1-mini

### Input Context

The AI receives:
- **Target user's ECHO profile:**
  - Age
  - Top 3 pillars (sorted by deviation from 0.5)
  - Tags/interests
  - Energy level (0-1)
  - Social capacity (0-1)
  - Diet, workout
- **Pair context:**
  - Shared hobbies
  - Similar traits (aligned pillars)
  - Tone alignment
- **Tone setting:** PLAYFUL/BALANCED/THOUGHTFUL

### Statement Difficulty

| Level | What It Reveals |
|---|---|
| EASY | Light lifestyle habit (surface-level) |
| MEDIUM | Social/communication pattern (behavioral) |
| HARD | Dating preference / deeper value (surprising) |

### Anti-Generic Rules

The AI is **banned** from these overused topics:
- Texting speed / reply time
- Ghosting / leaving on read
- Coffee preferences
- Exes / past relationships
- Weekend plans

**Heavy topics banned:**
- Trauma
- Medical/mental health
- Explicit sex
- Politics
- Religion

Instead, statements **must reference actual pillars/tags/hobbies:**
- High energy → "They need plans every weekend or they feel restless"
- Likes cooking → "They judge dates by whether they'll try their cooking"
- Introverted → "They recharge alone, even when really into someone"

### Data Quality Adaptation

If either user has low data quality or uses cohort defaults:

**Exploratory mode:**
- Broader statements applicable to general personality types
- Frame as "likely to" or "tends to" (not definitive)
- Focus on universal dating scenarios vs hyper-specific interests

Example:
- Generic: "They reply within 5 minutes or they're not interested" ❌
- Adaptive: "They need alone time even when dating someone they really like" ✅

---

## Scoring

Each round = 3 statements × 4 vote options.

**Calculation:**
```csharp
score = statements.Count(s =>
    guesserVote[s.Id] == targetVote[s.Id]
);
```

Total possible: 6 points (3 per round × 2 rounds).

**Note:** Unlike Know Me, there's no "isCorrect" field. Alignment **is** the score.

---

## AI Insight Generation

After both rounds, the AI writes a **1-2 sentence playful insight** based on alignment scores.

**Prompt:**
```
Two people played a dating game: Red Flag / Green Flag.
They judged statements about each other as GREEN/YELLOW/RED/DEPENDS.
Scoring = alignment (how often their guess matched the person's self-rating).

User A alignment: 2/3
User B alignment: 1/3

Write a 1–2 sentence insight that feels playful, dating-app friendly, and encourages conversation.
No JSON. No emoji overload. Keep it punchy.
```

**Examples:**
- Both high: "You're reading each other pretty well — keep going."
- Both low: "Okay… some surprises here. Ask why they picked what they picked."
- Scores differ: "One of you is clocking the vibe better — ask what gave it away."
- Mystery dynamic: "Someone's harder to read — that's either mystery or chaos. Explore it."

---

## ECHO Signals Recorded

### 1. RedFlagGameDepth

**Formula:**
```csharp
depthValue = (float)completedRounds / totalRounds;
```

Logged for both users (bidirectional).

**What it measures:**
- Completion rate (did they finish or abandon?)
- Higher depth = more engagement in values/boundaries exploration

### 2. FlagAgreementRate

**Formula:**
```csharp
totalPossible = 3 * scoredRounds;
agreementRate = (float)sumOfScores / totalPossible;
```

**Example:**
- Round 1: 2/3 matches
- Round 2: 1/3 matches
- Total: 3/6 = 0.5

**What it measures:**
- How aligned are their perceptions of each other?
- High agreement = they see each other accurately
- Low agreement = misaligned expectations / discovery phase

**Where it's used:**
- `ConnectionScoreBatchWorker` aggregates this into overall connection score
- Future deck selection (prioritize matches with better mutual understanding)

---

## Fallback Behavior

If AI call fails, hardcoded fallback statements:

1. "They'll cancel plans last minute if they're drained — and tell you honestly." (EASY)
2. "They want their partner to have a full life outside the relationship." (MEDIUM)
3. "They'd rather have an awkward honest conversation than let tension sit." (HARD)

Generic but functional — ensures the game never breaks.

---

## Frontend UX

**Guesser view:**
- Role card: "Your Turn to Guess — Predict their reaction"
- Statements phrased about the target ("they")

**Target view:**
- Role card: "Your Turn to Answer — React to statements about you"
- Statements auto-converted to second person (same conversion logic as Know Me)

**Voting UI:**
- 4 horizontal buttons (GREEN/YELLOW/RED/DEPENDS)
- Selected option highlights with gradient background
- No "submit" until all 3 statements voted

---

## Example Session

**Round 1:**
- User A guesses how User B will react to statements about B
- Statement 1: "They need alone time even when really into someone"
  - A votes: GREEN ✅
  - B votes: GREEN ✅
  - Match ✅
- Statement 2: "They'll text back in 5 minutes or they're ghosting"
  - A votes: YELLOW ⚠️
  - B votes: RED 🚩
  - No match ❌
- Statement 3: "They judge dates by whether they'll try their cooking"
  - A votes: DEPENDS 🤷
  - B votes: GREEN ✅
  - No match ❌

**Round 1 score:** 1/3

**Round 2:**
- Roles flip, statements now about User A
- **Round 2 score:** 2/3

**Final:**
- A alignment: 1/3
- B alignment: 2/3
- Winner: User B
- Insight: "Someone's harder to read — that's either mystery or chaos. Explore it."

**ECHO signals logged:**
- Both: `RedFlagGameDepth = 1.0` (completed 2/2 rounds)
- Both: `FlagAgreementRate = 0.5` ((1+2) / 6)

---

## Design Decisions

### Why 4 Options?

- Binary (green/red) is too simplistic
- 4 options allow nuance without overwhelming
- "Depends" catches context-sensitive answers

### Why No "Correct" Answer?

- Flags are subjective — what's green to one is yellow to another
- The game is about **alignment**, not moral judgment
- Avoids making users feel "wrong" about their values

### Why No Scores Shown Mid-Game?

- Prevents anchoring (changing votes to match)
- Keeps it authentic
- Reveal at the end is more fun

---

## Known Issues / Future

- [ ] Statement cache (avoid repeating for same pair)
- [ ] Category breakdown ("You aligned on lifestyle, diverged on communication")
- [ ] Badge for "Perfect Alignment" (6/6)
- [ ] Animated reveal (score counting up)
