# Woven Developer Guide

Welcome to the Woven development documentation. This guide covers everything you need to develop, test, and deploy Woven.

---

## Quick Start

**New to the project?** Start here:

1. **[Setup Guide](setup.md)** — Get your local environment running (10 minutes)
2. **[Code Patterns](patterns.md)** — Learn the project's architectural patterns
3. **[Contributing Guide](contributing.md)** — Understand our workflow and standards

---

## Documentation Index

### Getting Started
- **[Local Setup](setup.md)** — Install dependencies, start services, verify the stack
- **[Code Patterns](patterns.md)** — MapEndpoints, OnPush, Services layer, common patterns

### Development Workflow
- **[Contributing Guide](contributing.md)** — PR workflow, code review, design rules
- **[Git Workflow](git-workflow.md)** — Branch strategy, commit messages, rebasing
- **[Testing](testing.md)** — Run tests, CI pipeline, zero-errors mandate

### Technical Guides
- **[Database Migrations](database-migrations.md)** — Create and apply EF migrations
- **[Debugging](debugging.md)** — Common issues, troubleshooting, logging
- **[Deployment](deployment.md)** — CI/CD pipeline, Azure deployment, smoke checks
- **[Monitoring](monitoring.md)** — Logs, Application Insights, alerts

---

## Tech Stack

### Backend
- **Framework:** ASP.NET Core 10 (Minimal API)
- **Database:** PostgreSQL 16 + pgvector
- **Cache:** Redis 7
- **ORM:** Entity Framework Core 10
- **Auth:** JWT Bearer + HttpOnly cookies
- **AI:** OpenAI gpt-4.1-mini

### Frontend
- **Framework:** Angular 21
- **Language:** TypeScript 5.x
- **State:** OnPush change detection
- **HTTP:** Standalone HttpClient
- **Routing:** Angular Router

### Infrastructure
- **Cloud:** Azure (Container Apps, PostgreSQL Flexible Server, Redis, Blob Storage)
- **CI/CD:** GitHub Actions
- **IaC:** Terraform
- **Containers:** Docker + Docker Compose (local dev)

---

## Project Structure

```
Woven/
├── backend/
│   ├── WovenBackend/
│   │   ├── Endpoints/          # Minimal API route groups
│   │   ├── Services/           # Business logic layer
│   │   ├── Data/              # EF Core entities + DbContext
│   │   ├── Infrastructure/     # Middleware, exception handlers
│   │   ├── Auth/              # JWT + cookie helpers
│   │   └── Migrations/        # EF Core migrations
│   └── Woven.sln
├── frontend/
│   └── woven-frontend/
│       ├── src/app/
│       │   ├── pages/         # Page components
│       │   ├── components/    # Shared components
│       │   └── services/      # HTTP services
│       └── package.json
├── infra/
│   └── main.tf               # Terraform (all Azure resources)
├── docs/
│   ├── api/                  # API reference
│   ├── development/          # This directory
│   ├── architecture/         # System design
│   └── business/             # Product docs
└── docker-compose.yml        # Local dev environment
```

---

## Development Principles

### Evidence Over Speculation

Every implementation decision must be traceable to a concrete requirement. Do not add features or abstractions "in case we need them later."

### No Premature Abstraction

Write the simplest version that works. Introduce interfaces or base classes only when you have two concrete implementations that actually need it.

### Invisible AI

The app never shows users raw compatibility scores, community ratings, or AI confidence values. Every AI surface is ambient UX.

### Zero-Errors Mandate

Both `dotnet build` and `npx ng build` must produce **0 errors** before any change is considered complete.

**Backend:**
```bash
cd backend/WovenBackend
dotnet build
```

**Frontend:**
```bash
cd frontend/woven-frontend
npx ng build --configuration development
```

---

## Key Workflows

### Making a Change

1. Branch off `master`: `git checkout -b feature/your-feature`
2. Make your changes
3. Run `dotnet build` (backend) or `npx ng build` (frontend) — fix all errors
4. Run tests: `dotnet test` or `npm test -- --watch=false`
5. Commit with descriptive message
6. Open PR, fill in description
7. Request review, wait for CI to pass
8. Merge and delete branch

### Running Tests Locally

**Backend:**
```bash
cd backend/
dotnet test Woven.sln --no-build --configuration Release --verbosity normal
```

**Frontend:**
```bash
cd frontend/woven-frontend/
npm test -- --watch=false
```

### Applying Database Migrations

```bash
cd backend/WovenBackend
dotnet ef database update
```

See [database-migrations.md](database-migrations.md) for creating new migrations.

---

## Common Tasks

### Add a New Endpoint

1. Create `XxxEndpoints.cs` in `backend/WovenBackend/Endpoints/`
2. Follow MapEndpoints pattern (see [patterns.md](patterns.md))
3. Register in `Program.cs`: `app.MapXxxEndpoints();`
4. Add signal recording if behavioral event
5. Write tests

### Add a New Page

1. Generate component: `npx ng generate component pages/your-page`
2. Add route to `app.routes.ts`
3. Use `ChangeDetectionStrategy.OnPush`
4. HTTP calls go through services, not direct `HttpClient`
5. Call `cdr.markForCheck()` after async state changes

### Run Locally with Docker

```bash
# Start infrastructure only
docker compose up postgres redis azurite -d

# Or start full stack (backend + frontend + infra)
docker compose up
```

See [setup.md](setup.md) for full details.

---

## Getting Help

- **Code patterns?** → Read [patterns.md](patterns.md)
- **Build failing?** → Check [debugging.md](debugging.md)
- **How to contribute?** → Read [contributing.md](contributing.md)
- **Deploy questions?** → See [deployment.md](deployment.md)
- **Logs/monitoring?** → Check [monitoring.md](monitoring.md)

---

## Related Documentation

- **[API Reference](../api/README.md)** — Complete API endpoint documentation
- **[Architecture Overview](../architecture/README.md)** — System design and components
- **[Security Guide](../security/README.md)** — Security practices and auditing
- **[Business Logic](../business/rules.md)** — Product rules and mechanics

---

**Last Updated:** 2026-10-07  
**Maintained By:** Woven Engineering Team
