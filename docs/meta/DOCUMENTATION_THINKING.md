# Documentation Thinking

How this documentation suite was built, why it was built this way, and how to maintain it. This file is meta-documentation: it documents the documentation.

---

## Philosophy

One rule governs everything in this suite:

**Every claim must be traceable to source code, a config file, a migration, or a Terraform resource.**

Not to memory. Not to a design doc. Not to what the feature is supposed to do. To what is actually in the code right now.

Consequences of this rule:

- No aspirational claims ("Woven will become the leading behavioral matching platform") — not in technical docs, not in product docs, not in investor docs
- No generic boilerplate ("Industry-standard encryption protects user data") — the actual claim is "AES-256-GCM encryption on PII fields at the application layer, as implemented in the entity layer"
- No invented metrics ("Users typically exchange 12 messages") — if there is no data, write "[To be measured in beta]"
- No hedged speculation ("The matching algorithm likely uses cosine similarity") — read the file, write the fact: "embedding cosine similarity computed in DailyDeckOrchestrator"

This philosophy exists because documentation that contains a mix of facts and inventions is worse than documentation with only facts. Readers cannot tell which claims are verified. They either trust everything (dangerous) or trust nothing (useless).

---

## How This Suite Was Built

### The session structure

The entire suite — 35+ files covering technical architecture, AI/ML systems, signals, database design, API reference, frontend design, product features, investor materials, contributing guides, founder strategy, and this meta file — was written in a single Claude Code session in chronological order:

1. **Codebase audit**: 553 source files inventoried. File names, directory structure, service names, entity names, endpoint patterns. No deep reading — just mapping what exists.
2. **Key file reads**: Program.cs (full DI container and middleware pipeline), appsettings.json (all config keys, model name, batch schedules, ECHO weights), AiProfileService.cs, MatchScoringService.cs, KnowMeAgent.cs, MatchExplanationService.cs, DeliveryBoostService.cs, infra/main.tf, and others.
3. **Fact extraction**: specific values (weights, thresholds, service names, batch worker cron schedules, Azure resource tiers) extracted from source into a structured fact table.
4. **Parallel agent writing**: multiple Claude Code agents writing different file groups simultaneously, each receiving the relevant subset of extracted facts.
5. **Evidence grounding check**: each agent writes only from its received fact table — it does not read source code. This prevents context overflow and prevents the agent from inventing facts it was not given.

### What the parallel agent strategy looks like

Batch A (spawned simultaneously):
- Agent 1: BACKEND_DESIGN.md + ARCHITECTURE.md + SYSTEM_DESIGN.md
- Agent 2: AI_INTELLIGENCE_DEEP_DIVE.md + SIGNALS_VECTORS_SCORING.md + AI_ML_DOCUMENTATION.md

Batch B (after Batch A, spawned simultaneously):
- Agent 3: DATABASE_DESIGN.md + API_DOCUMENTATION.md
- Agent 4: FRONTEND_DESIGN.md + COMPONENTS_PAGES_TEMPLATES.md

...and so on through Batches C, D, E.

Each agent receives the relevant facts for its batch — not the entire fact table. Context focus prevents agents from wandering into topics outside their batch.

The speedup is 3-5x vs. sequential writing for large suites. For 35 files, this compresses what would be a multi-day writing project into a single session.

---

## The Evidence Extraction Workflow

The workflow that makes parallel agents reliable:

1. Read a source file (e.g., `MatchScoringService.cs`)
2. Extract the specific facts that will appear in documentation:
   - Service name: `MatchScoringService`
   - Number of scoring components: 16
   - Component names and weights: [list verbatim from source]
   - Called by: `DailyDeckOrchestrator`
   - Weight learning input: `WeightLearningService` reads personalized weights from this service's weight array
3. Pass this extracted fact table to the writing agent
4. The writing agent never reads the source file — it writes from the fact table

This indirection is the key. Without it, a writing agent will:
- Run out of context trying to read all relevant source files
- Invent plausible-sounding details when source files don't cover every question
- Mix verified facts with inferences without marking which is which

With the fact table pattern, the writing agent cannot invent what it was not given. If a fact is missing from the table, the agent writes "[not in source]" or omits the claim — it does not fabricate.

---

## Documentation Decay

These docs will drift from the code over time. This is inevitable. Mitigations:

**CLAUDE.md progress log**: the documentation mission progress log at the bottom of `CLAUDE.md` tracks which source files have been read and which doc files have been written. When a doc becomes stale (because the code it describes has changed), the log provides the source files to re-read before updating the doc.

**Memory files**: the `matching_engine_deep_dive.md` and `matching_engine_plan.md` memory files capture architectural decisions that predate the documentation sprint. When the matching engine changes, both the memory files and the documentation need updating.

**Batch worker schedule table**: the `Program.cs` scheduler section is the authoritative source for batch worker schedules. The schedule tables in documentation (particularly `BACKEND_DESIGN.md` and `SIGNALS_VECTORS_SCORING.md`) should be re-verified against `Program.cs` on every significant feature addition.

