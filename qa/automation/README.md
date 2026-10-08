# Local execution pilot

The worker has completed a bounded fixture-only handoff through both live
subscription-backed CLI tools. [Pilot Issue #103](https://github.com/gouthamganta/woven/issues/103),
[draft evidence PR #104](https://github.com/gouthamganta/woven/pull/104).
General task dispatch is **not enabled**. config.json has enabled=false and an
explicit Issue/path allowlist. This code cannot yet build arbitrary Woven tasks.

## What ran

GitHub approved pilot → isolated Git worktree → Claude writes one synthetic JSON
fixture → local exact-contract validation → Codex reviews the verified disk
snapshot → hash checked again → commit to isolated branch → draft PR → GitHub
Issue completion. No automatic merge/deployment or paid API-key fallback.

Earlier review attempts correctly failed when filesystem inspection was
unavailable/prohibited. The corrected review supplies worker-collected bytes
and SHA-256, and explicitly reports snapshot review rather than direct agent
filesystem inspection. Recovery reused the successful Claude artifact and made
one additional reviewer call; failed logs remain local.

## Commands

- `node --test qa/automation/pilot-contract.test.mjs`: deterministic offline
  checks for fixture contract, out-of-scope paths and malformed reviewer output.
- `node qa/automation/worker.mjs --once 103`: explicit allowlisted pilot only.
  Closed Issues are not rerun. Do not reopen it just to keep spending allowance.
- `node qa/automation/worker.mjs --once ISSUE --resume-review LOCAL-RUN-UUID`:
  manually recover the same blocked pilot's reviewer, after inspecting the cause.
- Logs, state, worker lock and retained worktrees are under ignored
  `qa/.local/automation/`. active.json records phase, sessions, commit and PR.

Subscription access is verified before model invocation. The worker removes
API-key/provider variables from child environments; Claude uses its account
login with explicit tools/no MCP or hooks, and Codex uses saved ChatGPT login
without user configuration. Existing account allowances still apply. Rate
limits, auth errors, tool failures and timeout stop the run without paid fallback.

One local worker lock prevents two copies on this machine. This is not a
distributed lock against independent phone/cloud agent sessions. Only one
explicitly approved pilot is allowlisted. Do not enable unattended general
polling without implementing and validating broader ownership and recovery.

For an active pilot, removing automation:approved or adding automation:paused
stops at the next phase boundary (not instantly in an active model call).
The pause additions are source/syntax checked; live pause/timeout drills remain
to be executed. The worker does not automatically run after boot.

## Phone connections

Codex standalone 0.161.0 remote daemon was started and reported connected to host
`gautam`. Use ChatGPT mobile Remote and select this host and the Woven workspace.
CLI pairing is available via the standalone package's remote-control pair
command if the host does not appear. Do not publish pairing codes.

Claude 2.1.293 is installed alongside the older global 2.1.20. Its Remote Control
server was started from Woven with worktree spawn mode, capacity 1, and no
pre-created in-place session. Open Claude mobile Code or claude.ai/code and
choose the Woven local environment. The existing older Claude session was not
replaced. New phone sessions do not automatically inherit that session's chat.

Keep the laptop/network and remote processes running. No public app/DB ports or
new hosted services were configured. Actual connection from the founder's phone
has not yet been verified. Record tasks/progress in GitHub regardless of device.

Official references:
- [Codex noninteractive runs](https://learn.chatgpt.com/docs/non-interactive-mode)
- [Codex phone guide](https://developers.openai.com/blog/mastering-codex-remote-for-engineering)
- [Claude programmatic runs](https://code.claude.com/docs/en/headless)
- [Claude Remote Control](https://code.claude.com/docs/en/remote-control)
