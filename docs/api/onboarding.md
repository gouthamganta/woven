# Onboarding API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

The onboarding flow guides new users through profile setup in multiple sequential steps. Each step updates `ProfileStatus` and returns the next route.

**Flow:**
1. `INCOMPLETE` → `/onboarding/start` (client-only welcome screen)
2. `WELCOME_DONE` → `/onboarding/basics` (demographics + location)
3. `BASICS_DONE` → `/onboarding/intent` (relationship goals)
4. `INTENT_DONE` → `/onboarding/foundational` (deep questions)
5. `FOUNDATION_DONE` → `/onboarding/details` (bio + optional fields)
6. `DETAILS_DONE` → `/onboarding/review` (review + confirm)
7. `COMPLETE` → `/home` (onboarding finished)

**Source:** [`backend/WovenBackend/Endpoints/OnboardingEndpoints.cs`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

## Endpoints

### GET /onboarding/state

**Description:** Returns current onboarding status and next route.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "profileStatus": "BASICS_DONE",
  "nextRoute": "/onboarding/intent",
  "completed": ["welcome", "basics"]
}
```

**Source:** [`OnboardingEndpoints.cs:88-152`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### POST /onboarding/welcome

**Description:** Marks welcome screen as completed.

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "profileStatus": "WELCOME_DONE",
  "nextRoute": "/onboarding/basics"
}
```

**Source:** [`OnboardingEndpoints.cs:155-183`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### PUT /onboarding/basics

**Description:** Set demographics, location, and preferences.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "fullName": "Jane Doe",
  "age": 28,
  "gender": "WOMAN",
  "interestedIn": ["MAN", "NON_BINARY"],
  "distanceMiles": 50,
  "ageMin": 25,
  "ageMax": 35,
  "location": {
    "city": "Seattle",
    "state": "WA",
    "lat": 47.6062,
    "lng": -122.3321
  },
  "relationshipStructure": "OPEN"
}
```

**Validation Rules:**
- `age` ≥ 18
- `distanceMiles` between 15-100
- `ageMin` ≥ 18, `ageMax` ≤ 99
- `ageMin` ≤ `ageMax`
- `lat` between -90 to 90, `lng` between -180 to 180
- Reject `(0, 0)` coordinates

**Response (200 OK):**
```json
{
  "profileStatus": "BASICS_DONE",
  "nextRoute": "/onboarding/intent"
}
```

**Errors:**
- `400`: Validation failure (see error message)

**Source:** [`OnboardingEndpoints.cs:186-325`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### PUT /onboarding/photos

**Description:** Upload 3-6 profile photos.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "photos": [
    {
      "url": "https://storage.blob.core.windows.net/...",
      "caption": "Hiking in the Cascades",
      "sortOrder": 0
    },
    {
      "url": "https://...",
      "caption": null,
      "sortOrder": 1
    },
    {
      "url": "https://...",
      "caption": null,
      "sortOrder": 2
    }
  ]
}
```

**Validation Rules:**
- 3-6 photos required
- Each `caption` ≤ 40 characters
- Photos uploaded via `/media/upload-token` first

**Response (200 OK):**
```json
{
  "success": true
}
```

**Source:** [`OnboardingEndpoints.cs:328-376`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### PUT /onboarding/intent

**Description:** Set relationship intent and openness.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "primaryIntent": "SERIOUS",
  "openness": ["dates", "friendship"],
  "reflectionSentence": "I'm looking for someone who values deep conversation"
}
```

**Response (200 OK):**
```json
{
  "profileStatus": "INTENT_DONE",
  "nextRoute": "/onboarding/foundational"
}
```

**Source:** [`OnboardingEndpoints.cs`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### GET /onboarding/foundational-questions

**Description:** Returns 8 deep questions for the user to answer.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "questions": [
    {
      "id": "conflict_resolution",
      "text": "How do you handle conflict in relationships?"
    },
    {
      "id": "growth_vs_comfort",
      "text": "Do you value growth or comfort more in a partnership?"
    }
  ]
}
```

