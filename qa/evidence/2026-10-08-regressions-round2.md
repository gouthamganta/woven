# Regression expansion, round 2 — 2026-10-08

Codex added **44 cases**: 30 backend and 14 frontend. Total suites now contain
45 backend and 24 frontend cases. No application implementation changed.
Execution tracker: #109, #147, #151 and #156; review: draft PR #146.

| Application snapshot | Passed | Failed | Meaning |
|---|---:|---:|---|
| Baseline backend `3df9759` | 41 | 4 | All four 72-hour boundary checks fail against its 36-hour rule |
| Candidate backend `5461c85` | 45 | 0 | Same tests pass, including approved 72-hour repair |
| Baseline frontend `3df9759` | 13 | 11 | Three client-side control gaps reproduced by multiple cases |

All cases ran; failing regressions were not skipped or converted into passing
expectations. The candidate run uses the test project's explicit
`WovenBackendProject` reference override. It does not modify candidate source.
Baseline and candidate results remain separate.

New backend coverage:

- Text/binary encryption round trips, empty and Unicode content, nonce variation,
  wrong key, tampered nonce/ciphertext/tag, truncated input, malformed configuration
  and purpose-scoped key separation (16 cases).
- Access/refresh secure flags, lifetime, logout expiry, missing/named cookie reads
  (7 cases). Generated headers only; browser transport and CSRF remain unverified.
- Match expiry across year/leap/daylight-offset/calendar boundaries and
  order-independent pair identity (7 cases). Worker/state/database semantics are
  not established by these pure-rule tests.

New frontend coverage:

- Expired/malformed/whitespace/missing-expiry stored tokens, future payload and
  credential forwarding boundaries (6 cases). Synthetic tokens are not provider
  tokens and do not establish server-side authenticity.
- Actual route paths/redirects/guards tested through Angular Router: two public
  paths and six protected paths (8 cases). Page components are replaced with a
  router-outlet probe to avoid unrelated provider/UI effects. This tests routing
  policy; it is not a full-browser page journey.

The 11 frontend failures group into **disabled protected-route authorization**
(six cases), **presence-only session validation** (four cases), and **credential
origin restriction** (one case). These are client-side findings, not proof that
protected backend APIs disclose data. Existing backend anonymous-denial evidence
is separate. Fix handoffs remain with Claude in #108/#114; #147 owns test work.

The first logout assertion incorrectly treated an empty parsed cookie segment
as an absent segment. The test oracle was corrected to assert an empty value;
the final logout test passes. It was not handed off as an application defect.

[Sanitized run results](2026-10-08-regressions-round2.json) record all case names,
results, durations and raw report hashes. [Case registry](../REGRESSION_CASES.json)
maps 69 distinct cases to test families with baseline/candidate outcomes.
Families remain partially tested; no family is marked complete solely from a
few related cases. Raw TRX, Vitest JSON and coverage artifacts are ignored locally.

Commands from the isolated QA worktree:

```powershell
dotnet test backend/WovenBackend.Tests/WovenBackend.Tests.csproj --logger "trx;LogFileName=round2-baseline-final.trx" --results-directory <local-evidence>
dotnet test backend/WovenBackend.Tests/WovenBackend.Tests.csproj -p:WovenBackendProject=<candidate-backend-project> --collect:"XPlat Code Coverage" --logger "trx;LogFileName=round2-candidate.trx" --results-directory <local-evidence>
node node_modules/@angular/cli/bin/ng.js test --watch=false --coverage --reporters=json --output-file=<local-evidence.json>
node qa/scripts/map-regression-evidence.mjs
```

Frontend and baseline backend commands intentionally exit nonzero until their
failing contracts are fixed. This draft is not eligible for readiness sign-off.
EF Relational version warnings remain. No paid calls, database resets, migration
execution or deployments ran. Full feature, integration, browser and recovery
coverage is still incomplete; proceed with the remaining suite Issues.
