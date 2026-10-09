# Onboarding Frontend

**Last Updated:** 2026-10-07  
**Location:** `frontend/woven-frontend/src/app/pages/onboarding/`

---

## Overview

The onboarding frontend consists of 9 sequential Angular components that collect user data for profile creation and ECHO matching. Each component uses `OnPush` change detection and the `OnboardingShellComponent` wrapper for consistent layout.

**Key patterns:**
- All components use `ChangeDetectionStrategy.OnPush` — call `cdr.markForCheck()` after async changes
- HTTP calls through `OnboardingService`, never directly in components
- `firstValueFrom()` for one-shot HTTP calls inside async methods
- Progressive state tracking via `ProfileStatus` enum
- Shared shell component for consistent header, progress, and navigation

---

## Components

### 1. Welcome (`welcome.ts`)

**Route:** `/onboarding/start` or `/onboarding/welcome`  
**Purpose:** Introduction screen explaining Woven's philosophy and what to expect  
**Status change:** `INCOMPLETE` → `WELCOME_DONE`

**Template highlights:**
- Three core philosophy statements (no endless scrolling, chosen for you, etc.)
- Expectation checklist with ◈/◇/♡ icons
- Single CTA button: "Let's build your profile →"

**Component:**
```typescript
export class WelcomeOnboardingComponent {
  loading = false;
  err = '';

  async next() {
    this.loading = true;
    this.err = '';
    this.cdr.markForCheck();
    try {
      const res = await firstValueFrom(this.onboarding.submitWelcome());
      this.router.navigateByUrl(res.nextRoute || '/onboarding/basics');
    } catch {
      this.err = 'Something went wrong. Please try again.';
    } finally {
      this.loading = false;
      this.cdr.markForCheck();
    }
  }
}
```

**API:** `POST /onboarding/welcome` → `{ profileStatus, nextRoute }`

---

### 2. Basics (`basics.ts`)

**Route:** `/onboarding/basics`  
**Purpose:** Core demographic and preference data  
**Status change:** `WELCOME_DONE` → `BASICS_DONE`  
**Step indicator:** 2 of 8

**Fields collected:**
- **Name** — First name (max 50 chars)
- **Date of birth** — Month/day/year dropdowns → calculates age
- **Gender** — Pill selector (man, woman, non-binary, transgender, gender-fluid, other, prefer not to say)
- **Pronouns** — Pill selector (he/him, she/her, they/them, other)
- **Sexual orientation** — Multi-select pills (optional)
- **Location** — City dropdown (currently hardcoded to Hyderabad)
- **Distance preference** — Slider (15–100 km, step 5)
- **Interested in** — Multi-select pills (men, women, non-binary people, everyone)
- **Looking for** — Multi-select pills (long-term, short-term, friendship, open to anything)
- **Age range** — Min/max numeric controls with +/− buttons

**Validation:**
- Age must be 18+
- Distance 15–100 km
- `ageMin` ≥ 18, `ageMax` ≤ 99, `ageMin` < `ageMax`
- `interestedIn` required (at least 1)
- Location city/state/lat/lng all required
- Coordinates validated: not (0,0), within valid ranges

**Location handling:**
```typescript
citySelectionChanged() {
  if (this.cityText === 'Hyderabad') {
    this.selectedCity = { 
      city: 'Hyderabad', 
      state: 'Telangana', 
      lat: 17.385, 
      lng: 78.4867 
    };
  } else {
    this.selectedCity = null;
  }
  this.cdr.markForCheck();
}
```

**Age calculation:**
```typescript
get age(): number | null {
  if (!this.dob) return null;
  const diff = Date.now() - new Date(this.dob).getTime();
  return Math.floor(diff / (1000 * 60 * 60 * 24 * 365.25));
}
```

**API:** `PUT /onboarding/basics` → `{ profileStatus, nextRoute }`

---

### 3. Photos (`photos.page.ts`)

**Route:** `/onboarding/photos`  
**Purpose:** Upload 3–6 profile photos  
**Status change:** None (photos saved directly, status managed by parent flow)  
**Step indicator:** 3 of 8

**Requirements:**
- Minimum: 3 photos
- Maximum: 6 photos
- Each photo: URL + optional caption (≤40 chars) + sortOrder
- First photo = primary (shown in all previews)

**Data structure:**
```typescript
export type PhotoPayloadItem = {
  url: string;          // base64 data URL for MVP
  caption?: string;     // <= 40 chars
  sortOrder: number;    // 1..6
};
```

**API:** `PUT /onboarding/photos` → `{ message, count }`

---

### 4. Intent (`intent.ts`)

**Route:** `/onboarding/intent`  
**Purpose:** Capture relationship intent and reflection sentence  
**Status change:** `BASICS_DONE` → `INTENT_DONE`  
**Step indicator:** 4 of 8