**Weight table in SIGNALS_VECTORS_SCORING.md**: the ConnectionScore formula weights (BalloonPopped=0.05, TrialRequested=0.10, TrialAccepted=0.25, ConversationDepth=0.20, DateAccepted=0.15, ExplicitFeedback=0.15, LoveReactions=0.10) come from `ConnectionScoreBatchWorker`. If these weights change, the documentation is wrong.

**The decay priority**: not all docs decay at the same rate. Terraform infrastructure docs (`CLOUD_INFRASTRUCTURE.md`) decay slowly — infrastructure changes are infrequent. Signal type tables decay quickly — new features add new signals. Prioritize re-verification of signal and scoring docs after each feature release.

---

## What Is Deliberately NOT Documented

**Raw code patterns**: the code is the documentation for code patterns. Documenting "here is how to write a for loop" or "here is how EF Core works" is noise. The docs in this suite document Woven-specific patterns: the MapXxxEndpoints pattern, the GetUserId helper, the MomentsRules.NowUtc() convention, the RequireAuthorization requirement.

**Git history**: use `git log` and `git blame`. Documentation should not be a changelog.

**Debugging solutions**: when a bug is found and fixed, the fix is in the code. The docs do not need to record "we found a bug where X and fixed it by doing Y." The code is the fix.

**Ephemeral task details**: within-session task tracking (what step we're on, what's next) belongs in `TodoWrite`, not in documentation files. TodoWrite is session-scoped. Documentation is persistent.

**Aspirational roadmap**: product roadmap items that have not been scoped belong in a product planning tool, not in technical documentation. The Known Gaps table in CLAUDE.md and in technical docs represents what is missing from what has been built — not what will be built someday.

---

## The CLAUDE.md as Living Document

`CLAUDE.md` is not just a one-time setup file. It is the primary mechanism for maintaining AI-assisted development continuity across sessions.

Update CLAUDE.md when:
- The architecture changes (new service added, new endpoint pattern, new entity)
- The feature vocabulary changes (new feature name introduced)
- A new hard design rule is established
- A new signal type is added to the ECHO pipeline
- A batch worker schedule changes
- A new known gap is identified or an existing gap is closed
- A documentation sprint is completed (update the progress log)

The documentation mission progress log at the bottom of CLAUDE.md is particularly important: it records which source files have been fully read in previous sessions. This prevents re-reading files unnecessarily and tells a new agent where to start in a continuation session.

---

## When to Re-Run Documentation

Trigger a documentation update pass (not a full re-write, but a targeted update) when:

**New signal type added**: update the signal type table in SIGNALS_VECTORS_SCORING.md and the EventType table in CLAUDE.md.

**New scoring component added to MatchScoringService**: update the 16-component table in AI_ML_DOCUMENTATION.md and the ConnectionScore weight table in FORECASTS.md and FUNDRAISING.md.

**Infrastructure changes (Terraform)**: update CLOUD_INFRASTRUCTURE.md. If new Azure services are added, update the architecture diagrams in ARCHITECTURE.md.

**New game or AI feature**: update AI_INTELLIGENCE_DEEP_DIVE.md and the feature vocabulary in GLOSSARY.md.

**Batch worker schedule changes**: update the schedule table in BACKEND_DESIGN.md and re-verify against Program.cs.

**Known gap closed**: remove from the Known Gaps table in CLAUDE.md and in relevant technical docs. Update FUNDRAISING.md if it was listed as a gap to be honest about.

Full re-write is only needed if the core architecture changes (e.g., if ECHO is replaced by a different ML approach, if the DB schema is significantly restructured, if the infrastructure moves off Azure).

---

## File Organization Rationale

The folder structure reflects who reads each document:

| Folder | Primary reader | Content scope |
|---|---|---|
| `technical/` | Implementors, engineers | Architecture, API, database, frontend, system design, DevOps, security |
| `ai_intelligence/` | Engineers + AI-curious stakeholders | ECHO pipeline, embeddings, signal scoring, AI prompts |
| `signals/` | Engineers building on ECHO | Signal anatomy, vector modalities, scoring formulas |
| `product/` | PMs, designers, stakeholders | Feature specs, user flows, mechanics, business rules |
| `investor/` | Investors, advisors | Pitch narrative, competitive positioning, technical credibility |
| `contributing/` | New engineers | Onboarding, coding standards, PR process, local setup |
| `founder/` | Founder | Fundraising strategy, financial framework, AI development workflow, Claude Code setup |
| `flowcharts/` | Visual thinkers | State machines, data flow diagrams, sequence diagrams |
| `research/` | Design and product | Design rationale, UX decisions, behavioral psychology grounding |
| `meta/` | This file | Documentation philosophy, maintenance guide |

The separation of `investor/` from `founder/` is intentional: investor docs are written to be shared with external parties. Founder docs contain internal strategy, honest gap assessments, and development workflow details that are not for external distribution.
