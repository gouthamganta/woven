# DOC-005 and DOC-006 retest

Checked HEAD: 560525aac5c579ece1d6682ded3cd962310c4a2b.
Working tree contains additional uncommitted documentation and QA changes;
the structural scan includes those files. This is a working-tree review, not
an immutable release snapshot.

## DOC-005 — changes requested

`node qa/scripts/audit-docs.mjs`: 225 active Markdown files, 9 missing local
link occurrences, 0 missing main-index targets. The earlier 46-count baseline
has improved, but a zero-broken-link claim is not verified. Some new linked
documents contain references to source files that do not exist.

Remaining occurrences (document line → target):

- docs/features/commons/api.md:48 → ../../../backend/WovenBackend/Endpoints/CommonsTilesEndpoints.cs
- docs/features/commons/api.md:87 → ../../../backend/WovenBackend/Services/Commons/OrbitService.cs
- docs/features/matches/trial-period.md:43 → ../../../backend/WovenBackend/Endpoints/TrialDecisionEndpoints.cs
- docs/systems/auth/README.md:176 → ../../backend/WovenBackend/SECRETS_SETUP.md
- docs/systems/caching/redis.md:576 → ../../backend/WovenBackend/SECRETS_SETUP.md
- docs/systems/embeddings/clip-embeddings.md:31 → ../../../backend/WovenBackend/Services/Trust/PhotoVerificationService.cs
- docs/systems/matchmaking/candidate-pool.md:4 and :61 → ./deck-selection.md
- docs/systems/seasons/configuration.md:406 → ../background-workers/README.md

Scanner scope: local link-target existence outside fenced examples; no anchor,
external link or implementation-accuracy guarantee. Details in docs-structure.json.
New stub existence is not itself evidence of correct contents.

## DOC-006 — changes requested

Confirmed corrected passages: deck rule now says 5, trust rule says 0.25, lazy
Spark earning is described, wallet tenths are documented, business JWT rule is
configurable with default 60 minutes, handler-specific body examples added, and
security overview's dev-endpoint claim describes runtime guards.

Remaining source-backed contradictions:

- docs/business/rules.md:54 still says balloon window is 72 hours, while its
  example at :58 uses AddHours(36). MomentsRules.cs:7 defines 36h.
- docs/business/state-machines.md:28 and :88 still show 72h.
- docs/security/authentication.md:60 still hardcodes 30 days / 43200, while
  JwtTokenService.cs:23 reads configured ExpiryMinutes with fallback 60. The
  security doc also describes 30-day delivery in its authentication sequence.
  Describe actual configuration-dependent behavior consistently, rather than
  replacing it with another universal expiry claim.
- docs/api/error-handling.md:9 still promises consistent correlation-bearing
  JSON across all errors, despite the corrected section below documenting
  endpoint-specific bodies containing only error. Fix the overview as well.

Both GitHub Issues remain open and move to Changes requested / owner Claude.
Existing backend compilation repair CL-003 remains verified; no code regression
test is needed for these documentation-only commits.
