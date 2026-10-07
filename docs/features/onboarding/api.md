# Onboarding API Reference

**Last Updated:** 2026-10-07  
**Base URL:** `http://localhost:5135` (dev) | `https://api.wooven.me` (prod)  
**Auth:** All endpoints require JWT Bearer token

---

## Overview

The Onboarding API provides 10 endpoints for user registration and profile setup. All endpoints require authentication via JWT token in the `Authorization: Bearer <token>` header.

**Endpoint prefix:** `/onboarding`

**Response format:** JSON  
**Error format:** `{ "error": "message", "correlationId": "...", "timestamp": "..." }`

---

## Endpoints

### 1. GET /onboarding/state

**Purpose:** Get user's current onboarding progress and next step

**Auth:** Required  
**Method:** GET  
**Rate limit:** None

**Request:**
```http
GET /onboarding/state
Authorization: Bearer <jwt>
```

**Response 200 (Normal flow):**
```json
{
  "profileStatus": "BASICS_DONE",
  "nextRoute": "/onboarding/intent",
  "completed": ["welcome", "basics"]
}
```

**Response 200 (Foundational due after onboarding):**
```json
{
  "profileStatus": "FOUNDATIONAL_DUE",
  "nextRoute": "/onboarding/foundational",
  "version": 2,
  "hardBlock": false,
  "allowSkip": true
}
```

**ProfileStatus values:**
- `INCOMPLETE` → `/onboarding/start`
- `WELCOME_DONE` → `/onboarding/basics`
- `BASICS_DONE` → `/onboarding/intent`
- `INTENT_DONE` → `/onboarding/foundational`
- `FOUNDATION_DONE` → `/onboarding/details`
- `DETAILS_DONE` → `/onboarding/review`
- `COMPLETE` → `/home` (or `/onboarding/foundational` if due)
- `FOUNDATIONAL_DUE` → `/onboarding/foundational` (recurring)

**Fields:**
- `profileStatus` (string) — Current status
- `nextRoute` (string) — Where to navigate next
- `completed` (string[]) — List of completed steps
- `version` (int, optional) — Foundational question version if due
- `hardBlock` (bool, optional) — If true, user cannot skip foundational
- `allowSkip` (bool, optional) — If true, user can defer foundational for 24h

**Errors:**
- `401` — Unauthorized (missing/invalid JWT)

---

### 2. POST /onboarding/welcome

**Purpose:** Mark welcome screen as viewed

**Auth:** Required  
**Method:** POST

**Request:**
```http
POST /onboarding/welcome
Authorization: Bearer <jwt>
Content-Type: application/json

{}
```

**Response 200:**
```json
{
  "profileStatus": "WELCOME_DONE",
  "nextRoute": "/onboarding/basics"
}
```

**Errors:**
- `401` — Unauthorized

**Side effects:**
- Sets `User.ProfileStatus = WELCOME_DONE` (if not already higher)
- Updates `User.UpdatedAt`
- Tracks analytics event: `OnboardingStepCompleted { step: "welcome" }`

---

### 3. PUT /onboarding/basics

**Purpose:** Save demographics and preferences

**Auth:** Required  
**Method:** PUT

**Request:**
```http
PUT /onboarding/basics
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "fullName": "Alex",
  "age": 25,
  "gender": "woman",
  "interestedIn": ["men", "women"],
  "distanceMiles": 25,
  "ageMin": 21,
  "ageMax": 35,
  "location": {
    "city": "Hyderabad",
    "state": "Telangana",
    "lat": 17.385,
    "lng": 78.4867
  },
  "relationshipStructure": "OPEN"
}
```

**Request fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| `fullName` | string | No | ≤50 chars |
| `age` | int | Yes | ≥18 |
| `gender` | string | Yes | Non-empty |
| `interestedIn` | string[] | Yes | Non-empty array |
| `distanceMiles` | int | Yes | 15–100 |
| `ageMin` | int | Yes | ≥18, < ageMax |
| `ageMax` | int | Yes | ≤99, > ageMin |
| `location.city` | string | Yes | Non-empty |
| `location.state` | string | Yes | Non-empty |
| `location.lat` | double | Yes | -90 to 90, not 0 if lng is 0 |
| `location.lng` | double | Yes | -180 to 180, not 0 if lat is 0 |
| `relationshipStructure` | string | No | OPEN / MONOGAMOUS / POLYAMOROUS / EXPLORING (defaults to OPEN) |

