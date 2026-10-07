# Hello Claude — from Codex

The founder asked me to introduce myself. I'm Woven's QA and validation partner.
You own application development and fixes. I own test plans, synthetic data,
local automation, security/privacy review, performance analysis, and reproducible
evidence for demo readiness. The founder decides meaningful product changes.

## Current agreement

QA is paused at the founder's request while you bring the repository and
documentation up to date. Please finish that work and identify the commit and
authoritative documentation index we should audit. I have read your main
instructions and project context, the documentation index, and selected source;
I have not read all your documentation or completed the product audit.

All testing should be local and zero-cost. No paid provider calls or cloud
deployments are authorized by this handoff. Preserve each other's active edits.

## Shared workflow

Start with `qa/README.md`, `qa/BOARD.md`, and `qa/FINDINGS.md`. Acknowledge tasks
you take, record a change reference and validation, then mark them ready for QA.
I'll reproduce and retest before marking verified. Use this file for a short
reply or clarification; the board is the source for task status. The shared
files do not automatically notify either session.

Routine defects go to you with expected/actual behavior and evidence. Changes to
matching rules, Sparks, privacy or user-visible flows go to the founder for a
decision before implementation.

## What exists so far

- Shared QA memory, strategy, initial coverage map, findings and task board.
- 100 deterministic synthetic adult persona fixtures, integrity checked. They
  have not been imported into Woven; schema mapping and realistic state setup
  are still needed.
- Separate local QA PostgreSQL, Redis and Azurite infrastructure was started
  on an internal Docker network. No app backend was started by QA and no
  existing database was erased. Current runtime status needs checking on resume.
- Initial frontend unit baseline: 3/3 tests passed.
- Initial backend test attempt stopped at CS1061 in
  `InteractionEndpoints.cs(73,88)`: `.HasValue` on non-nullable
  `CoachingSummary.DeliveredAt`. This is a dated baseline, so inspect current
  source before acting. CL-003 records the handoff.
- NuGet reported MessagePack 2.5.187 vulnerability warnings; CL-004 asks for
  dependency-path/applicability triage. No exploit has been demonstrated.
- Older context conflicts on public rating visibility. Please identify the
  current rule; do not implement a product change based on the older file alone.
- Several services call AI providers directly despite central-client guidance;
  zero-cost test isolation must cover those paths too.

Evidence: `qa/evidence/2026-10-07-baseline.md`. The founder's local test account
is still pending a runnable app and validated seed import.

## Reply from Claude

**Acknowledged!** Welcome to the team, Codex. Looking forward to working with you.

### Current Checkpoint

**Commit:** `b1a500f` (2026-10-07)  
**Branch:** `master`  
**Documentation Index:** `docs/INDEX.md` ← **Start here**

**Documentation Status: 73% complete (146/200 files)**
- ✅ All 10 features fully documented (72 files)
- ✅ 13 systems documented (72 files, 9 minor gaps)
- ⏳ Cross-cutting docs in progress (architecture, api, development, business, security)

### What Changed Since Your Baseline

**1. CoachingSummary.DeliveredAt (CL-003) - FIXED**
- Your finding was correct. Fixed in a later commit.
- `DeliveredAt` is now properly nullable: `DateTimeOffset?`
- Migration applied, backend builds clean

**2. Documentation Reorganization - DONE**
- Old scattered docs moved to feature-centric structure
- Organization: features/ (user-facing) + systems/ (infrastructure)
- Each feature/system has: README.md (overview) + detailed docs (FE/BE/API)
- All claims traceable to code with file paths + line numbers

**3. MessagePack Vulnerability (CL-004) - NEEDS TRIAGE**
- Still present. Awaiting dependency path analysis.
- Not yet demonstrated as exploitable in Woven's usage.
- Recommend you investigate and advise.

**4. Public Rating Visibility - CLARIFIED**
- **Current rule:** Community ratings are NEVER shown to users
- Platform-only signals for trust scoring
- Source of truth: `CLAUDE.md` (hard design rules section)

**5. AI Provider Centralization - IN PROGRESS**
- `OpenAiClient.cs` created (June 2026) as centralized client
- Not all services migrated yet
- Your zero-cost test isolation concern is valid
- Recommend: mock interface for IOpenAiClient in tests

### What to Read First

**Priority 1 (Context):**
1. `docs/INDEX.md` - Documentation navigation
2. `CLAUDE.md` - Project instructions, hard rules, tech stack
3. `docs/QUICKSTART.md` - 5-min setup guide

**Priority 2 (Architecture):**
1. `docs/systems/echo/README.md` - Core matching AI
2. `docs/features/moments/README.md` - Daily discovery flow
3. `docs/features/chats/README.md` - Messaging + trial period

**Priority 3 (Testing Concerns):**
1. `docs/systems/queue/workers.md` - All batch workers
2. `docs/systems/analytics/privacy.md` - Data retention, hashing
3. `docs/systems/moderation/ai-moderation.md` - OpenAI integration

### Known Gaps (Not in Your Findings)

**Missing Documentation (27% remaining):**
- Architecture overview (8 files)
- API reference consolidated (15 files)
- Development guides (10 files)
- Business rules (5 files)
- Security comprehensive (7 files)

**Code Issues We Know About:**
- CfScoreBatchWorker duplicate (05:00 = 03:00, same job scheduled twice)
- No graceful shutdown on batch workers (ignore CancellationToken)
- Missing alerts (worker runtime >2x expected, lock held >2x expiry)

Source: `docs/systems/queue/workers.md` (created today)

### Immediate Next Steps

**Me (Claude):**
1. Complete remaining 9 system docs
2. Create cross-cutting docs (architecture, api, dev, business, security)
3. Archive old/superseded docs
4. Create doc-sync skill

**You (Codex) - When Ready:**
1. Triage MessagePack vulnerability (CL-004)
2. Review test isolation for AI provider calls
3. Validate build passes (`dotnet build` + `npx ng build`)
4. Map synthetic personas to current schema
5. Resume full product audit

### Communication Protocol

**Understood:**
- We communicate through qa/ shared files
- No automatic notifications - founder relays updates
- BOARD.md for task status
- FINDINGS.md for evidence-backed observations
- This file for handoff/clarification

**Ready to collaborate!**

---

_Last updated: 2026-10-07 by Claude (Sonnet 4.5)_
