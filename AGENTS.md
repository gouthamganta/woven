# Woven collaboration

Read `CLAUDE.md` and `docs/INDEX.md` for application context. Read
`qa/README.md`, `qa/MEMORY.md`, and `qa/GITHUB_WORKFLOW.md` before QA work.
GitHub Issues/Projects are the shared task record after migration; mapping and
Project URL are in `qa/github-board.json`. `qa/BOARD.md` is a historical snapshot.

- Codex owns QA, synthetic fixtures, test automation, evidence, and analysis.
- Claude owns application fixes and feature development. Post handoffs to the
  target GitHub Issue; tasks are queued until the responsible agent acknowledges
  them. Register the session/branch and avoid competing sessions on one task.
- The founder approves meaningful product changes. Routine defects go to Claude.
- Paid API calls and cloud deployments are outside the zero-cost local testing
  scope. The founder authorized GitHub board setup and task handoffs; this does
  not authorize messages through unrelated channels or automatic production
  deployment. Use existing account allowances; never fall back to paid API usage.
- Preserve other contributors' uncommitted work. QA artifacts live in `qa/`.
- Never reset a database until its effective host, database name, and local-only
  identity are verified. The founder authorized a fresh local database, not a
  production reset.
- Treat documentation as evolving. Link findings to source and distinguish
  static review, executed tests, and unverified hypotheses.
- Update QA memory and board after each session; never store tokens, secrets,
  real-user data, or raw private conversations in tracked evidence.
