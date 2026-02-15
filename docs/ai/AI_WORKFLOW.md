# AI Development Workflow

> Step-by-step process for AI-assisted development on Woven.
> Follow these phases to ensure consistent, high-quality output.

---

## Overview

AI development follows a structured 5-phase approach:

```
┌─────────┐    ┌──────────┐    ┌──────────┐    ┌─────────┐    ┌────────┐
│ CONTEXT │───▶│ PLANNING │───▶│ IMPLEMENT│───▶│ VERIFY  │───▶│ COMMIT │
└─────────┘    └──────────┘    └──────────┘    └─────────┘    └────────┘
```

---

## Phase 1: Context Gathering

### Before Starting ANY Task

1. **Read the master context file**
   ```
   .claude/project-context.md
   ```

2. **Identify relevant documentation**
   - For state changes → `docs/STATE_MACHINES.md`
   - For business logic → `docs/BUSINESS_RULES.md`
   - For data flow → `docs/DATA_FLOW.md`

3. **Find existing patterns**
   - For backend → `docs/templates/backend/patterns.md`
   - For frontend → `docs/templates/frontend/patterns.md`

4. **Locate similar existing code**
   - Search for similar features
   - Identify patterns to follow
   - Note any conventions

### Questions to Answer

Before writing any code, ensure you can answer:

- [ ] What existing patterns should I follow?
- [ ] What state machines are involved?
- [ ] What business rules apply?
- [ ] What files will I need to modify?
- [ ] What new files will I need to create?
- [ ] Are there any non-negotiable invariants I must respect?

---

## Phase 2: Planning

### Create a Task Breakdown

For any non-trivial task, create an explicit plan:

```markdown
## Task: Add "Report User" Feature

### Backend Changes
1. Create `ReportService.cs` (interface + implementation)
2. Create `ReportEndpoints.cs` (minimal API)
3. Add `Report` model and migration
4. Register service in `Program.cs`

### Frontend Changes
1. Create `report.service.ts`
2. Add report button to user profile component
3. Create report modal component
4. Add report route handler

### Testing
1. Test endpoint authorization
2. Test validation rules
3. Test UI states (loading, error, success)
```

### Verify Against Checklist

Before implementing, verify:

- [ ] Plan follows existing architecture patterns
- [ ] No business rules will be violated
- [ ] All state transitions are valid
- [ ] Touch targets will be ≥ 44px
- [ ] Authentication is required on new endpoints

---

## Phase 3: Implementation

### Implementation Order

Always implement in this order:

```
1. Backend Model/Entity (if new)
2. Backend Service (business logic)
3. Backend Endpoint (API surface)
4. Frontend Service (API client)
5. Frontend Component (UI)
```

### Implementation Checklist - Backend

```
□ Model
  - [ ] Entity class created
  - [ ] DbContext updated
  - [ ] Migration generated
  - [ ] Migration reviewed (check Up and Down)

□ Service
  - [ ] Interface defined
  - [ ] Implementation follows patterns
  - [ ] Result types for fallible operations
  - [ ] Logging added
  - [ ] Registered in Program.cs

□ Endpoint
  - [ ] Uses Minimal API (not Controller)
  - [ ] .RequireAuthorization() present
  - [ ] DTOs defined for request/response
  - [ ] Error responses consistent
  - [ ] Registered in Program.cs
```

### Implementation Checklist - Frontend

```
□ Service
  - [ ] Types defined for all API shapes
  - [ ] Uses environment.apiUrl
  - [ ] Returns Observable<T>
  - [ ] JSDoc comments added

□ Component
  - [ ] Standalone component (not NgModule)
  - [ ] Loading state handled
  - [ ] Error state handled
  - [ ] Empty state handled (if applicable)
  - [ ] All touch targets ≥ 44px
  - [ ] Subscriptions cleaned up
  - [ ] Route registered
```

---

## Phase 4: Verification

### Self-Review Checklist

Before considering code complete:

