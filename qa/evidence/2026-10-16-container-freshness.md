# Local container freshness audit — 2026-10-16

Read-only inspection using docker ps/inspect/image inspect and local artifact checks. Source HEAD: 0f5dc4b; contributor has an uncommitted AfterthoughtIntegrationService edit, preserved.

| Container | State | Application artifact freshness |
| --- | --- | --- |
| woven-frontend (port 80) | running, unhealthy | Image created 2026-05-16 19:11 UTC; no source/artifact mounts. Stale. |
| woven-backend (port 5135) | running, unhealthy | Image created 2026-05-16 19:10 UTC; no source/artifact mounts. Stale. |
| woven-qa-frontend-1 (5180) | stopped | Mounts coverage-run/frontend-auth-build/browser; index.html absent at inspected host path. Not a current usable build. |
| woven-qa-backend-1 | stopped | SDK base image, mounted full-qa-runtime/backend DLL modified 2026-10-08. Stale application artifact. |

The running frontend contains main-D7L6MTTD.js. Backend health check fails because curl is absent; frontend health check reports connection refusal. These health checks alone do not establish application endpoint availability. Infrastructure images (PostgreSQL/Redis/Azurite) contain no Woven application source and are not evidence of app revision.

Conclusion: the Docker-served application does not represent current October source. Latest source tests remain separate evidence. No rebuild/restart, database mutation, application edit, deployment or paid call performed in this audit. Rebuild and recreate application containers from an identified source revision before browser acceptance testing; preserve current contributor edits.