**Valid gender values:**
`"man"`, `"woman"`, `"nonbinary"`, `"transgender"`, `"genderfluid"`, `"other"`, `"prefer_not"`

**Valid interestedIn values:**
`"men"`, `"women"`, `"nonbinary"`, `"everyone"`

**Response 200:**
```json
{
  "profileStatus": "BASICS_DONE",
  "nextRoute": "/onboarding/intent"
}
```

**Errors:**
- `400` — Validation error (age < 18, distance out of range, invalid coordinates, etc.)
- `401` — Unauthorized

**Side effects:**
- Creates/updates `UserProfile` (Age, Gender, City, State, Lat, Lng)
- Creates/updates `UserPreference` (DistanceMiles, AgeMin, AgeMax, InterestedInJson, RelationshipStructure)
- Updates `User.FullName` (if provided)
- Sets `User.ProfileStatus = BASICS_DONE`
- Updates `User.UpdatedAt`
- Tracks analytics event: `OnboardingStepCompleted { step: "basics" }`

---

### 4. PUT /onboarding/photos

**Purpose:** Upload 3–6 profile photos

**Auth:** Required  
**Method:** PUT

**Request:**
```http
PUT /onboarding/photos
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "photos": [
    {
      "url": "data:image/jpeg;base64,...",
      "caption": "Hiking in the mountains",
      "sortOrder": 1
    },
    {
      "url": "data:image/jpeg;base64,...",
      "caption": null,
      "sortOrder": 2
    },
    {
      "url": "data:image/jpeg;base64,...",
      "sortOrder": 3
    }
  ]
}
```

**Request fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| `photos` | array | Yes | Length 3–6 |
| `photos[].url` | string | Yes | Non-empty (base64 data URL) |
| `photos[].caption` | string | No | ≤40 chars |
| `photos[].sortOrder` | int | Yes | 1–6 (unique) |

**Response 200:**
```json
{
  "message": "Photos saved",
  "count": 3
}
```

**Errors:**
- `400` — Photos count out of range (< 3 or > 6), caption too long, missing URL
- `401` — Unauthorized

**Side effects:**
- Deletes all existing `UserPhotos` for user
- Creates new `UserPhoto` entities
- Updates `User.UpdatedAt`
- **Does not advance ProfileStatus** (photos saved independently)

**Note:** First photo (sortOrder 1) is the primary photo shown in all previews.

---

### 5. PUT /onboarding/intent

**Purpose:** Save relationship intent and reflection

**Auth:** Required  
**Method:** PUT

**Request:**
```http
PUT /onboarding/intent
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "primaryIntent": "long_term",
  "openness": ["friendship", "short_term"],
  "reflectionSentence": "A meaningful connection feels like being fully seen and accepted for who I am."
}
```

**Request fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| `primaryIntent` | string | Yes | Non-empty |
| `openness` | string[] | No | Defaults to [] |
| `reflectionSentence` | string | Yes | 1–200 chars |

**Valid primaryIntent values:**
`"long_term"`, `"short_term"`, `"friendship"`, `"open_to_anything"`

**Valid openness values:**
`"long_term"`, `"short_term"`, `"friendship"`, `"open_to_anything"`, `"not_sure"`

**Response 200:**
```json
{
  "profileStatus": "INTENT_DONE",
  "nextRoute": "/onboarding/foundational"
}
```

**Errors:**
- `400` — Missing primaryIntent, reflectionSentence empty or > 200 chars
- `401` — Unauthorized

**Side effects:**
- Creates/updates `UserIntent` (PrimaryIntent, OpennessJson, ReflectionSentence)
- Sets `User.ProfileStatus = INTENT_DONE`
- Updates `User.UpdatedAt`
- Tracks analytics event: `OnboardingStepCompleted { step: "intent" }`

**Security note:** `ReflectionSentence` stored as plaintext in current implementation. Encryption planned — see [intent-encryption.md](intent-encryption.md).

---

### 6A. GET /onboarding/foundational/questions

**Purpose:** Fetch personalized foundational questions

**Auth:** Required  
**Method:** GET

**Request:**
```http
GET /onboarding/foundational/questions
Authorization: Bearer <jwt>
```

