# Local Development Setup

This guide gets you from a fresh checkout to a fully running local stack in 10 minutes.

**Consolidated from:** `docs/contributing/LOCAL_SETUP.md`

---

## Prerequisites

Install these before starting:

| Tool | Version | Download |
|---|---|---|
| .NET SDK | 10.x | https://dotnet.microsoft.com/download |
| Node.js | 22.x | https://nodejs.org/ |
| Docker Desktop | Latest | https://www.docker.com/products/docker-desktop |
| Git | Any recent | https://git-scm.com/ |

**Verify installations:**
```bash
dotnet --version       # should print 10.x.x
node --version         # should print 22.x.x
docker --version       # verify Docker is installed
docker ps              # verify Docker is running
```

---

## Setup Steps

### 1. Clone Repository

```bash
git clone https://github.com/gouthamganta/woven.git
cd woven
```

---

### 2. Start Infrastructure Services

The project uses Docker Compose for local PostgreSQL, Redis, and Azure Blob Storage emulation.

**Start infrastructure containers:**
```bash
docker compose up postgres redis azurite -d
```

**What starts:**

| Service | Image | Local Port | Credentials |
|---|---|---|---|
| PostgreSQL 16 + pgvector | `pgvector/pgvector:pg16` | 5433 | user: `woven`, password: `woven`, db: `woven_db` |
| Redis 7 | `redis:7-alpine` | 6379 | No password |
| Azurite (Blob emulator) | `mcr.microsoft.com/azure-storage/azurite` | 10000 | Development storage |

**Wait for Postgres to initialize** (10-15 seconds) before proceeding.

**Verify services:**
```bash
docker compose ps
```

All three services should show status "running".

---

### 3. Configure Secrets

Two secrets are required that are **not** in source control:

1. **OpenAI API Key** — Required for AI features (match explanations, games, coaching)
2. **JWT Signing Key** — Must be at least 32 characters

**Recommended: .NET User Secrets** (stored outside project, never committed)

```bash
cd backend/WovenBackend
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-openai-key-here"
dotnet user-secrets set "Jwt:Key" "your-long-random-secret-at-least-32-characters-long"
```

**Alternative: Environment Variables**

```bash
# Windows PowerShell
$env:OpenAI__ApiKey = "sk-..."
$env:Jwt__Key = "your-32-char-secret"

# Linux/Mac
export OpenAI__ApiKey="sk-..."
export Jwt__Key="your-32-char-secret"
```

**All other settings** (connection strings, Redis URL, Azurite) have working defaults in `appsettings.json`.

---

### 4. Apply Database Migrations

Create the database schema:

```bash
cd backend/WovenBackend
dotnet ef database update
```

This applies all pending EF Core migrations to `woven_db` on port 5433.

**Success output:**
```
Build started...
Build succeeded.
Applying migration '20260525004334_InitialCreate'.
Applying migration '20260603000002_AddBridgeQuestion'.
...
Done.
```

**Troubleshooting:**
- If `dotnet ef` command not found: `dotnet tool install --global dotnet-ef`
- If connection fails: verify Postgres container is running (`docker compose ps`)

**pgvector Note:** pgvector columns work automatically in Docker. If running native Postgres locally, you may need to manually install the pgvector extension.

---

### 5. Start Backend

```bash
cd backend/WovenBackend
dotnet run
```

**Expected output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5135
```

**Verify backend is running:**
```bash
curl http://localhost:5135/health
```

Expected: HTTP 200 with health check response.

---

### 6. Install Frontend Dependencies

In a **new terminal** (keep backend running):

```bash
cd frontend/woven-frontend
npm ci
```

Use `npm ci` (not `npm install`) for reproducible installs from `package-lock.json`.

**Expected output:**
```
added 1234 packages in 30s
```

---

### 7. Start Frontend Dev Server

```bash
cd frontend/woven-frontend
npx ng serve --port 4202
```

**Expected output:**
```
** Angular Live Development Server is listening on localhost:4202 **
✔ Compiled successfully.
```

**The app makes API calls to** `http://localhost:5135` **directly** — there is no proxy configuration.

