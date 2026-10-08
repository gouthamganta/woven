# Woven shared GitHub workflow

Repository: https://github.com/gouthamganta/woven

Project: https://github.com/users/gouthamganta/projects/2

Views: [Delivery](https://github.com/users/gouthamganta/projects/2/views/2),
[Your decisions](https://github.com/users/gouthamganta/projects/2/views/3),
[Claude](https://github.com/users/gouthamganta/projects/2/views/4),
[Codex](https://github.com/users/gouthamganta/projects/2/views/5),
[Blocked](https://github.com/users/gouthamganta/projects/2/views/6),
[Completed](https://github.com/users/gouthamganta/projects/2/views/7).

GitHub Issues are the task record for all laptop, phone and cloud sessions.
`qa/github-board.json` maps the former Markdown task IDs to Issues and records
the Project URL after creation. The Markdown board is a historical snapshot;
update GitHub first once migration is complete.

## Workflow

Intake → Needs decision (when required) → Ready → In progress → Ready for QA →
Done. Failed QA goes to Changes requested; dependencies go to Blocked.

Use owner:claude, owner:codex or owner:founder to identify the responsible role.
These are labels, not separate GitHub accounts. Only one stage and owner role
should apply. Project Stage and Owner role fields mirror these values.

The project is private; Woven repository Issues are public. Keep secrets,
real-user information and sensitive unpatched exploit details out of public
Issues. Publish sanitized evidence links and summaries.

## Every agent session

1. Read this guide and the target Issue, including comments, before starting.
2. Check another session is not already working on the task. Record your role,
   session ID/link if available, worktree/branch and starting commit.
3. Work in a separate Git worktree for application edits; keep local secrets and
   test data outside Git. Local Docker services are not available to a separate
   cloud checkout unless explicitly arranged.
4. Post progress and blockers to the Issue. Do not rely on private chat history.
5. Handoff: link the change, tested commit, commands, outcomes, evidence and next
   action. Move the stage and owner together.
6. Codex retests fixes before Done. Founder decisions are required for meaningful
   product changes. Do not automatically merge or deploy.

Comment format:

```text
Agent: Claude / Codex / Founder
Session: ID or URL if available
Branch/worktree:
Starting/tested commit:
Status:
Changes/evidence:
Checks and actual results:
Remaining limitations:
Next owner/action:
```

## Setup and recovery

- `node qa/scripts/setup-github-project.mjs --plan`: show migration plan.
- `node qa/scripts/setup-github-project.mjs --issues-only`: migrate tasks with
  repository credentials; interrupted runs reuse task markers.
- `node qa/scripts/setup-github-project.mjs --project`: create/reuse the private
  Project, link Woven, add issues and fields. Requires GitHub project permission.
- `node qa/scripts/sync-github-project.mjs`: add new delivery Issues and mirror
  stage/owner labels into Project fields. `--watch` repeats every 60 seconds;
  local computer must remain running. This sync does not launch AI sessions.
- On Windows, `powershell -File qa/scripts/start-github-sync.ps1` starts one
  hidden local synchronizer. Its PID and logs are under ignored `qa/.local/`.
  It is not installed as an auto-start service. Restart after a reboot as needed.
- `node qa/scripts/github-task.mjs handoff QA-001 "Ready for QA" Codex qa/.local/handoff.md`:
  post a handoff and update role/stage together. Run sync afterward. Completion
  requires an evidence reference. This helper is not a distributed session lock.
- Credentials come from environment, authenticated GitHub CLI, or Git Credential
  Manager. They remain in memory and never appear in manifests or evidence.

Edit stage/owner labels or use the handoff helper; fields mirror labels. Direct
Project field edits are not synchronized back to Issues. Label conflicts are
reported for triage rather than silently resolved. Only one local sync process
should run, because it writes the mapping file.

Initial values are historical task states; migration does not establish live
sessions. Automatic dispatch and live session tracking are OPS-001 and OPS-003,
not yet enabled. The board does not wake agents by itself.

## Phone usage

Use the GitHub Project in your phone browser to submit, inspect and decide work.
For local agents, use native remote connections to this laptop:

- Codex: ChatGPT mobile Remote can control work on connected development hosts.
  Pair the correct host/workspace using the available remote controls; account
  access must be checked on the device. [Official guide](https://developers.openai.com/blog/mastering-codex-remote-for-engineering).
- Claude: enable `/remote-control` inside the existing local Claude Code session,
  then open its link/QR on your phone. This requires an eligible Claude
  subscription; API keys do not enable Remote Control. Keep the laptop and Claude
  process running. [Official guide](https://code.claude.com/docs/en/remote-control).

Phone pairing is not completed by creating a Project. Do not start competing
sessions on the same task or assume a cloud session sees local Docker/DB.
Existing account limits still apply; no paid fallback is configured.
