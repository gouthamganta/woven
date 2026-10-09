# Documentation review

Checkpoint: `7360cd5d354d71b661b7d57036049e5d2eed7d28`.
Founder reported documentation complete; QA resumed. This is the first review
batch, not a claim that every document has been read.

## Structural scan

Run `node qa/scripts/audit-docs.mjs` to refresh
`qa/evidence/docs-structure.json`. At this checkpoint:

- 208 Markdown files across the seven active documentation areas.
- 72 feature, 92 system, 8 architecture, 14 API, 10 development, 5 business,
  and 7 security files.
- All main-index local targets resolve.
- 46 missing local-link occurrences inside active documentation.
- Scanner excludes fenced examples and does not check anchors, external URLs,
  source-line validity, or semantic accuracy. Findings require human triage.
- API plan permits skipping assistant if no endpoint exists. No Assistant-named
  endpoint file or assistant route registration found in the scoped endpoint/
  Program searches. Therefore 14 files is a plan discrepancy to explain, not
  proof an API reference is missing. Do not fabricate an endpoint.

## Reading coverage

The inventory records hashes and starts all files as `not reviewed`. File-byte
scanning is not content review. The explicit statuses below are authoritative
for this first batch.

| Document | Review extent |
|---|---|
| docs/INDEX.md | Read in previous structure review; entry point checked again |
| docs/COMPLETION_PLAN.md | Read in previous structure review; stale tracker confirmed |
| docs/business/product-philosophy.md | Read in full; implementation claims require more verification |
| docs/business/rules.md | Partially reviewed in multiple sections; source discrepancies confirmed |
| docs/business/state-machines.md | Partial; match/balloon and Spark sections reviewed |
| docs/QUICKSTART.md | Read in full; setup not executed |
| docs/api/README.md | Read in full; error-format claim checked against source |
| docs/api/error-handling.md | First 100 lines reviewed; handler/source comparison started |
| docs/development/testing.md | Read in full; previous test results remain separate |
| docs/security/README.md | Partial; existing security assertions need validation |
| docs/security/authentication.md | First 100 lines reviewed; expiry claims conflict with other docs |
| docs/features/onboarding/README.md | Read in full; 5-question flow conflicts with business rules claiming 8 |
| docs/features/moments/README.md | Read in full; 5 candidates / 36h matches code constants |
| docs/features/sparks/README.md | Read in full; lazy refill matches service, initial balance needs runtime check |
| docs/features/chats/README.md | Read in full; opening ChatNote visibility contradicts hard privacy rule |

## Verified source discrepancies

1. `docs/business/rules.md:60` says balloon lifetime 72h; MomentsRules.cs:7
   sets 36h. Deck rule says 60 candidates; DeckSelectionService selects at most
   5. Moments README agrees with code.
2. `docs/business/rules.md:140` says daily refill unimplemented/manual only;
   SparkWalletService.cs:21 and :90 perform lazy earning on balance lookup.
3. `docs/business/rules.md:233` claims trust threshold 0.5;
   CandidatePoolService.cs:12 defines 0.25 and applies it around :103.
4. `docs/business/state-machines.md:351` says wallet is integer 0–10;
   SparkWallet.cs:12 stores integer tenths (50 = 5 sparks; max 100 in service).
5. JWT expiry is documented as 7 days (business), 30 days (security auth), and
   60 minutes (security overview). JwtTokenService.cs:23 reads configured
   ExpiryMinutes with fallback 60; CookieAuthHelper.cs:17 defaults separately to
   60 minutes. Effective local/production settings need independent validation.
6. Error docs promise every error body contains correlation/timestamp.
   ChatEndpoints.cs:159 returns `{ error = "MATCH_NOT_FOUND" }` directly;
   DomainExceptionHandler returns error/code/correlation without timestamp.
   The correlation header and exception-handler body are separate contracts.
7. Security README still claims DEBUG guard; checked source only guards by
   environment, as recorded in the previous handoff retest.

## Next review batches

Finish business rules/state diagrams and security overviews; then read each
feature's full frontend/backend/API details. Follow with ECHO/queue/integrations,
data lifecycle and operations. Attach source checks and acceptance scenarios to
the coverage map. Do not assume the folder's existence proves its claims.
