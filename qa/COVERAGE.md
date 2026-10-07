# Initial coverage map

Inventory only. All runtime coverage below is **not run**. Source locations are
starting points; Claude's expanding feature docs must supply current rules.

| Area | Documentation/source | Required evidence |
|---|---|---|
| Auth/session | Endpoints/AuthEndpoints.cs, DevAuthEndpoints.cs; docs/systems/auth | OAuth boundaries, cookie/Bearer behavior, expiry, sign-out, admin ownership, production dev-route absence |
| Onboarding | docs/features/onboarding | Required fields, adult eligibility, resume, validation, consent, vector bootstrap, cost-free degradation |
| Profile/preferences | docs/features/profile | Private vs public fields, updates, reciprocity, distance/age boundaries, deletion/export |
| Moments/Drawn | docs/features/moments | Daily deck, empty pools, response state, duplicate requests, daily reset, candidate exclusions |
| Sparks | docs/features/sparks | Spend/refund ledger integrity, zero funds, cap/reset, concurrency, retry/idempotency |
| Matches/Balloons | docs/features/matches | Both-user transitions, expiry/pop/unmatch/block, immutable closure, unauthorized access |
| Chat/trial/voice | docs/features/chats | Ordering, reconnect, duplicate sends, trial start/expiry/decisions, media access and playback signals |
| Commons | docs/features/commons | Feed visibility, orbit/dwell, pagination, moderation, tile expiry, abusive content, blocked users |
| Notifications | docs/features/notifications | Consent, preferences, local event generation, duplication, offline/error behavior; real push transport separate |
| Games/assistant | docs/features/games, docs/features/assistant | Authorization, malformed input, rate limits, prompt injection, missing-provider UX, response contract |
| ECHO | docs/systems/echo, Services/Matchmaking | Candidate constraints, scoring inputs, missing vectors, batch replay, safety signal separation, cohort effects |
| Embeddings | docs/systems/embeddings | Dimensions, provider failures, queue replay, missing modalities, mocked vs real semantic validity |
| Coaching/feedback | docs/systems/coaching, docs/systems/feedback | Delivery state, opt-out, visibility, ownership, eligibility, missing-provider behavior |
| Trust/moderation | docs/systems/trust, docs/systems/moderation | Block/report propagation, appeal/review paths if implemented, authorization, no public trust leakage |
| Storage/queue | docs/systems/media, docs/systems/queue | Upload permissions, content limits, retries, poison jobs, local emulator equivalence limits |
| Analytics/encryption | docs/systems/analytics, docs/systems/encryption | Event lineage, consent/retention, PII sanitation, field encryption, rotation/recovery |
| Operations/cache | docs/systems/caching; Program.cs; compose | Health/readiness, invalidation, restart, DB restore, correlation, worker fault recovery, zero-cost isolation |

For each executed case add a scenario ID, precise acceptance rule, fixture IDs,
test method, run artifact, actual result, defect/retest reference. Unit test counts
alone do not mark a feature covered.