**Fields collected:**
- **Primary intent** — Single choice grid (4 options)
  - `long_term` — Something lasting
  - `short_term` — Casual connection
  - `friendship` — Friendship first
  - `open_to_anything` — Open to anything
- **Also open to** — Multi-select pills (same options + "Still figuring it out")
- **Reflection sentence** — Textarea (200 char max)
  - Prompt: "What would a meaningful connection look like for you?"
  - Subtext: "This shapes how we think about your matches — it's for the engine, not your profile."
  - **Encrypted at rest** using AES-256-GCM (see [intent-encryption.md](intent-encryption.md))

**Intent grid styling:**
```scss
.intentGrid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
.intentCard {
  padding: 14px; background: rgba(255,255,255,0.03);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  display: grid; gap: 4px; text-align: left; cursor: pointer;
}
.intentCard.active {
  border-color: var(--gold-400);
  background: rgba(212,160,23,0.08);
}
```

**Character count warning:**
```html
<div class="charCount" [class.warn]="reflection.length > 170">
  {{ reflection.length }} / 200
</div>
```

**API:** `PUT /onboarding/intent` → `{ profileStatus, nextRoute }`

---

### 5. Foundational Questions (`foundational.component.ts`)

**Route:** `/onboarding/foundational`  
**Purpose:** 5 AI-generated questions covering all 8 pillars  
**Status change:** `INTENT_DONE` → `FOUNDATION_DONE`  
**Step indicator:** 5 of 8

**Flow:**
1. Component loads state from `GET /onboarding/state`
2. Fetches questions from `GET /onboarding/foundational/questions`
3. Displays one question at a time (carousel-style)
4. User answers all 5 (min 30 chars, max 400 chars each)
5. Submits all answers to `PUT /onboarding/foundational`

**Question structure:**
```typescript
type FoundationalQuestion = { id: string; text: string };
type AnswerRow = { questionId: string; answer: string };
```

**Gating:**
```typescript
minCharsPerAnswer = 30;
maxCharsPerAnswer = 400;

canNext(): boolean {
  const a = (this.currentAnswer() ?? '').trim();
  return a.length >= this.minCharsPerAnswer && 
         a.length <= this.maxCharsPerAnswer;
}

canSubmit(): boolean {
  if (!this.hasQuestions()) return false;
  if (!this.answers || this.answers.length !== 5) return false;
  return this.answers.every(x => 
    (x.answer ?? '').trim().length >= this.minCharsPerAnswer
  );
}
```

**Helper prompts (rotated per question):**
```typescript
helperPrompts = [
  'Keep it real — 1–3 sentences is perfect.',
  'Think "comfortable + safe", not "impressive".',
  'Small routines > big goals.',
  'Something personal that matters to you.',
  'Describe the everyday vibe (not a movie scene).',
  'Could be a project, a skill, a goal — anything that pulls you forward.'
];
```

**Defer button (v2+ only):**
- Only shown when `allowSkip = true` (versions 2+)
- `hardBlock = true` (v1) → no defer option
- Defer duration: 24 hours
- API: `POST /onboarding/foundational/defer`

**Validation:**
```typescript
private validate(): string | null {
  if (!this.hasQuestions()) return 'Question set is not ready. Reload.';
  if (!this.answers || this.answers.length !== 5) 
    return 'Answers are not ready. Reload.';

  for (let i = 0; i < 5; i++) {
    const ans = (this.answers[i]?.answer ?? '').trim();
    if (ans.length < this.minCharsPerAnswer) 
      return `Please write at least ${this.minCharsPerAnswer} characters for question ${i + 1}.`;
    if (ans.length > this.maxCharsPerAnswer) 
      return `Answer ${i + 1} must be ${this.maxCharsPerAnswer} characters or less.`;
  }

  const ids = this.answers.map(a => a.questionId);
  const unique = new Set(ids);
  if (unique.size !== 5) return 'Duplicate questions detected. Reload the page.';

  return null;
}
```

**SSR safety:**
```typescript
async ngOnInit() {
  const isBrowser = typeof window !== 'undefined' && !!window.localStorage;
  if (!isBrowser) {
    this.loading = true;
    return;
  }
  // ... rest of init logic
}
```

**API calls:**
1. `GET /onboarding/foundational/questions` → `{ version, questions[] }`
2. `PUT /onboarding/foundational` → `{ profileStatus, nextRoute }`
3. `POST /onboarding/foundational/defer` (optional) → `{ message, nextRoute }`

---

### 6. Details (`details.ts`)

