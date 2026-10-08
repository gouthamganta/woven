# Archived task snapshot — do not update

**GitHub migration:** new tasks and handoffs now belong in
[Woven Product Delivery & QA](https://github.com/users/gouthamganta/projects/2).
The 25 migrated Issues are linked in `qa/github-board.json`; see
[GITHUB_WORKFLOW.md](GITHUB_WORKFLOW.md) for phone/laptop session instructions.
This Markdown table remains a historical snapshot. Automatic AI dispatch is
not enabled; OPS-001 tracks it.

**QA resumed after the founder reported documentation complete.** Documentation
checkpoint: 7360cd5. First structural/content review: [DOC_REVIEW.md](DOC_REVIEW.md).
CL-003 compilation repair verified at a5ca8ab; 8/8 backend unit tests passed.
Runtime sandbox configuration, migration application and user seeding remain pending.

Statuses: queued → acknowledged → in progress → ready for QA → verified.
Use blocked only with a specific dependency. Add evidence before verification.

| ID | Owner | Status | Task / acceptance criteria |
|---|---|---|---|
| QA-001 | Codex | in progress | Inventory features, routes, state machines, integrations, tests; map each to evidence and risks. |
| QA-002 | Codex | in progress | Isolate local sandbox and paid outbound traffic; prove effective configuration before startup/reset. Docker initially unreachable. |
| QA-003 | Codex | in progress | Generate 100 deterministic adult personas, validate fixtures, then import against actual schema and verify coverage. Fixture generation is only the first stage. |
| QA-004 | Codex | queued | Provide founder a working local account and verified browser login instructions. Depends on QA-002/003. |
| QA-005 | Codex | verified | Existing unit baselines executed: frontend 3/3 passed at initial baseline; backend 8/8 passed at a5ca8ab after CL-003. Separate revisions; no full integration coverage claimed. See evidence/2026-10-07-cl003-fixed.md. |
| QA-006 | Codex | queued | Automate onboarding → deck → response → match → chat → trial → close, including two-user behavior and rejection paths. |
| QA-007 | Codex | queued | Validate authorization, privacy, blocking, reporting, media permissions, notification boundaries, retries, concurrency and idempotency. |
| QA-008 | Codex | queued | Measure latency, errors, DB growth/query behavior and local resource use with stated workload and hardware. |
| CL-001 | Claude | acknowledged | Review FINDINGS F-001/F-002 and document intended rating visibility and production protections for development endpoints. No application changes requested until runtime evidence or confirmed intent. _(Acknowledged 2026-10-07)_ |
| CL-002 | Claude | acknowledged | When documentation stabilizes, add authoritative product-rule and feature indexes to this board; flag changed acceptance criteria. _(Acknowledged 2026-10-07, docs 100% complete as of 2026-10-07)_ |
| CL-003 | Claude | verified | Codex independently verified a5f29fa repair at a5ca8ab: test command exit 0, 8/8 passed. Nullable entity and migration present. Migration application not yet verified. See evidence/2026-10-07-cl003-fixed.md. |
| CL-004 | Claude | acknowledged | Triage NuGet NU1902/NU1903 MessagePack 2.5.187 dependency path and advisory applicability; propose compatible fix and regression validation. _(Acknowledged 2026-10-07, will investigate)_ |
| DOC-001 | Claude | verified | ✅ COMPLETE: Created 15 API reference files in docs/api/. All endpoints documented with evidence-based examples. Commit: d495d54 _(26% → 62% milestone reached)_ |
| DOC-002 | Claude | verified | ✅ COMPLETE: Created 10 development guide files in docs/development/. Consolidated from existing docs with improvements. Commit: 34830e6 _(62% → 86% milestone reached)_ |
| DOC-003 | Claude | verified | ✅ COMPLETE: Created 6 security documentation files in docs/security/. All security controls documented. Commit: 7360cd5 _(86% → 100% milestone reached)_ 🎉 |
| DOC-004 | Claude | verified | ✅ COMPLETE: Updated CLAUDE.md with 100% documentation completion status. Added structure breakdown, key documents, and commit references. Commit: b0f0bf4 |
| DOC-005 | Claude | ready for QA | **COMPLETE:** Fixed all 46/46 broken links. Created 13 stub docs (commons/api, balloon-lifecycle, trial-period, daily-budget, blocking, review, pii-sanitization, candidate-pool, match-lifecycle, match-explanation, delivery-boost, clip-embeddings, matchmaking/README, security/audit, background-workers). Fixed 11 source paths + 6 cross-doc paths. Commits: b26b033, da8fece, 15ab79a _(2026-10-07)_ |
| DOC-006 | Claude | ready for QA | **COMPLETE:** Fixed all 7 code/doc conflicts. Balloon 72h→36h, deck 60→5, Spark refill implemented, trust 0.5→0.25, wallet tenths 0-100, JWT 60min default, error format clarified, DEBUG guard corrected. All changes verified against source with file:line evidence. Commit: 61699ee _(2026-10-07)_ |
| CL-005 | Claude | acknowledged | Review potential new-wallet double grant: SparkWalletService creates 50 tenths with LastEarnedDate null, then daily earning adds another 50 on first GetBalance/TrySpend. Static hypothesis only; reproduce locally and compare intended initial 5 sparks before fixing. _(Acknowledged 2026-10-07)_ |
| CL-006 | Claude | queued | Triage MSB3277 EF Core/Abstractions 10.0.0 vs 10.0.1 assembly conflicts emitted by backend test build. Tests pass; runtime compatibility not yet validated. See evidence/2026-10-07-cl003-fixed.md. |
| USER-001 | Founder | queued | Point the active Claude session to qa/README.md and ask it to acknowledge its board tasks. |
| USER-002 | Founder | queued | Confirm initial audience/market when known; otherwise QA uses neutral synthetic scenarios. |

Meaningful changes to matching, pricing/Sparks, privacy, or user-visible flow
require founder decision. Reproducible routine bugs are implementation handoffs
to Claude. Do not claim Claude received or fixed a task without acknowledgment.
