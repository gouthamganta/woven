# AI in Woven's Development

How Claude Code (Anthropic's CLI agent) was used throughout the development of Woven. This document is evidence-based: it describes what actually happened, not what could theoretically happen with AI-assisted development.

---

## Architecture Design

The ECHO pipeline architecture was designed with AI assistance. The core problems that required architectural thinking:

- **Signal inventory**: deciding which behavioral events to record and which to ignore. The final signal set (BalloonPopped, TrialRequested, TrialAccepted, ConversationDepth, DateAccepted, ExplicitFeedback, LoveReactions, TimeToFirstMessageMs) was arrived at through AI-assisted analysis of what signals are observable without user-initiated self-report.
- **Composite outcome score**: the ConnectionScore formula (7-signal weighted sum with specific weights) was designed through iterative discussion of what each signal represents and how to prevent any single signal from dominating the label.
- **Weight learning approach**: the choice of logistic regression (mini-batch gradient ascent) over a neural approach was an explicit architectural decision. LogReg is interpretable — the per-user weight vector can be audited. Neural approaches would be opaque. This mattered for trust and debugging.
- **LogReg vs. neural**: for a cold-start problem with sparse labels per user (minimum 10 samples before the model is trained), logistic regression generalizes better. A neural model would overfit. AI assistance was used to reason through this trade-off.
- **LinUCB bandit integration**: the exploration/exploitation structure in deck assembly uses a LinUCB contextual bandit. This was designed with AI assistance to ensure the bandit's exploration budget doesn't dominate the deck at the expense of quality.

---

## Feature Implementation

Specific features implemented with Claude Code assistance:

**BehavioralFingerprintService**: a 16-dimensional behavioral fingerprint vector computed per user. Each dimension corresponds to a behavioral trait derived from signal history. AI was used to design the dimension encoding scheme and verify the normalization approach.

**WeightLearningService**: mini-batch logistic regression weight learner. The gradient ascent loop, the MinSamples threshold logic (10 qualifying ConnectionScores, MinConnectionScore=0.08), and the regularization term were all implemented with AI assistance. AI reviewed the gradient update math before the code was written.

**Trial period mechanics**: the 3-minute commitment window (TrialUserAOpenedAt / TrialUserBOpenedAt tracking, TrialEndsAt = now + 3 minutes when both are non-null) was implemented with AI assistance, including the edge cases: what happens if one user closes the app, what happens if BLOCK is chosen, ghost refund wiring on all 3 unmatch close paths.

**Find Love flow**: the `MatchExplanationService` date idea generation, tone-adaptive output (tone bias reader per viewer), and the 3-idea structure were implemented with AI assistance. The prompt injection protection in `AiProfileService` was an AI-flagged concern during code review.

**LinUCB bandit in DailyDeckOrchestrator**: the full deck assembly pipeline including candidate pool retrieval, embedding cosine similarity scoring, 16-component match scoring, delivery boost, and LinUCB arm selection. AI was used to verify that the bandit's reward signal is correctly wired to ConnectionScore updates.

---

## Code Review

AI code review was applied to critical paths where correctness matters most:

**MatchScoringService**: review of the 16-component weight array for internal consistency. Verified that weights are normalized correctly and that no component has an arithmetic error in its contribution formula.

**Encryption**: AES-256-GCM implementation on PII fields. AI reviewed the key derivation, IV generation, and authentication tag handling. AES-GCM authentication tags must be verified before decryption — this is a correctness requirement, not an optimization.

**Signal recording**: `IMatchSignalService.RecordAsync(...)` call sites reviewed for completeness. Missing a signal recording call means a behavioral event never reaches the ConnectionScore labels — a silent model quality bug. AI review checked every behavioral event handler for the `RecordAsync` call.

**ConnectionScore formula**: verified that the 7-signal weights sum to 1.0 and that no signal double-counts a single behavioral event.

---

## Documentation

This entire documentation suite — 35+ files covering technical architecture, AI/ML systems, signals, database design, API, frontend, product, investor materials, contributing guides, founder strategy, and meta-documentation — was written in a single session using Claude Code.

The workflow:
1. Codebase audit: 553 source files inventoried to understand what exists
2. Key file reads: Program.cs, appsettings.json, AiProfileService.cs, MatchScoringService.cs, KnowMeAgent.cs, MatchExplanationService.cs, DeliveryBoostService.cs, infra/main.tf, and others read in full
3. Fact extraction: specific values (weights, thresholds, service names, batch schedules) extracted from source into structured tables
4. Parallel agent writing: multiple Claude Code agents write different file groups simultaneously, each receiving the relevant extracted facts
5. Evidence grounding: each agent writes only from extracted facts — no agent invents a claim

Total time for ~35 documentation files: a single session, not weeks of technical writing.

---

## The Workflow: Read → Extract → Write

The AI development workflow that prevents hallucination:

**Read source files first.** Before asking Claude Code to write documentation, change architecture, or review logic, the relevant source file must be read. Claude Code operates on actual code contents, not assumptions.

**Extract facts explicitly.** When passing context to a writing agent, the extracted facts are quoted verbatim: exact weights, exact thresholds, exact batch schedules, exact service names. The agent writes from those facts, not from general knowledge about dating apps.

**Write evidence-only output.** Every claim in the documentation must be traceable to a source file, a config value, a migration, or a Terraform resource. Aspirational claims, generic best practices, and invented metrics are explicitly excluded.

This workflow is documented in `CLAUDE.md` (the project instruction file) as a pattern to preserve across sessions.

---

## What AI Was NOT Used For

**CSS design tokens**: the UI consistency mandate (all values from CSS variable tokens in `styles.scss`, no raw hex, no raw px values outside the token system) was enforced by design rules, not AI generation. Raw hex values and pixel values are prohibited regardless of AI suggestions.

**Generating test data**: no seed tooling exists. AI was not used to create fake user data for testing. The system is tested against the real pipeline.

**Business decisions**: the monetization model (spark economy, no hard paywalls, premium tier deferred), the design rules (no hover translateY lifts, no age on Moments cards, no community ratings shown to users), and the product philosophy (invisible AI, curated vs. infinite scroll, commitment mechanics) are founder decisions, not AI recommendations. AI implements those decisions; it does not make them.

**Making trade-offs without clarity**: when a technical decision had non-obvious implications (e.g., LogReg vs. neural for weight learning, localStorage vs. httpOnly cookie for JWT), AI assistance surfaced the trade-offs and the founder made the decision.

---

## The CLAUDE.md Pattern

`CLAUDE.md` at the project root is the AI agent's project instruction file. It is loaded automatically at the start of every Claude Code session. It contains:

- **Architecture overview**: where each concern lives in the codebase, what patterns to follow
- **Feature vocabulary**: the exact terms to use (Deck, not "feed"; Magical, not "like"; Resonant, not "match"; Balloon, not "connection window"). Consistency in naming prevents confusion across sessions and across engineers.
- **Hard design rules**: the list of things that are never done, regardless of how reasonable they might look in isolation (no hover translateY, no age on cards, no ratings shown to users, background drift stays on)
- **Signal pipeline docs**: the full signal type table, the batch worker schedule, the scoring weights
- **Known gaps**: what is missing and why, with a clear fix path
- **Documentation mission progress log**: which source files have been read, which docs have been written, where to pick up next session

Without CLAUDE.md, every new AI session starts from scratch. With it, context about architecture decisions, naming conventions, and design rules is immediately available without re-explanation.

This is not a Claude Code feature. It is a pattern: maintain a single project instruction file that an AI agent can read at session start.

---

## The Memory System

Claude Code maintains persistent memory files at:
`C:\Users\gauta\.claude\projects\c--Users-gauta-Desktop-Woven\memory\`

Current memory files:
- `user_profile.md`: developer context, design instincts, working style
- `feedback_rules.md`: the hard design rules that recur across sessions
- `project_state.md`: what's built, what's pending, key architectural decisions, priority order
- `commons_architecture.md`: Commons feed signal pipeline, orbit feature, what's built vs. missing
- `my_tiles_spec.md`: My Tiles page spec (locked design)
- `project_cta_gate.md`: CTA gate option 3 (preference-based deck targeting for users with CTA backlog >20)
- `matching_engine_plan.md`: signal inventory, composite outcome score, weight learning redesign, build order
- `matching_engine_deep_dive.md`: full problem statement, signal anatomy, scoring formulas, fundamental flaws

Memory files capture decisions and context that don't belong in the codebase (they're not code) but need to survive across sessions (they can't live only in a conversation). They are the mechanism for long-term AI-assisted development continuity.

---

## The Parallel Agent Pattern

For large tasks — like writing 35+ documentation files in a single session — Claude Code supports spawning multiple agents in parallel, each receiving a specific subset of the task.

The pattern:
1. Extract all facts needed across all documents first (single sequential step)
2. Divide documents into batches by theme (technical, AI/ML, product, founder, etc.)
3. Spawn one agent per batch, each receiving the relevant facts for its batch
4. Agents run simultaneously — each writing independently with no dependencies on each other
5. Results arrive in parallel; each file is complete when its agent finishes

The speedup is real: 3-5 agents writing simultaneously is 3-5x faster than sequential writing. For a documentation sprint covering dozens of files, this compresses what would be a multi-day writing project into a single session.

The constraint: each agent receives a fixed fact table. Agents do not read source code — they write from the extracted facts. This prevents context overflow (each agent's context window stays manageable) and prevents hallucination (agents cannot invent facts they were not given).

---

## Lesson Learned: Evidence Grounding Prevents Hallucination

The single most important lesson from AI-assisted development on Woven:

AI works best when given exact facts — code contents, specific weights, exact thresholds, actual SQL constraints — rather than high-level descriptions.

"The matching engine uses machine learning" → the AI will invent plausible-sounding details.

"MatchScoringService has 16 components; the weight for TrialAccepted is 0.25; WeightLearningBatchWorker runs Sunday 04:00 UTC; MinSamples=10; MinConnectionScore=0.08" → the AI writes accurate documentation.

The discipline required: read the source file before writing about it. Never write about a system you haven't read. Quote specific values, not categories of values. The extra 2 minutes of reading prevents hours of correction.
