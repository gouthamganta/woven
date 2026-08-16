# Local Development Setup

This guide gets you from a fresh checkout to a fully running local stack.

---

## Prerequisites

Install these before starting:

| Tool | Version |
|---|---|
| .NET SDK | 10 |
| Node.js | 22 |
| Docker Desktop | Latest (must be running) |
| Git | Any recent version |

Verify your installs:
```bash
dotnet --version       # should print 10.x.x
node --version         # should print 22.x.x
docker --version       # must be running, not just installed
```

---

## 1. Clone the repository

```bash
git clone <repo-url>
cd Woven
```

---

## 2. Start infrastructure services

The project ships a `docker-compose.yml` that manages PostgreSQL, Redis, and Azurite (local Azure Blob Storage emulator).

Start only the infrastructure containers (not the app containers):
```bash
docker compose up postgres redis azurite -d
```

This starts:

| Service | Image | Local port |
|---|---|---|
| PostgreSQL 16 + pgvector | `pgvector/pgvector:pg16` | 5433 |
| Redis 7 | `redis:7-alpine` | 6379 |
| Azurite (blob emulator) | `mcr.microsoft.com/azure-storage/azurite:latest` | 10000 |

PostgreSQL credentials: user `woven`, password `woven`, database `woven_db`.

Wait a few seconds for Postgres to finish initializing before proceeding.

---

## 3. Configure developer secrets

Two secrets are required that are not in source control:

- `OpenAI__ApiKey` — your OpenAI API key (needed for AI features: ECHO explanations, KnowMe agent, Red/Green Flag agent, tagging)
- `Jwt__Key` — JWT signing secret, must be at least 32 characters

The recommended approach is .NET User Secrets (stored outside the project directory, never committed):

```bash
cd backend/WovenBackend
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "Jwt:Key" "your-long-random-secret-at-least-32-chars"
```

Everything else (connection strings, Redis URL, Azurite connection string) already has working defaults in `appsettings.json` that match the Docker Compose setup above.

---

## 4. Apply database migrations

From the backend directory:
```bash
cd backend/WovenBackend
dotnet ef database update
```

This applies all pending EF Core migrations to `woven_db` on port 5433.

**pgvector note:** pgvector columns cannot be applied via EF Migrations in the local environment because the pgvector extension is only available inside the Docker container. If any migration requires a pgvector column, apply the raw SQL manually:
```bash
docker compose exec postgres psql -U woven -d woven_db
```
Then run the SQL from the migration comment. This situation is documented per-migration when it applies.

---

## 5. Start the backend

```bash
cd backend/WovenBackend
dotnet run
```

The API starts on port **5135**. You should see output like:
```
Now listening on: http://localhost:5135
```

Verify: open `http://localhost:5135/health` in a browser or with curl. You should get a `200 OK`.

---

## 6. Install frontend dependencies

In a new terminal:
```bash
cd frontend/woven-frontend
npm ci
```

Use `npm ci` (not `npm install`) to get a reproducible install from `package-lock.json`.

---

## 7. Start the frontend dev server

```bash
cd frontend/woven-frontend
npx ng serve --port 4202
```

The Angular dev server starts on port **4202**. The app makes API calls to `http://localhost:5135` directly — there is no proxy configuration.

---

## 8. Verify the stack

| Check | URL | Expected |
|---|---|---|
| API health | `http://localhost:5135/health` | `200 OK` |
| Frontend | `http://localhost:4202` | Login screen |

If the frontend shows a blank screen or network errors, confirm the backend is running on 5135 and there are no CORS errors in the browser console.

---

## Running the full stack with Docker Compose

To run the entire application in containers (no local .NET or Node needed):
```bash
docker compose up
```

This starts all five services: `postgres`, `azurite`, `redis`, `backend` (port 5135), `frontend` (port 80).

Note: the containerized backend still needs `OpenAI__ApiKey` and `Jwt__Key`. Pass them via environment variables or a `.env` file before running `docker compose up`.

---

## Running tests

**Backend tests** (from the `backend/` directory):
```bash
dotnet test Woven.sln --no-build --configuration Release --verbosity normal
```

**Frontend tests** (from `frontend/woven-frontend/`):
```bash
npm test -- --watch=false
```

CI runs both test suites on every push to `master` and on every PR targeting `master`.

---

## Build verification (required before every PR)

**Backend — must produce 0 errors:**
```bash
cd backend/WovenBackend
dotnet build
```

**Frontend — must produce 0 errors:**
```bash
cd frontend/woven-frontend
npx ng build --configuration development
```

Do not open a PR until both commands pass clean. See `docs/contributing/CONTRIBUTING.md` for the full mandate.

---

## Configuration reference

Key values from `appsettings.json` (all work out of the box for local dev):

| Key | Default value |
|---|---|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5433;Database=woven_db;Username=woven;Password=woven` |
| `Redis:ConnectionString` | `localhost:6379,abortConnect=false` |
| `Azure:Storage:ConnectionString` | `UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://localhost:10000` |
| `OpenAI:Model` | `gpt-4.1-mini` |
| `OpenAI:DailyBudgetUsd` | `50.0` |
| `Jwt:ExpiryMinutes` | `60` |
| `Moderation:IsModerationEnabled` | `false` |

**Must be overridden (no working default):**

| Key | How to set |
|---|---|
| `OpenAI:ApiKey` | `dotnet user-secrets set "OpenAI:ApiKey" "sk-..."` |
| `Jwt:Key` | `dotnet user-secrets set "Jwt:Key" "min-32-char-secret"` |