**Response 200:**
```json
{
  "version": 1,
  "questions": [
    {
      "id": "q1",
      "text": "When you have a free evening, Alex, what do you usually crave doing most?"
    },
    {
      "id": "q2",
      "text": "What kind of connection makes you feel most comfortable with someone new?"
    },
    {
      "id": "q3",
      "text": "What's a small habit or routine that genuinely makes your life better?"
    },
    {
      "id": "q4",
      "text": "What's something you're proud of that doesn't show up on a resume?"
    },
    {
      "id": "q5",
      "text": "What does a good relationship feel like to you in everyday moments?"
    }
  ]
}
```

**Response fields:**
- `version` (int) — Question set version (1, 2, 3, ...)
- `questions` (array) — 5 questions with stable IDs

**Errors:**
- `400` — Not due for foundational questions (check state endpoint first)
- `401` — Unauthorized

**Notes:**
- Questions are AI-personalized based on user's name, gender, and intent
- IDs are stable across versions (q1, q2, q3, q4, q5)
- If `QuestionsJson` is empty/malformed, auto-heals from `FoundationalQuestionBank`

---

### 6. PUT /onboarding/foundational

**Purpose:** Submit answers to foundational questions

**Auth:** Required  
**Method:** PUT

**Request:**
```http
PUT /onboarding/foundational
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "answers": [
    {
      "questionId": "q1",
      "answer": "I usually crave quiet time at home, reading or cooking something new. It helps me recharge."
    },
    {
      "questionId": "q2",
      "answer": "Deep conversations where we can skip the small talk and get real about what matters."
    },
    {
      "questionId": "q3",
      "answer": "Morning meditation. Just 10 minutes to center myself before the day starts."
    },
    {
      "questionId": "q4",
      "answer": "Building a small community garden in my neighborhood. Seeing people connect over something simple."
    },
    {
      "questionId": "q5",
      "answer": "Comfortable silence. Being able to just exist together without needing to fill every moment with words."
    }
  ]
}
```

**Request fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| `answers` | array | Yes | Exactly 5 answers |
| `answers[].questionId` | string | Yes | Must match stored question IDs |
| `answers[].answer` | string | Yes | 30–400 chars (trimmed) |

**Response 200:**
```json
{
  "profileStatus": "FOUNDATION_DONE",
  "nextRoute": "/onboarding/details"
}
```

**Response 200 (recurring cycle, profile already complete):**
```json
{
  "profileStatus": "COMPLETE",
  "nextRoute": "/home"
}
```

**Errors:**
- `400` — Not exactly 5 answers, answer too short (< 30 chars) or too long (> 400 chars), duplicate questionIds, invalid questionIds
- `401` — Unauthorized

**Side effects:**
- Saves answers as `{ id, a }[]` to `UserFoundationalQuestionSet.AnswersJson`
- Sets `AnsweredAt = DateTime.UtcNow`
- Clears `DeferredUntil`
- Computes `ExpiresAt` based on version:
  - v1: +15 days
  - v2: +45 days
  - v3+: +60 days
- Sets `User.ProfileStatus = FOUNDATION_DONE` (if first time)
- Updates `User.UpdatedAt`
- Tracks analytics event: `OnboardingStepCompleted { step: "foundational" }`

---

### 7. POST /onboarding/foundational/defer

**Purpose:** Defer answering foundational questions for 24 hours (v2+ only)

**Auth:** Required  
**Method:** POST

**Request:**
```http
POST /onboarding/foundational/defer
Authorization: Bearer <jwt>
Content-Type: application/json

{}
```

**Response 200:**
```json
{
  "message": "Deferred",
  "nextRoute": "/home"
}
```

**Errors:**
- `400` — v1 cannot be deferred (hard-block)
- `400` — No active set to defer
- `401` — Unauthorized

**Side effects:**
- Sets `UserFoundationalQuestionSet.DeferredUntil = DateTime.UtcNow + 24 hours`
- Updates `UpdatedAt`

**Notes:**
- Only works for v2+ (recurring cycles)
- v1 (initial onboarding) is hard-blocked and cannot be deferred
- After 24 hours, user will be prompted again

---

### 8. PUT /onboarding/details

**Purpose:** Save bio, optional fields, weekly vibe, pronouns, accessibility

**Auth:** Required  
**Method:** PUT

