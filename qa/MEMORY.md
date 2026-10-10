# QA memory

## Current coordination

2026-10-08: #139 canonical pillar docs corrected on
codex-phone/139-fix-canonical-pillars. Code q6 covers Ambition, so false
no-question gaps removed; separate least-coverage note and #123 remain.
Evidence and full changed-file list: qa/evidence/2026-10-08-139-canonical-pillars.md.
Docs only; laptop Codex to verify.

DOC-005/006 handoffs posted to GitHub #93/#94 using authenticated local CLI/API,
so founder relay is unnecessary despite Claude MCP auth gap. Retest at 560525a
found 9 broken-link occurrences and remaining balloon/JWT/error-overview
contradictions. Both Issues moved to Changes requested / owner Claude with
exact reproduction. Evidence: qa/evidence/2026-10-07-doc-retest.md.

Local execution pilot completed in GitHub Issue #103 / draft PR #104. Fixture
only: Claude output, exact local checks, Codex disk-snapshot review and GitHub
handoff/closure. Earlier reviewer-policy failures retained; recovered reviewer
did not need another Claude run. General dispatcher config enabled=false.
Side-by-side Claude 2.1.293 and Codex 0.161.0 packages installed in qa/.local.
Codex remote host gautam connected; Claude Remote Ready in isolated-worktree
mode with capacity 1. Founder phone connection still unverified.

Shared board migration completed: private GitHub Project
https://github.com/users/gouthamganta/projects/2, 25 task Issues #77–#101,
six named views, eight delivery stages, role/priority fields. GitHub Issues are
the task record; qa/BOARD.md is a snapshot. Draft PR #102 publishes the workflow,
intake form and local sync/handoff helpers. Not merged because master pushes
under qa/.github can trigger Azure deploy. No AI dispatcher enabled.
Native phone pairing is pending OPS-002; dispatch/session tracking OPS-001/003.
See qa/GITHUB_WORKFLOW.md and qa/evidence/2026-10-07-github-project-setup.md.

Latest verified checkpoint a5ca8ab: real CL-003 repair landed in a5f29fa.
Independent backend test command exit 0, 8/8 passed (438 ms). CL-003 and QA-005
marked verified for compilation/existing unit baselines only. Migration exists
but DB application untested. MSB3277 EF assembly conflict queued as CL-006;
MessagePack warnings and 46 broken doc-link occurrences persist.
This supersedes historical compile-blocker notes below.

Latest recheck at 74ce4fb: only completion/context/board changed since 7360cd5.
Structural result remains 208 active docs, 14 API files, 46 broken-link
occurrences. DOC-005/006 remain queued; contradictory rule text remains.
Evidence: qa/evidence/2026-10-07-latest-check.md.

Latest: founder reported full documentation complete. QA resumed at 7360cd5.
Structural scan: 208 active Markdown files, 46 missing link occurrences, main
index targets all resolve. 14 API files; assistant omission may be intentional.
First content/source review documented in qa/DOC_REVIEW.md, not full coverage.
DOC-005/006 queue doc defects; CL-005 queues potential initial wallet double
grant for runtime reproduction. CL-003 compile repair remains outstanding.
Earlier pause notes below are historical and superseded by this resumption.

QA paused at the founder's request until Claude updates the repository and
documentation. Founder subsequently authorized an introduction to Claude;
`qa/CLAUDE_HANDOFF.md` contains it. No Claude acknowledgment received yet.

Claude acknowledgment received at aec9eac; docs/INDEX.md is the authoritative
entry point, documentation reported 73% complete. Targeted handoff retest is
authorized; full audit remains paused. Backend retest still failed CS1061 and
CL-003 returned to Claude. Source has environment guards but no claimed DEBUG
guard. MessagePack dependency path confirmed through SignalR Redis.

## Agreement — 2026-10-07

- Goal: investor-demo readiness followed by funded real-user beta; launch market
  and audience are not confirmed.
- Budget: zero paid services by default; tests run locally.
- Codex handles QA and evidence; Claude handles product implementation and is
  actively documenting the application. Avoid changing Claude's documentation.
- Founder permits clearing the local database and requests a local test account.
  No database has been cleared. Verify the target before any destructive action.
- Weekend sandbox missions may be useful, but no cloud deployment or recurring
  automation has been scheduled. Cost and exposure must be reviewed first.

## Observed environment

- Windows / PowerShell, repo `C:/Users/gauta/Desktop/Woven`.
- About 8 GB installed RAM, Intel Iris Xe graphics.
- dotnet, Node, npm, Docker, and Claude CLI are installed.
- Docker engine initially unreachable; recovered by launching Docker Desktop.
- Isolated woven-qa infrastructure is running on an internal container network;
  PostgreSQL woven_qa is healthy with 0 public tables, Redis responds PONG.
- Backend baseline failed compilation (CL-003). Frontend baseline: 3/3 unit tests
  passed. Persona fixture integrity passed; personas not yet imported.
- Baseline commit: `9b7a54e81acef1b94b44667d0eca6fb1fd0425ed`.
- Existing uncommitted implementation log and feature documentation belong to
  the active contributor. Preserve them.
- Backend .NET 10, frontend Angular 21, PostgreSQL 16/pgvector, Redis, Azurite.
- Compose publishes API 5135, frontend 80, database 5433, Redis 6379,
  blob emulator 10000. Older context describes frontend 4202.

## Resume sequence

1. Read board and findings; check git status for concurrent changes.
2. Audit effective sandbox configuration without printing secrets. Isolate
   storage and outbound traffic before starting backend workers.
3. Start/recover Docker and create a separate local QA database/volume.
4. Run migrations; validate schema-aware persona import and local login.
5. Run lifecycle smoke tests, authorization tests, and UI accessibility checks.
6. Send reproducible defects to Claude through the board; retest fixes.
7. Record run metadata and update this file. Never label an unexecuted test passed.
