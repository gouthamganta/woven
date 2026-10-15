# GitHub Project setup evidence

Project: https://github.com/users/gouthamganta/projects/2
Repository: https://github.com/gouthamganta/woven (public)
Project visibility independently queried: private.

## Completed and verified

- GitHub CLI v2.102.0 downloaded from official cli/cli release; archive checksum
  compared with release checksum file before extraction. Binary kept in ignored
  qa/.local/tools, not committed.
- Founder completed GitHub browser authorization with project permission.
  Credentials are read through GitHub CLI/Git Credential Manager and are not
  written to task plans, mappings, logs or tracked evidence.
- 25 task Issues created/reused using unique task markers: #77–#101.
  Original verified items imported closed; this preserves historical claims
  and evidence limitations, not an additional product-readiness assertion.
- Labels created for owner role, stage, priority and pipeline membership.
- Project linked to Woven. 25 items verified through GraphQL.
- Eight delivery options configured on both native Status and Stage fields;
  all 25 item values checked against their Issue stage labels/closed state.
- Six named views verified: Delivery board, Founder decisions, Claude queue,
  Codex QA, Blocked work, Completed evidence. GitHub's default View 1 also exists.
- Local one-shot synchronizer executed successfully. It adds delivery Issues
  and mirrors labels into fields; it does not execute agents or tests.
- Hidden Windows synchronizer started as PID 30300. Repeated startup reported
  the same existing process; first background scan checked 25 Issues and made
  0 redundant updates. Logs/PID live in ignored qa/.local/.
- JS syntax checks passed. Draft PR #102 publishes guide, scripts, task form
  and task mapping on chore/woven-github-pipeline, isolated from active work.

## Limits and next steps

- Stage/owner labels are authoritative; direct Project field edits are not
  synchronized back to Issues. Use the handoff helper or edit Issue labels.
- Local background sync needs laptop/network available. No startup service
  installed. Phone pairing is not verified.
- Task form is in draft PR #102 and not active on master until merged.
- No automatic agent dispatcher, session heartbeat or distributed task lock is
  installed. OPS-001/OPS-003 track that work.
- PR not merged: current master push workflow can trigger Azure deployments
  for qa/ and .github/ changes, outside this zero-cost board setup scope.
- GitHub Project is private but repository Issues are public. Only sanitized
  task/evidence summaries are appropriate there.
