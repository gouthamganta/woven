# Local coverage measurement ? 2026-10-08

Measured code coverage is low. These figures describe the specified local candidates and collectors, not merged master or complete product readiness. Full per-file counts and missing frontend files are in [the JSON report](2026-10-08-coverage-by-module.json).

Backend source: startup/72h candidate `5461c85`, with current QA tests. Coverlet measured **1.55% line / 1.03% branch coverage**, including 71,529 generated migration/snapshot lines. Excluding those files analytically gives **6.51% line coverage** (same executed lines; no new test run). Entity/model coverage largely comes from building EF models; it does not prove database persistence behavior.

| Area | Line coverage | Branch coverage |
|---|---:|---:|
| Data/model/entities | 60.19% | 8.33% |
| Other application services | 0.8% | 0.42% |
| Background workers | 0% | 0% |
| Security/encryption | 33.1% | 15.79% |
| Moments/Sparks/matches | 0.86% | 1.64% |
| ECHO/matchmaking | 5.57% | 4.11% |
| Migrations/generated snapshots | 0% | N/A |
| Middleware/infrastructure | 0% | 0% |
| Realtime hubs | 0% | 0% |
| API endpoints | 0% | 0% |
| Authentication | 59.21% | 23.08% |

Frontend source: auth candidate `176cf74` overlaid on the QA worktree for the run, then restored. The default V8 report measured **4.12% lines / 14.42% branches / 4.83% statements / 5.93% functions** across 65 reported files. Of 93 app TypeScript/template files inventoried, **29 are missing from the report**; 65 includes a file outside that app inventory. Whole frontend coverage is **unknown**. The older, smaller loaded-file percentage is not a comparable trend. Module initialization can produce small nonzero page figures without exercising the page behavior.

| Area | Line coverage | Branch coverage |
|---|---:|---:|
| Other frontend | 40% | 55% |
| Components | 3.59% | 17.49% |
| Core: auth | 94.73% | 80.7% |
| Onboarding components | 1.14% | 9.27% |
| Page: chats | 0.26% | 2.71% |
| Page: commons | 1.01% | 6% |
| Page: home | 0.85% | 12.5% |
| Page: landing-simple | 0.26% | 10% |
| Page: legal | 1.38% | 42.85% |
| Page: login | 33.62% | 23.25% |
| Page: matches | 0.86% | 7.59% |
| Page: moments | 0.63% | 5.63% |
| Page: my-tiles | 0.56% | 10.16% |
| Page: onboarding | 4.08% | 16.56% |
| Page: profile | 0.96% | 11.53% |
| Page: settings | 0.59% | 12.76% |
| Services | 10.19% | 31.34% |

Backend tests: **50/51 pass**, with the recent Saved-record contract failing until the removal change is integrated. Frontend tests: **51/51 pass**. These are pass rates, not coverage. External API, PostgreSQL, proxy, browser and concurrency probes are not aggregated into these unit coverage runs. Therefore endpoint code reports 0% in this run despite separate API execution evidence.

API requirements, integration requirements, browser/device/accessibility journeys, notifications, operations and security requirement coverage percentages remain **unknown**: their complete denominators have not been established. The registry has 130 named cases across 98 design families; no family is fully completed. Neither count supports a claim of 100% coverage or exhaustive edge cases.

Reproduction: run `dotnet test backend/WovenBackend.Tests/WovenBackend.Tests.csproj -p:WovenBackendProject=<startup-candidate>/backend/WovenBackend/WovenBackend.csproj --collect:"XPlat Code Coverage" --logger "trx;LogFileName=current-backend.trx" --results-directory <local-results>` from the QA worktree; run `node node_modules/@angular/cli/bin/ng.js test --watch=false --coverage --reporters=json --output-file=<local-results>/frontend-tests.json` from its frontend with the three auth-candidate source files overlaid. Restore those files afterward. Raw reports stay local; their SHA-256 hashes are recorded in JSON.
