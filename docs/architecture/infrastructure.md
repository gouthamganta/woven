# Infrastructure Architecture

**Cloud Provider:** Microsoft Azure  
**Region:** East US (primary)  
**Resource Group:** `woven-prod-rg`  
**IaC:** Terraform + GitHub Actions CI/CD

---

## Overview

Woven runs on Azure with a **consumption-tier Container Apps** architecture — scale-to-zero for API pods, fixed single-replica for workers.

**Design principles:**
- **Infrastructure as Code** — all resources defined in Terraform
- **GitOps CI/CD** — GitHub Actions + Azure OIDC (no long-lived credentials)
- **Consumption tier** — cost optimization (scale to 0 when idle)
- **Managed services** — PostgreSQL, Redis, Blob Storage, Key Vault (no self-hosted infra)
- **Internal ingress** — backend not publicly exposed (nginx SSL handshake issue)

---

## Resource Inventory

### Compute

| Resource | Type | SKU/Tier | Purpose |
|----------|------|----------|---------|
| **Container Apps Environment** | `Microsoft.App/managedEnvironments` | Consumption | Hosts API + workers pods |
| **API Container App** | `Microsoft.App/containerApps` | Consumption | .NET backend (scale 0–N) |
| **Workers Container App** | `Microsoft.App/containerApps` | Consumption | Background workers (min=max=1) |
| **Container Registry (ACR)** | `Microsoft.ContainerRegistry/registries` | Basic | Docker image storage (`wovenprodacr`) |

**Scaling rules (API pods):**
- Min replicas: 0 (scale to zero)
- Max replicas: 10 (cost cap)
- Scale trigger: HTTP requests (target 100 concurrent)

**Workers pod:**
- Min replicas: 1
- Max replicas: 1 (never scale — single-replica batch processing)
- Environment variable: `WOVEN_DISABLE_BATCH_WORKERS=false`

---

### Database

| Resource | Type | SKU/Tier | Purpose |
|----------|------|----------|---------|
| **PostgreSQL Flexible Server** | `Microsoft.DBforPostgreSQL/flexibleServers` | B_Standard_B1ms | Primary database |

**Configuration:**
- **Version:** PostgreSQL 16
- **Compute:** 1 vCore, 2 GB RAM
- **Storage:** 32 GB (auto-grow enabled)
- **Backups:** Daily (7-day retention)
- **Extensions:** `pgvector` 0.3.2
- **Max connections:** 100 (default)

**Networking:**
- Private endpoint (VNet integration)
- SSL enforced (`sslmode=Require`)

**Known issue:**  
No read replicas → analytics queries block real-time traffic. Mitigation: run heavy queries via batch workers at off-peak hours.

---

### Caching

| Resource | Type | SKU/Tier | Purpose |
|----------|------|----------|---------|
| **Azure Cache for Redis** | `Microsoft.Cache/Redis` | Basic C0 | Session cache, distributed locks |

**Configuration:**
- **Version:** Redis 6.x
- **Capacity:** 250 MB
- **Eviction policy:** `volatile-lru` (least recently used)
- **TLS:** Enabled

**Used for:**
- User profile caching (15 min TTL)
- Daily deck caching (1 day TTL)
- Rate limit state
- Distributed locks (batch worker coordination)

---

### Storage

| Resource | Type | SKU/Tier | Purpose |
|----------|------|----------|---------|
| **Blob Storage Account** | `Microsoft.Storage/storageAccounts` | Standard LRS | User photos, voice notes |
| **CDN Profile** | `Microsoft.Cdn/profiles` | Standard Microsoft | Static asset delivery |

**Blob containers:**
- `user-photos` — profile photos
- `voice-notes` — chat voice messages
- `tiles-media` — Commons tile media

**Access pattern:**
1. Client requests SAS token (`POST /media/upload-token`)
2. Client uploads to Blob via SAS token (direct, no backend proxy)
3. Client confirms upload (`POST /media/confirm`)
4. Backend stores Blob URL in database

**CDN:**  
Static assets (frontend bundle, landing page videos) served from Azure CDN (POP caching).

---

### Secrets Management

| Resource | Type | SKU/Tier | Purpose |
|----------|------|----------|---------|
| **Key Vault** | `Microsoft.KeyVault/vaults` | Standard | Secrets, connection strings, API keys |

