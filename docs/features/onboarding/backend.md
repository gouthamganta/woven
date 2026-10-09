# Onboarding Backend

**Last Updated:** 2026-10-07  
**Location:** `backend/WovenBackend/Endpoints/OnboardingEndpoints.cs`

---

## Overview

The onboarding backend provides 10 minimal API endpoints for user registration and profile setup. Each endpoint validates input, updates database entities, and advances the user's `ProfileStatus` enum.

**Key services:**
- `OnboardingEndpoints.cs` — HTTP endpoints (1121 lines)
- `FoundationalCycleService.cs` — Question set lifecycle management
- `FoundationalQuestionBank.cs` — Stable question bank (8 pillars, 6 questions)
- `OpenAiRewriteService.cs` — AI personalization of questions

**Security:**
- All endpoints require JWT auth (`[RequireAuthorization]`)
- User ID extracted via `EndpointHelper.GetUserId(ClaimsPrincipal)` (throws `UnauthorizedAccessException` if invalid)
- Intent reflection sentence encrypted with AES-256-GCM before DB storage

---

## OnboardingEndpoints.cs

**Pattern:** Minimal API route group, registered via `MapOnboardingEndpoints(WebApplication app)`

**Shared helper:**
```csharp
private static int GetUserId(ClaimsPrincipal user)
{
    // Primary: uid (your JWT)
    var uid = user.FindFirstValue("uid");
    if (int.TryParse(uid, out var id)) return id;

    // Fallback: sub / NameIdentifier
    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (int.TryParse(sub, out id)) return id;

    throw new UnauthorizedAccessException("Missing user id claim");
}
```

---

### Endpoints

#### 1. GET /onboarding/state

**Purpose:** Determine user's current onboarding step and next route  
**Auth:** Required  
**Returns:** `{ profileStatus, nextRoute, completed[] }`

**Logic:**
1. Load user from DB
2. If `ProfileStatus = COMPLETE`, check if foundational questions are due:
   - Calls `FoundationalCycleService.GetDueStateAsync(userId)`
   - If due, returns `{ profileStatus: "FOUNDATIONAL_DUE", nextRoute: "/onboarding/foundational", version, hardBlock, allowSkip }`
3. Otherwise, map `ProfileStatus` to next route:

```csharp
var nextRoute = u.ProfileStatus switch
{
    ProfileStatus.INCOMPLETE => "/onboarding/start",
    ProfileStatus.WELCOME_DONE => "/onboarding/basics",
    ProfileStatus.BASICS_DONE => "/onboarding/intent",
    ProfileStatus.INTENT_DONE => "/onboarding/foundational",
    ProfileStatus.FOUNDATION_DONE => "/onboarding/details",
    ProfileStatus.DETAILS_DONE => "/onboarding/review",
    ProfileStatus.COMPLETE => "/home",
    _ => "/onboarding/start"
};
```

**Completed array:**
```csharp
var completed = u.ProfileStatus switch
{
    ProfileStatus.INCOMPLETE => Array.Empty<string>(),
    ProfileStatus.WELCOME_DONE => new[] { "welcome" },
    ProfileStatus.BASICS_DONE => new[] { "welcome", "basics" },
    ProfileStatus.INTENT_DONE => new[] { "welcome", "basics", "intent" },
    ProfileStatus.FOUNDATION_DONE => new[] { "welcome", "basics", "intent", "foundational" },
    ProfileStatus.DETAILS_DONE => new[] { "welcome", "basics", "intent", "foundational", "details" },
    ProfileStatus.COMPLETE => new[] { "welcome", "basics", "intent", "foundational", "details", "review" },
    _ => Array.Empty<string>()
};
```

**Response:**
```json
{
  "profileStatus": "BASICS_DONE",
  "nextRoute": "/onboarding/intent",
  "completed": ["welcome", "basics"]
}
```

---

#### 2. POST /onboarding/welcome

