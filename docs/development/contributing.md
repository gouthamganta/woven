# Contributing to Woven

**Consolidated from:** `docs/contributing/CONTRIBUTING.md`

---

## Code Philosophy

### Evidence Over Speculation

Every implementation decision must be traceable to a concrete requirement: a product rule, a signal we record, a UI constraint we enforce.

**Do not add features or abstractions because they "might be useful."** If you can't point to why something exists, it probably shouldn't.

### No Premature Abstraction

Write the simplest version that works. Introduce an interface or base class **only when you have two concrete implementations** that actually need it — not in anticipation of a third.

### Minimal Comments

Code should be self-explanatory at the line level. Comments belong at the method or class level when the **why** is non-obvious (business rules, ECHO signal semantics, regulatory constraints).

**Never comment what the code does — comment why it does it.**

### Invisible AI Principle

The app never shows users raw compatibility scores, community ratings, or AI confidence values. Every AI surface is ambient UX.

**If a PR makes an AI value visible to users, it will not be merged.**

---

## Branch Strategy

- Branch off `master` for all work: `feature/your-feature-name`, `fix/short-description`, `chore/what-it-is`
- **One logical change per branch.** Do not bundle unrelated fixes.
- **Rebase on `master` before opening a PR** — merge commits in PRs are not accepted.
- Delete your branch after merge.

**Example:**
```bash
git checkout master
git pull origin master
git checkout -b feature/add-voice-notes
# ... make changes ...
git add .
git commit -m "feat: add voice note recording and playback"
git push origin feature/add-voice-notes
# Open PR on GitHub
```

---

## PR Workflow

1. **Open a draft PR** as soon as you have working code worth reviewing — do not wait for perfection.
2. **Fill in the PR description:** what changed, why, and what you tested.
3. **Self-review your own diff** before requesting review. Check the "0 errors" mandate below.
4. **Request at least one reviewer.** No self-merges.
5. **CI must be green** before merge. Both backend and frontend checks run on every PR.

---

## The "0 Errors" Mandate

### Backend

After every backend change, run:
```bash
cd backend/WovenBackend
dotnet build
```

The build must produce **0 errors** and **0 warnings you introduced**. If `dotnet build` fails, the PR is not ready for review.

### Frontend

After every frontend change, run:
```bash
cd frontend/woven-frontend
npx ng build --configuration development
```

This runs the TypeScript type-checker and produces a real bundle. It must produce **0 errors**. A build that passes `ng serve` but fails `ng build` is a broken build.

**Both checks run in CI on every PR push.** A PR with a failing build will not be merged regardless of how the diff looks.

---

## Signal Recording Requirement

Behavioral signals are the raw material of ECHO — the matchmaking ML pipeline. Any user action that constitutes a behavioral signal **must** call:

```csharp
await _matchSignalService.RecordAsync(
    viewerId, candidateId, eventType, eventValue, metadataJson, ct);
```

**Skipping this call means ECHO never learns from that event.** It is a data integrity violation, not an optional step.

**If you introduce a new behavioral event:**
1. Add a constant to `MatchSignalEventTypes`
2. Call `RecordAsync` in the endpoint or service that handles the action
3. Document the new signal in `docs/signals/SIGNALS_VECTORS_SCORING.md`

`MatchSignalLogs` is **append-only**. Never update or delete rows in that table.

---

## Design Rules Enforcement

These rules are **hard constraints**, not style preferences. Violations will be requested changes in code review.

| Rule | What it means |
|---|---|
| **No hover `translateY` lifts** | Hover states use glow, shadow, or color only. `:active` scale is fine. |
| **No age on Moments cards** | Cards show name, verification badge, explanation, and actions — nothing else. |
| **No community ratings displayed** | Star ratings and flag scores are platform-internal signals. Never render them for users. |
| **CSS variable tokens only** | Every visual value must use a token from `styles.scss`. No raw hex codes or pixel values outside the token system. |
| **`woven-bg` is off-limits** | Do not touch the background drift component unless the task explicitly requires it. |
| **No paywalls** | Spark economy is the soft gate. No hard feature locks behind payment. |
| **`ChatNote` data is backend-only** | Never surface a user's own notes or another user's notes in the UI. |

