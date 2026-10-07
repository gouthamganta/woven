# Onboarding Flow

**Last Updated:** 2026-08-17

---

## Overview

This document describes the **sequential user journey** through all 9 onboarding steps, including UI elements, validation rules, and backend transitions.

---

## Step 1: Welcome

**Route:** `/onboarding/welcome`  
**Component:** `WelcomeOnboardingComponent`  
**ProfileStatus:** `INCOMPLETE` → `WELCOME_DONE`  
**Endpoint:** `POST /onboarding/welcome`

### UI

- **Title:** "Welcome to Woven."
- **Content:**
  - 3 value propositions (5 people/day, behavioral learning, no scrolling)
  - 3 expectations (5 min setup, 5 questions, photos + answers)
  - Footer note: "Nothing is permanent except showing up."
- **CTA:** "Let's build your profile →"

### Behavior

1. User clicks CTA
2. Frontend calls `POST /onboarding/welcome`
3. Backend advances `ProfileStatus` → `WELCOME_DONE`
4. User navigates to `/onboarding/basics`

**Validation:** None (just acknowledgment of flow).

---

## Step 2: Basics

**Route:** `/onboarding/basics`  
**Component:** `BasicsOnboardingComponent`  
**ProfileStatus:** `WELCOME_DONE` → `BASICS_DONE`  
**Endpoint:** `PUT /onboarding/basics`

### Fields

| Field | Type | Validation | Storage |
|-------|------|------------|---------|
| First name | Text | Required, 50 chars max | `User.FullName` |
| Date of birth | 3 dropdowns (M/D/Y) | ≥18 years old | `UserProfile.Age` (computed) |
| Gender | Pills | Required, 1 selection | `UserProfile.Gender` |
| Pronouns | Pills | Optional, 1 selection | Stored but not shown (future use) |
| Sexual orientation | Pills (multi) | Optional | Stored but not shown |
| City | Dropdown | Required (Hyderabad only) | `UserProfile.City`, `State`, `Lat`, `Lng` |
| Distance | Slider | 15–100 km, step 5 | `UserPreference.DistanceMiles` |
| Interested in | Pills (multi) | Required, ≥1 selection | `UserPreference.InterestedInJson` |
| Looking for | Pills (multi) | Optional | Stored but not shown |
| Age range | Number inputs | 18–80, min < max | `UserPreference.AgeMin`, `AgeMax` |

### UI Notes

- **Date of birth**: Shows live age calculation ("X years old")
- **Distance slider**: Live preview of selected km
- **Age range**: +/− buttons + text input
- **Pills**: Multi-select via click (toggle on/off)

### Validation Rules

```csharp
// Server-side (OnboardingEndpoints.cs:186–233)
if (req.Age < 18)
    return Results.BadRequest("Age must be 18+");

if (req.DistanceMiles < 15 || req.DistanceMiles > 100)
    return Results.BadRequest("Distance must be 15–100 miles");

if (req.AgeMin > req.AgeMax)
    return Results.BadRequest("AgeMin cannot be greater than AgeMax");

if (req.InterestedIn == null || req.InterestedIn.Length == 0)
    return Results.BadRequest("InterestedIn is required");

// Coordinates required (no 0,0 placeholders)
if (req.Location.Lat == 0 && req.Location.Lng == 0)
    return Results.BadRequest("Invalid location coordinates");
```

### Data Storage

```csharp
// Creates or updates:
UserProfile {
    Age, Gender, City, State, Lat, Lng
}

UserPreference {
    DistanceMiles, AgeMin, AgeMax,
    InterestedInJson,       // JSON array
    RelationshipStructure   // Defaults to OPEN
}
```

---

## Step 3: Photos

**Route:** `/onboarding/photos`  
**Component:** `PhotosPage` (Ionic page, not Angular component)  
**ProfileStatus:** No change  
**Endpoint:** `PUT /onboarding/photos`

### Requirements

- **Count:** 3–6 photos
- **Caption:** Optional, 40 chars max per photo
- **Sort order:** First photo = primary profile photo

### UI

- Photo upload grid (drag to reorder)
- Caption input below each photo
- Delete button per photo
- CTA disabled until ≥3 photos uploaded

### Validation

```csharp
if (req.Photos == null || req.Photos.Length < 3 || req.Photos.Length > 6)
    return Results.BadRequest("Photos must be between 3 and 6");

if (p.Caption != null && p.Caption.Length > 40)
    return Results.BadRequest("Caption must be 40 characters or less");
```

### Data Storage

```csharp
// Deletes all existing UserPhoto rows, inserts new set
UserPhoto[] {
    UserId, Url, Caption, SortOrder, CreatedAt
}
```

**Note:** No ProfileStatus change — photos are required but don't gate progress independently.

---

## Step 4: Intent

**Route:** `/onboarding/intent`  
**Component:** `IntentOnboardingComponent`  
**ProfileStatus:** `BASICS_DONE` → `INTENT_DONE`  
**Endpoint:** `PUT /onboarding/intent`

### Fields

