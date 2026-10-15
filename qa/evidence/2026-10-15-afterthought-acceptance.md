Agent: Codex
Session: Afterthought acceptance audit, 2026-10-15
Local application reviewed: 93adf2d3ce346050b72f5a0699091dc8abe05e87
Worktree: qa/.local/pr195-review; branch qa/afterthought-review-20261015
Status: Changes requested. Backend completion claim fails acceptance.

Executed existing backend suite:177/177pass. Added24service/schema/registered-handler cases:15pass,9fail,0skipped. Failures are assertions on actual application behavior/EF metadata, not harness load failures. InMemory/registered-handler tests do not certify PostgreSQL, authentication middleware or full browser behavior.

Priority fixes for Claude:
1. P1 migration mismatch: current entity MatchAfterthought.MatchId is Guid, but AddMatchAfterthoughts creates integer match_id and nullable uuid MatchId1 as the FK. Migration metadata test fails Guid-vs-int. Reconcile model/snapshot/migration, use match_id for the FK, and prove isolated fresh/upgrade PostgreSQL acceptance without suppressing model drift.
2. P1 safety authorization: deferred endpoint allows actor2 to submit block feedback when actor1 blocked2. Test expected403, received200 and persisted safety feedback. Blocker only per approved spec; check the actual block ownership, including blocks added after a prior close reason.
3. P1 lost END feedback: actor1 submits END with rating2, actor2 later submits either END or CONTINUE. Only the second actor is stored; actor1's feedback is lost in both tested orders. Preserve each person's optional submission independently of who resolves the match; do not store a null placeholder for CONTINUE.
4. P1 duplicate/inability-to-defer: repeating block persists two Afterthoughts. Schema has no unique index on MatchId+FromUserId, so the deferred AnyAsync check does not provide concurrency safety either (race not executed). Separately, unmatch with {} creates an empty row; later actual feedback gets400 already_submitted. Distinguish no submission from an intentionally submitted optional response and enforce idempotent/unique storage.
5. P2 request validation: service accepts51-character reason and301-character note with no validation. EF max lengths are not API validation; PostgreSQL may reject later, and close handlers swallow all Afterthought exceptions. Validate before storage and provide observable persistence failures; closing safely must remain possible without feedback.

Static remaining work: no Afterthought consumers in WeightLearning/Trust or MatchSignalLogs, no full frontend implementation verified, and deferred trial-END is mapped from UNMATCH to unmatch (path provenance lost). These remain open; do not describe the whole feature as complete.

PR195 is still published at old b6385ee with conflicts and no build/test checks. Local candidate also deleted16QA files; Codex restored them in the isolated QA branch (root/Claude work untouched). Earlier production EmailHash transition and account-deletion FK0 blockers remain. Preserve founder-approved behavior and keep private ratings out of public surfaces. Existing rating-bars removal landed separately on master; verify the current combined source rather than reintroducing stale UI findings.

Next owner: Claude implement focused repairs and publish exact candidate; Codex retest these24cases plus current suites. No merge, reset, cloud deployment or paid provider calls. QA tests/restoration/evidence will be pushed for direct use; no founder relay needed.

Final full-suite execution:201total,192passed,9failed,0skipped. The same9acceptance failures reproduce in the combined run; existing177cases continue passing. Targeted24case run15pass/9fail. Only the xUnit assertion-style warning in the new test was corrected before final run; no application implementation changes. All16missingQAfiles restored from origin/master on the isolated branch. Local Claude frontend changes were not touched.