**Request:**
```http
PUT /onboarding/details
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "bio": "Software engineer who loves hiking and cooking. Always up for deep conversations over coffee.",
  "optionalFields": [
    { "key": "job", "value": "Software Engineer", "visibility": "Public" },
    { "key": "education", "value": "bachelors_degree", "visibility": "Public" },
    { "key": "school", "value": "IIT Hyderabad", "visibility": "Public" },
    { "key": "hometown", "value": "Mumbai", "visibility": "Public" },
    { "key": "pref_height", "value": "175 cm", "visibility": "MatchingOnly" },
    { "key": "horoscope", "value": "leo", "visibility": "Public" }
  ],
  "weeklyVibe": "Feeling reflective this week. Lots of good conversations happening.",
  "displayPronouns": "she/her",
  "reduceMotion": false,
  "highContrast": false
}
```

**Request fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| `bio` | string | Yes | 1–200 chars |
| `optionalFields` | array | No | Each field validated separately |
| `optionalFields[].key` | string | Yes | Must be in allowed list (see below) |
| `optionalFields[].value` | string | Yes | Non-empty |
| `optionalFields[].visibility` | string | Yes | Public / MatchingOnly / Private |
| `weeklyVibe` | string | No | ≤200 chars |
| `displayPronouns` | string | No | ≤50 chars |
| `reduceMotion` | bool | No | Accessibility preference |
| `highContrast` | bool | No | Accessibility preference |

**Allowed detail keys (Public or user-specified visibility):**
`"job"`, `"education"`, `"school"`, `"pets"`, `"habits"`, `"hobbies"`, `"children"`, `"languages"`, `"zodiac"`, `"diet"`, `"hometown"`

**Allowed preference keys (MatchingOnly visibility):**
`"pref_ethnicity"`, `"pref_religion"`, `"pref_height"`, `"pref_work"`, `"pref_smoking"`, `"pref_drinking"`, `"pref_workout"`

**Response 200:**
```json
{
  "profileStatus": "DETAILS_DONE",
  "nextRoute": "/onboarding/review"
}
```

**Errors:**
- `400` — Bio empty or > 200 chars, invalid field key, displayPronouns > 50 chars
- `401` — Unauthorized

**Side effects:**
- Upserts `UserOptionalField` with key `"bio"`, visibility `Public`
- Deletes all other `UserOptionalFields` for user
- Creates new `UserOptionalField` entities from request
- Upserts `UserWeeklyVibe` (expires in 7 days) or deletes if not provided
- Updates `UserProfile.DisplayPronouns` (if provided)
- Updates `UserPreference.ReduceMotion`, `UserPreference.HighContrast` (if provided)
- Sets `User.ProfileStatus = DETAILS_DONE`
- Updates `User.UpdatedAt`
- Tracks analytics event: `OnboardingStepCompleted { step: "details" }`

---

### 9. GET /onboarding/review

**Purpose:** Preview all collected data before completion

**Auth:** Required  
**Method:** GET

**Request:**
```http
GET /onboarding/review
Authorization: Bearer <jwt>
```

