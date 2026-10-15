# Woven QA workspace

Shared entry point for the founder, Codex, and Claude. GitHub Issues/Projects are
the active task record; local files hold QA context and evidence.

GitHub migration: [GITHUB_WORKFLOW.md](GITHUB_WORKFLOW.md) describes the shared
Issue/Project workflow for laptop and phone sessions. `github-board.json` maps
task IDs to Issues and the Project. The Markdown board is the pre-migration
snapshot; new work and handoffs belong in GitHub once setup completes.

- [Introduction and handoff to Claude](CLAUDE_HANDOFF.md): roles, pause status,
  initial results, and space for acknowledgment.

- [Historical board](BOARD.md): pre-migration snapshot; do not update it.
- [Memory](MEMORY.md): persistent context and next steps.
- [Test strategy](STRATEGY.md): scope, evidence, and readiness gates.
- [Findings](FINDINGS.md): source-linked observations awaiting validation.
- [Documentation review](DOC_REVIEW.md): reading coverage and source discrepancies.
- [Latest local QA checkpoint](evidence/2026-10-08-full-qa.md): executed checks,
  synthetic import, candidate fixes and remaining coverage.
- [Security verification](SECURITY_CONTROLS.md): tested controls and open gaps.
- `fixtures/personas.json`: reproducible synthetic input; latest report records
  import into a model-created sandbox, not successful migration validation.
- `evidence/`: sanitized run results. Private local output belongs in `.local/`.

Run `node qa/scripts/generate-personas.mjs` to generate 100 personas with no
network calls or dependencies. Run `node qa/scripts/verify-personas.mjs` to
validate the fixture. An application-aware import must be implemented and
validated before claiming these users exist in a newly created environment.
`seed/` now supplies a guarded importer; its latest isolated runtime import is
documented in the checkpoint above. It does not validate onboarding or AI quality.

Claude: read your GitHub queue, acknowledge the Issue with session/branch, and
post change references and evidence when handing it back. Codex retests before
closing it. The local synchronizer mirrors labels into Projects; it does not
automatically launch another AI session. See GITHUB_WORKFLOW.md.

`docker compose -f qa/compose.yaml up -d` starts only isolated QA infrastructure
once Docker is ready. It uses separate volumes and an internal container network.
Infrastructure is accessed with `docker compose exec` or from an eventual QA
backend on that network. No database/cache/blob ports are exposed to the host.
It does not start the app or import fixtures. Do not use the root compose file for
the QA sandbox without auditing its inherited secrets and integrations.

Local account access is pending sandbox startup and validated seed import. The
existing Development-only `POST /dev/login/{userId}` issues an access token and
cookie. Do not expose Development mode or its admin-token endpoint publicly.
