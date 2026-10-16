# Consolidated Afterthought and Photo Learning review — 2026-10-16

**QA failed; approval withheld.** Tested current master application `d10467e0e5d69157e22dec4c03be642fefb42f9e` in a separate checkout. PR195 had already been merged externally as d1b644c while the review was in progress. Photo commit d10467e was pushed directly to master; GitHub lists no associated PR. Issues120/198 were closed before this acceptance pass. No Codex approval or merge is implied by those external state changes.

## Executed matrix

| Suite | Actual result | Interpretation |
| --- | --- | --- |
| Frontend unit suite | 541/541 passed, 33 files | Tested source is identical between 209a979 and d10467e; no Docker image used |
| Backend contracts with QA additions | 200/219 passed, 19 failed, 0 skipped | 17 ECHO tests and one pending-recovery test depend on an obsolete InMemory provider; one duplicate test uses a no-op integration stub that never completes. These are QA adaptation debt, not 19 independent product defects |
| PostgreSQL integration matrix | 46/54 passed, 8 failed | Six failed scenarios share one service SQL error, one concurrency failure, one migration-chain failure |
| Photo protocol/worker fixtures | 9/13 passed, 4 failed | Two production-path blockers plus two defensive parsing checks; no paid inference |

Backend test output application DLL SHA256 exactly matches the pinned master's built DLL. Backend compiled successfully. Frontend production build result is recorded separately in summary JSON. Counts overlap in coverage and must not be added into a single acceptance total.

## Confirmed application findings — hand off together

1. **P1 / #120 — concurrent signal duplication persists on master.** Pause the first processor after its dedup check and before signal save. The second processor treats the still-active claim as interrupted and processes it. Resume the first: **two signals, expected one**. The newer single UPDATE/NOT EXISTS recovery operation does not protect the first processor's later insertion. Claim ownership/lifetime must distinguish active from abandoned work; a check-then-insert predicate cannot enforce effect uniqueness. Acceptance: gated overlapping scopes, simultaneous retries and failure recovery produce one effect.

2. **P1 / #120 — AfterthoughtService SQL query still throws.** Lines92–98 retain `FromSqlRaw(sql, new object[] { eventType, id }, ct)`. FromSqlRaw has no cancellation argument; the nested array is treated as a scalar parameter and produces the object[] mapping error. Keep ct on AnyAsync only. This breaks interrupted deferred recovery and completed-submission duplicate handling on **all four close paths** (six failing scenarios, one root cause). ExecuteSqlRawAsync's corrected collection-plus-ct overload is valid and passes. Acceptance: recover pending/interrupted submissions through the public handler, and reject true completed duplicates deliberately rather than throwing provider errors.

3. **P1 / #120 and #198 — full fresh migration chain fails.** A separately named, identity-verified disposable database fails at the older migration `20260604200154_FixMatchSignalLogOccurredAtType`: missing Users.CoachingOptedOut (SQLSTATE42703). The handwritten preceding coaching migration is not successfully supplying that column through the discovered chain. This is an inherited deployment blocker, not introduced by photo attributes. The new afterthought/photo migrations are not reached; no clean-install/schema-upgrade acceptance can be claimed. Do not apply manual production SQL as a workaround during QA.

4. **P1 / #198 — default asynchronous photo embedding response is dropped.** Fixture returns the documented starting state, output null and a polling URL. EmbedPhotoAsync immediately parses output, returns null and performs **zero polls**, so nothing is stored and attribute extraction is never reached. Provider async mode is default; no Prefer:wait header is set. This was inherited service behavior newly activated by the worker/upload path. Fix the initial embedding prediction lifecycle, including terminal failure, timeout and cancellation. A sync header alone still needs incomplete-response fallback. [Provider contract](https://replicate.com/docs/topics/predictions/create-a-prediction).

5. **P1 / #198 — photo-specific EXIF routine truncates JPEG scan data.** A synthetic SOI/APP1/SOS/entropy/EOI marker fixture loses its entropy payload and end marker: expected13 bytes after removing APP1, actual8. The loop treats the first compressed scan byte as another segment marker and exits. This is inherited in PhotoEmbeddingService, but the new automatic upload path depends on it. Preserve the scan stream after SOS, preferably using an established image/metadata implementation. Existing PiiSanitizer tests do not exercise this separate helper. This is a marker-stream regression test, not an image-decoder quality test.