**Secrets stored:**
- `Jwt--Secret` — JWT signing key
- `ConnectionStrings--DefaultConnection` — PostgreSQL connection string
- `OpenAi--ApiKey` — OpenAI API key
- `BlobStorage--ConnectionString` — Azure Blob connection string
- `Redis--ConnectionString` — Redis connection string
- `WebPush--VapidPrivateKey` — VAPID private key for Web Push
- `WebPush--VapidPublicKey` — VAPID public key

**Access:**
- **Production:** Managed Identity (Container Apps → Key Vault)
- **Development:** User Secrets (`dotnet user-secrets`)

**Setup guide:** [SECRETS_SETUP.md](../../backend/WovenBackend/SECRETS_SETUP.md)

---

### Networking

| Resource | Type | SKU/Tier | Purpose |
|----------|------|----------|---------|
| **Virtual Network (VNet)** | `Microsoft.Network/virtualNetworks` | — | Private networking |
| **Subnet (Container Apps)** | — | — | Container Apps environment |
| **Subnet (PostgreSQL)** | — | — | PostgreSQL private endpoint |

**Architecture:**
```
Internet → Azure Front Door (future)
         → Container Apps internal ingress (port 8080)
         → VNet (private)
            ├─ Container Apps Subnet (API + workers)
            └─ PostgreSQL Subnet (private endpoint)
```

**Known issue:**  
Nginx → backend SSL handshake failure (unresolved). Mitigation: use internal-only ingress.

**Future:**  
Azure Front Door for global CDN + WAF (Web Application Firewall).

---

### Monitoring & Observability

| Resource | Type | SKU/Tier | Purpose |
|----------|------|----------|---------|
| **Log Analytics Workspace** | `Microsoft.OperationalInsights/workspaces` | Pay-as-you-go | Centralized logs |
| **Application Insights** | `Microsoft.Insights/components` | — | APM, distributed tracing |

**Logs ingested:**
- Container stdout/stderr → Log Analytics
- Application logs (Serilog JSON) → Log Analytics
- HTTP requests → Application Insights

**Metrics tracked:**
- Request rate, latency (P50/P95/P99)
- Exception rate
- Database query time
- Redis cache hit rate

**Alerts configured:**
- HTTP 5xx rate > 5% (5 min window)
- Response time P95 > 2s (5 min window)
- Database CPU > 80% (10 min window)

**Known gap:**  
No Prometheus/Grafana yet — all metrics in Azure Monitor.

---

## CI/CD Pipeline

**Source:** GitHub (private repo: `gouthamganta/woven`)

**Workflow:** [.github/workflows/deploy-prod.yml](../../.github/workflows/deploy-prod.yml)

**Trigger:** Push to `master` branch

**Steps:**
1. **Build Docker image** (multi-stage Dockerfile)
2. **Push to ACR** (`wovenprodacr.azurecr.io/woven-backend:latest`)
3. **Update Container App** (Azure CLI: `az containerapp update`)
4. **Run smoke tests** (future)

**Authentication:**  
Azure OIDC (Workload Identity Federation) — no long-lived credentials, GitHub Actions gets short-lived tokens via OIDC.

**Terraform:**  
Infrastructure changes deployed via separate workflow ([.github/workflows/terraform-apply.yml](../../.github/workflows/terraform-apply.yml)).

---

## Environments

| Environment | Branch | URL | Purpose |
|-------------|--------|-----|---------|
| **Development** | Local | `http://localhost:4202` | Local dev (Docker Compose) |
| **Staging** | `staging` | (future) | Pre-production testing |
| **Production** | `master` | `https://wooven.me` | Live app |

**Development stack:**
```yaml
# docker-compose.yml
services:
  backend:
    build: ./backend/WovenBackend
    ports: ["5135:8080"]
    environment:
      ASPNETCORE_ENVIRONMENT: Development
  
  postgres:
    image: ankane/pgvector:v0.3.2
    ports: ["5433:5432"]
  
  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]
```

**Staging environment:**  
Not yet provisioned. When created, will be identical to production but smaller SKUs (cost optimization).

---

## Disaster Recovery

### Backup Strategy

