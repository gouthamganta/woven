# Issue #119 — community rating removal

- Branch: `codex-phone/119-remove-rating-bar`, based on master `3df9759`.
- Source of truth: R-2 read from `worktree-bridge-cse_01Mm5a2AcMHJDTSceZnHsEoo`.
- Removed rating aggregation and response fields from both Moments GET endpoints.
- Removed flag bars, helper, styles, frontend rating types, and New here badges
  whose condition depended solely on rating counts.

## Executed checks

- `dotnet build` in `backend/WovenBackend`: exit 0, 0 warnings, 0 errors.
  SDK 10.0.401 installed temporarily under `/tmp/woven-dotnet`.
- `dotnet test --no-restore` in `backend/WovenBackend.Tests`: exit 0,
  12 passed, 0 failed, 0 skipped. Four new cases execute actual mapped GET
  request delegates and deserialize their JSON, with 0 or 5 synthetic ratings.
  Existing MSB3277 EF Relational 10.0.4/10.0.12 conflict warning remains.
- `npx ng build` in `frontend/woven-frontend`: exit 0. Existing
  landing-simple component stylesheet warning: 40.92 kB against 20 kB budget.
- `git diff --check`: passed.

## Environment recovery and limitations

Initial `npm ci` failed because the existing lockfile lacks encoding. Used
`npm install --package-lock=false --no-audit --no-fund`; no manifest/lock edits.
Initial frontend build could not fetch Google Fonts in the default sandbox;
retry with network access passed. Initial test runner required network permission
for its local communication socket. Test fixture excludes unrelated PostgreSQL
vector fields because the in-memory provider cannot map them.

No production services, real-user data, paid APIs, database resets, merge or
deployment. Tests execute handlers with synthetic authentication and EF InMemory;
full HTTP middleware, PostgreSQL integration and browser visual QA remain for
Codex laptop. GitHub Issue is the active task record; historical board unchanged.
