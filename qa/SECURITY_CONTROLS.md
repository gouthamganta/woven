# Security verification checkpoint

Scope: local candidate `5461c85`, model-created synthetic schema. This is an
evidence matrix, not a certification. Strong-security sign-off is withheld.
The [QA report](evidence/2026-10-08-full-qa.md) states environment and limitations.

| Control | Evidence state | Required next verification |
|---|---|---|
| API authentication | Tested anonymous denial on 10 core routes; invalid JWT variants denied | All routes, claim validation, account state, expiry boundaries |
| Authorization | Tested ordinary-user admin denial and unrelated-user match/chat/media denial | Enumerate ownership/admin checks, nested resources, websocket groups |
| Frontend session/origin controls | Candidate176cf74 passes51 unit/router cases and build; not deployed | Manual-auth services, refresh, full browser and server policy |
| Deleted-account enforcement | Failed proposed fail-closed token check | Define revocation/account state policy; test all authenticated surfaces |
| Development endpoints | Production local-login route absent | Enumerate all dev/admin helpers; production image/build checks |
| Cookie/token handling | Generated-cookie unit contracts pass; browser/transport not validated; frontend session regressions fail | CSRF, token expiry/storage/origin restrictions and refresh/revocation |
| CORS | Untrusted-origin GET omitted allow-origin | Trusted/preflight/credential cases and production configuration |
| Rate limits | Repeat export 429; parallel daily cap persisted at five | Per-user/IP partitioning, Retry-After, proxy trust, abuse cases |
| Correlation/logging | Generated ID passed; long supplied value echoed | Validate bounds/characters, prevent secret/PII logging, retention |
| Data deletion/export | Unmatched delete/export pass; matched delete fails after partial data removal (#165) | Atomicity/retention, matched/blocked cases, processor policy |
| Encrypted identity search | PostgreSQL email equality fails to find loaded synthetic user (#166) | Safe searchable identity/linkage, migration/backfill and concurrency |
| ChatNotes | Founder allows matched-pair visibility; unrelated user denied | Reconcile docs; prove exclusion from unrelated responses and logging |
| Media | Foreign-owner probe denied | File/type/size validation, malicious uploads, signed URL lifetime |
| Realtime | Anonymous negotiation 401; authenticated negotiation 200 | Delivery, group isolation, reconnect and expired/revoked credentials |
| Database integrity | Fresh migrations failed; model schema workaround only | Fresh/upgrade migration tests, constraints, rollback/recovery |
| Transaction integrity | Parallel cap passed; partial persistence previously observed | Fault injection, lost commit, idempotency and outbox/refund behavior |
| Readiness | 200 despite pending migrations in non-migrating mode | Fail-closed deploy readiness with actionable health details |
| Dependency maintenance | Frontend audit had critical/high package entries; EF warnings remain | Verify current fix branches, reachability, lockfiles and OS images |
| Encryption/key rotation | Encryption primitive round-trip/tamper/key/configuration regressions pass; field coverage and rotation unverified | At-rest field inventory, applied rotation and recovery tests |
| Transport/deployment | Loopback HTTP only | Production HTTPS/HSTS/headers/proxy configuration in scoped environment |

Use [OWASP ASVS](https://owasp.org/projects/asvs) as the verification framework
and the [REST Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html)
for implementation review. The matrix does not assert ASVS compliance or assign
a verification level; a complete control inventory and associated evidence are
still required. Do not document a control as enforced until code/configuration
and the relevant runtime failure cases have been verified.
