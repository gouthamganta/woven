# Woven QA workspace

Shared entry point for the founder, Codex, and Claude. No hosted board required.

- [Introduction and handoff to Claude](CLAUDE_HANDOFF.md): roles, pause status,
  initial results, and space for acknowledgment.

- [Board](BOARD.md): owner, status, acceptance criteria, and handoffs.
- [Memory](MEMORY.md): persistent context and next steps.
- [Test strategy](STRATEGY.md): scope, evidence, and readiness gates.
- [Findings](FINDINGS.md): source-linked observations awaiting validation.
- `fixtures/personas.json`: reproducible synthetic input, not database seed proof.
- `evidence/`: sanitized run results. Private local output belongs in `.local/`.

Run `node qa/scripts/generate-personas.mjs` to generate 100 personas with no
network calls or dependencies. Run `node qa/scripts/verify-personas.mjs` to
validate the fixture. An application-aware import must be implemented and
validated before claiming these users exist in Woven.

Claude: read the board, acknowledge a task by changing its status, and add your
change reference when handing it back. Codex retests before closing it. This
folder is a shared filesystem handoff; it does not automatically notify a running
Claude session. The founder can direct Claude to `qa/README.md` once.

`docker compose -f qa/compose.yaml up -d` starts only isolated QA infrastructure
once Docker is ready. It uses separate volumes and an internal container network.
Infrastructure is accessed with `docker compose exec` or from an eventual QA
backend on that network. No database/cache/blob ports are exposed to the host.
It does not start the app or import fixtures. Do not use the root compose file for
the QA sandbox without auditing its inherited secrets and integrations.

Local account access is pending sandbox startup and validated seed import. The
existing Development-only `POST /dev/login/{userId}` issues an access token and
cookie. Do not expose Development mode or its admin-token endpoint publicly.
