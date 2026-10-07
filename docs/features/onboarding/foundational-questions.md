# Foundational Questions System

**Last Updated:** 2026-08-17

---

## Overview

The foundational questions system is the **core pillar assessment instrument** for ECHO matching. Every user answers 5 AI-generated questions during onboarding (version 1) and periodically thereafter (versions 2+) to keep their compatibility vector current.

**Key principle:** Questions are **stable instruments** (canonical IDs + semantic meaning) that are **rewritten for personalization** (user's name, gender, intent) while preserving pillar coverage.

---

## The 8 Pillars

ECHO uses **8 canonical pillars** to model compatibility:

| Pillar | What It Measures |
|--------|------------------|
| **Lifestyle** | Daily routines, activity preferences, home vs. out |
| **Energy** | Social battery, pace of life, intensity vs. calm |
| **Values** | Core beliefs, what matters most, life priorities |
| **Communication** | How they connect, conflict style, emotional expression |
| **Ambition** | Drive, goals, relationship to work/achievement |
| **Stability** | Routine vs. spontaneity, planning, risk tolerance |
| **Curiosity** | Learning, growth, intellectual engagement |
| **Affection** | Physical touch, emotional intimacy, love languages |

These pillars are **not shown to users** — they're internal ECHO signals.

---

## Question Bank Architecture

### File: `FoundationalQuestionBank.cs`

**Purpose:** Source of truth for all foundational questions. Defines 6 stable questions that cover all 8 pillars.

```csharp
public static BankQuestion[] GetQuestionsForVersion(int version)
{
    return new[]
    {
        new BankQuestion(
            Id: "q1",
            Text: "When you have a free evening, what do you usually crave doing most?",
            Pillars: new[] { "Lifestyle", "Energy" }
        ),
        new BankQuestion(
            Id: "q2",
            Text: "What kind of connection makes you feel most comfortable with someone new?",
            Pillars: new[] { "Communication", "Affection" }
        ),
        new BankQuestion(
            Id: "q3",
            Text: "What's a small habit or routine that genuinely makes your life better?",
            Pillars: new[] { "Lifestyle", "Stability" }
        ),
        new BankQuestion(
            Id: "q4",
            Text: "What's something you're proud of that doesn't show up on a resume?",
            Pillars: new[] { "Values", "Curiosity" }
        ),
        new BankQuestion(
            Id: "q5",
            Text: "What does a good relationship feel like to you in everyday moments?",
            Pillars: new[] { "Affection", "Communication" }
        ),
        new BankQuestion(
            Id: "q6",
            Text: "What are you working toward right now that genuinely excites you?",
            Pillars: new[] { "Ambition", "Curiosity" }
        )
    };
}
```

**Why 6 questions when users answer 5?**

Currently all 6 are used (v1 uses questions 1–6). The system is designed to support **rotation** in future versions where different combinations of 5 questions are selected from the bank.

---

## Pillar Coverage

Each question maps to **1–2 pillars** to ensure no single question is overloaded:

| Question | Pillars |
|----------|---------|
| q1 | Lifestyle, Energy |
| q2 | Communication, Affection |
| q3 | Lifestyle, Stability |
| q4 | Values, Curiosity |
| q5 | Affection, Communication |
| q6 | Ambition, Curiosity |

**Coverage summary:**
- Lifestyle: 2 questions (q1, q3)
- Energy: 1 question (q1)
- Values: 1 question (q4)
- Communication: 2 questions (q2, q5)
- Ambition: 1 question (q6)
- Stability: 1 question (q3)
- Curiosity: 2 questions (q4, q6)
- Affection: 2 questions (q2, q5)

**Note:** Ambition currently has the **least coverage** (1 question). This is a known gap — future versions may add a dedicated Ambition question.

---

## Personalization via OpenAI Rewrite

### File: `FoundationalCycleService.cs`

When a user reaches the foundational step, the system:

1. **Loads bank questions** (canonical base text)
2. **Fetches user context:**
   - First name (extracted from `User.FullName`)
   - Gender (from `UserProfile.Gender`)
   - Primary intent (from `UserIntent.PrimaryIntent`)
3. **Calls OpenAI** to rewrite each question in a warm, personalized tone

**Rewrite call** (FoundationalCycleService.cs:139–145):
```csharp
var rewritten = await _openAi.RewriteAsync(
    bank,
    new OpenAiRewriteService.RewriteUserContext(firstName, userProfile?.Gender, intent, userId),
    style: "warm, human, dating app",
    ct
);
```

**Example transformation:**

**Base (bank):**
> "When you have a free evening, what do you usually crave doing most?"

**Rewritten for "Priya" (woman, long-term intent):**
> "Priya, when you have a free evening, what do you find yourself craving most — solitude, adventure, or connection?"

**Why rewrite?**
- **Personalization** → Feels like the app knows you
- **Gender-appropriate phrasing** → Avoids awkward pronoun mismatches
- **Intent alignment** → Questions subtly adapt to user's stated relationship goals

---

## Question Set Lifecycle

### Creation

When a user reaches `/onboarding/foundational` for the **first time**, `FoundationalCycleService.CreateSet()` is called:

1. Loads bank questions for version 1
2. Rewrites with user context (OpenAI call)
3. Creates `UserFoundationalQuestionSet` row:
   ```csharp
   {
       UserId: userId,
       Version: 1,
       QuestionsJson: JSON.serialize(rewritten),  // Frozen rewritten text
       AnswersJson: "[]",
       AnsweredAt: null,
       ExpiresAt: DateTime.UtcNow,  // Set properly after answer
       DeferredUntil: null
   }
   ```

**Important:** `QuestionsJson` is **frozen** at creation time. Users always see the exact questions that were rewritten for them, even if they defer and return later.

---

### Answering

When user submits answers (`PUT /onboarding/foundational`):

1. **Validates:**
   - Exactly 5 answers
   - Each answer 30–400 chars (frontend enforces 30 min, backend enforces 400 max)
   - Answer IDs match question IDs
2. **Stores answers:**
   ```csharp
   AnswersJson = JSON.serialize([
       { id: "q1", a: "I usually crave quiet time..." },
       { id: "q2", a: "A connection that feels..." },
       // ...
   ]);
   ```
3. **Sets expiration:**
   ```csharp
   AnsweredAt = DateTime.UtcNow;
   ExpiresAt = DateTime.UtcNow.AddDays(version == 1 ? 15 : version == 2 ? 45 : 60);
   ```

**Why different expiration periods?**
- **v1 (onboarding):** 15 days → Encourages early re-engagement
- **v2:** 45 days → Users are established, less frequent updates
- **v3+:** 60 days → Long-term users, stable profiles

---

## Recurring Cycles

**After onboarding**, users are prompted to re-answer foundational questions on a schedule controlled by `ExpiresAt`.

### Due State Logic

`FoundationalCycleService.GetDueStateAsync()` determines if a user should see the foundational prompt:

```csharp
// 1. Check for active unanswered set
var active = await _db.UserFoundationalQuestionSets
    .Where(x => x.UserId == userId && x.AnsweredAt == null)
    .OrderByDescending(x => x.Version)
    .FirstOrDefaultAsync(ct);

if (active != null) {
    // If deferred, don't redirect until deferral ends
    if (active.DeferredUntil.HasValue && DateTime.UtcNow < active.DeferredUntil.Value)
        return (due: false, version: null, hardBlock: false);

    // v1 = hard-block, v2+ = soft-block
    var hard = active.Version == 1;
    return (due: true, version: active.Version, hardBlock: hard);
}

// 2. Check last answered set
var lastAnswered = await _db.UserFoundationalQuestionSets
    .Where(x => x.UserId == userId && x.AnsweredAt != null)
    .OrderByDescending(x => x.Version)
    .FirstOrDefaultAsync(ct);

// 3. No history => create v1 now
if (lastAnswered == null) {
    await CreateSet(userId, version: 1, ct);
    return (due: true, version: 1, hardBlock: true);
}

// 4. Not yet eligible
if (DateTime.UtcNow < lastAnswered.ExpiresAt)
    return (due: false, version: null, hardBlock: false);

// 5. Create next version
var nextVersion = lastAnswered.Version + 1;
await CreateSet(userId, nextVersion, ct);
return (due: true, version: nextVersion, hardBlock: false);
```

### Hard-Block vs. Soft-Block

| Version | Block Type | Can Defer? | Behavior |
|---------|------------|------------|----------|
| v1 | Hard-block | No | User **cannot access app** until answered |
| v2+ | Soft-block | Yes (24h) | User sees prompt, can defer via `POST /onboarding/foundational/defer` |

**Defer logic** (FoundationalCycleService.cs:89–100):
```csharp
public async Task DeferActiveAsync(int userId, TimeSpan duration, CancellationToken ct)
{
    var active = await GetActiveAsync(userId, ct);
    if (active == null) return;

    // v1 cannot defer
    if (active.Version == 1) return;

    active.DeferredUntil = DateTime.UtcNow.Add(duration);
    active.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

---

## Auto-Healing

If a `UserFoundationalQuestionSet` row has **empty or invalid `QuestionsJson`**, the `GET /onboarding/foundational/questions` endpoint auto-heals from the bank:

```csharp
// OnboardingEndpoints.cs:468–483
if (string.IsNullOrWhiteSpace(set.QuestionsJson) || set.QuestionsJson == "[]")
{
    var fallback = new[] {
        new { id = "q1", text = "When you have a free evening...", pillars = new[] { "Lifestyle", "Energy" } },
        // ... (hardcoded stable questions)
    };

    set.QuestionsJson = JsonSerializer.Serialize(fallback);
    set.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
}
```

**Why hardcoded fallback instead of calling bank?**

Safety. If the question bank service fails, users still get valid questions. The fallback is **v1 questions**, which cover all pillars.

---

## Answer Extraction for Matching

Once answers are stored, they feed into the **ECHO vector** via `UserVectorBuilder.BuildAndSaveV1Async()`.

The `OpenAiTaggingService` extracts:

1. **Pillar scores** (0.0–1.0 for each pillar):
   ```json
   {
       "Lifestyle": 0.72,
       "Energy": 0.45,
       "Values": 0.89,
       "Communication": 0.61,
       "Ambition": 0.34,
       "Stability": 0.58,
       "Curiosity": 0.77,
       "Affection": 0.82
   }
   ```

2. **Tags** (keywords per pillar):
   ```json
   {
       "Lifestyle": ["homebody", "cooking", "quiet"],
       "Energy": ["low-key", "recharge-alone"],
       "Values": ["family", "honesty", "growth"],
       // ...
   }
   ```

These are stored in `UserVector.VectorJson` and `UserVector.PillarEmbedding` (pgvector column for cosine similarity searches).

---

## Example User Flow

1. **User completes Intent step** → `ProfileStatus = INTENT_DONE`
2. **User navigates to `/onboarding/foundational`**
3. **Frontend calls `GET /onboarding/foundational/questions`:**
   - Server checks: No active set exists
   - Creates v1 set, rewrites questions with user context
   - Returns 5 personalized questions
4. **User answers questions one-by-one** (frontend UI)
5. **User clicks "Submit"** → `PUT /onboarding/foundational` with all 5 answers
6. **Server validates, saves answers:**
   ```sql
   UPDATE user_foundational_question_sets
   SET answers_json = '[ { "id": "q1", "a": "..." }, ... ]',
       answered_at = NOW(),
       expires_at = NOW() + INTERVAL '15 days'
   WHERE user_id = 123 AND version = 1;
   ```
7. **ProfileStatus advances** → `FOUNDATION_DONE`
8. **User proceeds to Details step**

---

## Database Schema

```sql
CREATE TABLE user_foundational_question_sets (
    id SERIAL PRIMARY KEY,
    user_id INT NOT NULL REFERENCES users(id),
    version INT NOT NULL,  -- 1, 2, 3, ...
    
    questions_json TEXT NOT NULL,  -- Frozen rewritten questions
    answers_json TEXT NOT NULL,    -- User's answers
    
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    answered_at TIMESTAMPTZ,       -- NULL until submitted
    expires_at TIMESTAMPTZ,        -- When next version is due
    deferred_until TIMESTAMPTZ,    -- NULL or defer deadline
    
    UNIQUE(user_id, version)
);

CREATE INDEX idx_foundational_user_answered
ON user_foundational_question_sets(user_id, answered_at);
```

---

## Future Enhancements

1. **Question rotation** — Select 5 from a pool of 10–12 bank questions (preserves pillar coverage)
2. **Dynamic pillar weights** — Adjust which pillars get more questions based on user's match feedback
3. **Voice answers** — Record answers via voice, transcribe + analyze tone/emotion
4. **Multi-language support** — Rewrite questions in user's preferred language
5. **Adaptive follow-ups** — Ask clarifying questions if initial answer is too vague

---

## Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| Questions are generic | OpenAI rewrite skipped | Check `QuestionsJson` — should have personalized text |
| "No active set" error | Set created but not fetched | Ensure `GET /foundational/questions` is called before PUT |
| Stuck on foundational | User deferred but UI still shows | Check `DeferredUntil` column, should be NULL or past |
| Auto-heal not working | Hardcoded fallback missing | Verify `OnboardingEndpoints.cs:468–483` logic |
| Pillar scores missing | Vector build failed | Check `[VectorBuilder]` logs after onboarding completion |