**Purpose:** Mark welcome screen as complete  
**Auth:** Required  
**Body:** `{}`  
**Returns:** `{ profileStatus, nextRoute }`

**Logic:**
1. Load user
2. If `ProfileStatus < WELCOME_DONE`, set to `WELCOME_DONE`
3. Update `UpdatedAt`
4. Save to DB
5. Track analytics event: `OnboardingStepCompleted` with `{ step: "welcome" }`
6. Return `{ profileStatus: "WELCOME_DONE", nextRoute: "/onboarding/basics" }`

---

#### 3. PUT /onboarding/basics

**Purpose:** Save demographics and preferences  
**Auth:** Required  
**Body:** `BasicsRequest` (see DTOs below)  
**Returns:** `{ profileStatus, nextRoute }`

**Validation:**
- `Age >= 18`
- `DistanceMiles` between 15 and 100
- `AgeMin >= 18`, `AgeMax <= 99`, `AgeMin < AgeMax`
- `InterestedIn` not null/empty
- `Gender` not null/whitespace
- `Location.City`, `Location.State` not null/whitespace
- `Location.Lat`, `Location.Lng` not null
- Coordinates not (0,0), within valid ranges (-90 to 90, -180 to 180)

**Entities updated:**
- **User**: `FullName` (optional), `UpdatedAt`
- **UserProfile**: `Age`, `Gender`, `City`, `State`, `Lat`, `Lng`
- **UserPreference**: `DistanceMiles`, `AgeMin`, `AgeMax`, `InterestedInJson`, `RelationshipStructure`

**RelationshipStructure parsing:**
```csharp
var relationshipStructure = RelationshipStructure.OPEN;
if (!string.IsNullOrWhiteSpace(req.RelationshipStructure))
{
    if (Enum.TryParse<RelationshipStructure>(
        req.RelationshipStructure.Trim(),
        ignoreCase: true,
        out var parsed))
    {
        relationshipStructure = parsed;
    }
}
```

**ProfileStatus transition:**
```csharp
if (u.ProfileStatus < ProfileStatus.BASICS_DONE)
    u.ProfileStatus = ProfileStatus.BASICS_DONE;
```

---

#### 4. PUT /onboarding/photos

**Purpose:** Save 3–6 profile photos  
**Auth:** Required  
**Body:** `PhotosRequest { Photos: PhotoDto[] }`  
**Returns:** `{ message, count }`

**Validation:**
- `Photos.Length` between 3 and 6
- Each photo: `Url` not null/whitespace
- Each caption: ≤40 chars (if provided)

**Logic:**
1. Load user
2. Delete all existing `UserPhotos` for user
3. Create new `UserPhoto` entities from request (ordered by `SortOrder`)
4. Insert into DB
5. Update `User.UpdatedAt`
6. Return `{ message: "Photos saved", count: newPhotos.Count }`

**Note:** Does not advance `ProfileStatus` (photos saved independently of flow)

---

#### 5. PUT /onboarding/intent

**Purpose:** Save relationship intent and reflection  
**Auth:** Required  
**Body:** `IntentRequest { PrimaryIntent, Openness[], ReflectionSentence }`  
**Returns:** `{ profileStatus, nextRoute }`

**Validation:**
- `PrimaryIntent` not null/whitespace
- `ReflectionSentence` not null/whitespace, ≤200 chars
- `Openness` defaults to `[]` if null

**Entities updated:**
- **UserIntent**: `PrimaryIntent`, `OpennessJson`, `ReflectionSentence`

**ProfileStatus transition:**
```csharp
if (u.ProfileStatus < ProfileStatus.INTENT_DONE)
    u.ProfileStatus = ProfileStatus.INTENT_DONE;
```

**Encryption:**
`ReflectionSentence` is stored as **plaintext** in current implementation. See [intent-encryption.md](intent-encryption.md) for planned encryption flow.

---

#### 6A. GET /onboarding/foundational/questions

