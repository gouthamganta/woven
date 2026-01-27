# Contributing to Woven

Thank you for your interest in contributing to Woven! This guide will help you get started.

## Code of Conduct

Be respectful, inclusive, and constructive. We're all here to build something great together.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Set up the development environment (see README.md)
4. Create a feature branch

```bash
git checkout -b feature/your-feature-name
```

## Development Workflow

### 1. Pick an Issue

- Check the issue tracker for open issues
- Comment on the issue to claim it
- If you have a new idea, open an issue first to discuss

### 2. Write Code

Follow these guidelines:

#### Backend (C#)

```csharp
// Use nullable reference types
public string? OptionalField { get; set; }

// Use async/await consistently
public async Task<T> DoSomethingAsync(CancellationToken ct)

// Return typed Results in endpoints
return Results.Ok(new { status = "SUCCESS" });
return Results.BadRequest(new { error = "ERROR_CODE" });

// Use dependency injection
public class MyService(ILogger<MyService> logger, WovenDbContext db)
```

#### Frontend (TypeScript)

```typescript
// Use strict typing
interface MyData {
  id: string;
  name: string;
  optional?: string | null;
}

// Use async/await with firstValueFrom for one-off calls
const data = await firstValueFrom(this.api.getData());

// Use standalone components
@Component({
  standalone: true,
  imports: [CommonModule],
})
```

### 3. Write Tests

- Unit tests for business logic
- Integration tests for API endpoints
- Component tests for frontend

### 4. Commit Your Changes

Use conventional commit messages:

```
feat: add trial decision modal
fix: correct rating validation range
docs: update API documentation
refactor: simplify match scoring logic
test: add unit tests for budget service
chore: update dependencies
```

### 5. Submit a Pull Request

- Fill out the PR template
- Link related issues
- Ensure CI passes
- Request review

## Pull Request Guidelines

### Title

Use conventional commit format:
```
feat: short description of the feature
```

### Description Template

```markdown
## Summary
Brief description of changes.

## Changes
- Added X
- Updated Y
- Fixed Z

## Testing
- [ ] Unit tests added/updated
- [ ] Manual testing performed
- [ ] No regressions found

## Screenshots (if applicable)
```

### Review Process

1. Automated checks must pass
2. At least one approving review required
3. No unresolved comments
4. Branch must be up-to-date with main

## Code Review Checklist

Reviewers should check:

- [ ] Code follows project conventions
- [ ] No security vulnerabilities introduced
- [ ] Error handling is appropriate
- [ ] Tests cover new functionality
- [ ] No hardcoded secrets or credentials
- [ ] Database migrations are safe
- [ ] API changes are documented

## Architecture Decisions

For significant changes, create an Architecture Decision Record (ADR):

```markdown
# ADR-001: Use PostgreSQL for Database

## Status
Accepted

## Context
We need a relational database for the application.

## Decision
Use PostgreSQL for its reliability, JSON support, and ecosystem.

## Consequences
- Positive: Strong ACID compliance, great tooling
- Negative: More complex than SQLite for development
```

## Database Migrations

When modifying the database:

1. Create a migration:
   ```bash
   dotnet ef migrations add DescriptiveName
   ```

2. Review the generated migration

3. Test on a development database:
   ```bash
   dotnet ef database update
   ```

4. Include migration in your PR

**Never:**
- Delete or modify existing migrations that are in production
- Make breaking changes without a migration strategy

## Security Guidelines

- Never commit secrets or credentials
- Use environment variables for configuration
- Validate all user input
- Use parameterized queries (EF Core handles this)
- Check authorization in every endpoint
- Log security events appropriately

## Questions?

- Open a discussion on GitHub
- Tag maintainers for urgent issues
- Check existing documentation first

## Recognition

Contributors will be recognized in:
- GitHub contributors list
- Release notes for significant contributions
- README acknowledgments

Thank you for contributing to Woven!
