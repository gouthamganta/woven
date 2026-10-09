# Product Rules Index — QA Quick Reference

**Purpose:** Authoritative list of product rules for test case design and acceptance validation.  
**Source:** Extracted from [docs/business/rules.md](../docs/business/rules.md)  
**Last Updated:** 2026-10-08  
**Status:** Active

⚠️ **Changes to these rules require founder approval before implementation.**

---

## Match Rules

| Rule ID | Rule | Enforcement | Test Coverage |
|---------|------|-------------|---------------|
| MATCH-001 | Mutual like required (◈/◇ combinations only) | MomentsEndpoints.cs | ⏳ Pending |
| MATCH-002 | No self-matches | CandidatePoolService SQL | ⏳ Pending |
| MATCH-003 | No duplicate matches | DB unique index (user_a_id, user_b_id) | ⏳ Pending |
| MATCH-004 | No repeat swipes (one response per candidate) | DB unique index + API check | ⏳ Pending |
| MATCH-005 | PURE match: same choice → edge_owner_id = null | MomentsEndpoints.cs | ⏳ Pending |
| MATCH-006 | EDGE match: different choice → edge_owner_id set | MomentsEndpoints.cs | ⏳ Pending |

---

## Balloon Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| BALLOON-001 | Balloon lifetime | **36 hours** | BalloonExpiryWorker (60s scan) | ⏳ Pending |
| BALLOON-002 | Balloon state immutable after close | Once CLOSED, never reopens | API validation | ⏳ Pending |
| BALLOON-003 | One pop per match | Either user, but only once | MatchesEndpoints.PopBalloon | ⏳ Pending |
| BALLOON-004 | Balloon expiry = match closed | Auto-close at BalloonExpiresAt | BalloonExpiryWorker | ⏳ Pending |

**Note:** CLAUDE.md incorrectly states 72h in one location. **Authoritative value: 36h** (verified in MomentsRules.cs:7)

---

## Trial Period Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| TRIAL-001 | Trial starts when both users open chat | Requires TrialUserAOpenedAt AND TrialUserBOpenedAt | ChatEndpoints.GetThread | ⏳ Pending |
| TRIAL-002 | Trial duration | **3 minutes** | Set at trial start | ⏳ Pending |
| TRIAL-003 | Trial decision options | CONTINUE / END / BLOCK | ChatEndpoints.TrialDecision | ⏳ Pending |
| TRIAL-004 | END reason required | no_spark / wrong_timing / not_my_type | API validation | ⏳ Pending |
| TRIAL-005 | CONTINUE → Find Love stage | Unlock final messaging stage | Match state update | ⏳ Pending |
| TRIAL-006 | BLOCK → immediate close + Block record | Creates Block entity | ChatEndpoints.TrialDecision | ⏳ Pending |

---

## Spark Economy Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| SPARK-001 | Initial balance | **5.0 sparks** (50 tenths) | SparkWalletService | ✅ Fixed (CL-005) |
| SPARK-002 | Daily earning | +5.0 sparks (max 10.0 total) | SparkWalletService lazy earn | ⏳ Pending |
| SPARK-003 | Wallet maximum | **10.0 sparks** (100 tenths) | SparkWalletService cap | ⏳ Pending |
| SPARK-004 | Drawn action cost | **1.0 spark** | SparkWalletService.TrySpend | ⏳ Pending |
| SPARK-005 | Ghost refund | **0.5 sparks** (match ends, no messages) | SparkWalletService.GhostRefund | ⏳ Pending |
| SPARK-006 | Wallet stored as tenths | Integer 0-100 (not decimal 0.0-10.0) | SparkWallet.cs | ⏳ Pending |

**Critical Fix:** CL-005 resolved double-grant bug (users were getting 10 sparks on signup instead of 5)

---

## Deck Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| DECK-001 | Daily deck size | **5 candidates** | DeckSelectionService | ⏳ Pending |
| DECK-002 | Deck generation time | Daily at 03:30 UTC | DailyDeckOrchestrator worker | ⏳ Pending |
| DECK-003 | Deck bucket allocation | 2 BALANCED + 2 EXPLORER + 1 BOOST | DeckSelectionService | ⏳ Pending |
| DECK-004 | No repeat candidates in same deck | Unique candidates per day | SQL distinct | ⏳ Pending |
| DECK-005 | Deck expires next generation | Old deck replaced, not appended | DailyDeckOrchestrator | ⏳ Pending |

**Note:** CLAUDE.md incorrectly states 60 candidates in one location. **Authoritative value: 5** (verified in DeckSelectionService)

---

## Trust & Safety Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| TRUST-001 | Minimum trust score for matching | **0.25** | CandidatePoolService SQL | ⏳ Pending |
| TRUST-002 | Block prevents future matches | No candidate pool inclusion | CandidatePoolService | ⏳ Pending |
| TRUST-003 | Report creates safety flag | ModerationService | ModerationService | ⏳ Pending |
| TRUST-004 | Trust score range | 0.0 (untrusted) to 1.0 (verified) | User.TrustScore | ⏳ Pending |

