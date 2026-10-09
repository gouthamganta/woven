# Findings

These are static observations, not proven runtime vulnerabilities.

## F-001 — Rating visibility requirements (RESOLVED)

**Original Issue:**
- `CLAUDE.md` hard design rules say no community ratings shown to users.
- `.claude/project-context.md` permits visibility when ratingCount >= 5.
- Impact: contradictory acceptance criteria for privacy/UI tests.

**Resolution (2026-10-08):**
- **Authoritative rule:** `CLAUDE.md` is source of truth
- **Intended behavior:** Community ratings are NEVER shown to users (hard design rule)
- **Current implementation:** ✅ Correct - No UI component renders rating counts
- **Acceptance criteria:** PRIVACY-001 in `qa/PRODUCT_RULES_INDEX.md`
- **Stale file:** `.claude/project-context.md` predates hard design rule decision
- **Action:** Document archived as historical context only

**Status:** ✅ DOCUMENTED — Added to product rules index as PRIVACY-001

## F-002 — Development endpoint isolation (DOCUMENTED)

**Original Issue:**
- `DevAuthEndpoints.cs` issues user tokens by ID and admin tokens without normal sign-in
- `Program.cs` registers these only in Development environment
- Concern: Public Development deployment would expose privileged access

**Protection Analysis (2026-10-08):**

**Guards in Place:**
1. **Runtime check:** `app.Environment.IsDevelopment()` (Program.cs:520-540)
   - DevAuthEndpoints registered ONLY when `ASPNETCORE_ENVIRONMENT=Development`
   - Production/Staging environments → endpoints NOT registered → 404
   
2. **Network isolation:** Azure Container Apps ingress is internal-only
   - No public internet access to backend
   - Additional defense-in-depth layer

**Important Correction:**
- **NO compile-time guard** (`#if DEBUG`) exists (Codex correctly identified)
- Protection is **runtime environment-based only**
- Previous Claude response incorrectly claimed compile-time exclusion

**Security Assessment:**
- ✅ **Sufficient for current architecture:** Environment check + internal ingress
- ⚠️ **Runtime dependency:** Relies on correct `ASPNETCORE_ENVIRONMENT` configuration
- ⚠️ **Not defense-in-depth:** Could be exposed if environment misconfigured

**Recommendations:**
1. **Smoke test (HIGH):** Add CI/CD check that `/debug/admin-token` returns 404 in staging/prod
2. **Environment validation (MEDIUM):** Startup assertion fails if `IsDevelopment()` && Azure deployment
3. **Consider compile guard (LOW):** Add `#if DEBUG` for additional safety layer

**Acceptance Criteria:**
- Documented in: `qa/PRODUCT_RULES_INDEX.md` (AUTH-003)
- Production verification: QA-007 (authorization tests)

**Status:** ✅ DOCUMENTED — Protection mechanism confirmed and indexed

## F-003 — Current seed endpoint is insufficient for deterministic QA

- `backend/WovenBackend/Endpoints/DevSeedEndpoints.cs` uses `new Random()`,
  alternating binary genders, one city, repeated answers, and DETAILS_DONE status.
- `buildVectors=true` invokes vector building; outbound costs need auditing.
- Impact: seed count alone does not establish complete profiles, realistic
  coverage, useful candidate pools, or reproducible matching outcomes.
- Status: fixture replacement prepared separately; database import pending.

## F-004 — Compose may enable paid integrations

- `docker-compose.yml` forwards `${OPENAI_API_KEY}` and enables backend workers
  by default. `WOVEN_DISABLE_BATCH_WORKERS` disables only selected workers.
- Impact: normal compose startup is not proof of a zero-cost test environment.
- Status: backend not started by QA. All external integrations and effective
  configuration require inventory; blank API keys alone do not prove isolation.

## F-005 — Backend compilation error (RESOLVED)

**Original Issue:**
- `dotnet test` failed with CS1061 at InteractionEndpoints.cs(73,88)
- `.HasValue` called on non-nullable `CoachingSummary.DeliveredAt`
- No backend unit tests ran

**Resolution (2026-10-07):**
- **Fixed in:** Commit `a5f29fa`
- **Change:** `CoachingSummary.DeliveredAt` changed from `DateTimeOffset` to `DateTimeOffset?`
- **Migration:** `20261007225937_MakeCoachingDeliveredAtNullable` applied
- **Verification:** 
  - `dotnet build` → 0 errors
  - `dotnet test` → 8/8 passed
- **Evidence:** qa/evidence/2026-10-07-cl003-fixed.md

**Status:** ✅ FIXED — Backend builds and all tests pass

## F-006 — MessagePack security vulnerabilities (RESOLVED)

**Original Issue:**
- NuGet reports MessagePack 2.5.187 NU1902/NU1903 advisories (2 HIGH + 9 MODERATE)
- Dependency: SignalR.StackExchangeRedis 10.0.1 → MessagePack 2.5.187

**Vulnerabilities:**
1. **CVE-2026-48109** (HIGH): Out-of-bounds reads via crafted LZ4 payloads
2. **CVE-2026-48506** (HIGH): Stack overflow via deeply nested arrays
3. 9 additional MODERATE severity advisories

**Exploitability:**
- ✅ Attack vector exists: SignalR chat messages (untrusted user data)
- ✅ Network accessible, no auth required
- ⚠️ Impact: Process crash, DoS, potential memory disclosure

**Resolution (2026-10-08):**
- **Fixed in:** Commit `25872f3` (CL-004)
- **Change:** SignalR.StackExchangeRedis 10.0.1 → 10.0.12
- **Transitive fix:** MessagePack 2.5.187 → 2.5.302 (patched)
- **Verification:**
  - `dotnet list package --vulnerable` → "no vulnerable packages"
  - All 8 tests pass, no regressions
- **Evidence:** qa/evidence/ (CL-004 handoff)

**Status:** ✅ FIXED — All vulnerabilities patched

## F-007 — Central AI client documentation disagrees with source

- CLAUDE.md says all OpenAI calls go through IOpenAiClient.
- CoachingSummaryWorker, ChatNoteEmbeddingWorker, TileEmbeddingService,
  MatchNarratorService and ModerationService contain direct provider endpoints.
- Impact: mocking or disabling only IOpenAiClient will not isolate all calls.
- Status: static observation; map all outbound paths before sandbox app startup.
