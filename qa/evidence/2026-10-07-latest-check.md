# Latest documentation checkpoint check

HEAD: `74ce4fb7b6bd489f0d579380a14a8165f926954a`.

Changes since 7360cd5 affect only CLAUDE.md, docs/COMPLETION_PLAN.md and
qa/BOARD.md. Completion tracking has been updated; the earlier source-backed
findings are not repaired by these commits.

Structural scan (`node qa/scripts/audit-docs.mjs`): 208 active Markdown files,
14 API files, 46 missing local-link occurrences, no missing main-index targets.
Details are in docs-structure.json. Assistant omission may be intentional but
the completion tracker still claims 15 API files.

Source spot checks still find:
- InteractionEndpoints.cs:73 calls DeliveredAt.HasValue while CoachingSummary.cs:11
  declares non-nullable DateTimeOffset.
- business/rules.md states 72h balloons, 60-candidate decks, manual-only refill,
  trust threshold 0.5, and 7-day JWT expiry; the discrepancies in DOC_REVIEW.md
  remain.
- security/README.md:84 still claims a DEBUG compile guard absent in checked code.

Backend test executed:
`dotnet test backend/WovenBackend.Tests/WovenBackend.Tests.csproj --no-restore --logger "trx;LogFileName=latest-retest.trx" --results-directory qa/.local/test-results`.
Exit 1: same CS1061 at InteractionEndpoints.cs(73,88). No backend unit tests
executed. NU1902/NU1903 MessagePack warnings persist. CL-003 remains open.
