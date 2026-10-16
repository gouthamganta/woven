# Afterthought atomicity retest — 2026-10-16

Tested application `0f5dc4b897e5215b991e9ea928064334e696a5e4` for PR #195 / Issue #120. PostgreSQL 16.14: **6 passed, 2 failed** in eight targeted scenarios. PR head rechecked and unchanged. Do not merge.

## Executed blockers

1. **P1: configured retries prevent integration.** With the same Npgsql EnableRetryOnFailure configuration as Program.cs, the explicit transaction throws InvalidOperationException: NpgsqlRetryingExecutionStrategy does not support user-initiated transactions. Wrap the entire transaction in Database.CreateExecutionStrategy().ExecuteAsync with appropriate replay state handling. The external-wrapper diagnostic passes; the application has no such wrapper. The submission service catches integration errors, leaving feedback pending.
2. **P2: substring deduplication discards distinct feedback.** For the same actor pair, seed an existing ExplicitFeedback signal whose metadata afterthoughtId is the current ID followed by zero. Processing the current ID matches that longer ID through Contains and creates no new signal. Expected two signals, actual one. Compare the complete structured ID or use a dedicated indexed identity.

## Passing controls

With retries disabled: normal processing and sequential replay; marker-save failure rolls back the signal and fresh-scope retry recovers; overlapping processors in separate contexts skip the locked row and produce one signal; pending deferred endpoint reaches the real integration; an existing exact signal repairs its completion marker. The sixth pass is the external execution-strategy diagnostic with retries enabled.

## Method and limits

Harness: qa/probes/afterthought-postgres/. [Machine-readable results](2026-10-16-atomic-postgres.json). Real application integration/signal services and deferred endpoint handler use a fresh PostgreSQL container, loopback port 55439, database woven_qa_atomic_0f5dc4b. Effective database/user identity checked before schema creation. Synthetic records only, no existing database reset. Dedicated disposable container stopped after the run.

Minimal three-table EnsureCreated schema: **not** application migration validation, HTTP middleware/authentication, TrustService, background worker, learner, or full-suite evidence. Trust is an unexpected-call stub for these non-safety cases. Earlier in-memory 219-case counts are historical; they cannot validate the newly introduced PostgreSQL raw SQL/transactions. This run does not establish 219/219. Prior PR acceptance gaps remain outside this retest. No application fixes, paid APIs, or deployments.

Next owner: Claude. Fix both defects; Codex then reruns relational cases and reconciles existing tests with PostgreSQL implementation.
