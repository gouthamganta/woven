# ECHO Agents

ECHO uses multiple specialized AI agents for games, coaching, explanations, and narrator enrichment. All agents call OpenAI via a centralized client with correlation tracking, rate limiting, and structured logging.

---

## Agent Roster

| Agent | Purpose | Model | Temperature | Max Tokens | When It Runs |
|---|---|---|---|---|---|
| **KnowMeAgent** | Generates guessing game questions | gpt-4.1-mini | 0.7 (default) | 500 | On-demand (game session start) |
| **RedGreenFlagAgent** | Generates red/green flag statements | gpt-4.1-mini | 0.7 (default) | 500 | On-demand (game session start) |
| **MatchExplanationService** | Writes 2-sentence match explanations | gpt-4.1-mini | 0.7 | 400 | Nightly (deck generation) |
| **MatchNarratorService** | Generates cinematic intro TTS | tts-1 (voice: nova) | N/A | N/A | Nightly (deck generation, optional) |
| **CoachingSummaryWorker** | Weekly coaching summaries | gpt-4.1-mini | 0.8 | 200 | Wednesday 18:00 UTC |

---

## 1. KnowMeAgent

**File:** `backend/WovenBackend/Services/Games/KnowMeAgent.cs`  
**Interface:** `IGameAgent`

### What It Does
Generates 3 multiple-choice questions for the **Know Me** game. One user guesses answers about the other user based on their profile data.

### Algorithm
1. **Loads target user's vector** (the person being guessed about)
2. **Builds enhanced prompt** with:
   - Age, top 3 pillars, tags/interests
   - Energy level, social capacity
   - Diet, workout preferences
   - Match context (shared tags, aligned pillars, shared hobbies)
   - Difficulty (EASY/MEDIUM/HARD)
   - Tone (PLAYFUL/THOUGHTFUL/BALANCED)
3. **Calls OpenAI** with JSON mode enabled
4. **Parses response** into 3 questions, each with 4 options (A/B/C/D), one correct

### Prompt Structure
```
TARGET PERSON:
- Age: 28
- Top traits: Energy (0.72), Values (0.68), Curiosity (0.65)
- Interests: hiking, cooking, photography
- Energy level: 0.72 (0=chill, 1=very active)
- Social capacity: 0.58 (0=introverted, 1=very social)
- Diet: vegetarian
- Workout: 3-4x/week

MATCH CONTEXT:
- Shared interests: hiking, photography
- Aligned values: Curiosity, Energy
- Intent alignment: both looking for serious relationship

GAME PARAMETERS:
- Difficulty: MEDIUM
- Tone: BALANCED

Generate 3 questions that MUST reference THIS person's actual interests/values:
1. EASY (~80% guessable)
2. MEDIUM (~50% guessable)
3. HARD (~30% guessable)

BANNED PATTERNS:
- "What's their weekend vibe?" (too generic)
- "What's their coffee order?" (overused)
- "Going out vs staying in?" (cliche)

INSTEAD: Reference THEIR actual interests.
```

### Data Quality Handling
If either user has `DataQuality.LOW` or `UsedCohortDefaults == true`:
- Uses **exploratory approach** (broader questions, fewer assumptions)
- Phrases as "tendencies" not facts
- Focuses on general lifestyle patterns vs niche interests

### Fallback
If OpenAI call fails or parsing errors:
```csharp
new QuestionData
{
    Text = "How do they prefer to recharge after a long week?",
    Options = [
        "Solo time at home",
        "Out with close friends", // Correct
        "Something active outdoors",
        "Doesn't need to decompress"
    ]
}
```

### Post-Game Insight
After both players complete their rounds, `GenerateInsightAsync` produces a 1-2 sentence playful reflection:
- Both scored 2/3: "You both got 2/3 - reading each other pretty well already."
- Mismatch: "One of you is better at this than the other. Interesting."
- Perfect: "Perfect scores! Either you're psychic or you actually listen."

---

## 2. RedGreenFlagAgent

**File:** `backend/WovenBackend/Services/Games/RedGreenFlagAgent.cs`  
**Interface:** `IGameAgent`

### What It Does
Generates 3 dating behavior statements for the **Red/Green Flag** game. Both users judge each statement (GREEN/YELLOW/RED/DEPENDS), then compare alignment.

### Algorithm
Same as KnowMeAgent, but generates statements instead of questions:
1. Loads target user's vector
2. Builds enhanced prompt with pillars, tags, lifestyle
3. Calls OpenAI (JSON mode)
4. Returns 3 statements (EASY/MEDIUM/HARD difficulty)

Each statement has 4 fixed options:
- GREEN ✅
- YELLOW ⚠️
- RED 🚩
- DEPENDS 🤷

Scoring = alignment (how often player's guess matches target's self-rating).

