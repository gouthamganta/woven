# Backend priority checkpoint - 2026-10-09

Founder resumed backend coverage work while Claude repairs PR #144. Application
baseline is current master `20a630ed4bdbdcb9c0a2db95081347e6bc091dad`; branch
`qa/backend-coverage-20261009`. Application code is unchanged. Shared root edits
and original local services were preserved.

Added **209 executable cases**. Final full backend run: **386/386 passed**,
**zero skipped**, reported test duration 27 seconds. Backend build passed with
zero warnings and zero errors. Release suite also passed **386/386**, zero skips,
in 15 seconds. Changed test files passed full format verification;
`git diff --check` passed. Initial harness failures were corrected: nullable
double theory values, JSON GUID comparison, and an orphan-thread fixture whose
original match deletion correctly cascaded to the thread. These were test-code
errors; final results do not suppress or skip application failures.

| Backend current-master collector | Covered | Total | Percentage |
|---|---:|---:|---:|
| Lines | 6,064 | 22,307 | 27.18% |
| Branches | 1,151 | 5,715 | 20.14% |

**Both 80% gates remain unmet.** The supplied previous measurement was 18.42%
lines / 12.11% branches. Current counters include all WovenBackend application
files under the existing policy, including unexecuted services, development
helpers and generated regex code. `Migrations/**/*.cs` remains excluded by the
existing generated-migration policy; migration acceptance is a separate required
check. No old-binary HTTP counters were combined with this run.

Executed additions cover:

- JWT signatures, issuer/audience, expiry, unsigned/malformed/tampered tokens,
  missing identity and invalid token-creation configuration. Real loopback HTTP
  tests exercise authentication/authorization middleware with chat handlers;
  anonymous/invalid credentials cannot write messages, and an admin token does
  not grant access to an unrelated pair's chat.
- Participant-only start/read/send and missing records; repeated chat start,
  rate-limit response, trimmed 1,000/1,001-character messages, persisted sends,
  response metadata, notification recipients, signal failure, and one-time
  mutual-message reflection unlock.
- Both ChatNotes shown to either matched participant, unrelated-note isolation,
  own-note/message reaction denial and repeated reaction deduplication.
- Trial timer starts after the second participant opens and does not restart;
  waiting/continue/end/block paths, valid private rating limits, terminal close,
  reason signals and idempotency replay. Existing approved 72-hour calendar
  boundary tests also pass on current master.
- Voice validation, typed metadata and listen/mutual-exchange signals; date
  interest notification deduplication, mutual venue gate, availability limits,
  actor-specific nudge dismissal and provider request forwarding.
- Reciprocal age/distance boundaries, incomplete coordinates, deck size/diverse
  selection, explorer eligibility, delivery boosts/history windows, score
  thresholds/redistribution/depth/trust limits, and outcome/signal persistence.
- Own-only data summary/export, actor-specific export quota, missing accounts,
  outbound block-list isolation and repeatable own voice-preference reset.
  Device fingerprint hashing, shared-device trust penalty, trust threshold,
  posting-velocity threshold and repeatable profile/photo bot scoring.
- Sanitized authentication/domain/unhandled error responses, correlation
  propagation and outbound identity-header removal with a fake HTTP transport.

Limits remain material: registered-handler tests bypass middleware; the separate
loopback host uses explicit test JWT configuration and does not validate full
`Program.cs` wiring. The algorithm allowlist test validates its explicit token
validation parameters, not production algorithm restrictions. EF InMemory
does not validate PostgreSQL queries, constraints, serializable concurrency,
FK-safe deletion or rollback. Account deletion, banned/deleted-token enforcement,
cookie/CSRF/CORS, realtime and real provider behavior remain open. No deletion
success or complete-product safety claim follows from these passes.

PR #144 remains draft at `687d2515be921e180d2d1a70ed9b65ee70db96f0`.
The latest #111/#144 handoff still reports snapshot drift and an undiscovered
manual migration; no completed repair has been posted. It was not merged and
the already-recorded failing migration run was not relabeled as a pass.

Largest measured application gaps: onboarding (1,060 uncovered lines), Program
(856), match endpoints (507), scoring (390), explanations (383), Moments (349),
insights (299), games, feedback and Commons. Continue backend coverage before
frontend remeasurement and gap filling. Sandbox personas, broader campaign,
paid providers and deployments did not run.

Reproduction:

```powershell
dotnet test backend/WovenBackend.Tests/WovenBackend.Tests.csproj --collect:"XPlat Code Coverage" --settings qa/backend.coverage.runsettings --logger "trx;LogFileName=tests.trx" --results-directory qa/.local/coverage-priority
dotnet build backend/WovenBackend/WovenBackend.csproj --no-restore
node qa/scripts/check-coverage.mjs backend <original-collector-coverage.cobertura.xml> <gate.json>
```

The final gate command intentionally returns exit 1 for this below-target result.
[Sanitized evidence](2026-10-09-backend-priority.json) records named new cases,
partial family/source mapping, per-file counters and private report/binary hashes.
[Gate result](2026-10-09-backend-priority-gate.json) records the failing target.
Raw TRX and coverage reports remain ignored locally. No family is marked complete.
