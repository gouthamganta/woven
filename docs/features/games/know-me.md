# Know Me — Personality Guessing Game

**Game Type:** `KNOW_ME`  
**Icon:** 🎯  
**Duration:** ~2 minutes  
**Rounds:** 2 (role swap)

---

## Concept

A two-player guessing game where users predict how their match would answer questions about themselves.

**Example:**
> Question: "What's their go-to trail snack?"  
> Options: A) Energy bars B) Fresh fruit C) Trail mix D) Chocolate

The guesser picks what they think the target chose. The target picks their actual answer. Score = number of matches.

---

## Game Flow

### Round 1

1. **Initiator** sees: "Your Turn to Guess — Predict their answers"
2. **Partner** sees: "Your Turn to Answer — Be honest about yourself"
3. AI generates 3 personalized questions (EASY → MEDIUM → HARD)
4. Guesser submits predictions
5. Target submits actual answers
6. Score calculated: matches out of 3

### Round 2

Roles flip. Same flow, new questions.

### Final Result

- Total scores displayed (A: X/6, B: Y/6)
- Winner or tie declared
- AI-generated insight ("You both got 2/3 — reading each other pretty well already.")

---

## Question Generation (AI Agent)

**Agent:** `KnowMeAgent.cs`  
**Model:** GPT-4.1-mini

### Input Context

The AI receives:
- **Target user's ECHO profile:**
  - Age
  - Top 3 pillars (e.g., Energy: 0.82, Lifestyle: 0.75)
  - Tags/interests (hiking, cooking, reading)
  - Energy level (0-1)
  - Social capacity (0-1)
  - Diet, workout habits
- **Pair context** (if available):
  - Shared tags
  - Aligned pillars
  - Shared hobbies
  - Intent alignment score
- **Difficulty level:** EASY/MEDIUM/HARD
- **Tone:** PLAYFUL/BALANCED/THOUGHTFUL

### Question Difficulty

| Level | Guessability | Example |
|---|---|---|
| EASY | ~80% from profile | "How do they prefer to recharge after a long week?" |
| MEDIUM | ~50% (read between lines) | "When they disagree with someone, what do they do first?" |
| HARD | ~30% (surprising reveal) | "What's their hidden skill no one expects?" |

### Anti-Generic Rules

The AI is **banned** from using these overused patterns:
- "What's their weekend vibe?"
- "What's their coffee order?"
- "How do they handle stress?"
- "Going out vs staying in?"
- "What's their ideal Saturday?"

Instead, questions **must reference actual interests/tags:**
- If they like hiking: "What's their go-to trail snack?"
- If they're creative: "What would they spend 3 hours making?"
- If they're high energy: "What sport would they try on a dare?"

### Data Quality Adaptation

If either user has:
- **Low data quality** (incomplete profile)
- **Cohort defaults used** (not enough behavioral data)

The AI switches to **exploratory mode:**
- Broader questions that work for most people
- Phrased as tendencies, not facts
- Focus on general patterns vs niche interests
- Example: "What **might** they enjoy?" vs "What do they do?"

This ensures new users get playable games without enough profile depth.

---

## Scoring

Each round has 3 questions × 4 options.

**Calculation:**
```csharp
score = questions.Count(q =>
    guesserAnswers[q.Id] == targetAnswers[q.Id]
);
```

Total possible: 6 points (3 per round × 2 rounds).

---

## AI Insight Generation

After both rounds complete, the AI generates a **1-2 sentence playful insight** based on scores.

**Examples:**
- Both got 2/3: "You both got it — seems like you're paying attention."
- Scores differ: "One of you is reading the other better. Interesting dynamic."
- Perfect scores: "Perfect scores! Either you're psychic or you actually listen."
- Both low: "Both missed some — might want to ask more questions."

**Prompt sent to AI:**
```
Two people played a guessing game about each other.

User A score: 2/3
User B score: 3/3

Generate a 1-2 sentence insight that's playful and insightful.

Return just the text, no JSON.
```

---

## ECHO Signals Recorded

**Signal:** `KnowMeDisclosureDepth`

**Formula:**
```csharp
depthValue = (float)completedRounds / totalRounds;
```

**Logged for both users** (bidirectional signal).

**What it measures:**
- Did they complete the game or abandon it?
- Higher depth = more engagement in getting to know each other
- Feeds into ECHO's connection score calculation

**Where it's used:**
- `ConnectionScoreBatchWorker` (nightly aggregation)
- Future match scoring adjustments

---

## Fallback Behavior

If the AI call fails (network error, rate limit, bad response):

**Fallback questions** (hardcoded):
1. "How do they prefer to recharge after a long week?"
2. "When they disagree with someone they care about, what do they do first?"
3. "What does a good first date look like to them?"

These are generic but functional — ensures the game never breaks.

---

## Frontend UX

**Guesser view:**
- Role card: "Your Turn to Guess — Predict their answers"
- Questions phrased in third person ("their", "they")

**Target view:**
- Role card: "Your Turn to Answer — Be honest about yourself"
- Questions auto-converted to second person ("your", "you")

**Conversion logic:**
```typescript
text.replace(/\btheir\b/g, 'your')
    .replace(/\bthey\b/g, 'you')
    .replace(/\bthem\b/g, 'you')
```

---

## Example Session

**Round 1:**
- User A (initiator) guesses about User B
- Q1: "What's their go-to way to unwind?" → A picks "Long walk", B picks "Long walk" ✅
- Q2: "What would they binge-watch?" → A picks "Mystery", B picks "Comedy" ❌
- Q3: "What's their morning ritual?" → A picks "Coffee", B picks "Workout" ❌
- **Round 1 score:** 1/3

**Round 2:**
- User B guesses about User A
- Questions generated fresh (personalized to A's profile)
- **Round 2 score:** 2/3

**Final:**
- A: 1/3 + 2/3 as target = 1/3 guesser score
- B: 2/3 + 1/3 as target = 2/3 guesser score
- Winner: User B
- Insight: "One of you is reading the other better — ask them what gave it away."

**ECHO signal logged:**
- Both users: `KnowMeDisclosureDepth = 1.0` (completed 2/2 rounds)

---

## Known Issues / Future

- [ ] Question cache (avoid repeating same questions for same pair)
- [ ] Animated score reveal (currently instant)
- [ ] Badge for "Perfect Round" (3/3)
- [ ] Game history in profile ("You've played 5 games")
