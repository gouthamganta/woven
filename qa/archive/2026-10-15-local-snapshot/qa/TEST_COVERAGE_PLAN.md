# Test coverage contract

Owner: Codex. Execution tracking lives in GitHub, not a Markdown board.

The target is **100% of inventoried requirements, routes, rules and states mapped
to explicit test cases**, with every case showing automated/manual/provider-blocked
status and evidence. This is a coverage target, not a claim already achieved.
Line/branch coverage must be measured separately and cannot prove every possible
edge case is tested. Do not raise percentages by excluding untested application
code, mocking away database behavior, or accepting assertions that only check 200.

## Traceability

`TEST_SURFACES.json` inventories literal registration sites, frontend routes and
service files with source locations. It is a static starting point; reconcile
with runtime endpoint metadata, route prefixes, conditional endpoints, workers,
hubs, middleware, documentation and the founder's current rules. Every surface
starts unmapped; discovering it does not mark it tested.

`TEST_CASE_FAMILIES.json` defines feature-specific case families and GitHub suite
Issues. Each family is a design obligation, not an executable test or a single
atomic test case. Expand it into named inputs, setup, oracle, persisted effects,
expected UI and evidence. Maintain IDs when source line numbers change.

For each API operation: successful contract; missing/invalid auth; foreign
ownership/role; missing/null/empty/invalid/type/size input; absent/deleted resource;
duplicate/idempotency; boundary/state transition; persistence/privacy; dependency
failure; relevant concurrency. Explicitly justify non-applicable dimensions.
Do not assume all anonymous routes must fail: public legal/auth/health routes
have separate rules. Fresh and upgrade migration suites use real PostgreSQL.

For each UI flow: success, empty/loading/error/retry; session expiry and logout;
back/deep-link/reload; optimistic rollback; duplicate clicks; slow/out-of-order
responses; reconnect/offline; keyboard/focus/labels; reduced motion; mobile and
desktop layout. Component/service tests and real-browser journeys serve different
purposes and both need evidence.

## Layers and local execution

- Backend unit tests for deterministic rules, JWT contracts and pure calculations.
- PostgreSQL/Redis/Azurite integration tests for migrations, constraints,
  transactions, retries, serialization and persisted side effects.
- Frontend unit/component tests for state, validation, request handling and UI
  recovery; HTTP mocks must validate request/response contracts.
- Playwright browser journeys through the same frontend proxy used for release.
  A permissive QA gateway cannot validate production routing.
- Two-user safety/lifecycle journeys, local concurrency/failure drills and
  dependency/security/coverage reports for the exact tested build.
- Provider adapters/fakes for zero-cost local scenarios. Provider authenticity,
  delivery and model quality remain distinctly unverified until independently
  tested; a mock success is not a provider pass.

## Release gate

Every enumerated surface/rule/state mapped or explicitly identified as a gap;
all approved must-pass cases executed; no unresolved critical/high defect without
an explicit founder decision; no silent skips or unexplained exclusions. Record
line/branch percentages with denominator, included modules and exclusions.
New features and fixes require relevant regression tests before QA completion.
Require special review for invalid configuration, UTC rollover, deletion/blocking,
partial commits, privacy and asynchronous worker/realtime behavior.

Known defects should have failing reproductions, kept clearly separate from
passing contract tests; fix and retest before closing the Issue. No artificial
promise that a finite test suite discovers every future defect.