**Source:** [`OnboardingEndpoints.cs`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs) + [`FoundationalQuestionBank.cs`](../../backend/WovenBackend/Services/FoundationalQuestionBank.cs)

---

### POST /onboarding/foundational

**Description:** Submit answers to foundational questions.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "answers": [
    {
      "questionId": "conflict_resolution",
      "answer": "I prefer to address issues directly and calmly..."
    },
    {
      "questionId": "growth_vs_comfort",
      "answer": "I value growth, even if it's uncomfortable..."
    }
  ]
}
```

**Validation:**
- 8 answers required
- Each answer 10-500 characters

**Response (200 OK):**
```json
{
  "profileStatus": "FOUNDATION_DONE",
  "nextRoute": "/onboarding/details"
}
```

**Note:** Embeddings and AI profile extraction happen in background after submission.

**Source:** [`OnboardingEndpoints.cs`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### PUT /onboarding/details

**Description:** Set bio, optional fields, and accessibility preferences.

**Authentication:** Required (JWT)

**Request:**
```json
{
  "bio": "Software engineer who loves hiking and live music. Looking for someone...",
  "optionalFields": [
    {
      "key": "job_title",
      "value": "Senior Engineer",
      "visibility": "PUBLIC"
    },
    {
      "key": "education",
      "value": "University of Washington",
      "visibility": "MATCHES_ONLY"
    }
  ],
  "weeklyVibe": "cafe_cozy",
  "displayPronouns": "she/her",
  "reduceMotion": false,
  "highContrast": false
}
```

**Validation:**
- `bio` 10-500 characters
- `weeklyVibe` optional (aesthetic preference)
- `displayPronouns` ≤ 50 characters

**Response (200 OK):**
```json
{
  "profileStatus": "DETAILS_DONE",
  "nextRoute": "/onboarding/review"
}
```

**Source:** [`OnboardingEndpoints.cs`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### GET /onboarding/review

**Description:** Returns complete profile for final review before activation.

**Authentication:** Required (JWT)

**Response (200 OK):**
```json
{
  "fullName": "Jane Doe",
  "age": 28,
  "gender": "WOMAN",
  "location": {
    "city": "Seattle",
    "state": "WA"
  },
  "photos": [
    {"url": "https://...", "caption": "Hiking"}
  ],
  "bio": "Software engineer...",
  "intent": {
    "primaryIntent": "SERIOUS",
    "openness": ["dates", "friendship"]
  },
  "foundationalAnswers": [
    {
      "id": "conflict_resolution",
      "q": "How do you handle conflict?",
      "a": "I prefer to address issues directly..."
    }
  ]
}
```

**Source:** [`OnboardingEndpoints.cs`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

### POST /onboarding/complete

**Description:** Finalizes onboarding and activates profile.

**Authentication:** Required (JWT)

**Request:** Empty body

**Response (200 OK):**
```json
{
  "profileStatus": "COMPLETE",
  "nextRoute": "/home"
}
```

**Side Effects:**
- Sets `ProfileStatus = COMPLETE`
- Triggers initial spark wallet creation (5 sparks)
- Enrolls user in daily deck generation
- Bootstraps AI profile vectors

**Source:** [`OnboardingEndpoints.cs:1055-1082`](../../backend/WovenBackend/Endpoints/OnboardingEndpoints.cs)

---

## Profile Status Flow

| Status | Next Route | What Gets Set |
|--------|-----------|---------------|
| `INCOMPLETE` | `/onboarding/start` | Nothing yet |
| `WELCOME_DONE` | `/onboarding/basics` | Acknowledged welcome |
| `BASICS_DONE` | `/onboarding/intent` | Demographics, location, preferences |
| `INTENT_DONE` | `/onboarding/foundational` | Relationship intent |
| `FOUNDATION_DONE` | `/onboarding/details` | Deep Q&A answers + embeddings |
| `DETAILS_DONE` | `/onboarding/review` | Bio, optional fields |
| `COMPLETE` | `/home` | Profile activated |

---

**Last Updated:** 2026-10-07  
**Source File:** `backend/WovenBackend/Endpoints/OnboardingEndpoints.cs`