**Purpose:** Fetch or generate personalized foundational questions  
**Auth:** Required  
**Returns:** `{ version, questions[] }`

**Logic:**
1. Check if user is due for foundational questions via `FoundationalCycleService.GetDueStateAsync()`
2. If not due and no active set, return 400 error
3. Get active question set via `FoundationalCycleService.GetActiveAsync(userId)`
4. If `QuestionsJson` is null/empty/`"[]"`, fallback heal:
   - Try loading from `FoundationalQuestionBank.GetQuestionsForVersion(set.Version)`
   - Serialize as `{ id, text, pillars }[]`
   - Save to DB
5. Deserialize `QuestionsJson` to `FoundationalQuestionDto[]`
6. If malformed (not 5 questions, missing IDs/text), heal again from bank
7. Return `{ version, questions: [{ id, text }] }`

**Fallback questions (hardcoded):**
```csharp
var fallback = new[]
{
    new { id = "q1", text = "When you have a free evening, what do you usually crave doing most?", pillars = new[] { "Lifestyle", "Energy" } },
    new { id = "q2", text = "What kind of connection makes you feel most comfortable with someone new?", pillars = new[] { "Connection", "Attachment" } },
    new { id = "q3", text = "What's a small habit or routine that makes your life better?", pillars = new[] { "Habits", "Stability" } },
    new { id = "q4", text = "What's something you're proud of that doesn't show up on a resume?", pillars = new[] { "Identity", "SelfWorth" } },
    new { id = "q5", text = "What does a good relationship feel like to you in everyday moments?", pillars = new[] { "Relationship", "ConflictRepair" } }
};
```

---

#### 6. PUT /onboarding/foundational

**Purpose:** Submit answers to 5 foundational questions  
**Auth:** Required  
**Body:** `FoundationalRequest { Answers: [{ QuestionId, Answer }] }`  
**Returns:** `{ profileStatus, nextRoute }`

**Validation:**
- `Answers.Length` exactly 5
- Each answer: `QuestionId` and `Answer` not null/whitespace
- Each answer: ≤400 chars (trimmed)
- All `QuestionIds` must match stored question IDs (no duplicates, no invalid IDs)

**Logic:**
1. Load active question set
2. Deserialize stored `QuestionsJson`
3. Validate submitted `QuestionIds` match stored question IDs
4. Save answers as `{ id, a }[]` to `AnswersJson`
5. Set `AnsweredAt = DateTime.UtcNow`
6. Clear `DeferredUntil`
7. Compute `ExpiresAt` based on version:
   - v1: +15 days
   - v2: +45 days
   - v3+: +60 days
8. Update `User.ProfileStatus` to `FOUNDATION_DONE` (if not already higher)
9. Save to DB
10. Return `{ profileStatus, nextRoute }` (next = `/onboarding/details` or `/home` if profile complete)

**ExpiresAt calculation:**
```csharp
set.ExpiresAt = DateTime.UtcNow.AddDays(
    set.Version == 1 ? 15 :
    set.Version == 2 ? 45 :
    60
);
```

---

#### 7. POST /onboarding/foundational/defer

**Purpose:** Defer answering foundational questions (v2+ only)  
**Auth:** Required  
**Body:** `{}`  
**Returns:** `{ message, nextRoute }`

**Logic:**
1. Get active question set
2. If `Version == 1`, return 400 error ("v1 cannot be deferred")
3. Call `FoundationalCycleService.DeferActiveAsync(userId, TimeSpan.FromHours(24))`
4. Return `{ message: "Deferred", nextRoute: "/home" }`

**Defer mechanism:**
- Sets `DeferredUntil = DateTime.UtcNow + 24 hours`
- `GetDueStateAsync()` checks `DeferredUntil` before returning `due = true`

---

#### 8. PUT /onboarding/details

