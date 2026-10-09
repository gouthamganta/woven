# Woven - Quickstart Guide

**Get Woven running locally in 5 minutes.**

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- [PostgreSQL 16+](https://postgresql.org/download/) (or Docker)

---

## 1. Clone & Setup Database

```bash
# Clone the repository
git clone https://github.com/gouthamganta/woven.git
cd woven

# Option A: Docker PostgreSQL (recommended)
docker run -d --name woven-db \
  -e POSTGRES_USER=woven \
  -e POSTGRES_PASSWORD=woven \
  -e POSTGRES_DB=woven_db \
  -p 5433:5432 \
  postgres:16-alpine

# Option B: Local PostgreSQL
createdb -U postgres woven_db
```

---

## 2. Configure Backend

```bash
cd backend/WovenBackend

# Initialize user secrets (keeps keys out of git)
dotnet user-secrets init

# Set required secrets
dotnet user-secrets set "Jwt:Key" "your-secret-key-min-32-characters-long-please"
dotnet user-secrets set "GoogleAuth:ClientId" "your-google-client-id.apps.googleusercontent.com"
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-openai-api-key"

# Connection string (if using custom DB settings)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=woven_db;Username=woven;Password=woven"
```

---

## 3. Run Migrations

```bash
# From backend/WovenBackend/
dotnet ef database update

# You should see: "Done."
```

---

## 4. Start Backend

```bash
# From backend/WovenBackend/
dotnet run

# ✅ Backend running at: http://localhost:5135
# ✅ Swagger UI at: http://localhost:5135/swagger
```

---

## 5. Start Frontend

```bash
# From frontend/woven-frontend/
npm install
npx ng serve --port 4202

# ✅ App running at: http://localhost:4202
```

---

## 6. Test It

1. Open http://localhost:4202
2. Click "Sign in with Google"
3. Complete onboarding
4. You're in!

---

## Common Issues

### "Connection refused" to database
- Check PostgreSQL is running: `docker ps` or `pg_isready`
- Verify port 5433 is correct in connection string

### "Npgsql.PostgresException: 42P01: relation does not exist"
- Run migrations: `dotnet ef database update`

### Build errors in backend
- Check .NET version: `dotnet --version` (must be 10.x)
- Run: `dotnet restore`

### Frontend won't start
- Check Node version: `node --version` (must be 22+)
- Delete `node_modules` and run `npm install` again

---

## Next Steps

- **[Full Setup Guide](development/setup.md)** - Detailed dev environment setup
- **[Architecture Overview](architecture/README.md)** - Understand the codebase
- **[CLAUDE.md](../CLAUDE.md)** - Project context for AI assistants
- **[Documentation Index](INDEX.md)** - Browse all docs

---

## Development Workflow

```bash
# Backend hot reload
cd backend/WovenBackend
dotnet watch run

# Frontend hot reload (already default with ng serve)
cd frontend/woven-frontend
npx ng serve --port 4202

# Run backend tests
dotnet test

# Frontend linting
npm run lint
```

---

**Need help?** Check [development/debugging.md](development/debugging.md) for common issues and solutions.
