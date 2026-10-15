# Coverage expansion — 2026-10-08

Codex created [16 suite Issues](https://github.com/gouthamganta/woven/issues/109)
(#147–#162) with 96 test-design families. The work is queued/in progress, not
complete. See [coverage contract](../TEST_COVERAGE_PLAN.md),
[family catalog](../TEST_CASE_FAMILIES.json), [static inventory](../TEST_SURFACES.json)
and [initial concrete mapping](../TEST_CASE_MAPPING.json). GitHub is the task
record; these files are test specifications and evidence.

Added seven backend JWT cases (signature, ordinary/admin claims, wrong key,
wrong audience, default/configured expiry) and seven frontend cases (anonymous
requests, bearer attachment, preserved body/headers, token removal, anonymous/
empty/removed-token navigation). They run against baseline application code
`3df9759`, not the separate `5461c85` candidate. No feature code changed.

Executed locally:

- Backend: **15/15 passed**, zero skipped, 450 ms. Existing EF Relational assembly
  warning remains. `dotnet test --collect:"XPlat Code Coverage"` measured
  122/93816 lines (**0.1300%**) and 15/5701 branches (**0.2631%**) in WovenBackend.
  Generated migration code is included; no custom exclusions were supplied.
- Frontend: **10/10 passed** in five spec files; coverage-enabled rerun 13.92 s.
  V8 reported **34.95% lines / 45.53% branches only for loaded files**. This is
  **not whole-app coverage**; absent routes/services must be brought into the
  denominator before a product coverage percentage is reported.
- The matching V8 collector was installed into ignored local tooling and linked
  through a dependency overlay in this isolated worktree. Original root
  dependencies and lockfile were not changed. No paid provider calls ran.

These tests do not prove authentication guards are wired into app routes,
expired/deleted/banned account enforcement, credential origin restrictions,
session refresh/CSRF, database integrity or complete UI safety. Relevant coverage
remains open in #147; existing defects remain open in #108/#114. Standalone
guard passes must not close the disabled-route-guard finding.

Static discovery found 133 backend registration sites, 26 frontend route entries,
159 backend service files and 21 frontend service/guard/interceptor files. The
scanner reads literal declarations, not compiled runtime metadata. Prefixes,
conditional registration and dynamic/multiline patterns need reconciliation.
Discovering a source is not testing it. Most inventory entries remain unmapped.

[Sanitized machine evidence](2026-10-08-coverage-baseline.json) records source-test
hashes, coverage-report hashes, exact totals and limitations. Raw Cobertura and
V8/HTML reports remain local under ignored directories. Finite test suites cannot
prove every possible defect absent; the gate is complete mapped requirements,
states and known boundaries, meaningful tests, honest metrics, and regression
tests for new features and fixes.
