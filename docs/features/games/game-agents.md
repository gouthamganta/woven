# Game Agents — AI Content Generation

**Status:** SHIPPED  
**Last Updated:** 2026-08-17

---

## Architecture

Games use a **factory pattern** to select the appropriate AI agent based on game type.

```csharp
IGameAgent agent = _agentFactory.GetAgent(session.GameType);
GameRoundData round = await agent.GenerateRoundAsync(context, ct);
string insight = await agent.GenerateInsightAsync(context, rounds, ct);
```

**Factory:** `GameAgentFactory.cs`  
**Interface:** `IGameAgent.cs`

---

## IGameAgent Interface

All game agents implement this contract:

```csharp
public interface IGameAgent
{
    Task<GameRoundData> GenerateRoundAsync(GameContext context, CancellationToken ct = default);
    Task<string> GenerateInsightAsync(GameContext context, List<RoundResult> rounds, CancellationToken ct = default);
}
```

### GameContext

The context object passed to agents contains:

```csharp
public class GameContext
{
    public Guid SessionId { get; set; }
    public Guid MatchId { get; set; }
    public int UserAId { get; set; }
    public int UserBId { get; set; }
    public int CurrentRound { get; set; }
    public int GuesserUserId { get; set; }
    public int TargetUserId { get; set; }
    
    // ECHO profile data
    public UserVectorData? UserAVector { get; set; }
    public UserVectorData? UserBVector { get; set; }
    
    // Personalization metadata
    public PairContext? PairContext { get; set; }
    public MatchBucketType Bucket { get; set; }
    public double IntentAlignment { get; set; }
    public GameDifficulty Difficulty { get; set; }
    public GameTone Tone { get; set; }
}
```

### UserVectorData

Per-user ECHO profile data:

```csharp
public class UserVectorData
{
    public int UserId { get; set; }
    public int Age { get; set; }
    public List<string> Tags { get; set; } = new();              // ["hiking", "cooking", ...]
    public Dictionary<string, double> Pillars { get; set; } = new();  // { "Energy": 0.82, ... }
    public Dictionary<string, double> Pulse { get; set; } = new();    // { "socialCapacity": 0.65, ... }
    public Dictionary<string, string> Lifestyle { get; set; } = new(); // { "diet": "vegetarian", ... }
}
```

---

## KnowMeAgent

**File:** `backend/WovenBackend/Services/Games/KnowMeAgent.cs`

### GenerateRoundAsync

**Inputs:**
- Target user's ECHO profile (person being guessed about)
- Pair context (shared tags, aligned pillars)
- Difficulty level (EASY/MEDIUM/HARD)
- Tone (PLAYFUL/BALANCED/THOUGHTFUL)

**Outputs:**
```csharp
public class GameRoundData
{
    public List<QuestionData> Questions { get; set; } = new();  // Always 3 questions
    public int TimeLimit { get; set; } = 90; // seconds
}

public class QuestionData
{
    public string Id { get; set; }           // "q1", "q2", "q3"
    public string Text { get; set; }         // Question text
    public List<OptionData> Options { get; set; }  // 4 options (A, B, C, D)
    public string Difficulty { get; set; }   // "EASY" | "MEDIUM" | "HARD"
    public string Category { get; set; }     // "lifestyle" | "personality" | "communication"
}

public class OptionData
{
    public string Id { get; set; }      // "a", "b", "c", "d"
    public string Text { get; set; }    // Option text
    public bool IsCorrect { get; set; } // ✅ Used for fallback only (AI doesn't mark correct)
}
```

### AI Prompt Strategy

**Prompt structure:**
1. **Target person data** (age, pillars, tags, energy, social capacity, diet, workout)
2. **Match context** (shared interests, aligned values, shared hobbies)
3. **Game parameters** (difficulty, tone)
4. **Data quality guidance** (exploratory mode if low data)
5. **Anti-generic rules** (banned question patterns)
6. **Requirements** (3 questions, 4 options each, personalized)

**Example prompt snippet:**
```
TARGET PERSON (the one being guessed about):
- Age: 27
- Top traits: Energy (0.82), Lifestyle (0.75), Communication (0.68)
- Interests/Tags: hiking, cooking, reading, yoga
- Energy level: 0.82 (0=chill, 1=very active)
- Social capacity: 0.65 (0=introverted, 1=very social)
- Diet: vegetarian
- Workout: daily yoga

MATCH CONTEXT (what these two people share):
- Shared interests: hiking, reading
- Aligned values: Communication, Energy

GAME PARAMETERS:
- Difficulty: MEDIUM - Mix of lifestyle and values questions. Some require reading between the lines.
- Tone: BALANCED - Conversational and warm. Mix of fun and meaningful.

Generate 3 questions that MUST reference THIS person's actual interests/values.
```

