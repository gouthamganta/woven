# QA memory

## Current coordination

Proxy/real-browser repair loop: Claude proxyc6b090c stacked draft168(base167)
passesnginx-t and16/16 actual contracts vs baseline3/16. Local frontend5180 now
mounts auth176cf74 build +candidate nginx via privateproxy.env; backend5461c85
unchanged, original containers untouched. Real mobile anonymous→login and signed
synthetic→moments/API200 checks pass without fake backend responses. Actual hub
websocket handshake succeeds; delivery/isolation/reconnect not proved. Query/
referrer artificial marker absent from real logs. Local ordinary-user launcher
http://127.0.0.1:5180/qa-access.html lasts1h, Git-ignored; regenerate via script.
Report2026-10-08-proxy-and-browser.md/JSON; no cloud deploy/paid fallback/main merge.

Further fix/retest: Claude-authored client176cf74 stacked draft167(base144)
passes same37 cases vs baseline18/37; expanded51/51 candidate tests/build pass.
QA overlays restored. No signature verification by client decoder; SEC remains
open. Matched deletion165 fails500 after messages1→0; SQLuserstillpresent,
vectors0. Email equality166 readonlyprobe loads100, sameEmail query returnsnull;
already-linked Google identity path not declared broken. Own importer fixed to
bounded decrypted fixture map: restores1 missing synthetic, nextinsert0;100users.
Four invalid manifests rejected with counts100→100. Actor32 partialstate not
repaired. Fresh migrations/atomicity/proxy/deps/CI remain blockers. Report:
2026-10-08-client-and-data-round.md and exact-scope JSON; no paid fallback.

Continued PostgreSQL QA after phone reviews: duplicate-choice race on candidate
5461c85 produced200/409/409/500 after one distinct warmup; DB total_used3 versus
responses2/notes2. Failed duplicate consumed a slot without saved action.124
returned Changes requested/Claude;108 notified missing correlation header on500.
Actors92/93/warmupTarget97 are synthetic local-model DB only; do not rerun same
state and assume fresh. Report2026-10-08-duplicate-choice-race.md/JSON. Six new
SavedCleanup regressions pass on phone141, not a full PostgreSQL cap proof.

Phone PR review:137 head dce8c89 passes independent12 tests +production build
+4 mocked browser checks (Deck/Drawn desktop/mobile), local merge with5461c85
auto-merges and same12 tests pass.141 head5de72ae passes8 existing+6 new Saved
cleanup tests, but conflicts with144 in InteractionBudgetService/MomentsRules;
probes aborted.142 head e02b628 canonical/q6 claims match code; full unchanged
docs not certified.140 head e387d02 is NOT draft and still contradicts ChatNote
visibility;143 already has settled founder decision. CI gates remain red;163
queues fmt/asset gate repairs,164 queues old PillarEmbedding128-vs1536 doc drift.
Direct comments posted on all4 PRs +linked Issues. No other explicitly named
claude-phone branch in open list. Evidence:2026-10-08-phone-pr-review.md.
All views/tasks remain incomplete; no merge/deploy/reset/paid call. Browser
fixtures were repaired rather than forcing clicks through modal overlays.

Round 2: added 44 cases (30 backend, 14 frontend) on qa/full-local-audit. Baseline
backend 41/45, candidate5461c85 backend45/45, frontend13/24. Four baseline
failures are approved72h vs36h. Eleven frontend failures cover three gaps:
unguarded routes (six), presence-only token validation (four), credential-origin
restriction (one); not eleven bugs or proof of backend auth bypass. Actual route
policy tested using RouterTestingHarness with probe page components. Encryption
and generated-cookie contracts pass; at-rest field coverage/rotation/browser
CSRF remain unverified. REGRESSION_CASES.json maps69 named cases to partial
families; report/evidence under evidence/2026-10-08-regressions-round2.*.
No skips to conceal failures; default test run is red until application repairs.
Claude owns #108/#114 fixes, Codex owns test suites. #151/#156 remain partial.

Founder requested complete backend/frontend test-case coverage, not smoke-only
QA. Codex created suite Issues #147–#162 (16 suites, 96 test-design families);
QA-COV-01 and QA-COV-16 are in progress, others queued. Inventory
TEST_SURFACES.json finds 133 literal backend registrations, 26 frontend route
entries, 159 backend service files and 21 frontend services/guards/interceptors.
This static inventory is not a complete runtime route list or tested coverage.
See TEST_COVERAGE_PLAN.md, TEST_CASE_FAMILIES.json and TEST_CASE_MAPPING.json.
Added 7 backend JWT contract cases and 7 frontend guard/interceptor cases on
qa/full-local-audit, with no application changes. Backend 15/15 and frontend
10/10 passed. Initial backend collector measured 122/93816 lines (~0.13%) and
15/5701 branches (~0.26%), including generated migrations; no exclusions added.
Frontend coverage collector now runs using isolated @vitest/coverage-v8 4.0.16
and a worktree dependency overlay; root dependencies unchanged. Report gives
34.95% lines / 45.53% branches for LOADED FILES ONLY, not whole frontend coverage.
Unloaded files must enter the denominator in QA-COV-16. See coverage-baseline
Markdown/JSON under evidence/ for measured totals and remaining limitations.
Goal: 100% enumerated requirements/states mapped and must-pass tests executed;
never promise a finite suite proves every possible edge case absent. Preserve
full denominator and distinguish passing mocked tests from integration/provider
evidence. Tests do not prove auth guards are enabled in routes (#114 still open).

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
