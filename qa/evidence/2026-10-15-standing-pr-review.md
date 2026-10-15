# Standing PR review — 2026-10-15

All15 open PRs inspected at the recorded heads. Scope: focused static/dependency review plus exact-head GitHub CI/failure-log analysis; prior local backend test execution explicitly distinguished. No new full integration or migration campaign. No automatic merges/deployments.

| PR | Head | Disposition |
|---|---|---|
| [#193](https://github.com/gouthamganta/woven/pull/193) | `05f8bd74` | No code blocker; draft |
| [#192](https://github.com/gouthamganta/woven/pull/192) | `18c52928` | Changes required |
| [#191](https://github.com/gouthamganta/woven/pull/191) | `4ea44495` | No code blocker identified |
| [#190](https://github.com/gouthamganta/woven/pull/190) | `ea635e53` | Runtime validation needed |
| [#189](https://github.com/gouthamganta/woven/pull/189) | `fd7b5f48` | No code blocker identified; overlap |
| [#188](https://github.com/gouthamganta/woven/pull/188) | `385f199d` | Runtime validation needed |
| [#186](https://github.com/gouthamganta/woven/pull/186) | `2f17f911` | Changes required |
| [#185](https://github.com/gouthamganta/woven/pull/185) | `08f1d19d` | Changes required |
| [#184](https://github.com/gouthamganta/woven/pull/184) | `aa114c1e` | Changes required |
| [#183](https://github.com/gouthamganta/woven/pull/183) | `35e2b249` | Changes required |
| [#182](https://github.com/gouthamganta/woven/pull/182) | `48b98093` | Changes required |
| [#180](https://github.com/gouthamganta/woven/pull/180) | `59ce488b` | Changes required |
| [#179](https://github.com/gouthamganta/woven/pull/179) | `8d24c05e` | Known failing regressions; keep draft |
| [#178](https://github.com/gouthamganta/woven/pull/178) | `0c51d288` | Changes required |
| [#144](https://github.com/gouthamganta/woven/pull/144) | `687d2515` | Blocked; changes required |

## PR #193

Evidence-only change; local differing records are explicitly archived, not active replacements. JSON/publication manifest inspected. Backend/frontend CI and CodeQL passed. The title checker rejected the qa type; corrected the title to chore(qa). Await the new metadata check; this is my own publication, not independent peer approval.

## PR #192

P1: package.json upgrades PrimeNG to 22 while Angular remains 21. CI npm ci fails ERESOLVE: @angular/cdk22 requires @angular/common ^22 or ^23, but the head has21. Keep compatible major versions, including @primeng/themes, and regenerate the lockfile. Tests did not execute.

## PR #191

Both IdentityModel packages advance together 8.19.2 to8.23.0. Backend/frontend build-tests and CodeQL passed at this head. Red assignment/auto-approval jobs are repository automation permissions: Forbidden and GitHub Actions is not permitted to approve pull requests. No new provider login/runtime integration executed; this is a static plus existing-CI review.

## PR #190

StackExchange.Redis jumps from2.8.16 to3.3.1. Backend/frontend build-tests and CodeQL passed; assignment failure is automation metadata. Static diff changes only the package version plus whitespace. A real local Redis cache/SignalR reconnect smoke is still needed before accepting the major upgrade; green unit CI is not that evidence.

## PR #189

Console sink6.0.0 to6.1.1; backend/frontend CI and CodeQL passed. Assignment/auto-approval failures are automation permissions, not test failures. PR188 contains the same sink update plus an AspNetCore major upgrade: choose a merge order and refresh the other branch; do not treat both as independent delivered fixes. No local logging smoke executed.

## PR #188

Serilog.AspNetCore8.0.3 to10.0.0 and Console6.0.0 to6.1.1. Backend/frontend CI and CodeQL passed; only assignment metadata failed. Package changes fit the app net10 target, and source registration was inspected, but startup/request logging and correlation output need a local smoke for the major upgrade. Overlaps PR189.

## PR #186

P1: Azure.Storage.Blobs12.30.1 breaks MediaService.cs:158. CI CS7036: GetBlobsAsync(BlobTraits, BlobStates, string, CancellationToken) now requires traits at this call. Update the call compatibly, add/retest listing and deletion contracts, and rerun backend CI. CodeQL C# also cannot build. No cloud storage call is authorized or required for this review.

## PR #185

P1: CI npm ci fails before tests: missing @emnapi/core1.11.3 and @emnapi/runtime1.11.3 from package-lock.json. Regenerate a complete lock using the declared toolchain, verify clean npm ci, then SSR/build/tests. This log does not establish an Express behavior regression.

## PR #184

P1: CI npm ci fails before tests on missing @emnapi/core1.11.3 and @emnapi/runtime1.11.3 lock entries. Repair lockfile reproducibly, then validate Motion12 to14 migration in actual call sites and browser animation cleanup. No runtime Motion defect is proven by this install failure.

## PR #183

P1: testing group upgrades TypeScript to7.0.2 but leaves Angular21 build tooling, whose peer contract is >=5.9 <6.0. CI fails ERESOLVE before tests. Keep the supported compiler range, align Node types with the runtime, and regenerate lock. PR182 also needs a different compiler range, so merging these together is not a solution.

## PR #182

P1: Angular22.2.2 build tooling requires TypeScript >=6.0 <6.1; this head retains5.9.3. CI fails npm ci ERESOLVE before build/tests. Upgrade framework, supported compiler, Node/runtime and component peer packages as one reviewed migration; do not bypass peers. PR183 proposes TS7 and therefore does not repair this mismatch.

## PR #180

P1: inherits PR178 required EmailHash change; CI CS9035 fails four existing User fixtures. Also UserDataEndpoints.cs deletion still assigns match participant FKs to0 although WovenDbContext configures Restrict references to Users; without a defined sentinel/schema strategy, matched-account deletion remains invalid. Its explicit BeginTransactionAsync is not wrapped in the configured retry execution strategy (Program.cs EnableRetryOnFailure). Putting operations in a transaction does not prove successful deletion. Reconcile identities/relations, make transaction retry-compatible, and prove real PostgreSQL success plus rollback integrity. No DB deletion was run in this fast review.

## PR #179

At8d24c05 local suite executed in the preceding publication session:658 total,655 pass,3 fail,0 skipped. Current GitHub CI also fails three mixed-case IntentOverlapIsCaseInsensitiveAndBonusIsCapped cases, matching issue181. Added tests use controlled providers, in-memory stores and local loopback; no full PostgreSQL/Program/cookie/provider acceptance is implied. Tests/harness and evidence reviewed, not an independent peer approval of my own work. Keep failures visible and retest Claude181 before acceptance; coverage80% remains incomplete.

## PR #178

P1: required User.EmailHash breaks existing test initializers (CI CS9035 in AiProfileContractTests, GameFeatureContractTests and MomentsRatingPrivacyTests). P1: migration initializes existing hashes to empty while auth switches immediately to hash-only identity reuse; the backfill is only a documentation snippet, so legacy accounts without an existing matching AuthIdentity can be duplicated. Deliver a guarded/backfilled migration path before switching lookup. The new index exists only in the migration, not model/snapshot: reconcile model metadata and prove no pending-model drift. Add existing-user identity reuse and race/uniqueness tests; schema upgrade not executed here.

## PR #144

Head687d2515 conflicts with master. CI backend job fails formatting before tests; migration snapshot check also fails. Existing exact-head QA comments document PendingModelChangesWarning, model/snapshot CHECK-column mismatch and undiscovered handwritten AddMatchSignalLog. Current diff still changes the CHECK expression without a snapshot reconciliation. The lifetime/auth/proxy slices already landed separately; rebase and isolate remaining repairs. Require successful fresh PostgreSQL migrations and upgrade evidence, not EnsureCreated or suppressed model warnings. No fresh migration rerun in this fast pass.

Automation: sampled assignment failure is Forbidden; sampled auto-approval failure says GitHub Actions cannot approve PRs. These are separate from app failures. PR193 title corrected to chore(qa); later checks may supersede the initial snapshot. No application fixes made.
