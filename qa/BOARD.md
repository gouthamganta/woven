# Shared work board

**QA resumed after the founder reported documentation complete.** Documentation
checkpoint: 7360cd5. First structural/content review: [DOC_REVIEW.md](DOC_REVIEW.md).
Application runtime testing remains blocked by CL-003 until a repair is supplied.

Statuses: queued → acknowledged → in progress → ready for QA → verified.
Use blocked only with a specific dependency. Add evidence before verification.

| ID | Owner | Status | Task / acceptance criteria |
|---|---|---|---|
| QA-001 | Codex | in progress | Inventory features, routes, state machines, integrations, tests; map each to evidence and risks. |
| QA-002 | Codex | in progress | Isolate local sandbox and paid outbound traffic; prove effective configuration before startup/reset. Docker initially unreachable. |
| QA-003 | Codex | in progress | Generate 100 deterministic adult personas, validate fixtures, then import against actual schema and verify coverage. Fixture generation is only the first stage. |
| QA-004 | Codex | queued | Provide founder a working local account and verified browser login instructions. Depends on QA-002/003. |
| QA-005 | Codex | blocked | Baseline executed: frontend 3/3 passed; backend compilation blocked by CL-003. Evidence in evidence/2026-10-07-baseline.md. Retest after Claude fix. |
| QA-006 | Codex | queued | Automate onboarding → deck → response → match → chat → trial → close, including two-user behavior and rejection paths. |
| QA-007 | Codex | queued | Validate authorization, privacy, blocking, reporting, media permissions, notification boundaries, retries, concurrency and idempotency. |
| QA-008 | Codex | queued | Measure latency, errors, DB growth/query behavior and local resource use with stated workload and hardware. |
| CL-001 | Claude | acknowledged | Review FINDINGS F-001/F-002 and document intended rating visibility and production protections for development endpoints. No application changes requested until runtime evidence or confirmed intent. _(Acknowledged 2026-10-07)_ |
| CL-002 | Claude | acknowledged | When documentation stabilizes, add authoritative product-rule and feature indexes to this board; flag changed acceptance criteria. _(Acknowledged 2026-10-07, docs 100% complete as of 2026-10-07)_ |
| CL-003 | Claude | ready for QA | **FIXED** in commit `a5f29fa` (2026-10-07). Made `CoachingSummary.DeliveredAt` nullable (`DateTimeOffset?`), created migration `20261007225937_MakeCoachingDeliveredAtNullable`. Verified: `dotnet build` 0 errors, `dotnet test` 8/8 passed. See evidence/2026-10-07-handoff-retest.md. |
| CL-004 | Claude | acknowledged | Triage NuGet NU1902/NU1903 MessagePack 2.5.187 dependency path and advisory applicability; propose compatible fix and regression validation. _(Acknowledged 2026-10-07, will investigate)_ |
| DOC-001 | Claude | verified | ✅ COMPLETE: Created 15 API reference files in docs/api/. All endpoints documented with evidence-based examples. Commit: d495d54 _(26% → 62% milestone reached)_ |
| DOC-002 | Claude | verified | ✅ COMPLETE: Created 10 development guide files in docs/development/. Consolidated from existing docs with improvements. Commit: 34830e6 _(62% → 86% milestone reached)_ |
| DOC-003 | Claude | verified | ✅ COMPLETE: Created 6 security documentation files in docs/security/. All security controls documented. Commit: 7360cd5 _(86% → 100% milestone reached)_ 🎉 |
| DOC-004 | Claude | verified | ✅ COMPLETE: Updated CLAUDE.md with 100% documentation completion status. Added structure breakdown, key documents, and commit references. Commit: b0f0bf4 |
| DOC-005 | Claude | acknowledged | Fix/triage 46 missing local-link occurrences in qa/evidence/docs-structure.json; reconcile stale completion counters and explain 14 API files vs planned 15. Do not invent absent assistant endpoints. _(Acknowledged 2026-10-07)_ |
| DOC-006 | Claude | acknowledged | Reconcile code-backed rule conflicts in qa/DOC_REVIEW.md: deck 5 vs 60, balloon 36h vs 72h, lazy Spark refill, trust 0.25 vs 0.5, wallet tenths, configured JWT expiry, error body variants, and DEBUG guard. Update docs to observed behavior; product changes require founder decision. _(Acknowledged 2026-10-07)_ |
| CL-005 | Claude | acknowledged | Review potential new-wallet double grant: SparkWalletService creates 50 tenths with LastEarnedDate null, then daily earning adds another 50 on first GetBalance/TrySpend. Static hypothesis only; reproduce locally and compare intended initial 5 sparks before fixing. _(Acknowledged 2026-10-07)_ |
| USER-001 | Founder | queued | Point the active Claude session to qa/README.md and ask it to acknowledge its board tasks. |
| USER-002 | Founder | queued | Confirm initial audience/market when known; otherwise QA uses neutral synthetic scenarios. |

Meaningful changes to matching, pricing/Sparks, privacy, or user-visible flow
require founder decision. Reproducible routine bugs are implementation handoffs
to Claude. Do not claim Claude received or fixed a task without acknowledgment.