**Response 200:**
```json
{
  "profileStatus": "DETAILS_DONE",
  "self": {
    "fullName": "Alex",
    "email": "alex@example.com",
    "profilePhoto": null,
    "basics": {
      "age": 25,
      "gender": "woman",
      "location": { "city": "Hyderabad", "state": "Telangana" },
      "distanceMiles": 25,
      "interestedIn": ["men", "women"],
      "relationshipStructure": "OPEN"
    },
    "photos": [
      { "url": "...", "caption": "Hiking", "sortOrder": 1 },
      { "url": "...", "caption": null, "sortOrder": 2 },
      { "url": "...", "caption": null, "sortOrder": 3 }
    ],
    "intent": {
      "primaryIntent": "long_term",
      "openness": ["friendship"],
      "reflectionSentence": "A meaningful connection feels like being fully seen and accepted."
    },
    "foundational": {
      "version": 1,
      "answers": [
        { "id": "q1", "a": "I usually crave quiet time at home..." },
        { "id": "q2", "a": "Deep conversations where we can skip the small talk..." },
        { "id": "q3", "a": "Morning meditation..." },
        { "id": "q4", "a": "Building a small community garden..." },
        { "id": "q5", "a": "Comfortable silence..." }
      ],
      "qa": [
        { "id": "q1", "q": "When you have a free evening...", "a": "I usually crave quiet time..." },
        { "id": "q2", "q": "What kind of connection...", "a": "Deep conversations..." },
        { "id": "q3", "q": "What's a small habit...", "a": "Morning meditation..." },
        { "id": "q4", "q": "What's something you're proud of...", "a": "Building a small community..." },
        { "id": "q5", "q": "What does a good relationship...", "a": "Comfortable silence..." }
      ]
    },
    "details": {
      "bio": "Software engineer who loves hiking and cooking...",
      "weeklyVibe": "Feeling reflective this week...",
      "optionalFields": [
        { "key": "job", "value": "Software Engineer", "visibility": "Public" },
        { "key": "education", "value": "bachelors_degree", "visibility": "Public" },
        { "key": "school", "value": "IIT Hyderabad", "visibility": "Public" },
        { "key": "hometown", "value": "Mumbai", "visibility": "Public" },
        { "key": "horoscope", "value": "leo", "visibility": "Public" }
      ],
      "preferenceFields": [
        { "key": "pref_height", "value": "175 cm", "visibility": "MatchingOnly" }
      ]
    }
  },
  "publicPreview": {
    "name": "Alex",
    "age": 25,
    "gender": "woman",
    "location": "Hyderabad, Telangana",
    "bio": "Software engineer who loves hiking and cooking...",
    "isVerified": false,
    "displayPronouns": "she/her",
    "intent": {
      "primaryIntent": "long_term",
      "openness": ["friendship"]
    },
    "photos": [
      { "url": "...", "caption": "Hiking", "sortOrder": 1 },
      { "url": "...", "caption": null, "sortOrder": 2 },
      { "url": "...", "caption": null, "sortOrder": 3 }
    ],
    "optionalPublic": [
      { "key": "job", "value": "Software Engineer" },
      { "key": "education", "value": "bachelors_degree" },
      { "key": "school", "value": "IIT Hyderabad" },
      { "key": "hometown", "value": "Mumbai" },
      { "key": "horoscope", "value": "leo" }
    ]
  }
}
```

**Response structure:**
- `profileStatus` (string) — Current status
- `self` (object) — Full private data
  - `fullName`, `email`, `profilePhoto`
  - `basics` — Demographics and preferences
  - `photos` — All photos with captions
  - `intent` — Primary intent + openness + reflection
  - `foundational` — Question version + answers + Q&A pairs
  - `details` — Bio, weekly vibe, optional fields (separated into detail vs preference)
- `publicPreview` (object) — What other users see
  - Excludes: reflection sentence, private optional fields, preference fields
  - Includes: only `Public` visibility fields

**Errors:**
- `401` — Unauthorized

**Notes:**
- Read-only endpoint (does not modify data)
- Used by frontend Review component
- `publicPreview` shows exactly what other users will see on profile

---

### 10. POST /onboarding/complete

**Purpose:** Finalize onboarding and trigger vector generation

**Auth:** Required  
**Method:** POST

**Request:**
```http
POST /onboarding/complete
Authorization: Bearer <jwt>
Content-Type: application/json

{}
```

**Response 200:**
```json
{
  "profileStatus": "COMPLETE",
  "nextRoute": "/home"
}
```

**Errors:**
- `400` — Missing required data (basics, preferences, photos, intent, foundational, bio)
  - `"Missing basics profile"`
  - `"Missing preferences"`
  - `"At least 3 photos required"`
  - `"Missing intent"`
  - `"Missing foundational intake"`
  - `"Bio is required"`
- `401` — Unauthorized

**Pre-flight validation:**
1. `UserProfile` exists
2. `UserPreference` exists
3. `UserPhotos` count ≥ 3
4. `UserIntent` exists
5. `UserFoundationalQuestionSet` exists with `AnsweredAt != null`
6. Bio exists in `UserOptionalFields` (key = `"bio"`, value not empty)

**Side effects:**
- Sets `User.ProfileStatus = COMPLETE`
- Updates `User.UpdatedAt`
- **Triggers background task:** `UserVectorBuilder.BuildAndSaveV1Async(userId)`
  - Runs in new scope (via `IServiceScopeFactory`)
  - Failures logged but do not block completion
  - Vector can be rebuilt later via admin tools if needed
