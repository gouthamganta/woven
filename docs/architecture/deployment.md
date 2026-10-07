# Deployment Architecture

**Platform:** Azure Container Apps  
**CI/CD:** GitHub Actions + Azure OIDC  
**IaC:** Terraform  
**Environments:** Production (live), Development (local)

---

## Overview

Woven deploys as **containerized workloads** on Azure Container Apps — lightweight, consumption-tier, scale-to-zero architecture for cost optimization.

**Design principles:**
- **GitOps** — all deployments triggered by Git push
- **Blue-green strategy** — zero-downtime deployments (future)
- **Immutable containers** — never patch running containers, always redeploy
- **Separation of concerns** — API pods scale independently from workers
- **No long-lived credentials** — Azure OIDC for CI/CD auth

---

## Architecture Overview

```
┌────────────────────────────────────────────────────────────────┐
│ GitHub (gouthamganta/woven)                                    │
│   └─ Push to master → GitHub Actions workflow                 │
└────────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌────────────────────────────────────────────────────────────────┐
│ GitHub Actions (CI/CD)                                         │
│   1. Build Docker image (multi-stage Dockerfile)              │
│   2. Push to ACR (wovenprodacr.azurecr.io)                    │
│   3. Update Container Apps (az containerapp update)           │
└────────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌────────────────────────────────────────────────────────────────┐
│ Azure Container Registry (ACR)                                │
│   wovenprodacr.azurecr.io/woven-backend:latest                │
└────────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌────────────────────────────────────────────────────────────────┐
│ Azure Container Apps Environment (woven-prod-env)             │
│                                                                 │
│  ┌──────────────────────────────┐  ┌─────────────────────────┐│
│  │ API Container App            │  │ Workers Container App   ││
│  │ - Min: 0, Max: 10            │  │ - Min: 1, Max: 1        ││
│  │ - Port: 8080 (internal)      │  │ - DISABLE_BATCH=false   ││
│  │ - DISABLE_BATCH=true         │  │ - Scheduled jobs        ││
│  │ - Real-time workers only     │  │ - Heavy batch workers   ││
│  └──────────────────────────────┘  └─────────────────────────┘│
│                                                                 │
│  VNet: 10.0.0.0/16                                             │
│   ├─ Subnet: Container Apps (10.0.1.0/24)                     │
│   └─ Subnet: PostgreSQL (10.0.2.0/24)                         │
└────────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌────────────────────────────────────────────────────────────────┐
│ External Services                                              │
│  - PostgreSQL Flexible Server (private endpoint)              │
│  - Redis (private endpoint)                                   │
│  - Blob Storage (public, SAS token auth)                      │
│  - Key Vault (managed identity auth)                          │
└────────────────────────────────────────────────────────────────┘
```

---

## Container Strategy

### API Container App

**Purpose:** Serve HTTP traffic (Moments, Chats, Games, etc.)

**Scaling:**
- **Min replicas:** 0 (scale to zero when idle)
- **Max replicas:** 10 (cost cap)
- **Scale trigger:** HTTP requests (target 100 concurrent)
- **Cooldown:** 60s (wait before scaling down)

**Environment variables:**
```env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
WOVEN_DISABLE_BATCH_WORKERS=true
```

**Health checks:**
- **Liveness:** `GET /health` (returns 200 OK)
- **Readiness:** `GET /health/ready` (returns 200 when DB + Redis connected)

**Resources:**
- **CPU:** 0.25 vCPU (250m)
- **Memory:** 512 MB

---

### Workers Container App

**Purpose:** Run batch jobs (ConnectionScore, CfScore, WeightLearning, etc.)

**Scaling:**
- **Min replicas:** 1
- **Max replicas:** 1 (never scale — single-replica batch processing)
- **Scale trigger:** None (always 1 replica)

**Environment variables:**
```env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
WOVEN_DISABLE_BATCH_WORKERS=false
```

**Resources:**
- **CPU:** 0.5 vCPU (500m)
- **Memory:** 1 GB