**Anti-generic rules enforced:**
- ❌ "What's their weekend vibe?"
- ❌ "What's their coffee order?"
- ❌ "How do they handle stress?"
- ✅ "What's their go-to trail snack?" (if they like hiking)
- ✅ "What would they spend 3 hours making?" (if creative)

### GenerateInsightAsync

**Inputs:**
- Both users' scores (A: X/3, B: Y/3)

**Output:**
- 1-2 sentence playful insight

**Example prompt:**
```
Two people played a guessing game about each other.

User A score: 2/3
User B score: 3/3

Generate a 1-2 sentence insight that's playful and insightful.

Examples:
- "You both got 2/3 - reading each other pretty well already."
- "One of you is better at this than the other. Interesting."
- "Perfect scores! Either you're psychic or you actually listen."

Return just the text, no JSON.
```

**Fallback logic:**
```csharp
if (userAScore == userBScore)
    return userAScore >= 2
        ? "You both got it - seems like you're paying attention."
        : "Both missed some - might want to ask more questions.";
else
    return "One of you is reading the other better. Interesting dynamic.";
```

---

## RedGreenFlagAgent

**File:** `backend/WovenBackend/Services/Games/RedGreenFlagAgent.cs`

### GenerateRoundAsync

**Inputs:**
- Target user's ECHO profile
- Pair context
- Tone (PLAYFUL/BALANCED/THOUGHTFUL)

**Outputs:**
Same `GameRoundData` structure, but:
- **Questions** = statements (not questions)
- **Options** = always the same 4 flags:
  - `{ id: "GREEN", text: "Green flag ✅" }`
  - `{ id: "YELLOW", text: "Yellow flag ⚠️" }`
  - `{ id: "RED", text: "Red flag 🚩" }`
  - `{ id: "DEPENDS", text: "Depends 🤷" }`

### AI Prompt Strategy

**Prompt structure:**
1. **Target person data** (same as KnowMe)
2. **Match context**
3. **Tone guidance**
4. **Data quality guidance**
5. **Statement rules** (mix of lifestyle/social/dating)
6. **Anti-generic rules** (banned topics)
7. **Heavy topics banned** (trauma, exes, politics, religion, medical)

**Example prompt snippet:**
```
TARGET PERSON (use their actual data):
- Age: 27
- Top traits: Energy (0.82), Lifestyle (0.75)
- Interests/Tags: hiking, cooking, yoga
- Energy (0-1): 0.82
- Social capacity (0-1): 0.65

TONE GUIDANCE: Conversational and warm. Mix of fun and meaningful.

STATEMENT RULES:
1) Each statement MUST reference THIS person's actual traits/tags/hobbies
2) No heavy topics (trauma, exes, politics, religion, medical/mental health, explicit sex)
3) Mix:
   - one light lifestyle habit (based on their actual interests)
   - one social/communication habit (based on their social capacity/energy)
   - one dating preference vibe habit
4) Each statement should be short (1 sentence) and a little provocative/fun

CRITICAL ANTI-GENERIC RULES:
NEVER use these banned topics/patterns:
- 'Texting speed' or 'reply time' (overused)
- 'Ghosting' or 'leaving on read' (negative)
- 'Coffee preferences' (boring)
- 'Exes' or past relationships (too heavy)

INSTEAD use THEIR actual pillars/tags/hobbies. For example:
- If they're high energy: "They need plans every weekend or they feel restless"
- If they like cooking: "They judge dates by whether they'll try their cooking"
```

**Expected JSON response:**
```json
{
  "statements": [
    { "text": "They'll cancel plans last minute if drained — and tell you honestly.", "difficulty": "EASY" },
    { "text": "They want their partner to have a full life outside the relationship.", "difficulty": "MEDIUM" },
    { "text": "They'd rather have an awkward honest conversation than let tension sit.", "difficulty": "HARD" }
  ]
}
```

### GenerateInsightAsync

**Inputs:**
- Both users' alignment scores (A: X/3, B: Y/3)

**Output:**
- 1-2 sentence playful insight

