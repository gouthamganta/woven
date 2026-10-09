# Redis Configuration

**Last Updated:** 2026-10-07

---

## Overview

Woven uses **Redis** as a distributed cache and SignalR backplane. This document covers connection configuration, environment setup, and deployment specifics.

---

## Connection Strings

### Format

```
<host>:<port>[,password=<pwd>][,ssl=True][,abortConnect=false]
```

### Environments

| Environment | Connection String | Notes |
|---|---|---|
| **Local** | `localhost:6379,abortConnect=false` | Docker Compose, no password |
| **Production** | `woven-prod-redis.redis.cache.windows.net:6380,password=***,ssl=True,abortConnect=false` | Azure Cache for Redis, TLS required |

---

## Configuration Sources

### 1. appsettings.json (Development Default)

**File:** `backend/WovenBackend/appsettings.json`

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379,abortConnect=false"
  }
}
```

This is the **fallback** for local development. Overridden by User Secrets or Key Vault.

---

### 2. User Secrets (Local Development)

**Recommended for local dev.** Keeps connection strings out of source control.

#### Initialize User Secrets

```bash
cd backend/WovenBackend
dotnet user-secrets init
```

This creates a GUID in `WovenBackend.csproj`:

```xml
<PropertyGroup>
  <UserSecretsId>your-guid-here</UserSecretsId>
</PropertyGroup>
```

#### Set Redis Connection String

```bash
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379,abortConnect=false"
```

#### View All Secrets

```bash
dotnet user-secrets list
```

#### Remove a Secret

```bash
dotnet user-secrets remove "Redis:ConnectionString"
```

#### Clear All Secrets

```bash
dotnet user-secrets clear
```

**Storage location:**
- Windows: `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`
- macOS/Linux: `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`

---

### 3. Azure Key Vault (Production)

**File:** `backend/WovenBackend/Program.cs` (lines 38–54)

```csharp
if (builder.Environment.IsProduction())
{
    var keyVaultName = builder.Configuration["KeyVault:Name"];
    if (!string.IsNullOrEmpty(keyVaultName))
    {
        var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
        builder.Configuration.AddAzureKeyVault(
            keyVaultUri,
            new Azure.Identity.DefaultAzureCredential());
    }
}
```

#### Secrets Stored in Key Vault

| Secret Name | Config Key | Value |
|---|---|---|
| `Redis--ConnectionString` | `Redis:ConnectionString` | `woven-prod-redis.redis.cache.windows.net:6380,password=***,ssl=True,abortConnect=false` |

**Note:** Azure Key Vault uses `--` (double dash) to represent `:` in hierarchical keys. The SDK automatically maps `Redis--ConnectionString` → `Redis:ConnectionString`.

#### Set via Azure CLI

```bash
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "Redis--ConnectionString" \
  --value "woven-prod-redis.redis.cache.windows.net:6380,password=***,ssl=True,abortConnect=false"
```

#### Retrieve Secret

```bash
az keyvault secret show \
  --vault-name woven-prod-kv \
  --name "Redis--ConnectionString" \
  --query "value" -o tsv
```

---

### 4. Environment Variables (Container Apps)

Azure Container Apps inject secrets as **environment variables** at runtime.

#### Terraform Configuration

**File:** `infra/modules/container_apps/main.tf`

```hcl
env {
  name        = "Redis__ConnectionString"
  secret_name = "redis-conn"
}
```

**Secret value** comes from Terraform variable:

```hcl
variable "redis_connection_string" {
  type      = string
  sensitive = true
}
```

**Set in GitHub Actions:**

```yaml
- name: Terraform Apply
  env:
    TF_VAR_redis_connection_string: ${{ secrets.REDIS_CONNECTION_STRING }}
  run: terraform apply -auto-approve
```

#### Verify at Runtime

Container Apps portal → Revision Management → Environment Variables:

```
Redis__ConnectionString = woven-prod-redis.redis.cache.windows.net:6380,...
```

**Note:** Environment variables use `__` (double underscore) to represent `:` in hierarchical keys. ASP.NET Core automatically maps `Redis__ConnectionString` → `Redis:ConnectionString`.

---

## Configuration Precedence

ASP.NET Core loads configuration in this order (later sources override earlier):

1. `appsettings.json`
2. `appsettings.{Environment}.json` (e.g., `appsettings.Production.json`)
3. **User Secrets** (Development only)
4. **Azure Key Vault** (Production only, if configured)
5. **Environment Variables**
6. Command-line arguments

**In practice:**
- **Local dev:** User Secrets override `appsettings.json`
- **Production:** Azure Key Vault overrides everything (env vars set from Key Vault via Terraform)

---

## Program.cs Registration

### IConnectionMultiplexer (Singleton)

**File:** `backend/WovenBackend/Program.cs` (lines 476–482)

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConnStr = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    var cfg = ConfigurationOptions.Parse(redisConnStr);
    cfg.AbortOnConnectFail = false;  // App starts even if Redis is down
    return ConnectionMultiplexer.Connect(cfg);
});
```

