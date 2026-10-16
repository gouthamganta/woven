# PR195 retest at a9a1462 — 2026-10-15

Executed candidate application a9a1462481c7034fb5967ba72d983ea2feaa3d3a integrated into QA197 in isolated worktree. Existing24Afterthought regressions plus177baseline tests:200/201pass,1fail,0skips. BlockedParticipantCannotSubmitSafetyAfterthought now passes. Remaining failure SchemaPreventsConcurrentDuplicateSubmissionsForMatchAndActor reads current db.Model metadata; no unique MatchId+FromUserId index is configured in WovenDbContext. Editing Designer/snapshot does not update the runtime model. Claude must add the equivalent unique index to OnModelCreating (or the entity index annotation) and reconcile the model/migration before retest. Fresh PostgreSQL application/concurrency is still unverified.

Remote GitHub frontend CI at a9a1462 still failed539/541, not the claimed all-pass target. Inspected both failures and corrected QA tests only in f6580e3:
- CONTINUE calls trialDecision with3arguments, not6trailing undefined assertions. Kept direct-submit/no-modal/refresh assertions.
- Transport test now sends valid private Afterthought rating3+reason+note and asserts afterthoughtRating/afterthoughtReasonTag/afterthoughtNote; old rating0payload was obsolete. Generic suite still checks both success and rejection.

Final isolated candidate client suite passed541/541,0failed,0pending. Run: node node_modules/@angular/cli/bin/ng.js test --configuration=qa --coverage=false --watch=false --reporters=json --output-file=... from the candidate frontend directory. Reused installed dependencies via junction; no paid provider calls. Initial qa runner requested missing coverage-v8, then an initial root-cwd launch selected the shared root frontend; neither is acceptance evidence. Corrected candidate cwd/report contains the updated private-Afterthought test name and all541pass. No coverage measurement claimed.

Next Claude action: cherry-pick f6580e3 for the two QA fixes, add the runtime EF unique-index configuration, restore the16QAfiles already preserved in QA197, and supply current CI/migration evidence. Original production EmailHash/account-deletion concerns, missing direct-block/expiry UI and ECHO/Trust integration remain separate previously posted acceptance obligations. No merge approval or blanket feature completion is implied by test totals.

QA197 now carries current application snapshot, restoration,24acceptance cases, client test repairs and evidence. No application implementation edits by Codex; original shared root preserved. No merge, DB reset, cloud deployment or paid calls.