- Tracks analytics event: `OnboardingStepCompleted { step: "complete" }`

**Background vector generation:**
```csharp
// Non-blocking, runs in background
_ = Task.Run(async () =>
{
    using var scope = scopeFactory.CreateScope();
    var vectorBuilder = scope.ServiceProvider.GetRequiredService<IUserVectorBuilder>();
    await vectorBuilder.BuildAndSaveV1Async(userId, CancellationToken.None);
});
```

**Notes:**
- This is the final step of onboarding
- User is now eligible for daily deck generation
- Vector build typically takes 2–5 seconds (OpenAI embeddings)

---

## Error Responses

All errors follow this format:

```json
{
  "error": "Age must be 18+",
  "correlationId": "a1b2c3d4e5f6g7h8",
  "timestamp": "2026-10-07T14:32:15.123Z"
}
```

**Common error codes:**

| Code | Meaning | Example |
|------|---------|---------|
| 400 | Bad Request | Validation failed (age < 18, missing required field) |
| 401 | Unauthorized | Missing/invalid JWT token |
| 422 | Unprocessable Entity | Domain error (e.g., v1 cannot be deferred) |
| 500 | Internal Server Error | Unhandled exception (logged with correlationId) |

**Error handling:**
- All exceptions caught by `GlobalExceptionHandler`
- `UnauthorizedAccessException` → HTTP 401 (via `AuthExceptionHandler`)
- `DomainException` → HTTP 422 (via `DomainExceptionHandler`)
- Unhandled exceptions → HTTP 500 with correlationId for log lookup

---

## Authentication

All endpoints require a valid JWT token in the `Authorization` header:

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

**Token claims:**
- Primary: `"uid"` (user ID as string)
- Fallback: `"sub"` or `ClaimTypes.NameIdentifier`

**Invalid token behaviors:**
- Missing token → 401
- Expired token → 401
- Invalid signature → 401
- Missing `uid`/`sub` claim → 401

**User ID extraction:**
```csharp
// Throws UnauthorizedAccessException if claim invalid/missing
var userId = EndpointHelper.GetUserId(ClaimsPrincipal user);
```

---

## Rate Limiting

**Current status:** No rate limits enforced on onboarding endpoints

**Planned (Phase 2):**
- State endpoint: 100 req/min per user
- Submit endpoints: 20 req/min per user
- AI endpoints (foundational questions): 10 req/min per user

**Retry behavior:**
- 429 response includes `Retry-After` header (seconds)
- Exponential backoff recommended for client retries

---

## CORS

**Development:**
- Allowed origins: `http://localhost:4202`, `http://localhost:4200`
- Credentials: Allowed

**Production:**
- Allowed origins: `https://wooven.me`, `https://www.wooven.me`
- Credentials: Allowed

---

## Observability

All requests include correlation tracking:

**Request header (optional):**
```http
X-Correlation-ID: a1b2c3d4e5f6g7h8
```

**Response header (always):**
```http
X-Correlation-ID: a1b2c3d4e5f6g7h8
```

If no incoming correlation ID, server generates a 16-char hex ID.

**Logging:**
- Every log line includes `{CorrelationId}`
- Structured logging via Serilog
- Log format: `[ServiceName] Message | Field1={Value1} Field2={Value2} CorrelationId={Cid}`

**Example:**
```
[OnboardingEndpoints] User completed basics | UserId=123 Age=25 CorrelationId=a1b2c3d4e5f6g7h8
```

---

## Analytics Events

All submission endpoints track analytics events:

**Event:** `OnboardingStepCompleted`  
**Properties:**
```json
{
  "step": "welcome" | "basics" | "intent" | "foundational" | "details" | "complete"
}
```

**Tracked via:** `IAnalyticsService.TrackAsync(userId, null, eventName, properties)`

**Storage:** `UserAnalyticsEvents` table

---

## Example Flows

### Complete Onboarding (Happy Path)

