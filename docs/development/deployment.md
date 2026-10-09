# Deployment Guide

CI/CD pipeline, Azure deployment, and smoke checks for Woven.

**Consolidated from:** `docs/technical/DEVOPS.md`

---

## Overview

Woven uses GitHub Actions for CI/CD with three primary pipelines:

| Pipeline | File | Trigger | Purpose |
|---|---|---|---|
| **CI** | `ci.yml` | PR + push to main/master | Build and test both stacks |
| **Deploy** | `deploy.yml` | Push to main/master (app changes only) | Build images, update Container Apps |
| **Infrastructure** | `terraform.yml` | Infra changes | Apply Terraform to Azure |

**The deploy pipeline is gated behind CI** — images are never built from a failing codebase.

---

## CI Pipeline

### Triggers

- `pull_request` — On any PR
- `push` to `main` or `master`
- `workflow_call` — Callable as reusable workflow (used by `deploy.yml`)

---

### Jobs

**Backend Job:**
1. Checkout code
2. Setup .NET 10
3. Restore NuGet cache
4. `dotnet restore Woven.sln`
5. `dotnet build Woven.sln --no-restore --configuration Release`
6. `dotnet test Woven.sln --no-build --configuration Release`

**Frontend Job:**
1. Checkout code
2. Setup Node 22 with npm cache
3. `npm ci`
4. `npm run build -- --configuration=production`
5. `npm test -- --watch=false`

**Both jobs must pass for CI to succeed.**

---

### Concurrency

```yaml
concurrency:
  group: ${{ github.ref }}
  cancel-in-progress: true
```

In-progress CI runs for the same ref are cancelled when a new push arrives.

---

## Deploy Pipeline

### Triggers

Push to `main` or `master`, **excluding** changes to:
- `infra/**`
- `docs/**`
- `*.md` files

**Pure documentation or infrastructure changes do not trigger an application deploy.**

---

### Concurrency

```yaml
concurrency:
  group: deploy-production
  cancel-in-progress: false
```

**Deploy runs never cancel in-progress deploys.** If two pushes land close together, the second deploy queues and waits.

---

### Pipeline Steps

**1. CI Gate:**
- Calls `ci.yml` as reusable workflow
- Both backend + frontend must pass
- If CI fails, pipeline stops before building images

**2. Build Job (needs: ci):**
- OIDC login to Azure
- Build backend image: `az acr build --image woven-backend:{sha}`
- Build frontend image: `az acr build --image woven-frontend:{sha}`
- Images pushed to ACR: `wovenprodacr.azurecr.io`

**3. Deploy Job (needs: build, environment: production):**
- OIDC login to Azure
- Inject OpenAI API key as Container App secret
- Update backend Container App with new image
- Update frontend Container App with new image
- Run smoke checks (see below)

---

### Image Tagging Strategy

Every Docker image is tagged with the **full Git commit SHA:**

```
wovenprodacr.azurecr.io/woven-backend:{github.sha}
wovenprodacr.azurecr.io/woven-frontend:{github.sha}
```

**Benefits:**
- Every deployed image traceable to exact commit
- Rollback possible by re-running `az containerapp update` with prior SHA
- No `latest` tag ambiguity

---

### Azure Resources

| Resource | Name | Purpose |
|---|---|---|
| Resource Group | `woven-prod-rg` | Contains all resources |
| Container Registry | `wovenprodacr` | Docker images |
| Backend Container App | `woven-prod-backend` | API (internal ingress, port 8080) |
| Frontend Container App | `woven-prod-frontend` | Web app (public ingress, port 80) |
| PostgreSQL | Flexible Server | Database (private VNet) |
| Redis | Azure Cache for Redis | Caching (private endpoint) |
| Blob Storage | Azure Storage Account | Media files |

---

## Smoke Checks

Both smoke checks run **after** container updates and use polling loops to wait for health.

### Backend Smoke Check

Polls the backend Container App revision for healthy state:

