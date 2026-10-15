# Frontend error-path tests and complete TypeScript coverage

Local run 2026-10-08, reviewed auth candidate `176cf74` overlaid and restored. Added 11 tests for 401 recovery, forbidden/conflict/rate-limit/server/network errors, successful response passthrough and correlation tracing. **61/62 total pass; 10/11 new cases pass.** The new storage-denied 401 contract fails: cleanup throws SecurityError before login navigation and masks the original HTTP error. Claude handoff: [#170](https://github.com/gouthamganta/woven/issues/170). This regression remains deliberately failing until independently verified after a fix.

The `qa` Angular test configuration includes all non-spec app TypeScript files and writes coverage even when tests fail. The local Vitest configuration limits execution to one worker. `verify-frontend-coverage.mjs` checks the inventory against coverage keys and fails if an app TypeScript file is missing. **72/72 TypeScript files reported, zero missing**, 4.12% lines (179/4338), 14.80% branches (316/2134), 5.40% statements and 6.26% functions. The 21 HTML templates are separately unmeasured; no whole frontend behavior coverage claim. This scope differs from the earlier loaded-file report; the matching rounded 4.12% is coincidental, not a comparable trend.

An initial HTML-inclusive attempt produced parse errors on uncompiled templates. An initial candidate-overlay attempt omitted the route file and was discarded as a candidate measurement. The runner now overlays exactly the three reviewed files. The final report above used the corrected runner and preserves the negative result.

Reproduce from the QA worktree: `node qa/scripts/run-frontend-qa.mjs <auth-candidate-worktree> <private-results-directory>`, then `node qa/scripts/verify-frontend-coverage.mjs <output-json>`. Without a candidate argument the runner tests the current branch, where baseline authentication failures are expected until PR167 is integrated. Never treat the candidate results as merged-master results. The verifier is a separate command because coverage completeness is independent of test pass status.

[Executed cases](2026-10-08-frontend-error-paths.json), [coverage and per-file counts](2026-10-08-frontend-complete-typescript-coverage.json), [independent dependency audit](2026-10-08-security-dependency-retest.json). Raw reports remain local; SHA-256 hashes are recorded. No paid calls, database reset, merge or deployment.

Dependency candidate `4e421ee`: fresh production audit reports zero vulnerabilities; independent development browser/server build passed (46.088s). Installed Angular core21.2.25/build21.2.26 match the lockfile. Production build, clean-install reproducibility, functional regression and dev dependency audit remain unverified. Issue #125 stays open.

The regression evidence mapper now preserves later cases when refreshing the old baseline; executing it retained all141 named cases and linked the new cases to partial families.
