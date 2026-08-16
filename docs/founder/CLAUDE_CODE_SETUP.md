# Claude Code Setup Guide for Woven

Practical guide for using Claude Code effectively in this project. Written for a developer who is already familiar with the codebase and wants to use AI assistance efficiently without introducing regressions or inconsistencies.

---

## Prerequisites

- Claude Code CLI installed and authenticated
- Repo open at `c:\Users\gauta\Desktop\Woven\` (the project root)
- Both backend and frontend dev environments available (see `CLAUDE.md` build commands)

When you open Claude Code from the Woven repo root, `CLAUDE.md` is automatically loaded. You do not need to re-explain the architecture, the feature vocabulary, or the design rules. They are already in context.

---

## The CLAUDE.md File

`CLAUDE.md` at the project root is the most important file in AI-assisted development on this project. It is loaded at the start of every Claude Code session and contains the authoritative context that prevents the AI from making consistency errors.

What it contains:

**Architecture overview**: where each concern lives (`Endpoints/`, `Services/`, `Services/Matchmaking/`, `data/Entities/`, `Migrations/`), the key patterns (RequireAuthorization on all endpoints, GetUserId helper, MomentsRules.NowUtc(), IMatchSignalService.RecordAsync).

**Feature vocabulary**: the exact terms to use. Always use these. Never deviate.

| Use this | Not this |
|---|---|
| Deck | feed, discovery, browse |
| Drawn | liked-you, likes, mutual |
| Magical | like, heart, star |
| Resonant | match, connect, link |
| Balloon | connection window, chat unlock |
| Tile | post, card, content |
| Commons | feed, explore, discover |
| ECHO | matching engine, algorithm, AI |
| Find Love | final stage, date unlock |
| Trial | trial period, commitment window |

**Hard design rules** (never violate these, regardless of how reasonable they look in isolation):
- No hover translateY lifts on any element. Hover = glow/shadow/color only.
- No age on Moments cards.
- Background drift (woven-bg component) stays on.
- No community ratings shown to users.
- No hard paywalls.
- All values use CSS variable tokens from styles.scss — no raw hex, no raw px outside the token system.
- ChatNote data is background signal only — never shown to users.
- JWT stays in localStorage (dev convenience; do not move to in-memory).

**Signal pipeline docs**: the full signal type table with EventType names, batch worker schedules.

**Known gaps**: what is missing and why. Read this before starting any work to avoid implementing something that collides with a known gap.

**Documentation mission progress log**: tracks which source files have been read and which docs are done. Update this log when you complete a documentation sprint.

---

## Memory System

Claude Code maintains persistent memory at:
`C:\Users\gauta\.claude\projects\c--Users-gauta-Desktop-Woven\memory\`

These files survive across sessions. They contain decisions and context that are not in the code itself but need to be remembered:

- `user_profile.md` — developer context and working style
- `feedback_rules.md` — the hard design rules
- `project_state.md` — current state, priorities, key decisions
- `commons_architecture.md` — Commons feed signal pipeline
- `matching_engine_plan.md` — ECHO pipeline design decisions
- `matching_engine_deep_dive.md` — full signal anatomy and scoring formulas
- `my_tiles_spec.md` — My Tiles page design (locked)
- `project_cta_gate.md` — CTA gate option 3 design

If you make a significant architectural decision during a session, ask Claude Code to update the relevant memory file. Otherwise it will be lost at session end.

---

## Effective Prompting Patterns

**Always read the source file before changing it.**

Do this:
> "Read `backend/WovenBackend/Services/Matchmaking/MatchScoringService.cs` and then add a new scoring component for SharedTileAffinity."

Not this:
> "Add a SharedTileAffinity component to the matching engine."

The first prompt grounds the AI in the actual code. The second invites invention.

**Quote exact values when asking about behavior.**

Do this:
> "WeightLearningBatchWorker requires MinSamples=10 and MinConnectionScore=0.08. If a user has 8 qualifying scores, does the batch job write personalized weights?"

Not this:
> "Does the weight learning job run for all users?"

Exact values prevent the AI from guessing and producing a plausibly-wrong answer.

**Say "evidence-only" when you want documentation that won't speculate.**

> "Write documentation for the ECHO pipeline. Evidence-only — every claim must be traceable to what's in the codebase."

This instruction suppresses the tendency to pad with generic best practices.

**Tell it which patterns to follow.**

For new endpoints:
> "Follow the MapXxxEndpoints pattern in `Endpoints/`. RequireAuthorization. Use GetUserId helper."

For new signals:
> "Add a new signal type to MatchSignalEventTypes constants class. Record it via IMatchSignalService.RecordAsync in the appropriate handler."

For new migrations:
> "Create a new migration following the yyyyMMdd format. Update WovenDbContextModelSnapshot. Note: pgvector columns must be applied manually via psql — do not generate migration SQL for vector columns."

---

## Plan Mode

Use plan mode before implementing any multi-file change. Plan mode (accessed via `/plan` in Claude Code) lets the AI reason through a change and surface trade-offs before writing any code.

When to use it:
- Adding a new scoring component to MatchScoringService (affects weight array, ConnectionScore formula, WeightLearningService input)
- Adding a new batch worker (affects Program.cs scheduler, WOVEN_DISABLE_BATCH_WORKERS isolation pattern)
- Adding a new embedding modality (affects EmbeddingBatchWorker, DailyDeckOrchestrator, pgvector column, migration)
- Changing the Trial period mechanics (affects multiple signal types, MatchSignalEventTypes, front-end state machine)
- Modifying the spark economy rules (affects InteractionBudgetService, SparkWalletService, ghost refund paths)

Plan mode output should be reviewed before approving implementation. The plan will surface files that need to change, patterns that must be followed, and edge cases that need handling.

---

## Background Agents

For research or writing tasks that are time-consuming but don't block other work, use background agents. Background agents run independently and notify you when complete.

Useful for:
- Reading and summarizing a large service file while you work on something else
- Writing documentation for a section of the codebase while reviewing another
- Running a build check after a set of changes while planning the next change

Background agents in parallel is the pattern that made the 35-file documentation sprint possible in a single session. Each agent received a specific task and a specific fact table; all ran simultaneously.

---

## Common Tasks Claude Code Handles Well in This Project

### Adding a new signal type

1. Add the EventType constant to `MatchSignalEventTypes` constants class
2. Add `RecordAsync(...)` call to the appropriate event handler
3. If the signal should feed ConnectionScore, update the weight array in `ConnectionScoreBatchWorker`
4. Update `CLAUDE.md` signal type table

### Writing a new migration

1. Create file in `Migrations/` with filename pattern `yyyyMMdd_DescriptiveName.cs`
2. Update `WovenDbContextModelSnapshot.cs`
3. Note: pgvector columns (vector type) cannot be applied via `dotnet ef` locally — apply manually via psql. Do not include vector column DDL in the auto-generated migration.

### Adding a new API endpoint

1. Create or extend the relevant file in `Endpoints/` following `MapXxxEndpoints` pattern
2. Register in `Program.cs` if a new file
3. All endpoints require `.RequireAuthorization()` unless explicitly public
4. Use `GetUserId(http.User)` helper for user ID extraction — do not use `ClaimsPrincipal` directly
5. Use `MomentsRules.NowUtc()` for timestamps — not `DateTime.UtcNow`

### Adding an Angular service method and component state

1. HTTP call goes in the service class, never directly in a component
2. Use `firstValueFrom()` for one-shot HTTP calls inside async methods
3. All pages use `ChangeDetectionStrategy.OnPush` — call `cdr.markForCheck()` or `cdr.detectChanges()` after any async state change
4. Optimistic UI pattern for sends: add temp message → confirm → silent reload

### Reviewing MatchScoringService changes

When adding or modifying a scoring component:
- Verify the weight array is consistent with the `ConnectionScoreBatchWorker` weight map
- Verify that `WeightLearningService` includes the new component in its feature vector
- Verify that the component's output range matches the expected input range for logistic regression (typically [0,1] or normalized)

---

## Known Limitations

**Cannot run migrations with pgvector locally.** pgvector is not installed in the local dev environment. Any migration that touches vector columns must be applied via psql against the remote PostgreSQL Flexible Server. Do not attempt `dotnet ef database update` for vector column migrations locally.

**Cannot start Docker services.** The development environment does not assume Docker Compose is running. Backend and frontend are started directly (`dotnet run`, `npx ng serve`).

**Cannot push to GitHub without explicit permission.** Claude Code will not push branches or create PRs without the developer explicitly requesting it. This is a safety behavior.

**Cannot verify batch worker outputs without a real database.** `ConnectionScoreBatchWorker` and `WeightLearningBatchWorker` require real signal data in the database to produce meaningful output. AI code review can verify logic, but end-to-end batch job verification requires a real environment.

**Build verification is required after every change.** Always run:
```bash
# Backend
cd backend/WovenBackend
dotnet build

# Frontend
cd frontend/woven-frontend
npx ng build --configuration development
```

Both must produce 0 errors before considering work done. Claude Code will remind you of this; do not skip it.
