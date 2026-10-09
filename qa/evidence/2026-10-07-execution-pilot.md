# Execution and phone endpoint pilot

Local date: 2026-10-07 (America/Indianapolis); raw run timestamps are UTC and
cross midnight. Tested base: 560525aac5c579ece1d6682ded3cd962310c4a2b.

## Verified

- Claude account login: claude.ai subscription (Pro). Codex login: ChatGPT.
- Side-by-side local packages installed: Claude 2.1.293, Codex 0.161.0. Existing
  active Claude/IDE binaries were not replaced.
- Offline pilot-contract tests: 4 passed, 0 failed. These test malformed fixture,
  extra fields, stage ordering, forbidden change paths and malformed review.
- Live pilot Issue #103 passed after reviewer-policy recovery. One synthetic
  fixture only; no backend/database/UI behavior validated.
- Claude session: 655b718f-6e53-49d2-b03a-85660b1fde58.
- Successful Codex snapshot-review session: 01a11971-c677-7fe3-bd25-f7a6b8c5fc73.
- Tested artifact commit: 4a2a0387c6cd43945a9286522ac049fbc8783eb4.
- Evidence/fixture published on automation/pilot-103-066d5463 in draft PR #104.
- Worktree confinement check and exact fixture validator passed before review;
  SHA-256 unchanged after reviewer call.
- Failed review attempts were preserved; success was not claimed until the
  supplied-snapshot method passed. Earlier command prohibitions/policy denials
  were orchestration problems; no broader execution permissions were enabled.
- Codex Remote startup returned status=connected, host=gautam.
- Claude Remote server returned Ready, worktree mode, capacity 0/1.

## Not verified or enabled

- General work queue execution, distributed locks, broad application tests,
  unattended startup and instant interruption are not enabled/validated.
- Pause-at-phase-boundary, timeout and process-spawn failure handling still need
  fault drills before broader unattended use.
- Phone-to-host connection has not been exercised by the founder yet.
- No automatic merge or deployment. Account allowances consumed by CLI calls;
  no paid API-key fallback configured. Reporting subscription eligibility does
  not imply unlimited inference or guarantee future account availability.