### Prompt Structure
```
TARGET PERSON:
- Age: 28
- Top traits: Energy (0.72), Values (0.68)
- Interests: hiking, cooking
- Energy: 0.72, Social: 0.58

STATEMENT RULES:
1) MUST reference THIS person's actual traits/tags/hobbies
2) No heavy topics (trauma, exes, politics, religion, mental health, sex)
3) Mix:
   - one light lifestyle habit (from their interests)
   - one social/communication habit (from social capacity/energy)
   - one dating preference vibe
4) Short (1 sentence), provocative/fun

BANNED TOPICS:
- 'Texting speed' or 'reply time' (overused)
- 'Ghosting' or 'leaving on read' (negative)
- 'Coffee preferences' (boring)
- 'Exes' (too heavy)

INSTEAD use THEIR pillars/tags:
- High energy: "They need plans every weekend or feel restless"
- Likes cooking: "They judge dates by whether they'll try their cooking"
- Introverted: "They recharge alone, even when really into someone"
```

### Fallback Statements
```csharp
"They'll cancel plans last minute if drained — and tell you honestly."
"They want their partner to have a full life outside the relationship."
"They'd rather have an awkward honest conversation than let tension sit."
```

### Post-Game Insight
```
Alignment 2/3: "You're reading each other pretty well — keep going."
Mismatch: "One of you is clocking the vibe better — ask what gave it away."
Low alignment: "Some surprises here. Ask why they picked what they picked."
```

---

## 3. MatchExplanationService

**File:** `backend/WovenBackend/Services/Matchmaking/MatchExplanationService.cs`  
**Interface:** `IMatchExplanationService`

### What It Does
Generates a **2-sentence explanation** for why a candidate appears in a user's daily deck.

Called by `DailyDeckOrchestrator` for all 5 selected candidates.

### Algorithm
1. **Loads MatchScore breakdown** (16 component scores)
2. **Identifies top 2-3 scoring dimensions** (e.g., IntentScore=92, PillarScore=85, LifestyleScore=78)
3. **Maps dimensions to human language**:
   - IntentScore → "You're both looking for something serious"
   - PillarScore → "You share core values around [pillar names]"
   - LifestyleScore → "Your day-to-day rhythms align"
4. **Calls OpenAI** with structured prompt
5. **Stores in MatchExplanations table** linked to candidate + date

### Prompt Structure
```
You are explaining why this person appeared in their daily deck.

VIEWER:
- Age: 27
- Top values: Curiosity (0.82), Energy (0.75)
- Looking for: serious relationship

CANDIDATE:
- Age: 28
- Top values: Curiosity (0.78), Values (0.72)
- Looking for: serious relationship

COMPATIBILITY SCORES (out of 100):
- IntentScore: 92 (both looking for serious, high commitment readiness)
- PillarScore: 85 (shared: Curiosity, Energy)
- LifestyleScore: 78 (diet: both vegetarian, workout: both 3-4x/week)

BUCKET: CORE_FIT

Write 2 sentences explaining why this is a good match.
- Reference actual data (not generic)
- No hyperbole ("perfect match", "soulmate")
- Conversational tone
- No emojis
```

### Example Outputs
```
"You're both looking for something serious and value depth over surface-level chat. 
Your shared curiosity and similar energy levels suggest good rhythm potential."

"Your lifestyles align — both vegetarian, both active 3-4x/week. 
And you're matched on intent: neither of you is here to waste time."
```

### Storage
```sql
INSERT INTO MatchExplanations (
    ViewerUserId,
    CandidateId,
    ExplanationText,
    Bucket,
    ScoreSnapshot,
    DateUtc
)
```

### Optional: Bridge Question
If `BridgeQuestion` field is populated (future feature), it adds a conversation starter:
- "Ask them: What's a skill you'd love to learn but haven't made time for yet?"

---

## 4. MatchNarratorService

**File:** `backend/WovenBackend/Services/Matchmaking/MatchNarratorService.cs`  
**Interface:** `IMatchNarratorService`

### What It Does
Enriches deck items with **cinematic intro fields**:
- **KenBurnsPhotoUrls** — up to 3 profile photos (Ken Burns zoom/pan animation)
- **CuratedQuote** — shortest bio/answer text ≥20 chars, trimmed to 120
- **NarrationUrl** — TTS audio of the quote (mp3)
- **NarrationExposed** — always `false` (used for 30-day baseline filtering in WeightLearning)

### Algorithm
1. **Load up to 3 profile photos** (sorted by SortOrder)
2. **Pick curated quote**:
   - Candidates: bio + foundational question answers
   - Filter: ≥20 chars
   - Sort: shortest first (more punchy)
   - Trim: max 120 chars, append "…" if truncated
3. **Generate TTS** (if quote exists + OpenAI key configured):
   - Check Azure Blob cache: `tts/{candidateId}/{yyyyMMdd}.mp3`
   - If missing: call OpenAI TTS API
   - Upload to blob with 48h TTL
   - Return public URL

### TTS Call
```http
POST https://api.openai.com/v1/audio/speech
Authorization: Bearer {apiKey}

{
  "model": "tts-1",
  "input": "I'm most at peace when I'm hiking alone at sunrise.",
  "voice": "nova",
  "response_format": "mp3"
}
```

**Voice:** `nova` (warm, neutral, conversational)

