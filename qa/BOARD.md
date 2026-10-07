# Shared work board

**QA is paused at the founder's request while Claude updates the repository and
documentation.** Task statuses below record the last work state; they do not
mean testing is currently running. See [Claude handoff](CLAUDE_HANDOFF.md).

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
| CL-002 | Claude | acknowledged | When documentation stabilizes, add authoritative product-rule and feature indexes to this board; flag changed acceptance criteria. _(Acknowledged 2026-10-07, docs 73% complete)_ |
| CL-003 | Claude | acknowledged | Codex retest at aec9eac failed with the same CS1061. DeliveredAt remains non-nullable; supplied commits do not contain the claimed fix. Supply actual repair and change reference, then return for QA. See evidence/2026-10-07-handoff-retest.md. |
| CL-004 | Claude | acknowledged | Triage NuGet NU1902/NU1903 MessagePack 2.5.187 dependency path and advisory applicability; propose compatible fix and regression validation. _(Acknowledged 2026-10-07, will investigate)_ |
| DOC-001 | Claude/Codex | in progress | Complete API Reference docs (15 files): README, authentication, rate-limiting, error-handling, + 11 endpoint docs (onboarding, moments, chats, matches, commons, profile, notifications, games, media, coaching, assistant). See docs/api/ directory. _(26% → 62% milestone)_ |
| DOC-002 | Claude/Codex | queued | Complete Development docs (10 files): README, setup, contributing, patterns, testing, debugging, git-workflow, database-migrations, deployment, monitoring. See docs/development/ directory. _(62% → 86% milestone)_ |
| DOC-003 | Claude/Codex | queued | Complete Security docs (6 files): authentication, encryption, pii, prompt-injection, security-audit, incident-response (README already exists). See docs/security/ directory. _(86% → 100% milestone)_ |
| DOC-004 | Claude | queued | Update CLAUDE.md with final documentation completion status and remove "docs 73% complete" note from CL-002. Mark documentation milestone as COMPLETE. |
| USER-001 | Founder | queued | Point the active Claude session to qa/README.md and ask it to acknowledge its board tasks. |
| USER-002 | Founder | queued | Confirm initial audience/market when known; otherwise QA uses neutral synthetic scenarios. |

Meaningful changes to matching, pricing/Sparks, privacy, or user-visible flow
require founder decision. Reproducible routine bugs are implementation handoffs
to Claude. Do not claim Claude received or fixed a task without acknowledgment.
