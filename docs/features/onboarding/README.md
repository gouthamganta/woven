# Onboarding Feature

**Last Updated:** 2026-08-17

---

## Overview

The onboarding feature is the multi-step user registration and profile setup flow that collects all data needed for ECHO matching and profile display. Users complete 9 sequential steps before entering the app.

**Status:** Production-ready  
**Location:** `frontend/woven-frontend/src/app/pages/onboarding/`  
**Backend:** `backend/WovenBackend/Endpoints/OnboardingEndpoints.cs`

---

## Journey Outline

The onboarding flow consists of 9 steps, each storing data incrementally and advancing the user's `ProfileStatus`:

| Step | Route | ProfileStatus Transition | Key Data |
|------|-------|--------------------------|----------|
| 1. Welcome | `/onboarding/welcome` | `INCOMPLETE` → `WELCOME_DONE` | Acceptance of flow |
| 2. Basics | `/onboarding/basics` | `WELCOME_DONE` → `BASICS_DONE` | Name, age, gender, location, preferences |
| 3. Photos | `/onboarding/photos` | — | 3–6 photos (saved directly, no status change) |
| 4. Intent | `/onboarding/intent` | `BASICS_DONE` → `INTENT_DONE` | Primary intent + openness + reflection sentence |
| 5. Foundational | `/onboarding/foundational` | `INTENT_DONE` → `FOUNDATION_DONE` | 5 AI-generated questions (8 pillar coverage) |
| 6. Details | `/onboarding/details` | `FOUNDATION_DONE` → `DETAILS_DONE` | Bio, optional fields, weekly vibe |
| 7. Lifestyle | `/onboarding/lifestyle` | — | (Merged into details) |
| 8. Review | `/onboarding/review` | — | Preview of all collected data (read-only) |
| 9. Complete | `/onboarding/complete` | `DETAILS_DONE` → `COMPLETE` | Triggers bootstrap vector generation |
| Start | `/onboarding/start` | — | Loading screen + transition to `/moments` |

**State tracking:** `GET /onboarding/state` returns `{ profileStatus, nextRoute, completed[] }` to route users to the correct step.

---

## Key Features

### 1. Intent Reflection Encryption
User's reflection sentence (Intent step) is **encrypted at rest** using AES-256-GCM before being stored in the database. Only the backend can decrypt it. This protects users' deeply personal reflections from DB leaks.

**Implementation:** `EncryptionService.cs` → [intent-encryption.md](intent-encryption.md)

---

### 2. AI-Generated Foundational Questions
Each user receives **5 personalized questions** generated from a stable question bank. The questions cover all 8 pillars but are rewritten by OpenAI to match the user's name, gender, and intent.

**Canonical 8 Pillars:**
- Lifestyle
- Energy
- Values
- Communication
- Ambition
- Stability
- Curiosity
- Affection

**Implementation:** `FoundationalQuestionBank.cs` + `FoundationalCycleService.cs` → [foundational-questions.md](foundational-questions.md)

---

### 3. Bootstrap Vector Generation
When a user completes onboarding (`POST /onboarding/complete`), a background task triggers `UserVectorBuilder.BuildAndSaveV1Async()` to create the user's initial ECHO matching vector.

This vector is used for:
- Daily deck generation
- Match scoring
- Pillar-based compatibility analysis

**Implementation:** `UserVectorBuilder.cs` → [bootstrap-vectors.md](bootstrap-vectors.md)

---

### 4. Recurring Foundational Cycles
After onboarding, users are prompted to re-answer foundational questions on a schedule:
- **v1:** Initial onboarding (hard-block)
- **v2:** 15 days after v1 (soft-block, deferrable 24h)
- **v3:** 45 days after v2 (soft-block, deferrable 24h)
- **v4+:** 60 days after previous (soft-block, deferrable 24h)

This keeps user vectors fresh and improves matching accuracy over time.

---

## Data Flow

```
User → Frontend (Angular) → Backend (ASP.NET) → PostgreSQL

1. User submits each step
2. Frontend sends HTTP PUT/POST to /onboarding/{step}
3. Backend validates, saves to DB, advances ProfileStatus
4. Frontend navigates to nextRoute
5. On completion: triggers vector generation (async)
```

**Navigation guard:** `OnboardingStateGuard` checks `GET /onboarding/state` and redirects users to the correct step if they refresh or bookmark a URL.

---

## Files

| File | Purpose |
|------|---------|
| [flow.md](flow.md) | Step-by-step breakdown of each onboarding screen |
| [foundational-questions.md](foundational-questions.md) | AI pillar question generation system |
| [intent-encryption.md](intent-encryption.md) | AES-256-GCM encryption of reflection sentence |
| [bootstrap-vectors.md](bootstrap-vectors.md) | Initial vector creation at onboarding completion |
| [frontend.md](frontend.md) | Angular components + routing |
| [backend.md](backend.md) | .NET endpoints + validation logic |
| [api.md](api.md) | HTTP API reference for all onboarding endpoints |

---

## Design Principles

1. **Progressive disclosure** — Each step asks for 1–2 related pieces of information, never overwhelming.
2. **Edit-friendly** — Users can navigate back to any step from the Review screen.
3. **Zero irreversible decisions** — Everything can be changed later in `/you/settings`.
4. **Privacy-first** — Intent reflection is encrypted, foundational answers never shown to other users.
5. **Fast + minimal** — ~5 minutes to complete (target: 3–4 minutes for focused users).

---

## Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| Stuck on foundational | `QuestionsJson = "[]"` in DB | Endpoint auto-heals from bank on GET |
| "No active set" error | User deferred but server doesn't track | Check `DeferredUntil` column |
| Vector not created | Background task failed | Check logs for `[VectorBuilder]` errors |
| Age validation fails | Client sends `dob` as partial string | Ensure `yyyy-mm-dd` format |
| City not saving | Missing `lat/lng` coordinates | Hardcoded to Hyderabad, validate presence |

---

## Next Steps

- **Horoscope field** — Designed but not yet added to onboarding flow
- **Voice onboarding** — Record answers via voice instead of typing (deferred to Phase 3)
- **Profile photos from camera** — Currently upload-only, no camera capture
- **Multi-language support** — Foundational questions rewrite in user's preferred language
