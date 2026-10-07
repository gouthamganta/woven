# Documentation Completion Plan

**Status:** 26% complete (11 of 42 files)  
**Target:** 100% (42 files total)  
**Updated:** 2026-10-07

---

## Remaining Work (31 files)

### DOC-001: API Reference (15 files) — Priority: HIGH
**Directory:** `docs/api/`  
**Progress:** 0 of 15 files  
**Milestone:** 26% → 62%

**Files to create:**

1. **README.md** — API overview, base URL, versioning, general patterns
2. **authentication.md** — Auth headers, JWT structure, cookie auth, token refresh
3. **rate-limiting.md** — Rate limits per endpoint, 429 responses, retry logic
4. **error-handling.md** — Error response format, correlation IDs, common errors

**Endpoint-specific docs (1 file per feature):**

5. **onboarding.md** — POST /onboarding/details, /foundational, /photos
6. **moments.md** — GET /moments/deck, POST /moments/respond, /choose
7. **chats.md** — GET /chats, /chats/{threadId}, POST /chats/{threadId}/message, /voice-message, /trial-decision
8. **matches.md** — GET /matches, POST /matches/{matchId}/pop, /unmatch
9. **commons.md** — GET /tiles, POST /tiles, /tiles/{tileId}/orbit
10. **profile.md** — GET /profile, PUT /profile, GET /profile/{userId}
11. **notifications.md** — GET /notifications, POST /notifications/read
12. **games.md** — POST /games/knowme/start, /submit, /redgreenflag/start, /submit
13. **media.md** — POST /media/upload-token, /confirm
14. **coaching.md** — GET /coaching/summary
15. **assistant.md** — POST /assistant/chat (if exists, otherwise skip)

**Format for each endpoint doc:**
```markdown
# [Feature] API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Endpoints

### POST /endpoint-path

**Description:** What this endpoint does

**Authentication:** Required (JWT)

**Request:**
\```json
{
  "field": "value"
}
\```

**Response (200 OK):**
\```json
{
  "result": "data"
}
\```

**Errors:**
- 400: Bad Request (validation failed)
- 401: Unauthorized (missing/invalid token)
- 404: Not Found (resource doesn't exist)
- 500: Internal Server Error

**Rate Limit:** 100 requests/minute per user

**Source:** `backend/WovenBackend/Endpoints/XxxEndpoints.cs`
```

---

### DOC-002: Development (10 files) — Priority: MEDIUM
**Directory:** `docs/development/`  
**Progress:** 0 of 10 files  
**Milestone:** 62% → 86%

**Files to create:**

1. **README.md** — Developer guide overview, quick start, where to find things
2. **setup.md** — Local development setup (already exists elsewhere, consolidate)
3. **contributing.md** — How to contribute, code style, PR process
4. **patterns.md** — Code patterns (MapEndpoints, OnPush, Services layer)
5. **testing.md** — Testing guide, test commands, coverage
6. **debugging.md** — Common issues, debugging tips, troubleshooting
7. **git-workflow.md** — Branch strategy, commit messages, PR workflow
8. **database-migrations.md** — EF migrations, how to add/apply migrations
9. **deployment.md** — Deploy guide, CI/CD pipeline, environments
10. **monitoring.md** — Logs, App Insights, metrics, alerts

**Reference:** Most content already exists in:
- `docs/contributing/LOCAL_SETUP.md`
- `docs/contributing/CONTRIBUTING.md`
- `docs/technical/DEVOPS.md`
- `docs/technical/TESTING.md`

**Task:** Consolidate and rewrite for `docs/development/` structure.

---

### DOC-003: Security (6 files) — Priority: HIGH
**Directory:** `docs/security/`  
**Progress:** 1 of 7 files (README.md exists)  
**Milestone:** 86% → 100%

**Files to create:**

1. ✅ **README.md** — Already exists
2. **authentication.md** — Auth security, JWT validation, cookie security
3. **encryption.md** — AES-256-GCM, key rotation, encrypted fields
4. **pii.md** — PII handling, sanitization, retention, anonymization
5. **prompt-injection.md** — Prompt protection, content filtering, moderation
6. **security-audit.md** — Audit logging, security events, monitoring
7. **incident-response.md** — Incident response process, escalation, postmortems

**Reference:**
- `docs/technical/ENCRYPTION_SECURITY_DESIGN.md`
- `docs/technical/SECURITY.md`
- `backend/WovenBackend/Infrastructure/`

---

## Evidence-Based Requirement

**ALL documentation must be evidence-based:**
- Reference actual code files (with line numbers)
- Quote actual config/environment variables
- Link to actual endpoints/services
- Include actual DB schema/entities
- NO generic boilerplate
- NO aspirational claims

**Template for citing code:**
```markdown
**Source:** `backend/WovenBackend/Endpoints/AuthEndpoints.cs:45-67`

**Implementation:**
\```csharp
// Actual code snippet from the file
\```
```