**Purpose:** Save bio, optional fields, weekly vibe, pronouns, accessibility  
**Auth:** Required  
**Body:** `DetailsRequest` (see DTOs)  
**Returns:** `{ profileStatus, nextRoute }`

**Validation:**
- `Bio` not null/whitespace, ≤200 chars
- Optional fields: keys must be in allowed lists (details or preferences)
- `DisplayPronouns` ≤50 chars (if provided)

**Allowed detail keys (visibility: Public or user-specified):**
```csharp
{ "job", "education", "school", "pets", "habits", "hobbies", 
  "children", "languages", "zodiac", "diet", "hometown" }
```

**Allowed preference keys (visibility: MatchingOnly):**
```csharp
{ "pref_ethnicity", "pref_religion", "pref_height", "pref_work", 
  "pref_smoking", "pref_drinking", "pref_workout" }
```

**Entities updated:**
- **UserOptionalFields**: All deleted (except `bio`), then re-created from request
- **UserOptionalField (bio)**: Upsert with `visibility = Public`
- **UserWeeklyVibe**: Upsert with `ExpiresAt = +7 days` (if provided, else deleted)
- **UserProfile**: `DisplayPronouns` (if provided)
- **UserPreference**: `ReduceMotion`, `HighContrast` (if provided)

**ProfileStatus transition:**
```csharp
if (u.ProfileStatus < ProfileStatus.DETAILS_DONE)
    u.ProfileStatus = ProfileStatus.DETAILS_DONE;
```

---

#### 9. GET /onboarding/review

**Purpose:** Preview all collected data before completion  
**Auth:** Required  
**Returns:** `ReviewResponse` (complex nested object)

**Response structure:**
```json
{
  "profileStatus": "DETAILS_DONE",
  "self": {
    "fullName": "...",
    "email": "...",
    "profilePhoto": "...",
    "basics": { "age": 25, "gender": "woman", "location": {...}, "distanceMiles": 25, "interestedIn": [...], "relationshipStructure": "OPEN" },
    "photos": [{ "url": "...", "caption": "...", "sortOrder": 1 }],
    "intent": { "primaryIntent": "...", "openness": [...], "reflectionSentence": "..." },
    "foundational": { "version": 1, "answers": [{ "id": "q1", "a": "..." }], "qa": [{ "id": "q1", "q": "...", "a": "..." }] },
    "details": { "bio": "...", "weeklyVibe": "...", "optionalFields": [...], "preferenceFields": [...] }
  },
  "publicPreview": {
    "name": "...",
    "age": 25,
    "gender": "woman",
    "location": "City, State",
    "bio": "...",
    "isVerified": false,
    "displayPronouns": "...",
    "intent": { "primaryIntent": "...", "openness": [...] },
    "photos": [{ "url": "...", "caption": "...", "sortOrder": 1 }],
    "optionalPublic": [{ "key": "job", "value": "..." }]
  }
}
```

**Logic:**
1. Load user, profile, preferences, photos, intent, foundational set (latest answered), optional fields, weekly vibe
2. Deserialize `InterestedInJson`, `OpennessJson`
3. Parse `FoundationalSet.AnswersJson` (robust parser handles `{ id, a }`, `{ questionId, answer }`, `{ Answer }`)
4. Parse `FoundationalSet.QuestionsJson`
5. Join questions + answers into `qa[]` array
6. Split optional fields into `detailOptional` (non-`pref_*`) and `preferenceFields` (`pref_*`)
7. Filter public-only fields for `publicPreview.optionalPublic`
8. Return nested response