```bash
az containerapp revision list \
  --name woven-prod-backend \
  --resource-group woven-prod-rg \
  --query "[?properties.healthState=='Healthy']"
```

- **30 attempts × 10 seconds = 5 minutes max**
- Exits success as soon as healthy revision found
- Exits failure if no healthy revision after 30 attempts

---

### Frontend Smoke Check

HTTP GET against the frontend Container App URL:

```bash
FQDN=$(az containerapp show \
  --name woven-prod-frontend \
  --resource-group woven-prod-rg \
  --query properties.configuration.ingress.fqdn -o tsv)

curl -f https://$FQDN
```

- **15 attempts × 10 seconds = 2.5 minutes max**
- Exits success on first HTTP 200
- Exits failure if no 200 after 15 attempts

---

## Secrets Management

### What Is Stored in GitHub Secrets

| Secret | Purpose | Sensitive? |
|---|---|---|
| `AZURE_CLIENT_ID` | OIDC app registration ID | No |
| `AZURE_TENANT_ID` | Azure tenant ID | No |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID | No |
| `OPENAI_API_KEY` | OpenAI API key | **Yes** |

**Database password** — Azure infrastructure only (never in GitHub)  
**JWT signing key** — Azure Container App env var (set via Terraform)

---

### OIDC Authentication

The deploy pipeline uses **OIDC federated credentials** (`azure/login@v2`).

**No service principal password or certificate is stored.** GitHub's OIDC token is exchanged for an Azure access token.

---

### OpenAI API Key Flow

1. GitHub Actions calls `az containerapp secret set` with API key value
2. Container App secret created/updated: `openai-api-key`
3. Environment variable references secret: `OpenAI__ApiKey=secretref:openai-api-key`
4. Container runtime injects secret value as env var

**Key never in:**
- Terraform state
- Docker image layers
- Git history

---

## Blue-Green Revision Rollout

Container Apps creates a new revision for each image update. Traffic shifts to the new revision **only after liveness and readiness probes pass**.

**If probes never pass:**
- Traffic stays on previous revision
- Smoke check scripts detect failure
- Pipeline fails

---

## Rollback

To rollback to a previous deployment:

1. Find previous commit SHA from git history
2. Manually run `az containerapp update` with that SHA's image tag

**Example:**
```bash
az containerapp update \
  --name woven-prod-backend \
  --resource-group woven-prod-rg \
  --image wovenprodacr.azurecr.io/woven-backend:abc123def456
```

**Images are not automatically deleted from ACR**, so any deployed SHA can be rolled back to.

---

## Environment Variables in Pipeline

| Variable | Value |
|---|---|
| `ACR_NAME` | `wovenprodacr` |
| `ACR_LOGIN_SERVER` | `wovenprodacr.azurecr.io` |
| `RESOURCE_GROUP` | `woven-prod-rg` |
| `BACKEND_APP` | `woven-prod-backend` |
| `FRONTEND_APP` | `woven-prod-frontend` |
| `IMAGE_TAG` | `${{ github.sha }}` |

---

## Local vs. Production Differences

| Concern | Local (Docker Compose) | Production (Azure) |
|---|---|---|
| **Blob storage** | Azurite emulator (port 10000) | Azure Blob Storage (private) |
| **Database** | pgvector/pgvector:pg16 (port 5433) | PostgreSQL Flexible Server (private VNet) |
| **Redis** | redis:7-alpine (port 6379) | Azure Cache for Redis (private endpoint) |
| **Backend reachability** | `http://localhost:5135` | Internal ingress only (not public) |
| **Frontend** | `http://localhost:4202` | Public Container App |
| **JWT key** | Default (insecure) | Proper signing key via env var |
| **OpenAI API key** | User Secrets or `.env` | Injected as Container App secret |
| **Batch workers** | Controlled by `WOVEN_DISABLE_BATCH_WORKERS` | Isolated to workers pod (min=1, max=1) |

---

**Source:** Consolidated from `docs/technical/DEVOPS.md`  
**Last Updated:** 2026-10-07