6. **P1 completeness / #120 — collection UI does not cover every eligible closure.** Static source review: MatchesService.block posts `{}`; chat-thread.block bypasses the Afterthought modal. There is no frontend deferred `/afterthought` client, expiry modal path or recipient/deferred collection flow. Thus direct blocks miss the safety reason, and expiry/other-participant collection required by Product Rules v2 R-1 is unreachable from current client wiring. Trial END/BLOCK and initiator unmatch modal wiring exist. No browser flow executed; this is a source-confirmed integration gap. Implement all eligible collection paths, respecting blocker-only safety and no stars for expiry/block.

## Additional photo defects and unverified readiness claims

- **P2 defensive parsing:** A succeeded attribute fixture with populated output but no urls.get is ignored, producing empty attributes. Parse terminal output before deciding to poll. This fixture does not prove Replicate normally omits urls.get.
- **P2 defensive parsing:** ParseZeroShotResult accepts a probability array whose length differs from labels and silently chooses a label. Validate count, finite values and confidence range; do not interpret arbitrary embedding output as calibrated probabilities. Actual configured model/version input/output compatibility is **unverified**, not a confirmed provider-schema defect.
- **Reliability gap (static):** Channel queue is in-memory/unbounded; failed/null embedding results are logged and discarded without durable retry/backfill. Restart also loses queued work. No recovery/quality acceptance was supplied for the new automatic pipeline. Worker continuing after one thrown item is verified; that is not durable delivery.
- The claimed manual `apply_photo_attributes_migration.sql` is absent from tracked commit d10467e. The repository **does contain** backend/WovenBackend.Tests/WovenBackend.Tests.csproj, contradicting the reason given for deferring tests.
- Issue198 lists height and PhotoSimilarity scope; delivered attributes omit height, while the commit says scoring integration is Week4. Resolve the scope record against the plan before calling the issue complete; no product scope changes made in QA.

## Verified coverage and fixes

All four service submission paths; five star normalizations; invalid ratings/paths/safety flags/oversized text; actor/target separation; opposite-participant feedback; real TrustService target caps including already-low trust; no safety mixing into matching; pending recovery; direct interrupted matching/safety recovery; pre-claim cancellation; mid-signal cancellation followed by fresh processor recovery; load failure after claim; signal-save-then-failure fresh retry; true sequential matching replay; exact IDs; deferred outsider/open/missing/blocked authorization guards all executed. Trust uses actual PostgreSQL Users plus an audit stub. HTTP handlers use synthetic identities, not real authentication middleware.

The latest trust-score guard stops redundant completed safety flags in the control; earlier safety replay failure is resolved on this master. The rating-bar removal appears in source; no public-rating display was found in the reviewed Moments template. Real nightly aggregation, weekly weight learning, live model inference, full browser flows and existing-production database upgrades remain unverified. No stale frontend/backend image was rebuilt or started.

## Evidence quality and reproduction

[PostgreSQL cases](2026-10-16-consolidated-postgres.json), [photo cases](2026-10-16-photo-learning.json), [suite summary](2026-10-16-consolidated-summary.json). Harnesses: qa/probes/afterthought-postgres and qa/probes/photo-learning. Select WOVEN_QA_CASES=consolidated, WOVEN_QA_EXTENDED=1, tested SHA, guarded connection and JSON path; run documented dotnet commands. Ordinary PostgreSQL tests use a five-entity projection; the migration test uses the real complete model/migration chain in a separate disposable database. Photo HTTP/EF projection does not certify vector persistence or inference quality.

An exploratory expanded run had a fixture metadata ID collide with a later row, giving an apparent cancellation failure. The fixture was corrected to a distant ID, the entire matrix rerun, and cancellation recovery passed. That apparent failure is **not** handed off as a product issue. A test harness model-key setup error and overlapping-build/file-copy errors were also corrected before final results. All final JSON reflects completed corrected runs.

GitHub is the live task record. Reopen120/198 as owner:claude, stage:changes-requested; Claude should acknowledge one consolidated fix plan. Codex owns porting the obsolete in-memory QA cases to relational coverage. Retest this complete matrix after fixes, not only the latest failing case. No paid providers, application edits, existing database reset, cloud deployment or production migration.
