# Instructions for Documentation Completion

**Current Status:** 26% complete (11 of 42 files)  
**Remaining:** 31 files (API Reference, Development, Security)  
**Plan:** `docs/COMPLETION_PLAN.md`  
**Board:** `qa/BOARD.md` (tasks DOC-001, DOC-002, DOC-003, DOC-004)

---

## 📋 What Needs to Be Done

### DOC-001: API Reference (15 files) ⏳ TODO
- **Directory:** `docs/api/`
- **Priority:** HIGH (most mechanical, good for parallel work)
- **Files:** README + auth + rate-limiting + error-handling + 11 endpoint docs

### DOC-002: Development (10 files) ⏳ TODO  
- **Directory:** `docs/development/`
- **Priority:** MEDIUM (requires consolidation)
- **Files:** README + setup + contributing + patterns + testing + debugging + git-workflow + migrations + deployment + monitoring

### DOC-003: Security (6 files) ⏳ TODO
- **Directory:** `docs/security/`
- **Priority:** HIGH
- **Files:** authentication + encryption + pii + prompt-injection + audit + incident-response (README exists)

---

## 🤖 Instructions for NEW CLAUDE SESSION

**Copy-paste this into a new Claude session:**

```
Complete the API Reference documentation (DOC-001 from qa/BOARD.md).

Read docs/COMPLETION_PLAN.md for full requirements.

Create 15 files in docs/api/:
1. README.md - API overview
2. authentication.md - Auth headers, JWT, cookies  
3. rate-limiting.md - Rate limits and 429 responses
4. error-handling.md - Error format and correlation IDs
5-15. Endpoint docs (onboarding, moments, chats, matches, commons, profile, notifications, games, media, coaching, assistant)

Requirements:
- Evidence-based: reference actual files in backend/WovenBackend/Endpoints/
- Include request/response examples from actual code
- Document all error codes
- Use the format template in docs/COMPLETION_PLAN.md

After completing, commit with:
"docs: complete API reference (15 files, 62% milestone)"

Then continue with DOC-002 (Development) and DOC-003 (Security).
```

---

## 🤖 Instructions for CODEX

**Copy-paste this into Codex session:**

```
I need help completing documentation. Please read docs/COMPLETION_PLAN.md and work on DOC-001 (API Reference - 15 files).

Create evidence-based documentation for all API endpoints by reading backend/WovenBackend/Endpoints/*.cs files.

Follow the format template in COMPLETION_PLAN.md and commit when done.

Work efficiently:
1. Read all endpoint files first
2. Create all 15 files in one session (batch mode)
3. Use consistent format
4. Cite source files with line numbers
5. Commit all files together

After DOC-001 is done, you can also help with DOC-003 (Security docs) if needed.
```

---

## 📊 Progress Tracking

| Task | Files | Status | Owner | Milestone |
|------|-------|--------|-------|-----------|
| ✅ Architecture + Business | 11/11 | DONE | Claude | 26% |
| ⏳ API Reference | 0/15 | TODO | Claude/Codex | 62% |
| ⏳ Development | 0/10 | TODO | Claude/Codex | 86% |
| ⏳ Security | 1/7 | TODO | Claude/Codex | 100% |

**Check progress:** `git log --oneline --grep="docs:" | head -10`

---

## ✅ Completion Checklist

When ALL documentation is done:

- [ ] All 42 files created and committed
- [ ] Evidence-based (no generic boilerplate)
- [ ] All source files referenced with line numbers
- [ ] Update CLAUDE.md (remove "docs 73% complete" note)
- [ ] Update qa/BOARD.md (mark DOC-001/002/003 as "ready for QA")
- [ ] Complete DOC-004 (final status update)
- [ ] Push all commits to GitHub
- [ ] Celebrate! 🎉

---

## 🚀 Quick Commands

**Check current status:**
```bash
find docs/api docs/development docs/security -name "*.md" | wc -l
```

**See what's missing:**
```bash
ls docs/api/
ls docs/development/
ls docs/security/
```

**Commit after batch creation:**
```bash
git add docs/
git commit -m "docs: complete [section] ([N] files, [X]% milestone)"
git push origin master
```

---

## 📝 Quality Standards

Every doc file must have:
1. ✅ **Evidence-based** - Cites actual code/config
2. ✅ **Source references** - File paths with line numbers
3. ✅ **Accurate examples** - Real request/response shapes
4. ✅ **No boilerplate** - No generic filler text
5. ✅ **Last Updated** - Date stamp (2026-10-07)

**Format template:**
```markdown
# [Topic Name]

**Last Updated:** 2026-10-07

---

## Section

Content with evidence...

**Source:** `path/to/file.cs:123-145`

**Implementation:**
\```csharp
// Actual code snippet
\```

---

## Related Documentation

- [Link to related doc](../other/file.md)
```

---

**For questions, see:** `docs/COMPLETION_PLAN.md` (full details)  
**Board tasks:** `qa/BOARD.md` (DOC-001, DOC-002, DOC-003, DOC-004)

Last updated: 2026-10-07
