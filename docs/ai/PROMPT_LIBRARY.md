# Woven Prompt Library

> Ready-to-use prompts for common development tasks.
> Copy, customize, and use these prompts for efficient AI development.

---

## Backend Prompts

### Create a New Service

```
Create a backend service for {FEATURE} following docs/templates/backend/service-template.md.

Service: {Feature}Service
Interface: I{Feature}Service

Methods:
- {MethodName}({params}) → {ReturnType}
- {MethodName}({params}) → {ReturnType}

Business rules:
- {Rule 1}
- {Rule 2}

Register as: Scoped
```

### Create a New Endpoint

```
Create minimal API endpoints for {Feature} following docs/templates/backend/endpoint-template.md.

File: Endpoints/{Feature}Endpoints.cs
Base route: /api/{feature}

Endpoints:
- GET / → List all
- GET /{id} → Get by ID
- POST / → Create
- PUT /{id} → Update
- DELETE /{id} → Delete

Use {Feature}Service for business logic.
```

### Create a Database Migration

```
Create migration for {description}.

Changes:
- Add {table/column}
- Modify {table/column}

Follow docs/templates/backend/migration-template.md.
Ensure Down() properly reverses Up().
```

### Add a New Action Endpoint

```
Add action endpoint to {Feature}Endpoints.cs:

Route: POST /api/{feature}/{id}/{action}
Service method: {Service}.{Method}

Request body:
- {field}: {type}

Response:
- {field}: {type}

Include proper error handling following standard error format.
```

---

## Frontend Prompts

### Create a New Page Component

```
Create a page component for {feature} following docs/templates/frontend/component-template.md.

Files:
- pages/{feature}/{feature}.page.ts
- pages/{feature}/{feature}.page.html
- pages/{feature}/{feature}.page.scss

Features:
- {Feature 1}
- {Feature 2}

States to handle:
- Loading
- Error
- Empty (if applicable)
- Success with data

Ensure all touch targets are ≥ 44px.
```

### Create a New Service

```
Create frontend service for {feature} following docs/templates/frontend/service-template.md.

File: services/{feature}.service.ts

API endpoints:
- GET /api/{feature} → list()
- GET /api/{feature}/{id} → get(id)
- POST /api/{feature} → create(data)
- PUT /api/{feature}/{id} → update(id, data)
- DELETE /api/{feature}/{id} → delete(id)

Include TypeScript interfaces for all request/response types.
```

### Add a New Component Feature

```
Add {feature} to {Component}Component.

Location: {file path}

Changes:
- Add {UI element}
- Add {handler method}
- Add {state variable}

Styling:
- Follow existing component patterns
- Ensure 44px touch targets
```

### Create a Modal/Dialog

```
Create {feature} modal component following frontend patterns.

Trigger: {how it opens}
Content: {what it shows}
Actions: {buttons/actions}

States:
- Default view
- Loading (during submit)
- Error (if action fails)

Close on: backdrop click, X button, successful action
```

---

## Full Stack Prompts

### Add a Complete Feature

```
Add {feature} feature to Woven.

## Backend

1. Model (if new):
   - Entity: {EntityName}
   - Fields: {list fields}

2. Service:
   - File: Services/{Feature}Service.cs
   - Methods: {list methods}

3. Endpoint:
   - File: Endpoints/{Feature}Endpoints.cs
   - Routes: {list routes}

## Frontend

1. Service:
   - File: services/{feature}.service.ts
   - Methods matching backend

2. Component:
   - File: pages/{feature}/{feature}.page.ts
   - Features: {list features}

Follow templates in docs/templates/.
```

### Add CRUD Operations

```
Add full CRUD for {Entity} across the stack.

## Backend
1. Entity: Models/{Entity}.cs
2. Service: Services/{Entity}Service.cs
3. Endpoints: Endpoints/{Entity}Endpoints.cs
   - GET /api/{entities}
   - GET /api/{entities}/{id}
   - POST /api/{entities}
   - PUT /api/{entities}/{id}
   - DELETE /api/{entities}/{id}

## Frontend
1. Service: services/{entity}.service.ts
2. List page: pages/{entities}/{entities}.page.ts
3. Detail/edit: pages/{entities}/{entity}-detail.page.ts

Follow existing patterns for similar features.
```

---

## Bug Fix Prompts

### Debug and Fix

```
Bug: {description of bug}

Expected: {expected behavior}
Actual: {actual behavior}

Location: {suspected file/area}

Please:
1. Identify the root cause
2. Propose a fix
3. Implement the fix
4. Verify no regression
```

### Fix UI Issue

```
UI issue in {component}:

Problem: {description}
Expected: {expected appearance/behavior}

Please fix while:
- Maintaining existing design patterns
- Keeping touch targets ≥ 44px
- Not affecting other components
```

### Fix API Error

```
API error in {endpoint}:

Error: {error message}
Request: {method} {path}
Payload: {if applicable}

Please:
1. Trace the error source
2. Fix the issue
3. Add appropriate error handling
4. Return user-friendly error message
```

---

## Refactoring Prompts

### Extract to Service

```
Extract {functionality} from {Component} to a service.

Current location: {file path}
Code to extract: {description or line numbers}

New service:
- File: services/{feature}.service.ts
- Methods to create: {list methods}

Update component to use new service.
```

### Consolidate Duplicates

```
Consolidate duplicate {code type} found in:
- {file 1}
- {file 2}
- {file 3}

Create shared:
- Location: {where to put shared code}
- Update all usages

Preserve all existing behavior.
```

### Improve Component Structure

```
Refactor {Component} to improve structure.

Current issues:
- {issue 1}
- {issue 2}

Goals:
- {goal 1}
- {goal 2}

Preserve all existing functionality.
Follow component template patterns.
```

---

## Documentation Prompts

### Document a Service

```
Add JSDoc documentation to {Service}.

Document:
- Class description
- Each public method
- Parameters and return types
- Any thrown exceptions
```

### Document an Endpoint

```
Add OpenAPI/Swagger documentation to {Endpoints}.

For each endpoint:
- Summary
- Description
- Parameters
- Request body schema
- Response schemas (success + error)
```

---

## Testing Prompts

### Write Unit Tests

```
Write unit tests for {Service/Component}.

Test cases:
- {case 1}
- {case 2}
- {case 3}

Include:
- Happy path
- Error cases
- Edge cases
```

---

## Quick Customization

Replace these placeholders in any prompt:

| Placeholder | Replace With |
|-------------|--------------|
| `{feature}` | Feature name (lowercase) |
| `{Feature}` | Feature name (PascalCase) |
| `{FEATURE}` | Feature name (description) |
| `{Entity}` | Database entity name |
| `{entities}` | Plural route name |
| `{Component}` | Component class name |
| `{Service}` | Service class name |
| `{file path}` | Full path to file |
| `{description}` | Clear description |

---

## Best Practices for Prompts

1. **Be specific** - Vague prompts get vague results
2. **Reference templates** - Don't repeat pattern descriptions
3. **List requirements** - Bullet points are clear
4. **Specify constraints** - Touch targets, auth, etc.
5. **Include context** - Similar code, related files