| Field | Type | Validation | Storage |
|-------|------|------------|---------|
| Primary intent | Cards (single) | Required | `UserIntent.PrimaryIntent` |
| Also open to | Pills (multi) | Optional | `UserIntent.OpennessJson` |
| Reflection sentence | Textarea | Required, 200 chars max | `UserIntent.ReflectionSentence` (encrypted) |

### Intent Options

```typescript
const INTENTS = [
  { key: 'long_term',        label: 'Something lasting' },
  { key: 'short_term',       label: 'Casual connection' },
  { key: 'friendship',       label: 'Friendship first' },
  { key: 'open_to_anything', label: 'Open to anything' },
];
```

### UI

- **Primary intent:** 2×2 grid of cards with label + subtitle
- **Openness:** Pills (multi-select, can include primary intent)
- **Reflection:** Textarea with live char count (warns at 170/200)

### Validation

```csharp
if (string.IsNullOrWhiteSpace(req.PrimaryIntent))
    return Results.BadRequest("PrimaryIntent is required");

if (string.IsNullOrWhiteSpace(req.ReflectionSentence))
    return Results.BadRequest("ReflectionSentence is required");

if (req.ReflectionSentence.Length > 200)
    return Results.BadRequest("ReflectionSentence must be 200 characters or less");
```

### Encryption

**Reflection sentence is encrypted before storage** using AES-256-GCM. See [intent-encryption.md](intent-encryption.md).

---

## Step 5: Foundational Questions

**Route:** `/onboarding/foundational`  
**Component:** `FoundationalComponent`  
**ProfileStatus:** `INTENT_DONE` → `FOUNDATION_DONE`  
**Endpoints:**
- `GET /onboarding/foundational/questions`
- `PUT /onboarding/foundational`

### Flow

1. **Load questions:** `GET /onboarding/foundational/questions` returns 5 personalized questions
2. **One-by-one UI:** User answers 1 question at a time (progress: "1/5", "2/5", etc.)
3. **Submit all:** `PUT /onboarding/foundational` sends all 5 answers at once

### Questions

**Generated from stable question bank** via `FoundationalQuestionBank.GetQuestionsForVersion(1)`, then rewritten by OpenAI with user context (name, gender, intent).

Example questions:
```json
[
  {
    "id": "q1",
    "text": "When you have a free evening, what do you usually crave doing most?",
    "pillars": ["Lifestyle", "Energy"]
  },
  {
    "id": "q2",
    "text": "What kind of connection makes you feel most comfortable with someone new?",
    "pillars": ["Communication", "Affection"]
  }
  // ... 3 more
]
```

**6 questions cover all 8 pillars** (see [foundational-questions.md](foundational-questions.md)).

### UI

- One question per screen
- Textarea with 30–400 char validation (live counter)
- Helper prompts below each question ("Keep it real — 1–3 sentences is perfect.")
- Back/Next navigation between questions
- Submit button on question 5

### Validation

```csharp
if (req.Answers == null || req.Answers.Length != 5)
    return Results.BadRequest("Exactly 5 answers are required");

if (a.Answer.Trim().Length > 400)
    return Results.BadRequest("Each answer must be 400 characters or less");

// Frontend enforces 30-char minimum
```

### Data Storage

```csharp
UserFoundationalQuestionSet {
    UserId, Version,
    QuestionsJson,  // Frozen rewritten questions
    AnswersJson,    // { id, a }[]
    AnsweredAt,     // Set when submitted
    ExpiresAt,      // v1: +15 days, v2: +45 days
    CreatedAt, UpdatedAt
}
```

**Version 1 is hard-block** (cannot skip). Future versions (v2+) are soft-block and deferrable.

---

## Step 6: Details

**Route:** `/onboarding/details`  
**Component:** `DetailsOnboardingComponent`  
**ProfileStatus:** `FOUNDATION_DONE` → `DETAILS_DONE`  
**Endpoint:** `PUT /onboarding/details`

### Fields

| Field | Required | Max Length | Storage |
|-------|----------|------------|---------|
| Bio | Required | 300 chars | `UserOptionalField` (key: "bio", visibility: Public) |
| Job title | Optional | 80 chars | `UserOptionalField` (key: "job") |
| Hometown | Optional | 80 chars | `UserOptionalField` (key: "hometown") |
| Education level | Optional | — | `UserOptionalField` (key: "education") |
| School/University | Optional | 100 chars | `UserOptionalField` (key: "school") |
| Height | Optional | 20 chars | `UserOptionalField` (key: "height") |
| Zodiac sign | Optional | — | `UserOptionalField` (key: "zodiac") |
| Weekly vibe | Optional | 200 chars | `UserWeeklyVibe` (expires in 7 days) |
| Display pronouns | Optional | 50 chars | `UserProfile.DisplayPronouns` |
| Reduce motion | Optional | Boolean | `UserPreference.ReduceMotion` |
| High contrast | Optional | Boolean | `UserPreference.HighContrast` |

### Optional Fields

Additional fields: `pets`, `habits`, `hobbies`, `children`, `languages`, `diet`.