**Why singleton?**
- `IConnectionMultiplexer` is **thread-safe** and **expensive to create**
- One shared TCP connection multiplexed across all requests
- Reconnects automatically if connection drops

### ICacheService (Singleton)

```csharp
builder.Services.AddSingleton<ICacheService, CacheService>();
```

**Why singleton?**
- `CacheService` is **stateless** (only wraps `IConnectionMultiplexer`)
- Registered as singleton to match multiplexer lifetime
- `_cacheKey` derived once at construction (from `IEncryptionService`)

### SignalR Backplane

**File:** `backend/WovenBackend/Program.cs` (lines 455–458)

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(
        builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379",
        opts => { opts.Configuration.AbortOnConnectFail = false; });
```

SignalR uses **pub/sub** on the same Redis instance. Separate internal connection from `IConnectionMultiplexer`.

---

## Docker Compose (Local Dev)

**File:** `backend/docker-compose.yml`

```yaml
services:
  redis:
    image: redis:7-alpine
    container_name: woven-redis
    ports:
      - "6379:6379"
    command: redis-server --appendonly no --save ""
    networks:
      - woven-network

  backend:
    # ...
    environment:
      - Redis__ConnectionString=redis:6379,abortConnect=false
    depends_on:
      - redis
```

**Notes:**
- No persistence (`--appendonly no --save ""`) — cache only
- Backend service connects via service name `redis:6379` (Docker internal DNS)
- Host machine accesses via `localhost:6379`

### Start Redis

```bash
cd backend
docker-compose up redis -d
```

### Check Logs

```bash
docker logs woven-redis
```

### Connect via redis-cli

```bash
docker exec -it woven-redis redis-cli
> ping
PONG
```

---

## Azure Cache for Redis

### Provisioning (Terraform)

**File:** `infra/main.tf`

```hcl
resource "azurerm_redis_cache" "woven" {
  name                = "woven-prod-redis"
  location            = azurerm_resource_group.woven.location
  resource_group_name = azurerm_resource_group.woven.name
  capacity            = 0   # Basic C0 (250MB)
  family              = "C"
  sku_name            = "Basic"
  enable_non_ssl_port = false
  minimum_tls_version = "1.2"

  redis_configuration {
    maxmemory_policy = "allkeys-lru"
  }

  tags = {
    environment = "production"
    project     = "woven"
  }
}

output "redis_primary_key" {
  value     = azurerm_redis_cache.woven.primary_access_key
  sensitive = true
}
```

### Connection Details

| Property | Value |
|---|---|
| **Name** | `woven-prod-redis` |
| **Tier** | Basic C0 (250MB) |
| **Port** | 6380 (SSL), 6379 (non-SSL, disabled) |
| **Host** | `woven-prod-redis.redis.cache.windows.net` |
| **TLS Version** | 1.2+ |
| **Eviction Policy** | `allkeys-lru` (evict least recently used keys) |

### Get Access Keys

```bash
az redis list-keys \
  --resource-group woven-prod-rg \
  --name woven-prod-redis \
  --query "primaryKey" -o tsv
```

### Regenerate Access Key

```bash
az redis regenerate-key \
  --resource-group woven-prod-rg \
  --name woven-prod-redis \
  --key-type Primary
```

**Warning:** Regenerating invalidates the old key. Update Key Vault secret immediately.

---

## Connection Options

### AbortOnConnectFail

```csharp
cfg.AbortOnConnectFail = false;
```

**Purpose:** Allow app to start even if Redis is temporarily unreachable.

**Behavior:**
- `true` (default): App crashes on startup if Redis connection fails
- `false`: App starts, multiplexer retries connection in background

**Woven uses:** `false` (graceful degradation)

### SSL/TLS

```
ssl=True
```

**Production:** TLS required (port 6380)  
**Local:** No TLS (port 6379)

### Password

```
password=<key>
```

**Production:** Azure access key (from Key Vault)  
**Local:** No password

---

## Troubleshooting

### Connection Timeout

**Error:**
```
StackExchange.Redis.RedisConnectionException: It was not possible to connect to the redis server(s)
```

**Possible causes:**
1. Redis is not running
2. Wrong connection string
3. Firewall blocking port
4. Azure Private Link misconfigured

**Debug steps:**

```bash
# Check Redis is running
docker ps | grep redis   # Local
az redis show --name woven-prod-redis --resource-group woven-prod-rg --query "provisioningState"   # Azure