**Foundational answer parsing (defensive):**
```csharp
// Handles multiple JSON shapes:
// { "id": "q1", "a": "..." }
// { "questionId": "q1", "answer": "..." }
// { "id": "q1", "Answer": "..." }

if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
    id = idProp.GetString();

if (el.TryGetProperty("a", out var aProp) && aProp.ValueKind == JsonValueKind.String)
    a = aProp.GetString();

if (string.IsNullOrWhiteSpace(id) &&
    el.TryGetProperty("questionId", out var qidProp) &&
    qidProp.ValueKind == JsonValueKind.String)
    id = qidProp.GetString();

if (string.IsNullOrWhiteSpace(a) &&
    el.TryGetProperty("answer", out var ansProp) &&
    ansProp.ValueKind == JsonValueKind.String)
    a = ansProp.GetString();

if (string.IsNullOrWhiteSpace(a) &&
    el.TryGetProperty("Answer", out var ansProp2) &&
    ansProp2.ValueKind == JsonValueKind.String)
    a = ansProp2.GetString();
```

---

#### 10. POST /onboarding/complete

**Purpose:** Finalize onboarding, trigger bootstrap vector generation  
**Auth:** Required  
**Body:** `{}`  
**Returns:** `{ profileStatus, nextRoute }`

**Validation (pre-flight checks):**
- UserProfile exists
- UserPreference exists
- UserPhotos count ≥ 3
- UserIntent exists
- UserFoundationalQuestionSet exists with `AnsweredAt != null`
- Bio (in UserOptionalFields with key = "bio") not null/whitespace

**Logic:**
1. Validate all required data exists (return 400 if any missing)
2. Set `User.ProfileStatus = COMPLETE`
3. Update `User.UpdatedAt`
4. Save to DB
5. **Trigger background vector build** (non-blocking):
   ```csharp
   try
   {
       var scopeFactory = http.RequestServices.GetRequiredService<IServiceScopeFactory>();
       var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();

       _ = Task.Run(async () =>
       {
           try
           {
               using var scope = scopeFactory.CreateScope();
               var vectorBuilder = scope.ServiceProvider
                   .GetRequiredService<IUserVectorBuilder>();

               await vectorBuilder.BuildAndSaveV1Async(userId, CancellationToken.None);
           }
           catch (Exception ex)
           {
               logger.LogError(ex, "[Onboarding] Failed to build vector for user {UserId}", userId);
           }
       });
   }
   catch
   {
       // Silent fail - vector build is non-critical for onboarding
   }
   ```
6. Track analytics event: `OnboardingStepCompleted` with `{ step: "complete" }`
7. Return `{ profileStatus: "COMPLETE", nextRoute: "/home" }`

**Vector build notes:**
- Runs in new scope to avoid capturing scoped DbContext from request
- Failures logged but do not block onboarding completion
- Vector can be rebuilt later via admin tools if needed

---

## DTOs

**BasicsRequest:**
```csharp
public record BasicsRequest(
    string? FullName,
    int Age,
    string Gender,
    string[] InterestedIn,
    int DistanceMiles,
    int AgeMin,
    int AgeMax,
    LocationDto Location,
    string? RelationshipStructure // optional (defaults to OPEN)
);

public record LocationDto(string City, string State, double? Lat, double? Lng);
```

**PhotosRequest:**
```csharp
public record PhotoDto(string Url, string? Caption, int SortOrder);
public record PhotosRequest(PhotoDto[] Photos);
```

**IntentRequest:**
```csharp
public record IntentRequest(
    string PrimaryIntent,
    string[] Openness,
    string ReflectionSentence
);
```

**FoundationalRequest:**
```csharp
public record FoundationalQuestionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text
);

public record FoundationalAnswerDto(string QuestionId, string Answer);
public record FoundationalRequest(FoundationalAnswerDto[] Answers);
```

**DetailsRequest:**
```csharp
public record OptionalFieldDto(string Key, string Value, VisibilityLevel Visibility);

public record DetailsRequest(
    string Bio,
    OptionalFieldDto[] OptionalFields,
    string? WeeklyVibe,
    string? DisplayPronouns,
    bool? ReduceMotion,
    bool? HighContrast
);
```