**Preference fields** (prefix `pref_`): `pref_ethnicity`, `pref_religion`, `pref_height`, `pref_work`, `pref_smoking`, `pref_drinking`, `pref_workout`.

All optional fields have **visibility levels**:
- `Public` — Shown on profile
- `MatchingOnly` — Used for scoring, not displayed
- `Private` — Never shown to others (not used in onboarding)

### Validation

```csharp
if (string.IsNullOrWhiteSpace(req.Bio) || req.Bio.Length > 200)
    return Results.BadRequest("Bio is required and must be 200 characters or less");

// Allowed keys checked against whitelist
var allowedDetailKeys = new HashSet<string> {
    "job","education","school","pets","habits","hobbies","children","languages","zodiac","diet","hometown"
};
```

---

## Step 7: Review

**Route:** `/onboarding/review`  
**Component:** `ReviewOnboardingComponent`  
**ProfileStatus:** No change (read-only)  
**Endpoint:** `GET /onboarding/review`

### UI

- **Photos strip:** All uploaded photos (primary marked)
- **Basics:** Name, gender, location, distance, interested in
- **Intent:** Primary intent + reflection sentence
- **Foundational:** "✓ Answered — these power your compatibility matches."
- **Details:** Bio + optional fields (if filled)

Each section has an **"Edit"** button that navigates back to that step.

### CTA

"This is me — let's go →" triggers `POST /onboarding/complete`.

---

## Step 8: Complete

**Route:** `/onboarding/complete` (endpoint only, no UI component)  
**ProfileStatus:** `DETAILS_DONE` → `COMPLETE`  
**Endpoint:** `POST /onboarding/complete`

### Validation

Checks that all required data exists:
```csharp
if (!profile) return Results.BadRequest("Missing basics profile");
if (!pref) return Results.BadRequest("Missing preferences");
if (photosCount < 3) return Results.BadRequest("At least 3 photos required");
if (!intent) return Results.BadRequest("Missing intent");
if (!foundational) return Results.BadRequest("Missing foundational intake");
if (string.IsNullOrWhiteSpace(bio)) return Results.BadRequest("Bio is required");
```

### Bootstrap Vector Trigger

**Non-blocking background task** fires `UserVectorBuilder.BuildAndSaveV1Async(userId)`:

```csharp
_ = Task.Run(async () => {
    using var scope = scopeFactory.CreateScope();
    var vectorBuilder = scope.ServiceProvider.GetRequiredService<IUserVectorBuilder>();
    await vectorBuilder.BuildAndSaveV1Async(userId, CancellationToken.None);
});
```

See [bootstrap-vectors.md](bootstrap-vectors.md) for details.

---

## Step 9: Start

**Route:** `/onboarding/start`  
**Component:** `StartOnboardingComponent`  
**ProfileStatus:** No change (transition screen)

### UI

- **Symbol:** ◈ (floating animation)
- **Headline:** "Building your deck..."
- **Description:** "ECHO is analyzing your responses and finding your first matches."
- **Status text:** Rotates every 2.5s:
  - "Analyzing your profile"
  - "Finding compatible matches"
  - "Reviewing shared interests"
  - "Preparing your deck"

### Behavior

1. Component polls `GET /onboarding/state` every 3 seconds
2. When `profileStatus === "COMPLETE"`, shows checkmark + "You're all set"
3. **Fallback:** After 15 seconds, shows "ready" regardless of status
4. **CTA:** "Enter Woven" → navigates to `/moments`

**Why the delay?** Bootstrap vector generation is async. Most users complete in <5 seconds, but the fallback ensures no one waits forever.

---

## Navigation Guards

**OnboardingStateGuard** (frontend/src/app/guards/onboarding-state.guard.ts):

- Calls `GET /onboarding/state`
- Redirects users to `nextRoute` if they skip ahead or refresh
- Prevents URL manipulation (e.g., going to `/onboarding/review` before completing `/onboarding/basics`)

**Hard-block vs. Soft-block:**
- **v1 foundational** (onboarding): Hard-block, cannot skip
- **v2+ foundational** (recurring): Soft-block, can defer 24h via `POST /onboarding/foundational/defer`

---

## Error Handling

| Error | HTTP Code | Frontend Behavior |
|-------|-----------|-------------------|
| Missing required field | 400 | Shows inline error, user corrects |
| Age < 18 | 400 | Shows "You must be 18+" warning |
| Invalid coordinates | 400 | Shows "Invalid location" (shouldn't happen with dropdown) |
| No active foundational set | 400 | Redirects to `/home` or shows "Not due" message |
| Onboarding incomplete | 401 | Auth failure, redirects to `/login` |

---

## Analytics Events

Tracked via `IAnalyticsService`:

```csharp
AnalyticsEvents.OnboardingStepCompleted
```

**Payload:**
```json
{ "step": "welcome" | "basics" | "intent" | "foundational" | "details" | "complete" }
```

Used to measure:
- Drop-off rates per step
- Time spent per step
- Completion funnel