```
□ Code Quality
  - [ ] No console.log (except debug markers)
  - [ ] No commented-out code
  - [ ] No TODO comments left behind
  - [ ] No TypeScript `any` without justification
  - [ ] Functions < 50 lines

□ Security
  - [ ] All endpoints require auth
  - [ ] No sensitive data in logs
  - [ ] Input validated
  - [ ] No SQL injection vectors
  - [ ] No XSS vectors

□ UX
  - [ ] Touch targets ≥ 44px
  - [ ] Loading states shown
  - [ ] Error messages user-friendly
  - [ ] Empty states helpful

□ Business Rules
  - [ ] Budget system respected
  - [ ] Rating threshold (≥5) enforced
  - [ ] State machines followed
  - [ ] Timing uses UTC
```

### Test Mentally

Walk through these scenarios:

1. **Happy path** - Does it work when everything is correct?
2. **Empty state** - What if there's no data?
3. **Error state** - What if the API fails?
4. **Boundary cases** - What about edge cases?
5. **Auth state** - What if user isn't logged in?

---

## Phase 5: Commit

### Commit Message Format

```
type: Short description (imperative mood)

- Detail 1
- Detail 2

Refs: #issue-number (if applicable)
```

Types:
- `feat:` New feature
- `fix:` Bug fix
- `refactor:` Code restructure without behavior change
- `docs:` Documentation only
- `style:` Formatting, no code change
- `test:` Adding tests
- `chore:` Maintenance tasks

### Example

```
feat: Add report user functionality

- Add Report model and service
- Create report endpoint with POST /api/reports
- Add report button to user profile
- Create report modal with reason selection

Refs: #42
```

---

## Common Workflows

### Adding a New Feature (Full Stack)

```
1. Context
   → Read project-context.md
   → Find similar existing feature
   → Note patterns used

2. Backend
   → Create model (if new entity)
   → Generate migration
   → Create service interface + implementation
   → Create endpoint
   → Register in Program.cs

3. Frontend
   → Create service
   → Create component(s)
   → Add route
   → Verify touch targets

4. Verify
   → Run through self-review checklist
   → Test all states manually

5. Commit
   → Stage relevant files
   → Write descriptive commit message
```

### Fixing a Bug

```
1. Context
   → Understand the bug completely
   → Find where it occurs
   → Understand the expected behavior

2. Root Cause
   → Trace the data flow
   → Identify the actual cause
   → Determine the fix

3. Fix
   → Make minimal necessary changes
   → Don't refactor unrelated code
   → Preserve existing behavior

4. Verify
   → Bug is fixed
   → No regression introduced
   → Related functionality still works

5. Commit
   → Clear description of what was wrong
   → Clear description of the fix
```

### Refactoring

```
1. Context
   → Understand current behavior fully
   → Identify what needs to change
   → Plan incremental changes

2. Refactor
   → Make changes in small steps
   → Verify after each step
   → Keep tests passing

3. Verify
   → Behavior unchanged
   → Code is cleaner
   → Performance acceptable

4. Commit
   → Explain why refactoring was needed
   → Describe what changed
```

---

## AI-Specific Guidelines

### When Stuck

1. **Re-read the context files** - The answer is often there
2. **Look for similar code** - Pattern match from existing
3. **Break down smaller** - Divide into smaller tasks
4. **Ask for clarification** - Better to ask than assume

### What Not to Do

- ❌ Don't guess at patterns - look them up
- ❌ Don't skip the verification phase
- ❌ Don't make changes beyond what's requested
- ❌ Don't add "improvements" that weren't asked for
- ❌ Don't leave TODOs without resolution

### When to Stop and Ask

Stop and ask the user when:

- Requirements are ambiguous
- Multiple valid approaches exist
- Changes would affect other features
- Security implications are unclear
- Business rules are uncertain

---

## Quick Reference

| Phase | Key Action |
|-------|------------|
| Context | Read docs, find patterns |
| Planning | Break down, verify approach |
| Implement | Follow templates exactly |
| Verify | Use checklists |
| Commit | Clear, descriptive message |