**Review DTOs:**
```csharp
public record PublicPreviewIntentDto(string PrimaryIntent, string[] Openness);
public record PublicPreviewPhotoDto(string Url, string? Caption, int SortOrder);
public record PublicOptionalFieldPublicDto(string Key, string Value);

public record FoundationalStoredAnswerDto(string Id, string A);
public record FoundationalQaDto(string Id, string Q, string A);
```

---

## FoundationalCycleService.cs

**Location:** `backend/WovenBackend/Services/FoundationalCycleService.cs`  
**Purpose:** Manage lifecycle of foundational question sets (versioning, eligibility, deferral)

**Dependencies:**
- `WovenDbContext` — DB access
- `OpenAiRewriteService` — Question personalization

**Constants:**
```csharp
private const int V1_INTERVAL_DAYS = 15;     // v2 eligible after v1 + 15 days
private const int V2_INTERVAL_DAYS = 45;     // v3 eligible after v2 + 45 days
private const int V3PLUS_INTERVAL_DAYS = 60; // v4+ eligible after v3 + 60 days
```

---

### Methods

#### GetDueStateAsync(userId, ct)
**Returns:** `(bool due, int? version, bool hardBlock)`

**Logic:**
1. Check for active unanswered set (ordered by version DESC)
2. If active set exists:
   - If `DeferredUntil` is set and not expired, return `(false, null, false)`
   - If `QuestionsJson` is empty, heal from bank (no OpenAI call here)
   - Return `(true, version, hardBlock)` where `hardBlock = (version == 1)`
3. Load last answered set (ordered by version DESC)
4. If no history, create v1 set and return `(true, 1, true)`
5. If `DateTime.UtcNow < lastAnswered.ExpiresAt`, return `(false, null, false)` (not eligible yet)
6. Create next version set, return `(true, nextVersion, false)` (soft-block)

**Important:** Does **not** call OpenAI during state check (performance). Questions healed from static bank if missing.

---

#### GetActiveAsync(userId, ct)
**Returns:** `UserFoundationalQuestionSet?`

Query for unanswered set, ordered by version DESC.

---

#### DeferActiveAsync(userId, duration, ct)
**Returns:** `Task`

**Logic:**
1. Get active set
2. If `Version == 1`, exit (v1 cannot defer)
3. Set `DeferredUntil = DateTime.UtcNow + duration`
4. Update `UpdatedAt`
5. Save to DB

---

#### ComputeNextEligibleAt(version, answeredAtUtc)
**Returns:** `DateTime`

Helper to compute `ExpiresAt` based on version:
```csharp
var days = version switch
{
    1 => V1_INTERVAL_DAYS,
    2 => V2_INTERVAL_DAYS,
    _ => V3PLUS_INTERVAL_DAYS
};

return answeredAtUtc.AddDays(days);
```

---

#### CreateSet(userId, version, ct) — **private**

**Logic:**
1. Load question bank via `FoundationalQuestionBank.GetQuestionsForVersion(version)`
2. Load user context (first name, gender, primary intent)
3. Call `OpenAiRewriteService.RewriteAsync(bank, userContext, style)` to personalize questions
4. Create `UserFoundationalQuestionSet` entity:
   ```csharp
   new UserFoundationalQuestionSet
   {
       UserId = userId,
       Version = version,
       QuestionsJson = JsonSerializer.Serialize(
           rewritten.Select(q => new { id = q.Id, text = q.Text, pillars = q.Pillars })
       ),
       AnswersJson = "[]",
       SignalsJson = "{}",
       CreatedAt = DateTime.UtcNow,
       UpdatedAt = DateTime.UtcNow,
       ExpiresAt = DateTime.UtcNow, // placeholder, set on answer time
       AnsweredAt = null,
       DeferredUntil = null
   }
   ```
5. Insert to DB

**User context extraction:**
```csharp
var userProfile = await _db.UserProfiles
    .Include(p => p.User)
    .Where(p => p.UserId == userId)
    .Select(p => new { FullName = p.User.FullName, p.Gender })
    .FirstOrDefaultAsync(ct);

var firstName = ExtractFirstName(userProfile?.FullName);

var intent = await _db.UserIntents
    .Where(i => i.UserId == userId)
    .Select(i => i.PrimaryIntent)
    .FirstOrDefaultAsync(ct);

var style = "warm, human, dating app";
```