**Route:** `/onboarding/details`  
**Purpose:** Bio, optional profile fields, weekly vibe  
**Status change:** `FOUNDATION_DONE` → `DETAILS_DONE`  
**Step indicator:** 6 of 9

**Fields collected:**
- **Bio** — Textarea (300 char max, optional)
- **Job title** — Text input (80 chars)
- **Hometown** — Text input (80 chars)
- **Education** — Pill selector (high school, bachelor's, master's, etc.)
- **School / University** — Text input (100 chars)
- **Height** — Text input (20 chars, optional, saved as `pref_height` → `MatchingOnly`)
- **Zodiac sign** — Pill selector (12 signs, optional)

**Field visibility rules:**
- **Public** fields: `job`, `hometown`, `school`, `education`, `horoscope`
- **MatchingOnly** fields: `pref_height`
- **Bio** always saved with `visibility: Public`

**Field builder:**
```typescript
buildFields() {
  const f: { key: string; value: string; visibility: string }[] = [];
  if (this.jobTitle)       f.push({ key: 'job',        value: this.jobTitle,       visibility: 'Public' });
  if (this.hometown)       f.push({ key: 'hometown',   value: this.hometown,       visibility: 'Public' });
  if (this.school)         f.push({ key: 'school',     value: this.school,         visibility: 'Public' });
  if (this.educationLevel) f.push({ key: 'education',  value: this.educationLevel, visibility: 'Public' });
  if (this.height)         f.push({ key: 'pref_height',value: this.height,         visibility: 'MatchingOnly' });
  if (this.horoscope)      f.push({ key: 'horoscope',  value: this.horoscope,      visibility: 'Public' });
  return f;
}
```

**Skip button:**
```typescript
async skip() {
  this.bio = '';
  await this.next();
}
```

**API:** `PUT /onboarding/details` → `{ profileStatus, nextRoute }`

---

### 7. Lifestyle (merged into Details)

**Route:** `/onboarding/lifestyle`  
**Status:** Merged into Details step (step 6)  
**Note:** Originally a separate step, now consolidated for faster onboarding flow

---

### 8. Review (`review.ts`)

**Route:** `/onboarding/review`  
**Purpose:** Preview all collected data before finalizing  
**Status change:** None (read-only)  
**Step indicator:** 8 of 9

**Data sections:**
1. **Photos** — Thumbnail strip (primary photo badged)
2. **Basics** — Name, gender, location, distance, interested in
3. **Intent** — Primary intent + reflection sentence
4. **Bio** — Full bio text
5. **Foundational** — "✓ Answered" marker (actual answers not shown)

**Edit buttons:**
- Each section has an "Edit" button
- Navigates back to the corresponding step
- Data preserved, allows refinement

**Confirm button:**
- CTA: "This is me — let's go →"
- Triggers `POST /onboarding/complete`
- Starts background vector generation

**Review data loading:**
```typescript
async ngOnInit() {
  try {
    this.review = await firstValueFrom(this.onboarding.getReview());
  } catch {
    this.err = 'Could not load your profile. Please try again.';
  } finally {
    this.loading = false;
    this.cdr.markForCheck();
  }
}
```

**Fallback field access:**
```typescript
get basics()  { return this.review?.basics  ?? this.review?.self?.basics  ?? this.review; }
get intent()  { return this.review?.intent  ?? this.review?.self?.intent  ?? null; }
get photos()  { return this.review?.photos  ?? this.review?.self?.photos  ?? []; }
get bio()     { return this.review?.bio     ?? this.review?.self?.bio     ?? ''; }
```

**API:** `GET /onboarding/review` → `{ profileStatus, self, publicPreview, ... }`

---

### 9. Complete (programmatic)

**Route:** None (triggered from Review)  
**Purpose:** Finalize onboarding, trigger vector bootstrap  
**Status change:** `DETAILS_DONE` → `COMPLETE`

**Backend triggers:**
1. Validates all required data exists (basics, preferences, 3+ photos, intent, foundational, bio)
2. Sets `ProfileStatus = COMPLETE`
3. Starts background task: `UserVectorBuilder.BuildAndSaveV1Async(userId)`
4. Returns `{ profileStatus: "COMPLETE", nextRoute: "/home" }`

**No frontend component** — handled via service call from Review page.

---

## OnboardingShellComponent

**Purpose:** Shared wrapper for all onboarding pages  
**Location:** `onboarding-shell.ts`

**Inputs:**
- `title: string` — Main heading
- `subtitle?: string` — Optional subheading
- `stepNumber?: number` — Current step (1-based)
- `totalSteps?: number` — Total steps
- `stepLabel?: string` — Step name for breadcrumb