# Test connection
redis-cli -h localhost -p 6379 ping   # Local
redis-cli -h woven-prod-redis.redis.cache.windows.net -p 6380 --tls -a <key> ping   # Azure

# Check logs
docker logs woven-redis   # Local
az redis list-logs --resource-group woven-prod-rg --name woven-prod-redis   # Azure
```

### Authentication Failed

**Error:**
```
NOAUTH Authentication required
```

**Cause:** Missing or wrong password in production.

**Fix:**
```bash
# Get correct key
az redis list-keys --name woven-prod-redis --resource-group woven-prod-rg

# Update Key Vault
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "Redis--ConnectionString" \
  --value "woven-prod-redis.redis.cache.windows.net:6380,password=<correct-key>,ssl=True,abortConnect=false"
```

### TLS Required

**Error:**
```
Server does not support TLS connections
```

**Cause:** Trying to connect to port 6379 in production (non-SSL).

**Fix:** Use port 6380 with `ssl=True`.

### Memory Full / Eviction

**Symptom:** Unexpected cache misses, `evicted_keys` counter increasing.

**Cause:** Redis max memory reached (250MB on Basic C0).

**Fix:**
```bash
# Check memory usage
az redis show-stats --name woven-prod-redis --resource-group woven-prod-rg

# Scale up to C1 (1GB) via Terraform
# Edit infra/main.tf:
capacity = 1
```

Then apply:
```bash
terraform apply
```

---

## Security

### Network Isolation

**Production:**
- Azure Private Link (no public internet access)
- Container Apps → Redis via virtual network
- Redis firewall rules block all public IPs

**Local:**
- Docker internal network
- Redis not exposed to host network (only via `localhost:6379`)

### Secret Rotation

**Quarterly rotation:**
1. Generate new access key in Azure Portal
2. Update Key Vault secret (`Redis--ConnectionString`)
3. Restart Container Apps (picks up new secret)
4. Invalidate old key after 24h grace period

**GitHub Actions (automated):**
- Terraform stores Redis key as sensitive output
- CI/CD pipeline updates GitHub Secret `REDIS_CONNECTION_STRING`
- Next deployment uses new key

---

## Monitoring

### Azure Portal

**Metrics:**
- Azure Portal → woven-prod-redis → Metrics
- Key metrics:
  - Cache Hits / Misses (hit rate)
  - Used Memory (eviction risk)
  - Connected Clients (connection leaks?)
  - CPU Usage (overloaded?)

**Alerts:**
- Used Memory > 200MB → scale warning
- Connected Clients > 100 → connection leak
- Cache Hit Rate < 70% → investigate key patterns

### Application Logs

All Redis operations log to `[Cache]` prefix:

```
[Cache] GetAsync miss (Redis unavailable) for key deck:123:2026-10-07
[Cache] SetAsync failed (Redis unavailable) for key session:456
```

Structured logs include:
- `{Key}` — cache key
- `{CorrelationId}` — request trace ID

---

## FAQs

**Q: Why Basic tier instead of Standard?**  
A: Standard has replication (HA) but costs 3x more. Current load doesn't justify it. Redis outages are non-critical (graceful degradation).

**Q: Can I connect to production Redis from my laptop?**  
A: No. Production Redis uses Private Link (no public endpoint). Use Azure CLI or Bastion host.

**Q: How do I clear production cache?**  
A: Don't use `FLUSHDB` in prod (destroys all keys). Delete specific keys:
```bash
redis-cli -h woven-prod-redis.redis.cache.windows.net -p 6380 --tls -a <key> DEL "deck:*"
```
Or use SCAN + DEL loop for patterns.

**Q: What happens during Redis failover?**  
A: Basic tier has no failover (single instance). Standard tier auto-failovers to replica. `AbortOnConnectFail=false` lets app retry connection.

---

## Related Docs

- **[Caching Overview](./README.md)** — When to use cache, architecture
- **[Cache Keys](./cache-keys.md)** — Key naming conventions
- **[CacheService Implementation](./implementation.md)** — Code walkthrough
- **[Secrets Setup](../../backend/WovenBackend/SECRETS_SETUP.md)** — User Secrets + Key Vault guide

---

## Changelog

| Date | Change |
|---|---|
| 2026-10-07 | Added Redis configuration documentation |
| 2026-06-04 | Migrated to Azure Key Vault for secrets |
| 2026-05-20 | Added TLS requirement for production |
| 2026-05-01 | Initial Redis setup (SignalR backplane) |