| Resource | Backup Frequency | Retention | Recovery Time Objective (RTO) |
|----------|------------------|-----------|-------------------------------|
| **PostgreSQL** | Daily (automated) | 7 days | < 1 hour (point-in-time restore) |
| **Blob Storage** | Soft delete | 7 days | Instant (undelete) |
| **Container Images** | Retained in ACR | Infinite | Instant (redeploy) |
| **Terraform state** | Versioned in Azure Storage | Infinite | Manual (re-apply) |

**Database recovery:**
```bash
# Point-in-time restore (Azure CLI)
az postgres flexible-server restore \
  --resource-group woven-prod-rg \
  --name woven-db-restored \
  --source-server woven-db \
  --restore-time "2026-10-06T12:00:00Z"
```

**Blob storage recovery:**
```bash
# Undelete soft-deleted blob (Azure CLI)
az storage blob undelete \
  --container-name user-photos \
  --name photo123.jpg \
  --account-name wovenstorage
```

### High Availability

**Current state:**  
Single region (East US), no geo-redundancy.

**Future:**
- **Database:** Read replicas in secondary region (West US)
- **Blob Storage:** Geo-redundant storage (GRS)
- **Azure Front Door:** Global load balancing + failover

---

## Cost Breakdown

**Monthly Azure costs (current):**

| Resource | Monthly Cost (USD) |
|----------|-------------------|
| Container Apps (API + workers) | $30 (consumption tier) |
| PostgreSQL Flexible Server (B1ms) | $15 |
| Redis Basic C0 | $16 |
| Blob Storage (100 GB) | $2 |
| Key Vault | $1 |
| Log Analytics + App Insights | $10 |
| **Total** | **$74/month** |

**Projected costs (10,000 users):**

| Resource | Monthly Cost (USD) |
|----------|-------------------|
| Container Apps (scaled to 3 replicas) | $90 |
| PostgreSQL GP_Standard_D2s_v3 | $150 |
| Redis Standard C1 | $75 |
| Blob Storage (1 TB) | $20 |
| CDN (100 GB egress) | $8 |
| **Total** | **$343/month** |

**Cost optimization strategies:**
- Consumption tier (scale to zero)
- Burstable database SKU (B1ms)
- Blob lifecycle policies (move old media to cool tier)

---

## Security

### Network Security

- **Private endpoints:** PostgreSQL, Redis (no public access)
- **VNet integration:** Container Apps in isolated subnet
- **SSL/TLS enforced:** All connections encrypted in transit

### Identity & Access

- **Managed Identity:** Container Apps → Key Vault, Blob Storage (no credentials in code)
- **RBAC:** Least-privilege access (dev team has Contributor, CI/CD has specific roles)
- **Azure AD:** User authentication for Azure Portal (MFA enforced)

### Secrets Management

- **Key Vault:** All secrets stored, rotated quarterly (manual)
- **User Secrets:** Development only (never committed to Git)
- **Environment variables:** Injected at runtime (no hardcoded secrets)

### Compliance

- **HTTPS only:** All traffic encrypted
- **Encrypted at rest:** Database, Blob Storage, Redis (Azure-managed keys)
- **Audit logs:** Security Audit Log table + Azure Monitor

**Future:**
- **WAF (Web Application Firewall)** — Azure Front Door
- **DDoS protection** — Azure DDoS Protection Standard
- **Penetration testing** — annual third-party audit

---

## Known Issues

| Issue | Impact | Mitigation | Fix ETA |
|-------|--------|------------|---------|
| **Nginx → backend SSL handshake failure** | External ingress fails | Use internal-only ingress | Unresolved |
| **No read replicas** | Analytics queries block traffic | Run at off-peak hours | Q1 2027 |
| **No staging environment** | Risk of production bugs | Manual QA before deploy | Q4 2026 |
| **No geo-redundancy** | Single point of failure (region outage) | None | Q2 2027 |

---

## Next Steps

1. **Provision staging environment** — identical to prod, smaller SKUs
2. **Add read replicas** — offload analytics to secondary DB
3. **Implement Azure Front Door** — global CDN + WAF
4. **Automate key rotation** — quarterly rotation via Azure Functions
5. **Set up Prometheus + Grafana** — custom metrics dashboards
6. **Geo-redundant backups** — PostgreSQL geo-replication

---

**Last Updated:** 2026-10-07  
**Evidence:** Azure Portal, Terraform files (not in repo — stored separately), [CLAUDE.md](../../CLAUDE.md) infra section
