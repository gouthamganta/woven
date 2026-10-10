# Issue #139 — canonical pillar documentation

Branch: codex-phone/139-fix-canonical-pillars. Starting master: 3df9759.
Canonical pillars: Lifestyle, Energy, Values, Communication, Ambition, Stability,
Curiosity, Affection. Emotional Rhythm is a separate behavioral scoring component.

## Files corrected

- CLAUDE.md
- PRODUCT_STORY.md
- docs/business/glossary.md
- docs/features/commons/README.md
- docs/features/commons/feed-algorithm.md
- docs/systems/echo/embeddings.md
- docs/systems/seasons/README.md
- docs/systems/seasons/api.md
- docs/technical/AI_ML_DOCUMENTATION.md
- docs/technical/BACKEND_DESIGN.md
- docs/technical/SYSTEM_DESIGN.md
- docs/technical/TECHNICAL_DEBT_AND_IMPROVEMENTS.md

QA evidence and qa/MEMORY.md are also updated; all changes are Markdown.

## Source and verification

FoundationalQuestionBank.cs:23-25 defines the canonical list; :59-62 defines q6,
covering Ambition and Curiosity. Removed the false no-foundational-question gap
claims from CLAUDE and technical docs. The separate least-coverage note in
onboarding/foundational-questions.md remains; this does not close #123.

Used rg (grep equivalent) across CLAUDE.md, PRODUCT_STORY.md and all docs Markdown
for Emotional Rhythm/emotional_rhythm, pillar lists, Ambition and missing-question
claims. Kept behavioral scoring/embedding references. Executed canonical-list
assertions and q6 coverage assertion; stale full pillar-list scan passed.

- dotnet build in backend/WovenBackend: exit 0, 0 warnings/errors.
- npx ng build in frontend/woven-frontend: exit 0; existing landing-simple
  stylesheet budget warning (40.92 kB vs 20 kB).
- dotnet test in backend/WovenBackend.Tests: exit 0, 8/8 passed;
  existing MSB3277 EF Relational 10.0.4/10.0.12 warning remains.
- git diff --check: passed.

Temporary .NET SDK 10.0.401; reused frontend dependencies with no lockfile changes.
No application code, database, real-user data, paid APIs, merge or deploy changes.
Documentation scope review remains for laptop Codex.
