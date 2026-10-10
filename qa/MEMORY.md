## Backend coverage resumed - 2026-10-09

Founder explicitly resumed backend tests while Claude repairs144. Isolated
qa/backend-coverage-20261009 starts at current master20a630e (146 merged).
Added209 cases; final386/386pass,0skipped,27s. Build0warnings/0errors;
changed-test whitespace and diff checks pass. Current binary collector6064/22307
lines27.18%,1151/5715branches20.14%; both80%gates remain unmet. Existing generated
migration exclusion only; no old-binary union. Auth real loopback middleware plus
registered chat/trial/block/ChatNote/voice/date handlers, matching/scoring/history,
trust, data export/isolation and sanitized request/response contracts. InMemory
does not prove relational/concurrency/deletion integrity; loopback test JWT config
does not prove Program/cookie/CSRF/CORS wiring. No account-state or migration
sign-off.144drafthead687d2515 unchanged, latest111/144handoff still failing;
no completed repair to retest or merge. Source/test family mappings partial;
evidence2026-10-09-backend-priority.md/.json and gate. Largest remaining backend
gaps onboarding/Program/matches/scoring/explanations/Moments/insights/feedback.
Continue backend80% before frontend remeasure; sandbox/personas/broader campaign
deferred. No application edits, cloud deployment, paid providers or root resets.

## PR queue priority checkpoint - 2026-10-09

Founder parked new testcase/coverage work until pending PRs/commits are reviewed. Reviewed 21 original PRs, 8 local Claude fix branches, 5 newer master commits. Checkpoint: 14 merged, 10 superseded/incompatible closed, QA146 awaiting final CI, migration144 unaccepted. Current master42bb309 application plus existing suites: BE177/177, FE540/540; final full npm audit0; production build passed with existing stylesheet warning; 3/3 isolated startup-key rejection checks pass. Current BE unit collector18.42%lines/12.11%branches; do not join older HTTP binary. No frontend coverage rerun, no new persona/performance campaign. Current80%objective unfinished and remains parked per founder sequencing. Metadata/peer dependency fixes and two existing harness assertions adapted; no new test-case campaign. CodeQL50/51 false-positive triage recorded, note457 remains open. SourceFlowAfterthought120 not complete in trial/block UI, static gap handed to Claude; broader108/124/165/166 not closed. Fresh empty PostgreSQL migration144 fails PendingModelChangesWarning;43EFmigrations discovered, manualAddMatchSignalLog absent. Do not suppress warnings/useEnsureCreated as proof. Approved72h split177 and auth/proxy176 landed without blocked schema changes. Deploy workflow234665844 disabled to prevent cloud spending; do not re-enable without founder deployment authorization. Root shared edits/original Docker services preserved; do not regenerate unwanted test-access launcher. Evidence qa/evidence/2026-10-09-pr-queue.md and .json; update final merge status from GitHub.

## Frontend line and branch gate reached - 2026-10-09