---

## FoundationalQuestionBank.cs

**Location:** `backend/WovenBackend/Services/FoundationalQuestionBank.cs`  
**Purpose:** Stable question bank with canonical IDs and pillar mappings

**Model:**
```csharp
public record BankQuestion(string Id, string Text, string[] Pillars);
```

**Canonical 8 pillars:**
```csharp
public static readonly string[] CanonicalPillars = new[]
{
    "Lifestyle", "Energy", "Values", "Communication", 
    "Ambition", "Stability", "Curiosity", "Affection"
};
```

---

### GetQuestionsForVersion(version)
**Returns:** `BankQuestion[]`

**6 questions cover all 8 canonical pillars with intentional overlap:**

| ID | Text | Pillars |
|----|------|---------|
| q1 | When you have a free evening, what do you usually crave doing most? | Lifestyle, Energy |
| q2 | What kind of connection makes you feel most comfortable with someone new? | Communication, Affection |
| q3 | What's a small habit or routine that genuinely makes your life better? | Lifestyle, Stability |
| q4 | What's something you're proud of that doesn't show up on a resume? | Values, Curiosity |
| q5 | What does a good relationship feel like to you in everyday moments? | Affection, Communication |
| q6 | What are you working toward right now that genuinely excites you? | Ambition, Curiosity |

**Implementation:**
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

**Note:** Currently all versions use the same 6 questions. Future versions can rotate phrasing or pillar coverage **without changing IDs**.

**Pillar coverage:**
- Lifestyle: q1, q3
- Energy: q1
- Communication: q2, q5
- Affection: q2, q5
- Stability: q3
- Values: q4
- Curiosity: q4, q6
- Ambition: q6

**Missing from v1 bank:** None — all 8 pillars covered (though q6 was added to cover Ambition).

---

## OpenAiRewriteService.cs

**Purpose:** Personalize question text using GPT-4.1-mini  
**Method:** `RewriteAsync(BankQuestion[] bank, RewriteUserContext context, string style, ct)`

**User context:**
```csharp
public record RewriteUserContext(
    string? FirstName,
    string? Gender,
    string? PrimaryIntent,
    int UserId
);
```

**Prompt template:**
```
You are rewriting dating app questions to feel more personal.

User context:
- Name: {FirstName ?? "there"}
- Gender: {Gender ?? "person"}
- Intent: {PrimaryIntent ?? "connection"}

Style: {style}

Rewrite these questions to feel warmer and more personal:
[bank questions as JSON]

Return ONLY valid JSON array of { id, text, pillars }.
```

**OpenAI call:**
```csharp
var response = await _openAi.ChatAsync(new OpenAiRequest(
    Model: "gpt-4.1-mini",
    Messages: messages,
    MaxTokens: 800,
    Purpose: "foundational_question_rewrite"
), ct);
```

**Fallback:** On AI failure, return bank questions as-is (no personalization).

**Rate limiting:** Uses centralised `IOpenAiClient` with exponential backoff + jitter.

---

## Entity Models

**User:**
```csharp
public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string? FullName { get; set; }
    public ProfileStatus ProfileStatus { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum ProfileStatus
{
    INCOMPLETE,
    WELCOME_DONE,
    BASICS_DONE,
    INTENT_DONE,
    FOUNDATION_DONE,
    DETAILS_DONE,
    COMPLETE
}
```

**UserProfile:**
```csharp
public class UserProfile
{
    public int UserId { get; set; }
    public int Age { get; set; }
    public string Gender { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string? DisplayPronouns { get; set; }
}
```