### Caching
- Blob path: `tts/{candidateId}/{yyyyMMdd}.mp3`
- TTL: 48 hours (blob lifecycle policy)
- Cache hit avoids API call (saves cost + latency)

### Frontend Behavior
- If `NarrationUrl == null` → Ken Burns animation plays silently
- If `NarrationUrl != null` → Ken Burns + audio plays on card flip

### Baseline Protection
`NarrationExposed` is always `false` until voice launch. WeightLearningService filters out ConnectionScores where `NarrationExposed == true` for 30 days post-launch to prevent ECHO weights from being biased by the cinematic experience itself.

---

## 5. CoachingSummaryWorker

**File:** `backend/WovenBackend/Services/Coaching/CoachingSummaryWorker.cs`

See [coach.md](./coach.md) for full details.

**Quick summary:**
- Runs Wednesday 18:00 UTC
- Generates 3-5 sentence coaching reflection per qualified user
- Model: `gpt-4.1-mini`, temp 0.8
- Tone: warm, perceptive friend (no metrics, no comparisons)
- Eligibility: ≥14 days, ≥3 deck interactions/week, not opted out

---

## Centralized OpenAI Client

**File:** `backend/WovenBackend/Services/OpenAiClient.cs`  
**Interface:** `IOpenAiClient`

All ECHO agents call OpenAI via this client.

### Features
- **Correlation ID** on every request (X-Correlation-ID header + User-Agent)
- **Exponential backoff** on 429/5xx (3 retries: 1s → 4s → 12s, with jitter)
- **Respects Retry-After** header on 429
- **Structured token logging** per call: `Model, Purpose, Tokens (prompt/completion/total), CorrelationId`
- **Rate limit guard** (ASP.NET Core rate limiter "openai-global")

### Methods
```csharp
Task<OpenAiChatResponse> ChatAsync(OpenAiRequest request, CancellationToken ct = default);
Task<float[]?> EmbedAsync(string text, int dimensions, CancellationToken ct = default);
Task<byte[]?> TtsAsync(string text, string voice = "nova", CancellationToken ct = default);
```

### Request Structure
```csharp
var response = await _openAi.ChatAsync(new OpenAiRequest(
    Model: "gpt-4.1-mini",
    Messages: [
        new("system", systemPrompt),
        new("user", userPrompt)
    ],
    MaxTokens: 400,
    Temperature: 0.7f,
    Purpose: "match_explanation"  // for log tracing
), ct);
```

### Logging
```
[OpenAI] Chat request | Model=gpt-4.1-mini Purpose=match_explanation MaxTokens=400 CorrelationId=a3f7b2c1d4e5f6g7
[OpenAI] Chat complete | Model=gpt-4.1-mini Purpose=match_explanation Tokens=287 (prompt=195 completion=92) CorrelationId=a3f7b2c1d4e5f6g7
```

### Error Handling
- **429 (rate limit)** → exponential backoff, throw `OpenAiException` after 3 retries
- **5xx (server error)** → retry with backoff
- **4xx (client error)** → throw immediately (don't retry)
- **Network timeout** → retry once

---

## Resilient Client Wrapper (Games)

**File:** `backend/WovenBackend/Services/Games/IOpenAiResilientClient.cs`

Game agents use `IOpenAiResilientClient` which wraps `IOpenAiClient` with:
- **Circuit breaker** (after 5 consecutive failures, open circuit for 30s)
- **Cost tracking** (logs token usage per game type)
- **Timeout enforcement** (10s max per call)

This prevents cascading failures if OpenAI is degraded during high game traffic.

---

## Agent Configuration

**appsettings.json:**
```json
{
  "OpenAI": {
    "ApiKey": "OVERRIDE_IN_USER_SECRETS_OR_KEYVAULT"
  },
  "Cinematic": {
    "NarrationLaunchDate": null  // Set when voice narration goes live
  }
}
```

**Model Constants:**
- Chat: `gpt-4.1-mini`
- Embeddings: `text-embedding-3-small` (128 or 512 dimensions)
- TTS: `tts-1`, voice `nova`

---

## Testing Agents

**Game Agents:**
- Spawn a test game session via `POST /games/sessions`
- Call `GenerateRoundAsync` with mock `GameContext`
- Assert: 3 questions/statements, each with valid options
- Validate: no banned patterns in question text

**Match Explanation:**
- Generate a mock `MatchScore` with known component values
- Call `GenerateAndSaveExplanationAsync`
- Assert: 2 sentences, no hyperbole, references actual data

**Narrator:**
- Mock candidate with 3 photos + bio
- Call `BuildNarratorFieldsAsync`
- Assert: 3 KenBurnsPhotoUrls, CuratedQuote ≤120 chars, NarrationUrl (if TTS succeeds)

**Coaching:**
- Mock user with 7 days of signals
- Call `CoachingSummaryWorker.ProcessUserAsync`
- Assert: 3-5 sentences, warm tone, no numbers shown

---

## See Also

- [coach.md](./coach.md) — Weekly coaching system details
- [overview.md](./overview.md) — ECHO architecture
- [configuration.md](./configuration.md) — appsettings.json structure
