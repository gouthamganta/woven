# Debugging Guide

Common issues and troubleshooting steps for local development.

---

## Backend Issues

### "Connection refused" to PostgreSQL

**Problem:** Backend cannot connect to database  
**Symptoms:** `Npgsql.NpgsqlException: Connection refused`

**Solution:**
```bash
# Check if Postgres container is running
docker compose ps

# If not running, start it
docker compose up postgres -d

# Wait 10 seconds for initialization, then retry
```

**Verify connection:**
```bash
# Windows PowerShell
docker exec -it woven-postgres-1 psql -U woven -d woven_db

# Linux/Mac
docker exec -it woven-postgres-1 psql -U woven -d woven_db
```

---

### "Connection refused" to Redis

**Problem:** Backend cannot connect to Redis  
**Symptoms:** `StackExchange.Redis.RedisConnectionException`

**Solution:**
```bash
# Check if Redis container is running
docker compose ps

# Start if not running
docker compose up redis -d

# Test connection
docker exec -it woven-redis-1 redis-cli ping
# Should respond: PONG
```

---

### "dotnet ef" command not found

**Problem:** EF Core tools not installed  

**Solution:**
```bash
dotnet tool install --global dotnet-ef
```

**Verify:**
```bash
dotnet ef --version
```

---

### OpenAI API errors

**Problem:** `401 Unauthorized` or `Invalid API key` errors  

**Solution:** Verify secret is set:
```bash
cd backend/WovenBackend
dotnet user-secrets list
```

**Should show:**
```
OpenAI:ApiKey = sk-...
Jwt:Key = ...
```

**If missing, set it:**
```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key-here"
```

---

### Port 5135 already in use

**Problem:** `Address already in use` error  

**Solution:**

**Windows:**
```powershell
netstat -ano | findstr :5135
taskkill /PID <pid> /F
```

**Linux/Mac:**
```bash
lsof -ti:5135 | xargs kill -9
```

---

## Frontend Issues

### Blank screen / white page

**Problem:** Frontend loads but shows nothing  

**Checklist:**
1. Check browser console for errors (F12)
2. Verify backend is running: `curl http://localhost:5135/health`
3. Check network tab for failed API calls
4. Ensure accessing `http://localhost:4202` (not https)

**Common causes:**
- Backend not running
- CORS errors (should not occur locally)
- TypeScript errors preventing compile

---

### "Cannot GET /" or 404 errors

**Problem:** Angular routes not working  

**Solution:** Ensure you're using Angular dev server:
```bash
npx ng serve --port 4202
```

**Not:**
```bash
npx ng build && serve dist/
```

**Reason:** Dev server has built-in fallback routing for SPA. Static file server does not.

---

### API calls failing with CORS

**Problem:** `CORS policy blocked` errors in console  

**Solution (should not happen locally):**
1. Verify backend `Program.cs` has CORS configured:
```csharp
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());
```

2. Restart backend after CORS changes

---

### Port 4202 already in use

**Problem:** `Port 4202 is already in use`  

**Solution:**

**Windows:**
```powershell
netstat -ano | findstr :4202
taskkill /PID <pid> /F
```

**Linux/Mac:**
```bash
lsof -ti:4202 | xargs kill -9
```

---

## Docker Issues

### "Cannot connect to Docker daemon"

**Problem:** Docker commands fail  

**Solution:**
1. Verify Docker Desktop is running (system tray icon)
2. Restart Docker Desktop
3. Check Docker status: `docker ps`

---

### Containers won't start

**Problem:** `docker compose up` fails  

**Solution:**
```bash
# View logs
docker compose logs

# Force recreate
docker compose down
docker compose up postgres redis azurite -d

# Nuclear option: clean everything
docker compose down -v  # WARNING: deletes volumes
docker compose up -d
```

---

### Database migration errors

**Problem:** `dotnet ef database update` fails  

**Common causes:**
1. **Postgres not running** → Start container
2. **Wrong connection string** → Check `appsettings.json`
3. **Migration syntax error** → Review migration file
4. **pgvector not installed** → Use Docker image `pgvector/pgvector:pg16`

**Check migration status:**
```bash
dotnet ef migrations list
```

---

## Build Errors

### Backend build fails

**Check:**
1. Run `dotnet restore` first
2. Review error messages carefully
3. Check for missing NuGet packages
4. Verify .NET 10 SDK installed: `dotnet --version`

**Clean build:**
```bash
dotnet clean
dotnet restore
dotnet build
```

---

### Frontend build fails

**Check:**
1. Run `npm ci` to reinstall dependencies
2. Check TypeScript errors in console
3. Verify Node 22 installed: `node --version`

**Clean build:**
```bash
rm -rf node_modules package-lock.json
npm install
npx ng build --configuration development
```

---

## Logging and Debugging

### Enable detailed backend logs

**Modify `appsettings.json`:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
```

**Then restart backend.**

---

### View correlation IDs

All API requests include `X-Correlation-ID` header for tracing.

**Check in logs:**
```
[MatchService] Pool built | UserId=123 Count=15 CorrelationId=a1b2c3d4e5f67890
```

**Check in error responses:**
```json
{
  "error": "MATCH_NOT_FOUND",
  "correlationId": "a1b2c3d4e5f67890"
}
```

---

### Inspect database

**Connect to Postgres:**
```bash
docker exec -it woven-postgres-1 psql -U woven -d woven_db
```

**Useful queries:**
```sql
-- List all tables
\dt

-- Check migrations
SELECT * FROM "__EFMigrationsHistory";

-- View users
SELECT id, email, full_name, profile_status FROM users LIMIT 10;

-- Check matches
SELECT * FROM matches WHERE user_a_id = 123 OR user_b_id = 123;
```

---

### Inspect Redis

**Connect to Redis:**
```bash
docker exec -it woven-redis-1 redis-cli
```

**Useful commands:**
```bash
# List all keys
KEYS *

# Get a value
GET "rl:upload:123:2026-10-07"

# Check TTL
TTL "rl:upload:123:2026-10-07"

# Clear all (DEV ONLY!)
FLUSHALL
```

---

**Last Updated:** 2026-10-07