**Why separate container app:**
- **Resource isolation** — batch jobs don't starve API traffic
- **Single-replica guarantee** — workers run scheduled jobs exactly once (no duplicates)
- **Independent scaling** — API pods scale to traffic, workers stay fixed

---

## Docker Image Build

**Dockerfile:** [backend/WovenBackend/Dockerfile](../../backend/WovenBackend/Dockerfile)

**Multi-stage build:**
```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["WovenBackend.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "WovenBackend.dll"]
```

**Build command:**
```bash
docker build -t wovenprodacr.azurecr.io/woven-backend:latest .
```

**Image size:**  
~200 MB (after multi-stage build optimization)

---

## CI/CD Pipeline

**Workflow file:** `.github/workflows/deploy-prod.yml` (not in repo — stored separately)

**Trigger:** Push to `master` branch

### Pipeline Steps

**1. Build Docker image**
```yaml
- name: Build Docker image
  run: |
    docker build -t wovenprodacr.azurecr.io/woven-backend:${{ github.sha }} \
      -t wovenprodacr.azurecr.io/woven-backend:latest \
      ./backend/WovenBackend
```

**2. Push to ACR**
```yaml
- name: Log in to ACR
  uses: azure/docker-login@v1
  with:
    login-server: wovenprodacr.azurecr.io
    username: ${{ secrets.ACR_USERNAME }}
    password: ${{ secrets.ACR_PASSWORD }}

- name: Push image
  run: |
    docker push wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
    docker push wovenprodacr.azurecr.io/woven-backend:latest
```

**3. Update Container Apps**
```yaml
- name: Update API Container App
  run: |
    az containerapp update \
      --name woven-api \
      --resource-group woven-prod-rg \
      --image wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}

- name: Update Workers Container App
  run: |
    az containerapp update \
      --name woven-workers \
      --resource-group woven-prod-rg \
      --image wovenprodacr.azurecr.io/woven-backend:${{ github.sha }}
```

**4. Run smoke tests (future)**
```yaml
- name: Smoke test
  run: |
    curl -f https://api.wooven.me/health || exit 1
```

---

## Authentication (Azure OIDC)

**GitHub Actions → Azure**

**No long-lived credentials** — GitHub Actions gets short-lived access tokens via OIDC.

**Setup:**
1. Register Azure AD application
2. Create federated credential (GitHub repo → Azure AD app)
3. Grant permissions (Contributor on `woven-prod-rg`)

**Workflow authentication:**
```yaml
- name: Azure Login
  uses: azure/login@v1
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
```

**Benefits:**
- No service principal credentials in GitHub Secrets
- Short-lived tokens (1 hour TTL)
- Audit trail (Azure AD logs every login)

---

## Database Migrations

**Strategy:** Run migrations automatically on app startup.

**Code (Program.cs):**
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
    await db.Database.MigrateAsync();  // Applies pending migrations
}
```

**Why automatic:**
- No manual SQL scripts
- Migrations version-controlled (EF Core migrations)
- Atomic (transaction per migration)

**Risks:**
- Long migrations block app startup (mitigation: run migrations in init container — future)
- Breaking schema changes require blue-green deployment (future)

---

## Zero-Downtime Deployment (Future)

**Current:** Rolling update (brief downtime during container restart)

**Future:** Blue-green deployment

**Blue-green pattern:**
1. Deploy new version to "green" slot (inactive)
2. Run smoke tests on green slot
3. Switch traffic from "blue" (active) to "green"
4. Keep blue slot for rollback (5 min window)
5. Decommission blue slot

**Azure Container Apps support:**  
Container Apps Revisions + Traffic Splitting (not yet implemented)

---

## Rollback Strategy

**Manual rollback (current):**
```bash
# Redeploy previous image
az containerapp update \
  --name woven-api \
  --resource-group woven-prod-rg \
  --image wovenprodacr.azurecr.io/woven-backend:<previous-sha>
