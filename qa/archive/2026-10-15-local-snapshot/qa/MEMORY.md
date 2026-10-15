# QA memory

## Current coordination

Latest sustained QA: phone PRs137/141/142 and Claude rules140 reviewed directly,
with independent tests/diffs and findings posted. Claude client176cf74 (#167,
stacked on144) passes51/51 tests+build; originalsame37 baseline18/37. Proxy
c6b090c (#168 stacked167) passesnginx-t and16/16 realcontracts vs3/16 original.
Real mobile anon→login and signedsynthetic→moments/backend200 pass; actual hub
websocket handshake succeeds, delivery/isolation/reconnect not verified. Query/
referrer marker redaction checked in logs. Local QA frontend5180 now uses these
candidates via privateproxy.env; root appcontainers/master unchanged. One-hour
ordinary synthetic launcher at http://127.0.0.1:5180/qa-access.html, Git-ignored.
Registry130 named entries/98 partialfamilies, not all pass or completecoverage.
Data failures124 duplicatequota(3 debits/2 saved),165 partialmatcheddeletion,
166 encryptedemailreuse remain Claude work; CI163 and docs164 also open.
Own importer repeats1→0 inserts and rejects4 invalidmanifests at100→100 users.
Failed actor32 partialstate intentionally retained. Sourceevidence dbf20e7 PR146,
reports phone-pr-review/client-and-data-round/proxy-and-browser under evidence.
No Google/provider/migration/fullsecurity sign-off; no cloud deploy, mainmerge,
reset or paidfallback. General dispatcher remainsdisabled; bounded local Claude
loops actually executed, independently retested.113/114 now ReadyforQA/Codex.

Phone PRs now independently reviewed at exact heads:137(dce8c89)12 tests+build
+4 mocked browser checks pass; merge with144 auto-merges and same12 tests pass.
141(5de72ae)8 tests+6 new cleanup cases pass, but conflicts with144 in budget/rule
files; probes aborted.142(e02b628)targeted canonical/q6 docs pass; pre-existing
dimension drift164.140(e387d02)not draft, ChatNote contradiction still needs143.
Shared CI fail gates queued163 (including three new rating-test whitespace lines).
Continued real PostgreSQL QA: four duplicate choices after warmup gave200/409/
409/500; budget3 vs responses2/notes2.124 moved Changes requested/Claude;108
notified missing error correlation. Evidence committedaa348b1 in draft146; full
QA stays In progress. Registry76 named entries/design97 families, not complete
coverage. No merge, reset, deploy or paid call. Direct handoffs posted; no general
agent dispatcher started. RootMemory history below is earlier checkpoints.

Latest round2:44 cases added on qa/full-local-audit, commit8291fad, draftPR146.
Backend baseline41/45 (four72h contracts fail), candidate5461c85 backend45/45,
frontend13/24 (eleven cases across three control gaps: six route authorization,
four invalid-session, one credential-origin restriction). No server bypass proven.
Actual route paths/guards tested through RouterTestingHarness with probe page
rendering. Cookie/encryption primitives pass, not browser CSRF or key-rotation
sign-off. REGRESSION_CASES.json maps69 named cases to partial families. #108/#114
have direct Claude handoffs, #151/#156 moved In progress; #147 remains In progress.
All expected failures stay visible; no skipped cases or feature code changes.
Report/evidence: evidence/2026-10-08-regressions-round2.md and matching JSON.

Coverage expansion checkpoint: Codex created #147–#162 (16 suite Issues,
96 design families) and published 14 new regressions plus measured evidence in
draft PR #146, commit 2f9fa85, qa/full-local-audit. Tests: backend 15/15,
frontend 10/10 passed on baseline application 3df9759, not candidate 5461c85.
Backend line coverage 122/93816 (~0.13%), branch 15/5701 (~0.26%), including
generated migrations. Frontend 34.95% lines/45.53% branches applies ONLY to
loaded files; not a whole-app percentage. #147 and #162 remain In progress;
others queued. TEST_COVERAGE_PLAN.md, TEST_CASE_FAMILIES.json,
TEST_CASE_MAPPING.json and TEST_SURFACES.json define obligations/traceability,
not a new task board. Most source surfaces remain unmapped. Standalone guard
passes do not resolve disabled route guards (#114). Raw coverage stays local.

2026-10-08 full QA resumed. Baseline 3df9759, Claude-authored candidate 5461c85
published as draft PR #144 (fix/qa-startup-and-72h); no master merge/deployment.
Local API uses separate MODEL-CREATED woven_qa_model schema because fresh
migrations still fail (#111). Imported 100 synthetic adults; deletion probe
removed one, so 99 remain. Root app code is unchanged; candidate alone has 72h.
Persisted match expiry is exactly 72h. Latest targeted sets: API 34/35,
corrected lifecycle 18/18, security 11/12. Concurrent eight choices yielded five
successes and three cap denials; independent DB counts budget/responses/notes=5.
Final existing backend tests 8/8 pass (542ms), EF warnings remain. Earlier
frontend build and existing tests 3/3 passed; guest UI scope only.
Founder clarified in Codex: show BOTH ChatNotes to the matched pair. Third-user
chat denial passed. Correct contradictory docs via GitHub #143, not a privacy
feature change. #126 contains this clarification. #108 has security follow-up:
deleted-account token still receives authenticated response, unbounded
correlation echo and readiness despite pending migrations. See
evidence/2026-10-08-full-qa.md and SECURITY_CONTROLS.md for explicit coverage gaps.
Continue #109 with migration/routing/auth fixes, fault injection, full feature
journeys and retention/provider tests. No paid API fallback, general dispatcher
still disabled. Keep public Issue summaries sanitized; private runtime secrets
and raw synthetic conversations remain in ignored .local. Task record is
GitHub only; do not update BOARD.md. Older checkpoint entries below are history.

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

## Backend priority continuation - 2026-10-09 (supersedes parked coverage)

Founder resumed backend tests now while Claude repairs144. Current-master baseline
20a630e, isolated qa/.local/backend-coverage-next, qa/backend-coverage-20261009.
209 cases added in ee1ba0c, draftPR179. Debug/Release386/386pass,0skipped;
backendbuild0warnings/0errors, changed-test fullformat and diff checks pass.
Current collector6064/22307lines27.18%,1151/5715branches20.14%; both80%gates
unfinished. Existing generated migration exclusion only; no old-binary union.
Auth loopback middleware, chat/ChatNotes/trial/block/voice/date handlers,
matching/trust/history, own data export/isolation and error/PII contracts.
InMemory does not prove PostgreSQL/transactions/deletion; loopback explicitJWT
config does not certify Program/cookies/CSRF/CORS or deleted/banned accounts.
No application edits, database reset, paid provider, deploy or persona campaign.
144drafthead687d2515 unchanged with no completed migration repair to retest.
Next: continue backend80%; frontend remeasurement/gaps remain after it.
Evidence qa/evidence/2026-10-09-backend-priority.md/.json/gate;209newcases mapped
partially in179. GitHub109/147/152/156/158/162 updated;162Inprogress, notcomplete.
https://github.com/gouthamganta/woven/pull/179 . Shared root edits preserved.