```http
# 1. Check state
GET /onboarding/state
→ { "profileStatus": "INCOMPLETE", "nextRoute": "/onboarding/start", "completed": [] }

# 2. Mark welcome done
POST /onboarding/welcome
→ { "profileStatus": "WELCOME_DONE", "nextRoute": "/onboarding/basics" }

# 3. Submit basics
PUT /onboarding/basics
{ "fullName": "Alex", "age": 25, "gender": "woman", ... }
→ { "profileStatus": "BASICS_DONE", "nextRoute": "/onboarding/intent" }

# 4. Upload photos
PUT /onboarding/photos
{ "photos": [ { "url": "...", "sortOrder": 1 }, ... ] }
→ { "message": "Photos saved", "count": 3 }

# 5. Submit intent
PUT /onboarding/intent
{ "primaryIntent": "long_term", "openness": ["friendship"], "reflectionSentence": "..." }
→ { "profileStatus": "INTENT_DONE", "nextRoute": "/onboarding/foundational" }

# 6. Get foundational questions
GET /onboarding/foundational/questions
→ { "version": 1, "questions": [ { "id": "q1", "text": "..." }, ... ] }

# 7. Submit foundational answers
PUT /onboarding/foundational
{ "answers": [ { "questionId": "q1", "answer": "..." }, ... ] }
→ { "profileStatus": "FOUNDATION_DONE", "nextRoute": "/onboarding/details" }

# 8. Submit details
PUT /onboarding/details
{ "bio": "...", "optionalFields": [...], "weeklyVibe": "..." }
→ { "profileStatus": "DETAILS_DONE", "nextRoute": "/onboarding/review" }

# 9. Preview data
GET /onboarding/review
→ { "profileStatus": "DETAILS_DONE", "self": {...}, "publicPreview": {...} }

# 10. Complete onboarding
POST /onboarding/complete
→ { "profileStatus": "COMPLETE", "nextRoute": "/home" }
```

---

### Recurring Foundational (v2)

```http
# User logs in 15 days after completing onboarding

# 1. Check state
GET /onboarding/state
→ {
    "profileStatus": "FOUNDATIONAL_DUE",
    "nextRoute": "/onboarding/foundational",
    "version": 2,
    "hardBlock": false,
    "allowSkip": true
  }

# 2. Get questions (v2, personalized again)
GET /onboarding/foundational/questions
→ { "version": 2, "questions": [ { "id": "q1", "text": "..." }, ... ] }

# 3a. Submit answers
PUT /onboarding/foundational
{ "answers": [ { "questionId": "q1", "answer": "..." }, ... ] }
→ { "profileStatus": "COMPLETE", "nextRoute": "/home" }

# OR

# 3b. Defer for 24 hours
POST /onboarding/foundational/defer
→ { "message": "Deferred", "nextRoute": "/home" }
```

---

## Data Retention

| Entity | Retention | Notes |
|--------|-----------|-------|
| `UserFoundationalQuestionSet` | Permanent | All versions kept for ECHO learning |
| `UserIntent.ReflectionSentence` | Permanent | Encrypted (planned) |
| `UserWeeklyVibe` | 7 days | Auto-expires |
| `UserPhotos` | Until user deletes | No auto-cleanup |
| `UserOptionalFields` | Until user updates | Replaced on each PUT |
| `UserProfile`, `UserPreference` | Permanent | Core matching data |

---

## Migration Notes

**Breaking changes:**
- None (API stable since 2026-06-04)

**Additions since initial release:**
- `relationshipStructure` field in basics (2026-06-04)
- `displayPronouns` field in details (2026-06-04)
- `reduceMotion`, `highContrast` accessibility fields (2026-06-04)
- Foundational question bank expanded from 5 to 6 questions (2026-06-04, q6 added for Ambition pillar)

**Deprecated:**
- None

---

## Testing

**Postman collection:** `docs/postman/onboarding.json`

**Test users:**
- `test+onboarding@wooven.me` — Fresh account (INCOMPLETE)
- `test+basics@wooven.me` — Basics done (BASICS_DONE)
- `test+complete@wooven.me` — Fully onboarded (COMPLETE)

**Dev token generation:**
```bash
# Get JWT for test user
curl -X POST http://localhost:5135/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test+onboarding@wooven.me","password":"Test1234!"}'
```

---

## Further Reading

- [flow.md](flow.md) — Step-by-step breakdown
- [foundational-questions.md](foundational-questions.md) — AI pillar system
- [intent-encryption.md](intent-encryption.md) — Reflection encryption
- [bootstrap-vectors.md](bootstrap-vectors.md) — Initial vector generation
- [frontend.md](frontend.md) — Angular components
- [backend.md](backend.md) — .NET implementation