**Example prompt:**
```
Two people played a dating game: Red Flag / Green Flag.
They judged statements about each other as GREEN/YELLOW/RED/DEPENDS.
Scoring = alignment (how often their guess matched the person's self-rating).

User A alignment: 2/3
User B alignment: 1/3

Write a 1–2 sentence insight that feels playful, dating-app friendly, and encourages conversation.
No JSON. No emojis overload. Keep it punchy.
```

**Fallback logic:**
```csharp
if (userAScore == userBScore)
    return userAScore >= 2
        ? "You're reading each other pretty well — keep going."
        : "Okay… some surprises here. Ask why they picked what they picked.";
else
    return "One of you is clocking the vibe better — ask what gave it away.";
```

---

## OpenAI Client Integration

Both agents use the centralized `IOpenAiClient` (not direct HttpClient).

**Call pattern:**
```csharp
var response = await _openAi.ExecuteAsync(
    purpose: "game-knowme",       // or "game-redflag"
    prompt: prompt,
    useJson: true,                // or false for insight generation
    ct: ct
);
```

**What ExecuteAsync provides:**
- Exponential backoff + jitter (3 retries: 1s / 4s / 12s)
- 429 handling (respects Retry-After header)
- X-Correlation-ID on every request
- Token usage logging
- Circuit breaker (future)
- Cost tracking (future)

**Model:** `gpt-4.1-mini` (from appsettings)

---

## Difficulty Determination

**Calculated by `GameService.DetermineDifficulty(PairContext)`:**

```csharp
private GameDifficulty DetermineDifficulty(PairContext? pairContext)
{
    if (pairContext == null)
        return GameDifficulty.MEDIUM;

    var alignedCount = pairContext.AlignedPillars.Count;

    if (alignedCount >= 3)
        return GameDifficulty.HARD;   // High alignment = harder questions

    if (alignedCount >= 1)
        return GameDifficulty.MEDIUM;

    return GameDifficulty.EASY;       // Low alignment = easier questions
}
```

**Rationale:**
- High pillar alignment → they already know each other well → ask harder questions
- Low alignment → just getting to know each other → keep it easy/fun

---

## Tone Determination

**Calculated by `GameService.DetermineTone(PairContext)`:**

```csharp
private GameTone DetermineTone(PairContext? pairContext)
{
    if (pairContext == null)
        return GameTone.BALANCED;

    var userTone = pairContext.UserProfile.ConversationTone;
    var candidateTone = pairContext.CandidateProfile.ConversationTone;

    if (userTone == "playful" && candidateTone == "playful")
        return GameTone.PLAYFUL;

    if (userTone == "thoughtful" || candidateTone == "thoughtful" ||
        userTone == "calm" || candidateTone == "calm")
        return GameTone.THOUGHTFUL;

    return GameTone.BALANCED;
}
```

**Tone mapping:**
- Both playful → PLAYFUL game
- Either thoughtful/calm → THOUGHTFUL game
- Default → BALANCED

---

## Fallback Behavior

If the AI call fails (network error, rate limit, bad JSON):

**KnowMe fallback questions:**
1. "How do they prefer to recharge after a long week?"
2. "When they disagree with someone they care about, what do they do first?"
3. "What does a good first date look like to them?"

**RedGreenFlag fallback statements:**
1. "They'll cancel plans last minute if they're drained — and tell you honestly."
2. "They want their partner to have a full life outside the relationship."
3. "They'd rather have an awkward honest conversation than let tension sit."

These are **generic but functional** — ensures the game never breaks even if OpenAI is down.

---

## Error Handling

```csharp
try
{
    var parsed = JsonSerializer.Deserialize<OpenAiQuestionsResponse>(response,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (parsed?.Questions != null && parsed.Questions.Count >= 3)
    {
        return new GameRoundData { Questions = ... };
    }
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "[KnowMe] Failed to parse OpenAI response");
}

return GenerateFallbackRound();
```

**Logging includes:**
- Correlation ID (from middleware)
- Session ID
- Round number
- Difficulty, Tone
- Error messages

---

## Future Enhancements

- [ ] **Question cache:** Store generated questions to avoid repeating for same pair
- [ ] **A/B testing:** Track which difficulty/tone combinations get highest completion rates
- [ ] **Multi-language support:** Translate prompts + questions
- [ ] **Custom game types:** User-submitted game templates
- [ ] **Adaptive difficulty:** Adjust mid-game based on performance