**Template structure:**
```html
<div class="shell">
  <header class="header">
    <div class="progress" *ngIf="stepNumber && totalSteps">
      <span class="stepLabel">{{ stepLabel }}</span>
      <div class="progressBar">
        <div class="progressFill" [style.width.%]="progress"></div>
      </div>
      <span class="stepCounter">{{ stepNumber }} / {{ totalSteps }}</span>
    </div>
  </header>

  <main class="content">
    <h1 class="title">{{ title }}</h1>
    <p class="subtitle" *ngIf="subtitle">{{ subtitle }}</p>
    <ng-content></ng-content>
  </main>
</div>
```

**Progress calculation:**
```typescript
get progress(): number {
  if (!this.stepNumber || !this.totalSteps) return 0;
  return (this.stepNumber / this.totalSteps) * 100;
}
```

---

## OnboardingService

**Location:** `frontend/woven-frontend/src/app/onboarding/onboarding.service.ts`  
**Purpose:** HTTP client wrapper for all onboarding endpoints

**Methods:**

| Method | Endpoint | Returns |
|--------|----------|---------|
| `getState()` | `GET /onboarding/state` | `{ profileStatus, nextRoute, completed[], version?, hardBlock?, allowSkip? }` |
| `submitWelcome()` | `POST /onboarding/welcome` | `{ profileStatus, nextRoute }` |
| `submitBasics(payload)` | `PUT /onboarding/basics` | `{ profileStatus, nextRoute }` |
| `savePhotos(payload)` | `PUT /onboarding/photos` | `{ message, count }` |
| `submitIntent(payload)` | `PUT /onboarding/intent` | `{ profileStatus, nextRoute }` |
| `getFoundationalQuestions()` | `GET /onboarding/foundational/questions` | `{ version, questions[] }` |
| `submitFoundationalAnswers(payload)` | `PUT /onboarding/foundational` | `{ profileStatus, nextRoute }` |
| `deferFoundational()` | `POST /onboarding/foundational/defer` | `{ message, nextRoute }` |
| `saveDetails(payload)` | `PUT /onboarding/details` | `{ profileStatus, nextRoute }` |
| `getReview()` | `GET /onboarding/review` | `ReviewResponse` |
| `complete()` | `POST /onboarding/complete` | `{ profileStatus, nextRoute }` |

---

## Navigation Flow

```
/onboarding/start (loads state, redirects to next incomplete step)
  ↓
/onboarding/welcome → POST /onboarding/welcome
  ↓
/onboarding/basics → PUT /onboarding/basics
  ↓
/onboarding/photos → PUT /onboarding/photos
  ↓
/onboarding/intent → PUT /onboarding/intent
  ↓
/onboarding/foundational → GET questions → PUT answers
  ↓
/onboarding/details → PUT /onboarding/details
  ↓
/onboarding/review → GET /onboarding/review
  ↓
(Review confirm) → POST /onboarding/complete
  ↓
/home
```

**State guard:**
- All routes protected by `OnboardingStateGuard`
- Guard calls `GET /onboarding/state`
- Redirects user to `nextRoute` if not on correct step
- Allows bookmarking / refresh without losing progress

---

## Design Patterns

### 1. OnPush Change Detection
All components use `ChangeDetectionStrategy.OnPush` for performance. Always call `cdr.markForCheck()` after async operations:

```typescript
async next() {
  this.loading = true;
  this.cdr.markForCheck(); // ← CRITICAL
  try {
    const res = await firstValueFrom(this.onboarding.submitBasics(...));
    this.router.navigateByUrl(res.nextRoute);
  } finally {
    this.loading = false;
    this.cdr.markForCheck(); // ← CRITICAL
  }
}
```

### 2. Progressive Disclosure
Each step asks for 1–2 related pieces of information. Never more than 7 form fields visible at once.

### 3. Optimistic Validation
Client-side validation prevents API calls with invalid data. Backend validates again for security.

### 4. Error Recovery
- All API errors caught and displayed as inline messages
- No crashes, no navigation on error
- User can retry without losing filled data

### 5. Skip Affordance
Only Details step has a "Skip" button. All other steps are required for profile completion.

---

## Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| Buttons not responding | Forgot `cdr.markForCheck()` | Add after all state changes |
| SSR hydration mismatch | Init logic runs on server | Wrap in `isBrowser` check |
| Age dropdown wrong year range | Hardcoded year calculation | Use `new Date().getFullYear() - 18 - i` |
| Foundational stuck loading | Questions API failed | Check network tab, verify JWT |
| Review shows wrong data | Stale state | Reload from API on init |

---

## Future Enhancements

- **Voice input** for foundational answers (Phase 3)
- **Camera capture** for photos (currently upload-only)
- **Multi-language** question rewriting
- **Auto-save** progress as user types (debounced)
- **Horoscope field** in basics (designed, not wired)
