# QA strategy and evidence contract

## Founder sequencing - 2026-10-09

Current primary phase: build executable feature test suites and reach at least
80% line and branch coverage for backend and frontend application code. Review
coding-agent PRs and close scoped QA deliverables when their actual acceptance
criteria pass. Record unexpected outcomes, but do not start a separate broad
bug-finding/fixing campaign during coverage construction.

Full sandbox journeys, AI-persona campaigns, performance missions and grouped
bug/cause analysis follow the code/feature coverage phase. Claude owns application
fixes and main-element implementation. The gates below describe later product
readiness; they do not reorder this coverage-first instruction. No new paid calls
or production deployment is authorized.

## Readiness gates

1. Reproducible environment: local services, schema revision, synthetic seed,
   restart/reset instructions, effective isolation and no paid calls.
2. Functional demo: a real browser can complete the core two-user lifecycle.
3. Safety/data: authorization by ownership, block/report behavior, private field
   visibility, consent, deletion/export, malicious input, and media permissions.
4. Reliability: retries, idempotency, simultaneous requests, expired sessions,
   lost connections, worker failures, empty states and consistent error UX.
5. UI: keyboard use, screen-reader labels, contrast, focus, mobile layout,
   loading/error recovery and reduced-motion behavior.
6. Operations: correlation IDs, actionable local logs, health/readiness,
   notification delivery/error paths, DB backup/restore and failure drills.
7. Demo evidence: all required scenarios verified; unresolved critical/high
   findings triaged with explicit founder acceptance or fixes and retests.

Thresholds and industry standards must be tied to current primary references
before claims of compliance. No certification is implied by running a checklist.

## Coverage matrix to build

For each feature record source/docs, intended business rule, happy path,
authorization/negative path, state transitions, concurrency, UX/accessibility,
data effects, integrations, test method, and evidence link. Include onboarding,
profile/preferences, Moments/Drawn, Sparks, matches/Balloons, trial/chat/voice,
Commons/moderation, notifications, games, coaching/assistant and ECHO workers.

## Each run records

- Run ID, UTC timestamp, tested commit plus dirty working-tree status.
- OS/hardware, runtime/tool versions, image IDs and migration version.
- Sanitized configuration summary, fixture hash, scenario/workload and command.
- Expected/actual behavior; pass/fail/blocked/not run; test counts and duration.
- Sanitized logs, screenshots where useful, reproduction and finding IDs.
- Artifact SHA-256 hashes and retest references for important evidence.

Do not store authentication tokens, real PII, private chats or secrets in Git.
Record a tested snapshot because Claude may edit files during a run. Code review,
mocked integration tests and live provider tests are distinct evidence categories.

## Personas and measurements

Use deterministic adults across age ranges, geographies, preferences, intents,
activity levels and lifecycle states. Add explicit fixture cases for age and
distance boundaries, incomplete profiles, exhausted wallets, blocked pairs,
expired matches, no candidates and malformed input. Synthetic behavior is
scripted simulation; it is not a human panel or a validated psychological model.

Measure request p50/p95/p99, failures, throughput, resource use and database
behavior under named workloads. Report machine limits; do not extrapolate local
results into cloud capacity. ECHO tests check constraints, determinism where
expected, signal integrity and cohort behavior. They do not prove romantic fit.

## Investor evidence

Keep demonstrated product capabilities, measured engineering quality and
unvalidated market hypotheses separate. Synthetic engagement/retention never
becomes a traction claim. A demo-ready product is distinct from beta-ready
operations and evidence of real user outcomes.

## Weekend mission template

Name a hypothesis, acceptance criteria, participants, fixed build, data policy,
cost ceiling, capture plan and teardown owner. Run locally first. Any public
sandbox needs a concrete deployment and cost/security review before launch;
Development login endpoints must remain unreachable. No deployment is scheduled.
