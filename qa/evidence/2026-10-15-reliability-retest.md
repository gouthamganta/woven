# Afterthought reliability retest — 2026-10-15

Agent:Codex
Application reviewed:b46e10b085d88de3dd7ea92658e797d172a1e71b
QA branch:qa/afterthought-review-20261015;isolated qa/.local/pr195-review

## Original fixes verified

All216existing tests pass,0skips. Both originally failing before-effect retry and sequential replay cases are fixed. ProcessedAt nullable migration/state and signal metadata are present. Normalization/trust isolation/caps and the earlier201cases stay green. This is an actual executed216/216 result, not a target.

## Three additional boundary cases fail

Extended full suite219cases:216pass,3fail,0skips. All three use actual application services/handler code; no harness failures.

1. RetryAfterSignalCommitButBeforeProcessedMarkerDoesNotDuplicateSignal: a SaveChangesInterceptor fails the completion-marker save after real MatchSignalService has persisted its signal. Dispose the scope, reload in a fresh context (ProcessedAt still null) and retry. Two ExplicitFeedback rows now exist for the same afterthoughtId. Signal insertion and marker completion are separate saves; the boolean/timestamp guard does not make the side effect atomic or independently unique.
2. OverlappingProcessorsFromSeparateScopesCreateOnlyOneSignal: two independent contexts load the same pending Afterthought; a deterministic rendezvous holds both before signal recording. Both finish successfully and insert a signal, producing2rows with the same afterthoughtId. This is a controlled InMemory reproduction of the check-then-act gap, not a live PostgreSQL load/concurrency proof. A processed flag alone does not serialize processing.
3. DeferredEndpointAllowsRecoveryOfPendingIntegration: POST/matches/{matchId}/afterthought with an existing pending row returns400, not200. MatchesEndpoints checks AnyAsync for any existing submission before calling RecordAsync, so public deferred retries never reach the new pending-retry branch. Make that endpoint distinguish completed from pending and test end-to-end recovery, rather than only direct service calls.

Next Claude action: make effect+completion retry-safe using a transaction/atomic claim and an independently enforced event identity or equivalent design; preserve Npgsql execution-strategy compatibility. Pending API retries must reach recovery. Verify pre-effect failure, post-effect failure, fresh-scope replay, overlapping scopes and full suite; keep safety feedback isolated. ProcessedAt is useful state but not alone an exactly-once guarantee.

## QA-only client correction integrated

The two client test fixes already validated541/541 were cherry-picked onto a separate checkout ofb46e10b and fast-forward-pushed directly to PR195 as813c270. Only two assertion lines changed; no application source changes by Codex and shared root left untouched. One asserts the actual3-argument CONTINUE call; the other verifies valid private Afterthought rating3+reason+note transport instead of legacy rating0payload. Exact-head GitHub CI at813c270 is pending; do not call it passed until complete. Claude must incorporate remote813c270 before the next push; no force push used.

Three new regression cases and restoration/evidence are in draftQA197. Prior frontend flow, identity/deletion,16QAfile restoration and PostgreSQL migration acceptance obligations remain separate from this scoped test result. No new coverage, live batch/learner or production sign-off. No merge, DB reset, paid provider or Azure deployment.

Status:#120Changesrequested/Claude for the three boundary repairs; original two findings explicitly verified fixed.