---

## Backend Code Conventions

- All endpoints require `.RequireAuthorization()`. **Exceptions:** `/health`, `/auth/google`, explicitly public onboarding steps. If you're adding an endpoint without auth, explain why in the PR.
- Extract the current user with `EndpointHelper.GetUserId(http.User)` — do not parse the JWT claim directly.
- Use `MomentsRules.NowUtc()` for all timestamp calculations. **Never call `DateTime.UtcNow` directly.**
- Return HTTP errors with `Results.Problem(...)` or `Results.NotFound()` — not raw strings or custom objects.
- Entity column names are snake_case configured via `.HasColumnName(...)` in the DbContext. Match the convention when adding new properties.

**Example endpoint:**
```csharp
app.MapGet("/matches", async (WovenDbContext db, HttpContext http, CancellationToken ct) =>
{
    var userId = EndpointHelper.GetUserId(http.User);
    var now = MomentsRules.NowUtc();
    
    var matches = await db.Matches
        .Where(m => m.UserAId == userId || m.UserBId == userId)
        .ToListAsync(ct);
    
    return Results.Ok(new { count = matches.Count, matches });
})
.RequireAuthorization()
.WithName("GetMatches");
```

---

## Frontend Code Conventions

- All page components use `ChangeDetectionStrategy.OnPush`. After any async state change, call `cdr.markForCheck()` or `cdr.detectChanges()`. **Forgetting this produces invisible bugs.**
- HTTP calls belong in service classes. Components call services — **never `HttpClient` directly.**
- One-shot HTTP calls inside `async` methods use `firstValueFrom()`.
- For chat sends, apply **optimistic UI**: add a temporary message immediately, confirm on success, do a silent reload.
- Route parameters are extracted by walking the full route tree. See the `getThreadIdFromRouteTree()` pattern in `chat-thread` for the canonical approach.

**Example service call:**
```typescript
// ❌ Bad: HttpClient in component
constructor(private http: HttpClient) {}

async loadMatches() {
  this.matches = await firstValueFrom(
    this.http.get<any>('/matches')
  );
}

// ✅ Good: Service layer
constructor(private matchService: MatchService) {}

async loadMatches() {
  this.matches = await this.matchService.getMatches();
  this.cdr.markForCheck(); // OnPush detection
}
```

---

## Migration Conventions

- **File name pattern:** `yyyyMMddHHmmss_DescriptiveName.cs` (e.g., `20260525004334_AddDateIdeasJson.cs`)
- Update `WovenDbContextModelSnapshot.cs` whenever you add entity properties.
- pgvector columns cannot be applied via EF Migrations locally (pgvector is not installed in local Postgres). Apply those columns manually via `psql` and document the raw SQL in your PR description.

**See [database-migrations.md](database-migrations.md) for full guide.**

---

## Code Review Expectations

Reviewers check:

1. **Correctness** — Does it do what the PR says?
2. **Signal coverage** — Does any new behavioral event call `RecordAsync`?
3. **Design rule compliance** — Does any new UI element violate the hard rules above?
4. **Build cleanliness** — 0 errors in both backend and frontend.
5. **Auth** — Every new endpoint has `.RequireAuthorization()` or a documented reason it doesn't.
6. **Abstraction hygiene** — Is the complexity justified by the requirement?

**Reviewers do not check line-by-line style** unless it violates the conventions above. Nits are optional feedback, not blocking.

---

## Pre-Merge Checklist

Before requesting review, verify:

- [ ] `dotnet build` produces 0 errors (backend)
- [ ] `npx ng build --configuration development` produces 0 errors (frontend)
- [ ] `dotnet test` passes (backend)
- [ ] `npm test -- --watch=false` passes (frontend)
- [ ] All new endpoints have `.RequireAuthorization()` or documented exception
- [ ] All new behavioral events call `RecordAsync`
- [ ] No design rules violated (no raw scores shown, CSS tokens used, etc.)
- [ ] PR description filled out (what, why, tested)
- [ ] Self-reviewed the diff

---

**Source:** Consolidated from `docs/contributing/CONTRIBUTING.md`  
**Last Updated:** 2026-10-07
