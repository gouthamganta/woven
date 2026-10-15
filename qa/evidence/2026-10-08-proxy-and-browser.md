# Local proxy repair and real-browser check — 2026-10-08

Claude-authored proxy `c6b090c`, client `176cf74`, backend `5461c85`.
[Stacked draft #168](https://github.com/gouthamganta/woven/pull/168) changes one
nginx template over client #167. Codex independently tested it in the isolated
loopback frontend. Original application containers and master were not changed.

The **same 16 contracts** improved from **3 passed / 13 failed** on the original
proxy to **16/16 passed** on the repaired configuration. `nginx -t` passed.
These cover real JSON API routes, overlapping document routes, inherited shell
security headers, anonymous denial and signed SignalR negotiation. A synthetic
query/referrer marker was absent from actual nginx access logs while method/path
logging remained; no real token was used for the redaction probe.

Actual mobile Chrome, external requests blocked:

- Anonymous `/moments` navigation reached `/login`, no page errors.
- Signed synthetic account reached `/moments`; actual backend API returned200
  for Moments, dynamic intake, pending tasks and feedback prompt,204 for coaching.
  No intercepted backend fixtures were used in these browser checks.
- `/hubs/woven` websocket was observed with successful SignalR handshake and no
  socket errors. This is not message-delivery, recipient-isolation or reconnect
  verification.

[Sanitized results and raw hashes](2026-10-08-proxy-and-browser.json) and
[query-log check](2026-10-08-proxy-log-check.json). Browser screenshots and the
one-hour ordinary-user launcher stay Git-ignored. The launcher at
`http://127.0.0.1:5180/qa-access.html` uses a real locally signed sandbox JWT;
it does not validate Google sign-in. Regenerate with
`node qa/scripts/create-local-access.mjs <workspace>` after expiry; never publish
the generated credential page or expose this sandbox externally.

QA Compose can select explicit frontend artifact/template paths through private
environment files. Its current `proxy.env` selects the auth build and candidate
template; this is a reversible local-only runtime update. No paid API fallback,
database reset, cloud deployment or main merge ran.

Limits: the database is still **model-created**, not migration-verified; actual
Google/provider, HTTPS/certificate verification, upload body sizes, full browser
journeys and all security controls remain unverified. Existing SSL verification
and upload-size settings were preserved, not certified. Parent migration, quota
atomicity, partial deletion, identity lookup and dependency/CI blockers remain.