**Note:** CLAUDE.md incorrectly states 0.5 threshold in one location. **Authoritative value: 0.25** (verified in CandidatePoolService.cs:12)

---

## Privacy Rules (Hard Design Rules)

| Rule ID | Rule | Enforcement | Test Coverage |
|---------|------|-------------|---------------|
| PRIVACY-001 | **NO community ratings shown to users** | Platform-only signals | ⏳ Pending |
| PRIVACY-002 | NO raw compatibility scores shown | ECHO outputs explanations, not numbers | ⏳ Pending |
| PRIVACY-003 | NO age on Moments cards | Card shows: name, badge, explanation, actions only | ⏳ Pending |
| PRIVACY-004 | ChatNote data shown to matched pairs only | Visible to match participants, denied to third parties (auth enforced) | ⏳ Pending |

**Source:** CLAUDE.md "Hard design rules" section (non-negotiable)

---

## Authentication Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| AUTH-001 | JWT expiry (default) | **60 minutes** | JwtTokenService.cs:23 | ⏳ Pending |
| AUTH-002 | JWT stored in | localStorage (dev) + HttpOnly cookies (prod) | CookieAuthHelper | ⏳ Pending |
| AUTH-003 | DevAuth endpoints | Development environment ONLY | Program.cs IsDevelopment() | ⏳ Pending |

**Note:** Documentation conflicts exist (7/30/60 days mentioned). **Authoritative: 60 minutes** with configurable override.

---

## Data Retention Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| RETENTION-001 | MatchSignalLogs retention | **90 days** | ConnectionScoreBatchWorker scan window | ⏳ Pending |
| RETENTION-002 | Session logs retention | TBD | Not yet implemented | ❌ Not Implemented |
| RETENTION-003 | PII hashing | SHA-256 for analytics | AnalyticsService | ⏳ Pending |

---

## Rate Limiting Rules

| Rule ID | Rule | Value | Enforcement | Test Coverage |
|---------|------|-------|-------------|---------------|
| RATE-001 | Message send limit | TBD (RateLimitService exists) | RateLimitService | ⏳ Pending |
| RATE-002 | AI game action limit | TBD | RateLimitService | ⏳ Pending |
| RATE-003 | Rate limit response | HTTP 429 with retryAfter seconds | API middleware | ⏳ Pending |

---

## UI/UX Rules (Hard Design Rules)

| Rule ID | Rule | Source | Test Coverage |
|---------|------|--------|---------------|
| UX-001 | **NO hover translateY lifts** | CLAUDE.md | ⏳ Pending |
| UX-002 | Hover = glow/shadow/color ONLY | CLAUDE.md | ⏳ Pending |
| UX-003 | Background drift stays ON | CLAUDE.md | ⏳ Pending |
| UX-004 | NO paywalls (Spark economy is soft gate) | CLAUDE.md | ⏳ Pending |
| UX-005 | Full CSS variable token system | CLAUDE.md | ⏳ Pending |

---

## Change Log

| Date | Rule ID | Change | Reason | Approval |
|------|---------|--------|--------|----------|
| 2026-10-09 | PRIVACY-004 | Changed: background-only → visible to matched pairs | DOC-QA-001 (#143), founder decision 2026-10-08 | Founder ✓ |
| 2026-10-07 | BALLOON-001 | Corrected: 72h → 36h | DOC-006 fix, code evidence | Founder ✓ |
| 2026-10-07 | DECK-001 | Corrected: 60 → 5 | DOC-006 fix, code evidence | Founder ✓ |
| 2026-10-07 | TRUST-001 | Corrected: 0.5 → 0.25 | DOC-006 fix, code evidence | Founder ✓ |
| 2026-10-07 | SPARK-001 | Fixed double-grant bug | CL-005 (economy-breaking) | Founder ✓ |

---

## Usage Instructions

**For Test Case Design:**
1. Each rule ID becomes a test scenario
2. "Value" column = expected behavior
3. "Enforcement" column = code to verify/mock

**For Acceptance Validation:**
1. Check "Test Coverage" status before marking task complete
2. Rules marked "⏳ Pending" need test coverage
3. Rules marked "❌ Not Implemented" are design-only

**When Rules Change:**
1. Update this index FIRST
2. Add entry to Change Log with founder approval
3. Update affected test cases
4. Re-run regression suite

**Cross-Reference:**
- Full rules with examples: [docs/business/rules.md](../docs/business/rules.md)
- State machines: [docs/business/state-machines.md](../docs/business/state-machines.md)
- Design philosophy: [docs/business/product-philosophy.md](../docs/business/product-philosophy.md)