```

**Automated rollback (future):**
- Smoke test failure → auto-rollback
- High error rate (> 5% HTTP 5xx) → auto-rollback
- Container crash loop → auto-rollback

---

## Configuration Management

### Secrets (Azure Key Vault)

**Loaded at runtime** (not build time):

```csharp
if (builder.Environment.IsProduction())
{
    var keyVaultName = builder.Configuration["KeyVault:Name"];
    var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
    builder.Configuration.AddAzureKeyVault(
        keyVaultUri,
        new Azure.Identity.DefaultAzureCredential());
}
```

**Managed Identity auth:**  
Container Apps → Key Vault (no credentials)

**Secrets:**
- `Jwt--Secret`
- `ConnectionStrings--DefaultConnection`
- `OpenAi--ApiKey`
- `BlobStorage--ConnectionString`
- `Redis--ConnectionString`

---

### Environment Variables

**Set via Terraform:**
```hcl
resource "azurerm_container_app" "api" {
  name                = "woven-api"
  resource_group_name = azurerm_resource_group.rg.name

  template {
    container {
      name   = "woven-backend"
      image  = "wovenprodacr.azurecr.io/woven-backend:latest"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "WOVEN_DISABLE_BATCH_WORKERS"
        value = "true"
      }
    }
  }
}
```

---

## Monitoring Deployment Health

### Metrics to Track

| Metric | Threshold | Alert |
|--------|-----------|-------|
| **HTTP 5xx rate** | > 5% | Critical |
| **Response time P95** | > 2s | Warning |
| **Container restarts** | > 3 in 5 min | Critical |
| **Database connection failures** | > 10 in 5 min | Critical |

### Post-Deployment Checks

**Automated (future):**
1. Health endpoint returns 200 OK
2. Database connectivity (health/ready)
3. Redis connectivity (health/ready)
4. OpenAI API key valid (smoke test)

**Manual (current):**
1. Open https://wooven.me
2. Log in
3. Navigate to Moments (deck loads)
4. Send a chat message

---

## Infrastructure as Code (Terraform)

**Files:** (stored separately, not in app repo)

**Structure:**
```
terraform/
├── main.tf                 # Resource group, VNet, Container Apps
├── database.tf             # PostgreSQL
├── storage.tf              # Blob Storage, ACR
├── security.tf             # Key Vault, managed identities
├── monitoring.tf           # Log Analytics, App Insights
├── variables.tf            # Input variables
└── outputs.tf              # Output values (URLs, connection strings)
```

**Apply workflow:**
```bash
cd terraform
terraform init
terraform plan -out=tfplan
terraform apply tfplan
```

**State storage:**  
Azure Storage Account (backend configured in Terraform)

---

## Environment Comparison

| Aspect | Development | Production |
|--------|-------------|------------|
| **Infra** | Docker Compose (local) | Azure Container Apps |
| **Database** | PostgreSQL 16 (Docker) | Azure PostgreSQL Flexible Server |
| **Secrets** | User Secrets (dotnet) | Azure Key Vault |
| **Logs** | Console (human-readable) | Log Analytics (JSON) |
| **Scaling** | 1 replica (fixed) | 0–10 replicas (auto-scale) |
| **SSL** | None (http only) | Enforced (https) |

---

## Known Issues

| Issue | Impact | Mitigation | Fix ETA |
|-------|--------|------------|---------|
| **Manual rollback** | Slow recovery from bad deploy | Keep previous image tag documented | Q4 2026 |
| **No blue-green** | Brief downtime during deploy | Deploy at low-traffic hours (3–5 AM UTC) | Q1 2027 |
| **Migrations block startup** | Long migrations delay app availability | Keep migrations small (< 5s) | Q1 2027 |
| **No smoke tests** | Bad deploy may go unnoticed | Manual post-deploy checks | Q4 2026 |

---

## Next Steps

1. **Implement blue-green deployment** — zero-downtime deploys
2. **Add automated smoke tests** — health checks + critical flows
3. **Auto-rollback on failure** — detect errors → rollback automatically
4. **Init containers for migrations** — run migrations before app starts
5. **Staging environment** — test deployments before production

---

**Last Updated:** 2026-10-07  
**Evidence:** GitHub Actions logs, Azure Portal, [Program.cs](../../backend/WovenBackend/Program.cs) migration code
