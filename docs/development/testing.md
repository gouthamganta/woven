# Testing Guide

**Consolidated from:** `docs/technical/TESTING.md`

---

## The Zero-Errors Mandate

Before any change is considered complete, both build commands must produce zero errors:

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

A change that compiles with warnings is acceptable. **A change that produces errors is not mergeable.**

---

## Backend Tests

### Running Locally

```bash
cd backend/
dotnet test Woven.sln --no-build --configuration Release --verbosity normal
```

**Flags:**
- `--no-build` — Assumes you already ran `dotnet build`
- `--configuration Release` — Uses optimized release configuration
- `--verbosity normal` — Shows test names and results

**Alternative (with build):**
```bash
dotnet test Woven.sln --configuration Release
```

---

### What Gets Tested

All test projects in `Woven.sln` are discovered and executed automatically. No test project needs to be specified individually.

---

## Frontend Tests

### Running Locally

```bash
cd frontend/woven-frontend/
npm test -- --watch=false
```

**Flags:**
- `--watch=false` — Runs once and exits (required for CI)
- Without it, test runner stays in watch mode for development

**Development mode (auto-rerun on changes):**
```bash
npm test
```

---

## CI Pipeline

Both test suites run automatically on every push and pull request to `main` / `master`.

### Backend CI Steps

| Step | Command | Notes |
|---|---|---|
| Setup .NET | `actions/setup-dotnet@v4` | Version 10.0.x |
| NuGet cache | Cache key: `nuget-{os}-{hash(.csproj)}` | Invalidated when any .csproj changes |
| Restore | `dotnet restore` | Restores NuGet packages |
| Build | `dotnet build --configuration Release --no-restore` | Must produce 0 errors |
| Test | `dotnet test --configuration Release --no-build` | All test projects run |

---

### Frontend CI Steps

| Step | Command | Notes |
|---|---|---|
| Setup Node | `actions/setup-node@v4` | Version 22, npm cache enabled |
| Install | `npm ci` | Clean install from lock file |
| Build | `npm run build` | Production configuration |
| Test | `npm test -- --watch=false` | Single-run mode |

**Both jobs must pass for PR to be mergeable.**

---

## Concurrency

CI uses `cancel-in-progress` per ref — if a second push arrives on the same branch while CI is running, the in-flight run is cancelled.

```yaml
concurrency:
  group: ${{ github.ref }}
  cancel-in-progress: true
```

This keeps CI feedback current without wasting runner time.

---

## Post-Deploy Smoke Checks

These run **after deployment**, not before merge. They verify the deployed artifact starts correctly.

### Backend Smoke Check

Polls the backend Container App revision for healthy state:

- **30 attempts × 10 seconds = 5 minutes max**
- Uses Azure CLI to query revision health
- Fails deployment if no healthy revision within window

**Command:**
```bash
az containerapp revision list \
  --name woven-prod-backend \
  --resource-group woven-prod-rg \
  --query "[?properties.healthState=='Healthy']"
```

---

### Frontend Smoke Check

HTTP GET against the frontend Container App URL:

- **15 attempts × 10 seconds = 2.5 minutes max**
- Expects HTTP 200 from nginx
- Fails deployment if no 200 within window

**Command:**
```bash
FQDN=$(az containerapp show --name woven-prod-frontend ... --query properties.configuration.ingress.fqdn)
curl -f https://$FQDN
```

---

## Local Development Verification Workflow

1. Make code change
2. Run `dotnet build` (backend) or `npx ng build` (frontend) — fix all errors
3. Run `dotnet test` (backend) or `npm test -- --watch=false` (frontend)
4. Commit and push — CI runs both suites automatically

---

## What Is Not Documented

The following testing infrastructure was not identified in reviewed source files:

- **Integration tests** — No integration test project confirmed
- **Mocking framework** — No specific library (Moq, NSubstitute) documented
- **E2E tests** — No Playwright, Cypress, or similar framework documented

This does not mean these do not exist — they were not present in the reviewed documentation.

---

**Source:** Consolidated from `docs/technical/TESTING.md`  
**Last Updated:** 2026-10-07