---

## Instructions for Claude

**To complete DOC-001 (API Reference):**
```
Complete the API Reference documentation (DOC-001).

Create 15 files in docs/api/:
1. README.md - API overview
2. authentication.md - Auth headers, JWT, cookies
3. rate-limiting.md - Rate limits and 429 responses
4. error-handling.md - Error format and correlation IDs
5-15. Endpoint docs (onboarding, moments, chats, matches, commons, profile, notifications, games, media, coaching, assistant)

Requirements:
- Evidence-based: reference actual endpoint files in backend/WovenBackend/Endpoints/
- Include request/response examples from actual code
- Document all error codes
- Include rate limits if implemented
- Reference source files with line numbers
- Use the format template in docs/COMPLETION_PLAN.md

After completing, commit with message:
"docs: complete API reference (15 files, 62% milestone)"
```

**To complete DOC-002 (Development):**
```
Complete the Development documentation (DOC-002).

Create 10 files in docs/development/:
1. README.md - Developer guide overview
2. setup.md - Local setup (consolidate from contributing/LOCAL_SETUP.md)
3. contributing.md - Contribution guide
4. patterns.md - Code patterns (MapEndpoints, OnPush, Services)
5. testing.md - Testing guide
6. debugging.md - Common issues and troubleshooting
7. git-workflow.md - Branch strategy and commits
8. database-migrations.md - EF migrations guide
9. deployment.md - Deploy guide
10. monitoring.md - Logs and metrics

Reference existing docs:
- docs/contributing/LOCAL_SETUP.md
- docs/contributing/CONTRIBUTING.md
- docs/technical/DEVOPS.md
- docs/technical/TESTING.md

Consolidate and rewrite for clarity. Evidence-based only.

After completing, commit with message:
"docs: complete development guide (10 files, 86% milestone)"
```

**To complete DOC-003 (Security):**
```
Complete the Security documentation (DOC-003).

Create 6 files in docs/security/ (README.md already exists):
1. authentication.md - Auth security (JWT validation, cookie security)
2. encryption.md - AES-256-GCM, key rotation, encrypted fields
3. pii.md - PII handling, sanitization, retention policies
4. prompt-injection.md - Prompt protection and content filtering
5. security-audit.md - Audit logging and security monitoring
6. incident-response.md - Incident response procedures

Reference:
- docs/technical/ENCRYPTION_SECURITY_DESIGN.md
- docs/technical/SECURITY.md
- backend/WovenBackend/Infrastructure/
- backend/WovenBackend/Services/

Evidence-based: cite actual implementations.

After completing, commit with message:
"docs: complete security documentation (6 files, 100% milestone)"
```

---

## Instructions for Codex

**Quick-start command:**
```
I need help completing the documentation. Please read docs/COMPLETION_PLAN.md and work on DOC-001 (API Reference - 15 files). Create evidence-based documentation for all API endpoints by reading the actual endpoint files in backend/WovenBackend/Endpoints/. Follow the format template and commit when done.
```

**Parallel execution strategy:**
- Codex: Work on DOC-001 (API Reference) — most mechanical, high file count
- Claude: Work on DOC-002 (Development) — requires consolidation/rewrite
- Either: DOC-003 (Security) — can be done by either agent

**Efficiency tips for Codex:**
1. Read all endpoint files first: `ls backend/WovenBackend/Endpoints/*.cs`
2. Create all 15 files in one session (batch mode)
3. Use consistent format across all endpoint docs
4. Cite source files with line numbers
5. Include actual request/response shapes from code
6. Commit all 15 files together

---

## Success Criteria

**For each file:**
- ✅ Evidence-based (cites actual code/config)
- ✅ Includes source file references
- ✅ No generic boilerplate
- ✅ Accurate as of 2026-10-07
- ✅ Follows format template

**For the milestone:**
- ✅ All 42 files created
- ✅ All files committed
- ✅ CLAUDE.md updated with "docs 100% complete"
- ✅ Board task DOC-004 completed

---

## Timeline Estimate

**DOC-001 (API Reference):** 2-3 hours (15 files, mostly mechanical)  
**DOC-002 (Development):** 1-2 hours (10 files, consolidation work)  
**DOC-003 (Security):** 1 hour (6 files, reference existing docs)  

**Total:** 4-6 hours for completion

---

## Current Progress Tracker

| Milestone | Files | Status | Percentage |
|-----------|-------|--------|------------|
| Architecture + Business | 11/11 | ✅ DONE | 26% |
| API Reference | 0/15 | ⏳ TODO | 62% |
| Development | 0/10 | ⏳ TODO | 86% |
| Security | 1/7 | ⏳ TODO | 100% |
| **TOTAL** | **12/43** | **28%** | - |

**Note:** 43 files total (11 arch/business + 15 api + 10 dev + 7 security)

Last updated: 2026-10-07 by Claude Sonnet 4.5