Added 99 client tests, all 99 pass. Final 540 total, 537 pass, 3 known failures (#170/#171/#172); no failed suite loads. All 72/72 app TS files included: 83.09% lines, 80.44% branches, 80.87% statements, 73.34% functions. Only the configured line/branch gate passes; functions remain below 80 and full feature/browser/accessibility/security completion is not claimed. 21 external HTML templates separately unmeasured. Angular/V8 mapping counters changed when additional prototype modules loaded (4355 valid lines, 2189 branches); no app exclusions or threshold changes. Backend unchanged: unit 16.18% lines/11.68% branches; archived identical-binary union 35.55%/19.16%. Backend coverage is the next primary gap. Tests include login, onboarding polling, legacy push, Balloon timing/actions, canvas/scroll lifecycle, rendered ChatNote/game/Commons and prototype form/server guards. Initial DOM timing and missing matchMedia errors corrected; only final verified run is acceptance evidence. Candidate auth/routes overlay restored. No app fixes, new sandbox/persona/provider/performance run, master merge or deployment. GitHub authoritative; QA branch qa/full-local-audit, draft PR146. Evidence: 2026-10-09-client-80-*.json and .md.

## Coverage resumed after founder challenged stopping - 2026-10-09

Added163 more frontend cases; final441total438pass,3knowncontracts unchanged. Frontend72/72TS70.93%lines/72.21%branches;target80unmet. Backendlatest173total172pass,unit16.18/11.68%;archivedsamebinaryunion35.55/19.16%. No newbackend/sandbox/persona/provider/performancecampaign in this continuation. Added Home/assistant,92transportcases,legal/Pulse-sheet,push,realtime,landing media/motion tests. SignalR modulemock did not intercept bundled source; replaced with actual builder-prototype stubs and verified11cases. Initial mock failures excluded from product findings, finalall163newcasespass. Source files/auth overlays restored. Continue primarycoverage, no checkpoint-completion claim. Evidence2026-10-09-coverage-resume-*.json.

## Founder sequencing and coverage-first checkpoint - 2026-10-09

Coverage/feature test suites primary; >=80% lines and branches remains unmet. Sandbox/persona/performance/full bug-cause campaigns deferred. Added219 new tests in this phase, all219passed: backend173total172pass (Saved unmerged expectation), frontend278total275pass (3known contracts). Frontend72/72TS55.62%lines/59.98%branches,21externalHTMLseparate. Backendunit16.18%/11.68%;same-binary archivedHTTP+unitunion35.55%/19.16% (no newHTTP/sandboxrun). Scoped documentationQA139 closed against142e02b628b per acceptance requiringdraftPR+handoff; PR142stillopen/draft/unmerged, sharedCIissue163separate. Preservelatestuserordering. Newcode in AiProfile/Game/Endpoint tests and account/chat/game/Commons/onboarding specs. Evidence2026-10-09-coverage-first-cases.json. Continue with remaining Home/assistant/landing/services and backend handler/service gaps.

## Finding classification correction - 2026-10-09

Latest5 failed assertions are3 reproduced frontend component/service defects (170/171/172),1 pending PR141 expectation (Saved behavior absent from tested5461c85),1 proposed correlation-ID security requirement (108). Do not call all5 confirmed product defects, or call isolated component/service tests browser verification. Preserve failed results; distinguish intended pending change/proposed requirement from established-contract regression. Reporter correction in2026-10-09 coverage evidence.

## Primary80% coverage campaign - 2026-10-09

Target>=80% lines AND branches per layer, not reached. Backend combined same-binary unit+realHTTP union31.33%lines/14.71%branches;frontend72/72TS21.59%/27.64%;21externalHTML separately unmeasured. Added119tests since instruction (40BE+79FE); BE90/91pass (Saved known), FE138/141pass (170storage,171mediaHTTP,172PASS); realHTTP34/35pass (108correlation). Gates fail nonzero. Reusable Docker API collector verified with fresh synthetic DB clone+ownRedis, stopped owncontainers, private appcopy restored. EF QA testRelational10.0.12 alignment removes prior warning. Evidence qa/evidence/2026-10-09-coverage-delivery.md; largestremainingBEgaps chat/onboarding/games/insights/AI;continue tests, do not claim complete.

## Executed delivery checkpoint - 2026-10-08

Added 11 frontend HTTP recovery/tracing contracts; final auth candidate176cf74 run61/62 pass, sole new failure storage-denied401 (#170). Added all-TypeScript coverage config, single-worker runner with overlay restoration and denominator verifier:72/72 appTS included, line4.12%/branch14.80%;21HTML templates separately unmeasured. Registry141 named cases, families still partial. Independent security125 production audit0 vulnerabilities on4e421ee; installed versions match lock core21.2.25/build21.2.26. Independent development browser/server build passed in46.088s; production build and functional regressions remain pending. Evidence: qa/evidence/2026-10-08-frontend-delivery.md.

## Current checkpoint ? 2026-10-08

GitHub Issues/Projects are authoritative; the older board instructions below are historical. QA branch `qa/full-local-audit`, draft PR146; application candidates PR144/167/168 are separate and unmerged. Latest coverage and scope: `qa/evidence/2026-10-08-coverage-by-module.md` and JSON. Backend 50/51 tests pass (pending Saved cleanup fails one); frontend auth candidate 51/51 pass. Backend line coverage 1.55% with generated migrations, 6.51% excluding them; frontend loaded-file line coverage 4.12%, whole frontend unknown. Selected external runtime probes are separate from these collectors. Controlled choice failure deterministically leaves budget charged without response/note (#124); account deletion and encrypted email reuse defects #165/#166 remain. Claude atomicity attempt exited without edits; no repair claimed. Local sandbox uses EnsureCreated model database, not a proven migration path. 100 synthetic adults exist; no paid API calls or main merge/deployment. Remaining QA/review checklist lives in #109; do not call partial suites complete.

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

2026-10-08: #138 Saved/Pending trace found no Drawn dependency. Removed legacy
budget/cache branches, Saved boost and stale support navigation; database
columns, tables and enum values preserved. Branch codex-phone/138-remove-saved-pending.
Evidence: qa/evidence/2026-10-08-138-saved-pending.md; laptop QA required.

2026-10-08: Codex phone implemented #119 on
`codex-phone/119-remove-rating-bar` from master `3df9759`. Removed Deck/Drawn
ratings from API/UI, including rating-dependent New here badges. Backend build
passed with 0 warnings/errors; production Angular build passed with the existing
landing-simple stylesheet budget warning. Backend tests passed 12/12, including
4 synthetic endpoint JSON regression cases; existing MSB3277 EF dependency
warning remains. Evidence: qa/evidence/2026-10-08-119-rating-removal.md.
Laptop Codex must verify UI and PostgreSQL behavior before Done; no merge/deploy.

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
