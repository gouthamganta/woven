# Woven — Testing

---

## The Zero-Errors Mandate

Before any change is considered complete — whether backend or frontend — both build commands must produce zero errors:

```bash
# Backend — run from backend/WovenBackend/
dotnet build

# Frontend — run from frontend/woven-frontend/
npx ng build --configuration development
```

A change that compiles with warnings is acceptable. A change that produces errors is not mergeable.

---

## Backend Tests

### Running Locally

```bash
# From the backend/ directory
dotnet test Woven.sln --no-build --configuration Release --verbosity normal
```

`--no-build` assumes you have already run `dotnet build` to verify zero errors. Pass `--build` if you want the test runner to build first.

### What Gets Tested

All test projects in `Woven.sln` are discovered and executed automatically. No test project needs to be specified individually.

---

## Frontend Tests

### Running Locally

```bash
# From frontend/woven-frontend/
npm test -- --watch=false
```

`--watch=false` runs the test suite once and exits (required for CI and for verifying a change). Without it, the test runner stays open in watch mode.

---

## CI Pipeline

Both test suites run automatically on every push and pull request to `main` / `master`.

### Trigger Events

```yaml
on:
  push:
    branches: [main, master]
  pull_request:
    branches: [main, master]
  workflow_call:   # allows this workflow to be called by other workflows
```

### Concurrency

The CI workflow uses `cancel-in-progress` per ref — if a second push arrives on the same branch while CI is running, the in-flight run is cancelled and a new one starts. This keeps CI feedback current without wasting runner time.

### Backend CI Steps (in order)

| Step | Tool / Action | Notes |
|---|---|---|
| Set up .NET | `actions/setup-dotnet@v4` | Version `10.0.x` |
| NuGet cache | Cache key: `nuget-${{ runner.os }}-${{ hashFiles('**/*.csproj') }}` | Invalidated when any .csproj changes |
| Restore | `dotnet restore` | Restores all NuGet packages |
| Build | `dotnet build --configuration Release --no-restore` | Release config; zero errors required |
| Test | `dotnet test --configuration Release --no-build --verbosity normal` | All test projects in solution |

### Frontend CI Steps (in order)

| Step | Tool / Action | Notes |
|---|---|---|
| Set up Node | `actions/setup-node@v4` | Version `22`, npm cache enabled |
| Install | `npm ci` | Clean install from `package-lock.json` |
| Build | `npm run build` | Production configuration |
| Test | `npm test -- --watch=false` | Single-run, no watch mode |

Both backend and frontend jobs must pass for a pull request to be mergeable.

---

## What Is Not Documented

The following testing infrastructure items were not identified in reviewed source files:

- **Integration test harness**: no integration test project or harness is explicitly documented. Tests against the live PostgreSQL database or Service Bus are not confirmed to exist.
- **Mocking framework**: no specific mocking library (e.g., Moq, NSubstitute) is documented in reviewed files.
- **End-to-end tests**: no Playwright, Cypress, or similar E2E framework is documented.

This does not mean these do not exist — it means they were not present in the reviewed source set.

---

## Post-Deploy Smoke Checks (CD, not pre-merge)

The following checks run after deployment, not before merge. They are part of the CD pipeline, not the CI test suite.

### Backend Smoke Check

The CD pipeline polls the backend `/health` endpoint after a new Container App revision is activated:

- Up to 30 attempts, 10-second intervals (5 minutes total)
- Uses the Azure CLI to query the revision's health status
- Fails the deployment if the revision does not become healthy within the window

### Frontend Smoke Check

The CD pipeline performs an HTTP GET against the frontend Container App URL after deployment:

- Up to 15 attempts, 10-second intervals (2.5 minutes total)
- Expects HTTP 200 from the nginx `/health` route
- Fails the deployment if the frontend does not respond within the window

These smoke checks do not replace the pre-merge CI test suite — they verify that the deployed artifact starts correctly.

---

## Local Development Verification Workflow

1. Make code change.
2. Run `dotnet build` (backend) or `npx ng build --configuration development` (frontend). Fix all errors.
3. Run `dotnet test` (backend) or `npm test -- --watch=false` (frontend).
4. Commit and push — CI will run both suites again automatically.
