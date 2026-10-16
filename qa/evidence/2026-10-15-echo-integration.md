# ECHO Afterthought integration QA — 2026-10-15

Agent: Codex
Application head:3e7d3ac5ca2b7309e577fa2666994dc2f39c1ecf
Branch/worktree:qa/afterthought-review-20261015;qa/.local/pr195-review
Status:Changes requested; completion not accepted.

## Executed

Full backend suite216cases:214passed,2failed,0skipped. All201earlier acceptance/baseline cases pass, including the previously missing runtime EF unique index. Added15ECHO integration cases:13pass,2fail. No harness-load errors. Existing acceptance harness adapted to the new constructor and deliberately isolates integration; separate ECHO cases use real AfterthoughtService, AfterthoughtIntegrationService, MatchSignalService and TrustService over synthetic InMemory storage, with only auditing/cache side effects stubbed.

Passed: all five normalization values1→0,2→0.25,3→0.5,4→0.75,5→1; directional viewer/candidate signal storage; no trust change for ordinary feedback; safety block cap0.15/other-safety cap0.30; existing lower trust not increased; safety creates no matching signals even on a historical row containing stars; no-rating expiry creates no artificial rating/penalty; missing record has no side effects; real submission persists then triggers integration.

## Two reproduced recovery gaps

1. RetryingSavedSubmissionAfterTransientIntegrationFailureRecoversItsLearningSignal fails: inject a temporary MatchSignalService failure, submit once, then recover the dependency and retry submission. Feedback remains saved exactly once but signals remain0. AfterthoughtService catches/logs integration failure, then its duplicate-record guard rejects retry before integration can run. Source search finds no durable retry/reconciliation worker or pending-processing marker. Saved feedback is not lost from the DB, but its learning contribution can remain absent after a transient failure. Supply a durable recovery path and prove eventual single application.
2. ReprocessingAnAfterthoughtDoesNotDuplicateItsLearningSignal fails: call ProcessAfterthoughtAsync twice for one saved feedback row; actual MatchSignalService stores2ExplicitFeedback rows for the same actor/pair/value. Add a stable feedback/event identity and idempotent processing before introducing retry/replay. This demonstrates duplicate events, not a measured double training-weight effect: the current batch aggregates MaxValue for ExplicitFeedback.

Claude owns both application repairs. Preserve safe match closure and saved feedback; do not convert optional feedback into a required blocking network operation. Suggested acceptance: dependency fails before or after signal persistence, retry/replay eventually creates one attributable signal, safety remains trust-only, and concurrent processing is safe under real PostgreSQL constraints.

## Pipeline limits and remaining scope

Static source inspection confirms ConnectionScoreBatchWorker has defaultExplicitFeedback weight0.13 and WeightLearningService reads composite ConnectionScores. Nightly PostgreSQL batch and weekly learning were not executed; end-to-end learning quality, MaxValue history aggregation and MinConnectionScore filtering are not certified by these component tests. No full migration/upgrade/concurrency sign-off.

Exact-head remote frontendCI still539/541 because the two QA assertion fixes from f6580e3 have not been integrated into195. QA197's prior isolated candidate client541/541 result applies with those test-only repairs; no new client suite or coverage measurement in this backend-only continuation. Prior direct-block/expiry UI, production identity/deletion and16QAfile-deletion findings remain separate acceptance obligations.

QA tests, restored16files and evidence are published in draft197. No application fixes by Codex, database reset, paid provider, cloud deploy or merge. Next:Claude repair recovery/idempotency, take QA test corrections, publish exact candidate, then Codex retests. Keep120Changesrequested/Claude.
