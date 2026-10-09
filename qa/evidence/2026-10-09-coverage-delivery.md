# Coverage campaign checkpoint - 2026-10-09

Primary acceptance target: **at least80% lines and branches independently for backend and frontend application code**. Both gates currently fail; no release or completion claim. No first-party services/endpoints are excluded. Generated migrations are separately reported and still require fresh PostgreSQL migration validation.

| Measurement | Lines | Branches |
|---|---:|---:|
| Backend unit collector | 9.89% | 5.30% |
| Backend real HTTP/PostgreSQL collector, isolated Redis | 27.34% | 10.09% |
| Backend combined counter union | **31.33%** | **14.71%** |
| Frontend all72 app TypeScript files | **21.59%** | **27.64%** |

Backend union verifies the same application binary SHA-256 and joins source/method/IL branch identifiers; percentages are not added. It covers7031/22444 application lines and846/5751 branches. All72 frontend TypeScript files are present, with937/4338 lines and590/2134 branches hit. Its21 external HTML templates remain separately unmeasured. Rendered onboarding controls are tested, including actual disabled states, failed Continue clicks and review content; class-only tests do not establish template coverage.

**119 additional tests delivered since the80% instruction:**40 backend and79 frontend. Backend90/91 pass, with the existing Saved-removal contract pending PR141 integration. Frontend138/141 pass, with3 visible application failures: storage-denied401 (#170), failed blob upload reported as success (#171), and stale deck completion after failed PASS (#172). Real API smoke34/35 pass; the remaining bounded correlation-ID security contract belongs to #108. These counts are pass rates, not coverage percentages.

Individual file results already above80% lines and branches include Moments92.05/82.17, ChatService100/92.30 and MediaService100/100 before rendered onboarding additions. A fully covered file can still contain a failing contract; MediaService is the example.

The reusable API collector creates a fresh clone of the100-user synthetic model database, uses dedicated Redis without persistence, runs only on the internal QA network through loopback5182, and stops its own backend/gateway/cache afterward. Instrumentation modifies a private compiled copy and verifies restoration; original runtime/DB are preserved. This is a snapshot clone, not migration proof. An initial empty collector was rejected; a subsequent automated cold-start timeout was preserved and startup allowance corrected. Windows PDB paths left generated migration lines in raw Linux reports; the gate partitions only those paths explicitly. No paid provider calls, cloud deployment, merge or production database operation.

Reproduce from this QA checkout with prepared local QA infrastructure:

- Frontend: `node qa/scripts/run-frontend-qa.mjs <auth-candidate-worktree> <fresh-private-results>`; then `node qa/scripts/verify-frontend-coverage.mjs <coverage-json>` and `node qa/scripts/check-coverage.mjs frontend <coverage-json>`.
- Backend unit: `node qa/scripts/run-backend-qa.mjs <candidate-WovenBackend.csproj> <fresh-private-results>`. The QA test dependencies align EF Core/Relational10.0.12.
- Real API: install the free local Coverlet.Console6.0.4 tool in the SDK10 container to qa/.local/coverlet-cli; run `node qa/scripts/run-api-coverage.mjs <workspace-root> <compiled-Debug-backend-directory>`. The script requires existing private runtime/model env files and the100-user synthetic model database.
- Combined: `node qa/scripts/merge-backend-coverage.mjs <unit-coverage.json> <http-coverage.json> <summary.json> <unit-identity.json> <http-identity.json>`; then `node qa/scripts/check-coverage.mjs backend-combined <summary.json>`.

Coverage thresholds and all failed/blocked checks return nonzero; the API smoke harness previously returned0 on assertion failures and has been corrected. Failed tests still produce coverage, and missing or empty denominators are rejected. Helpers and SQL/source-key behavior are simulated only where explicitly stated; no transaction concurrency claim from the in-memory adapter.

[Executed cases](2026-10-09-coverage-batches.json), [backend union and per-file counts](2026-10-09-backend-combined-coverage.json), [frontend per-file counts](2026-10-09-frontend-coverage.json), [real API collector evidence](2026-10-09-backend-http-coverage.json), [backend gate](2026-10-09-backend-combined-gate.json), [frontend gate](2026-10-09-frontend-gate.json). Raw reports remain private, with hashes recorded. Highest remaining backend gaps include chat/onboarding endpoints, games, insights and AI fallback paths. The primary80% task remains active.