**UserPreference:**
```csharp
public class UserPreference
{
    public int UserId { get; set; }
    public int DistanceMiles { get; set; }
    public int AgeMin { get; set; }
    public int AgeMax { get; set; }
    public string InterestedInJson { get; set; } // ["men", "women", ...]
    public RelationshipStructure RelationshipStructure { get; set; }
    public bool ReduceMotion { get; set; }
    public bool HighContrast { get; set; }
}

public enum RelationshipStructure
{
    OPEN,
    MONOGAMOUS,
    POLYAMOROUS,
    EXPLORING
}
```

**UserPhoto:**
```csharp
public class UserPhoto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Url { get; set; }
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}
```

**UserIntent:**
```csharp
public class UserIntent
{
    public int UserId { get; set; }
    public string PrimaryIntent { get; set; }
    public string OpennessJson { get; set; } // ["long_term", "friendship", ...]
    public string ReflectionSentence { get; set; }
}
```

**UserFoundationalQuestionSet:**
```csharp
public class UserFoundationalQuestionSet
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Version { get; set; } // 1, 2, 3, ...
    public string QuestionsJson { get; set; } // [{ id, text, pillars }]
    public string AnswersJson { get; set; } // [{ id, a }]
    public string SignalsJson { get; set; } // Future: extracted signals
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ExpiresAt { get; set; } // When next version becomes eligible
    public DateTime? AnsweredAt { get; set; }
    public DateTime? DeferredUntil { get; set; }
}
```

**UserOptionalField:**
```csharp
public class UserOptionalField
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
    public VisibilityLevel Visibility { get; set; }
}

public enum VisibilityLevel
{
    Public,           // Shown on profile
    MatchingOnly,     // Used for matching, not displayed
    Private           // Never shown to other users
}
```

**UserWeeklyVibe:**
```csharp
public class UserWeeklyVibe
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Text { get; set; }
    public DateTime ExpiresAt { get; set; } // +7 days from creation
}
```

---

## Design Patterns

### 1. Progressive Status Tracking
Each endpoint advances `ProfileStatus` only if user is not already past that step. This allows users to edit earlier steps without breaking flow.

### 2. Idempotent Updates
PUT endpoints delete + recreate related entities (photos, optional fields) rather than merging. This simplifies logic and ensures clean state.

### 3. Defensive JSON Parsing
Review endpoint handles multiple JSON shapes for foundational answers (`{ id, a }`, `{ questionId, answer }`, `{ Answer }`). This tolerates schema evolution.

### 4. Background Task Safety
Vector build uses `IServiceScopeFactory` to create new scope, avoiding captured DbContext. Failures logged but do not block completion.

### 5. Auto-Healing Questions
If `QuestionsJson` is empty/malformed, endpoints auto-heal from `FoundationalQuestionBank` rather than failing. This prevents user-facing errors from data corruption.

---

## Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| "No active set" error | User deferred, but backend doesn't track | Check `DeferredUntil` in DB |
| v1 cannot defer | Hard-block enforced | Return 400 with clear message |
| Question IDs mismatch | Frontend sent wrong IDs | Validate against stored `QuestionsJson` |
| Vector build fails | OpenAI timeout or DB lock | Log error, allow retry via admin tools |
| Coordinates (0,0) rejected | Frontend sent placeholder | Validate `lat != 0 || lng != 0` |

---

## Security

- **JWT auth required** on all endpoints
- **User ID claim validation** via `EndpointHelper.GetUserId()` (throws on invalid)
- **Intent encryption** planned (AES-256-GCM) — see [intent-encryption.md](intent-encryption.md)
- **No raw SQL** — all queries via EF Core (parameterized)
- **No mass assignment** — DTOs map explicitly to entities

---

## Future Enhancements

- **Horoscope field** in basics (designed, not wired)
- **Voice transcription** for foundational answers
- **Multi-language question rewriting** (detect user's locale)
- **Signal extraction** from answers (populate `SignalsJson`)
- **Admin endpoint** to trigger vector rebuild
