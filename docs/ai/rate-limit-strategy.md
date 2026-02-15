# Rate Limit Strategy for AI Development

> How to efficiently use AI tokens when developing on Woven.
> Follow these strategies to maximize output while minimizing costs.

---

## Core Principles

### 1. Context Over Repetition

**DO**: Reference documentation rather than repeating it
```
"Follow the service template in docs/templates/backend/service-template.md"
```

**DON'T**: Ask AI to regenerate standard patterns every time
```
"Create a service with the standard patterns we always use..."
```

### 2. Batch Related Changes

**DO**: Group related changes into single sessions
```
Session 1: Complete backend for feature X
Session 2: Complete frontend for feature X
```

**DON'T**: Switch contexts frequently
```
Session 1: Start backend endpoint
Session 2: Start frontend component
Session 3: Back to backend service
Session 4: Back to frontend...
```

### 3. Incremental Verification

**DO**: Verify after each logical unit
```
1. Create service → verify
2. Create endpoint → verify
3. Create frontend service → verify
4. Create component → verify
```

**DON'T**: Write everything then debug
```
1. Write all backend + frontend
2. Spend hours debugging interconnected issues
```

---

## Token-Efficient Prompting

### Be Specific

**Efficient**:
```
Create a backend service for user ratings following
docs/templates/backend/service-template.md.

Methods needed:
- GetRating(userId) → RatingDto
- SubmitRating(fromId, toId, value) → Result
```

**Wasteful**:
```
I need you to create a service. It should handle ratings.
Users can rate other users. The rating should be stored.
We need to be able to get ratings too. Make sure it follows
our patterns. The patterns are... [repeats entire template]
```

### Reference, Don't Repeat

**Efficient**:
```
Add endpoint following docs/templates/backend/endpoint-template.md
for the RatingService we just created.
```

**Wasteful**:
```
Create an endpoint. It should use minimal API pattern.
Make sure to add RequireAuthorization. Use the standard
error response format. The format is { error: "..." }.
Register it in Program.cs...
```

### Use Templates

**Efficient**:
```
Create RatingEndpoints.cs using the endpoint template.
Routes:
- GET /api/ratings/{userId}
- POST /api/ratings
```

**Wasteful**:
```
Create a new file called RatingEndpoints.cs. In this file,
create a static class. Add a method called MapRatingEndpoints
that takes IEndpointRouteBuilder. Create a group at /api/ratings.
Add RequireAuthorization...
```

---

## Session Management

### Session Types

| Type | Duration | Use For |
|------|----------|---------|
| Micro | < 5 min | Quick fixes, typos, simple changes |
| Standard | 15-30 min | Single feature, one logical unit |
| Extended | 1-2 hours | Complex feature across stack |

### Starting a Session

Always start with:
```
Working on: [feature/bug name]
Context: [relevant files/docs]
Goal: [specific deliverable]
```

Example:
```
Working on: Add report user feature
Context: Similar to block feature in BlockService.cs
Goal: Complete backend (service + endpoint)
```

### Ending a Session

Summarize:
```
Completed:
- Created ReportService with interface
- Created ReportEndpoints at /api/reports
- Registered in Program.cs

Next session:
- Frontend service and component
```

---

## Efficient Patterns

### Pattern 1: Template-First

```
1. Point AI to template
2. Specify only what differs
3. Let AI fill in standard parts
```

Example:
```
Create FeatureService following service-template.md.
Specific methods:
- DoThing(userId, data) → Result<Thing>
- GetThings(userId) → List<Thing>
```

### Pattern 2: Similar-First

```
1. Point to existing similar code
2. Specify differences
3. AI adapts existing patterns
```

Example:
```
Create ReportService similar to BlockService.
Differences:
- Stores reason field
- Notifies moderator queue
```

### Pattern 3: Checklist-Driven

```
1. Share relevant checklist
2. AI uses it as implementation guide
3. Verify against same checklist
```

Example:
```
Implement feature using backend checklist from AI_WORKFLOW.md.
Verify each item as you go.
```

---

## What to Pre-Generate

### Generate Once, Use Many Times

Keep these in your docs for quick reference:

1. **Type definitions** - Common interfaces/types
2. **Error messages** - Standard user-facing messages
3. **Validation rules** - Business validation patterns
4. **SQL patterns** - Common query patterns

### Example: Pre-Generated Types

```typescript
// Save in a reference doc, AI can use without regenerating

export interface ApiError {
  error: string;
  message: string;
  details?: Record<string, string>;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ActionResult<T = void> {
  success: boolean;
  data?: T;
  error?: string;
}
```

---

## Anti-Patterns to Avoid

### 1. The Explanation Loop

**Problem**: Asking AI to explain then implement
```
"Explain how to create a service"
"Now create the service"
"Explain what you created"
```

**Solution**: Skip explanations, use templates
```
"Create service per service-template.md with these methods..."
```

### 2. The Perfection Spiral

**Problem**: Endless refinement
```
"Make it better"
"Can you improve this?"
"What about edge cases?"
"Make it more robust"
```

**Solution**: Specify requirements upfront
```
"Create service with:
- Input validation
- Error handling for [specific cases]
- Logging at INFO level"
```

### 3. The Context Dump

**Problem**: Pasting entire files for small changes
```
[Pastes 500 lines]
"Change line 42"
```

**Solution**: Reference files, specify locations
```
"In UserService.cs, modify GetUser method (line ~42) to also fetch ratings"
```

### 4. The Rebuild

**Problem**: Regenerating instead of modifying
```
"Recreate the component with this change..."
```

**Solution**: Request targeted edits
```
"In ProfileComponent, add a report button after the block button"
```

---

## Token Budget Guidelines

### Per Task Estimates

| Task Type | Estimated Tokens | Strategy |
|-----------|------------------|----------|
| Bug fix | 500-1000 | Direct, minimal context |
| New endpoint | 1000-2000 | Template reference |
| New component | 1500-3000 | Template + examples |
| Full feature | 3000-6000 | Batched sessions |
| Refactor | 2000-5000 | Incremental changes |

### Reducing Token Usage

1. **Use file references** instead of pasting code
2. **Use template references** instead of pattern descriptions
3. **Be specific** about what you need
4. **Batch related changes** in single sessions
5. **Verify incrementally** to catch issues early

---

## Quick Reference

| Strategy | Token Savings |
|----------|---------------|
| Reference templates | 50-70% |
| Point to similar code | 40-60% |
| Batch related changes | 30-50% |
| Specific prompts | 20-40% |
| Skip explanations | 20-30% |
