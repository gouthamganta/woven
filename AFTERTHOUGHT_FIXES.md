# Afterthought QA Fixes

**Date:** 2026-10-15  
**Status:** ✅ FIXES APPLIED  
**QA Report:** https://github.com/gouthamganta/woven/blob/a538307/qa/evidence/2026-10-15-afterthought-acceptance.md

## Fixed Issues

### P1: Migration Mismatch (Guid vs int)
**Problem:** Migration created `int match_id` + `Guid? MatchId1` instead of `Guid match_id`

**Fix:** `20261015232818_AddMatchAfterthoughts.cs`
- Changed `match_id` from `int` to `Guid`  
- Removed wrong `MatchId1` nullable column
- Updated FK to point to `match_id` with CASCADE delete
- Added unique index on `(match_id, from_user_id)` to prevent duplicates

**Files:**
- [20261015232818_AddMatchAfterthoughts.cs](backend/WovenBackend/Migrations/20261015232818_AddMatchAfterthoughts.cs)

---

### P2: Request Validation (Text Length)
**Problem:** Service accepted 51-char reason + 301-char note without validation

**Fix:** `AfterthoughtService.cs`
- Added validation before storage:
  - `reasonTag` max 50 chars
  - `noteText` max 300 chars
- Throws `ArgumentException` if exceeded

**Files:**
- [AfterthoughtService.cs](backend/WovenBackend/Services/Moments/AfterthoughtService.cs)

---

### P1: Safety Authorization
**Problem:** Blocked user could submit block feedback when already blocked by other user

**Fix:** `ChatEndpoints.cs` trial-decision
- Check if user was blocked by other party before allowing BLOCK decision
- Returns `403 CANNOT_BLOCK_ALREADY_BLOCKED_BY` if blocked
- Only save Afterthought if feedback is provided (not empty)

**Files:**
- [ChatEndpoints.cs:702-746](backend/WovenBackend/Endpoints/ChatEndpoints.cs#L702-L746)

---

### P1: Lost END Feedback
**Problem:** First user's END feedback was lost when second user submitted their decision

**Fix:** `ChatEndpoints.cs` trial-decision
- Save Afterthought for first decider immediately (waiting state)
- Save Afterthought for second decider when match closes
- Both users' feedback now preserved independently
- Only save if feedback provided (not CONTINUE with no data)

**Files:**
- [ChatEndpoints.cs:755-819](backend/WovenBackend/Endpoints/ChatEndpoints.cs#L755-L819)

---

### P1: Duplicate Records & Empty Submissions
**Problem:** 
1. No unique constraint allowed duplicate Afterthoughts
2. Empty `{}` unmatch request created empty row, blocking later feedback

**Fix:**

**Migration:**
- Added unique index on `(match_id, from_user_id)`

**Service:**
- Added idempotency check with `AnyAsync` before insert
- Throws `InvalidOperationException` if already submitted

**Unmatch Endpoint:**
- Only save Afterthought if `rating` or `reasonTag` provided
- Empty request (`{}`) no longer creates empty row

**Files:**
- [20261015232818_AddMatchAfterthoughts.cs](backend/WovenBackend/Migrations/20261015232818_AddMatchAfterthoughts.cs)
- [AfterthoughtService.cs](backend/WovenBackend/Services/Moments/AfterthoughtService.cs)
- [MatchesEndpoints.cs:390-405](backend/WovenBackend/Endpoints/MatchesEndpoints.cs#L390-L405)

---

## Summary

| Issue | Priority | Status | Files Changed |
|-------|----------|--------|---------------|
| Migration Guid/int mismatch | P1 | ✅ Fixed | Migration |
| Text length validation | P2 | ✅ Fixed | AfterthoughtService |
| Safety authorization | P1 | ✅ Fixed | ChatEndpoints |
| Lost END feedback | P1 | ✅ Fixed | ChatEndpoints |
| Duplicate records | P1 | ✅ Fixed | Migration, Service, Endpoints |

**Build Status:** ✅ 0 errors, 0 warnings

## Files Modified
1. `backend/WovenBackend/Migrations/20261015232818_AddMatchAfterthoughts.cs`
2. `backend/WovenBackend/Services/Moments/AfterthoughtService.cs`
3. `backend/WovenBackend/Endpoints/ChatEndpoints.cs`
4. `backend/WovenBackend/Endpoints/MatchesEndpoints.cs`

## Remaining Work (Not in Scope)

Static items from QA report (separate tasks):
- ECHO integration: Feed to WeightLearningService
- ECHO integration: Feed safety flags to TrustService only
- MatchSignalLogs integration
- Full frontend verification (Codex noted only backend tested)
- Trial-END path provenance mapping

---

**Next:** Codex retest with these 24 regression cases + full suite