---

## Verify the Stack

| Component | URL | Expected |
|---|---|---|
| Backend Health | http://localhost:5135/health | HTTP 200 |
| Frontend | http://localhost:4202 | Login screen |
| PostgreSQL | localhost:5433 | Connect with `psql` |
| Redis | localhost:6379 | Connect with `redis-cli` |
| Azurite | http://localhost:10000 | Blob storage endpoint |

**If frontend shows blank screen:**
1. Check browser console for errors
2. Verify backend is running on port 5135
3. Check for CORS errors (should not occur locally)

---

## Running Full Stack with Docker

To run **entire application** in containers (no local .NET or Node needed):

```bash
docker compose up
```

**What starts:**
- `postgres` (port 5433)
- `redis` (port 6379)
- `azurite` (port 10000)
- `backend` (port 5135)
- `frontend` (port 80)

**Secrets required:** Set `OpenAI__ApiKey` and `Jwt__Key` via `.env` file:

```env
# .env file in project root
OpenAI__ApiKey=sk-your-key
Jwt__Key=your-32-char-secret
```

---

## Running Tests

**Backend tests:**
```bash
cd backend/
dotnet test Woven.sln --no-build --configuration Release --verbosity normal
```

**Frontend tests:**
```bash
cd frontend/woven-frontend/
npm test -- --watch=false
```

CI runs both test suites on every push to `master` and on every PR.

---

## Build Verification (Pre-PR Checklist)

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

**Do not open a PR** until both commands pass clean. See [contributing.md](contributing.md) for full requirements.

---

## Configuration Reference

### Backend (`appsettings.json`)

**Default values (work out of box):**

| Key | Value |
|---|---|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5433;Database=woven_db;Username=woven;Password=woven` |
| `Redis:ConnectionString` | `localhost:6379,abortConnect=false` |
| `Azure:Storage:ConnectionString` | `UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://localhost:10000` |
| `OpenAI:Model` | `gpt-4.1-mini` |
| `Jwt:ExpiryMinutes` | `43200` (30 days) |

**Must be overridden (no default):**

| Key | How to Set |
|---|---|
| `OpenAI:ApiKey` | User Secrets or env var |
| `Jwt:Key` | User Secrets or env var |

---

## Troubleshooting

### "Connection refused" errors

**Problem:** Backend cannot connect to Postgres/Redis  
**Solution:** Ensure Docker services are running:
```bash
docker compose ps
```

All services should show "Up". If not:
```bash
docker compose up postgres redis azurite -d
```

---

### "dotnet ef" command not found

**Problem:** EF Core tools not installed  
**Solution:** Install global tool:
```bash
dotnet tool install --global dotnet-ef
```

---

### Frontend network errors

**Problem:** API calls failing with CORS or network errors  
**Solution:** 
1. Verify backend is running: `curl http://localhost:5135/health`
2. Check frontend console for exact error
3. Ensure you're accessing frontend at `http://localhost:4202` (not https)

---

### Port already in use

**Problem:** "Address already in use" error  
**Solution:**

**Backend (port 5135):**
```bash
# Windows
netstat -ano | findstr :5135
taskkill /PID <pid> /F

# Linux/Mac
lsof -ti:5135 | xargs kill
```

**Frontend (port 4202):**
```bash
# Windows
netstat -ano | findstr :4202
taskkill /PID <pid> /F

# Linux/Mac
lsof -ti:4202 | xargs kill
```

---

**Next Steps:**
- Read [patterns.md](patterns.md) to learn code architecture
- Review [contributing.md](contributing.md) for PR workflow
- Check [debugging.md](debugging.md) for common issues

---

**Source:** Consolidated from `docs/contributing/LOCAL_SETUP.md`  
**Last Updated:** 2026-10-07
