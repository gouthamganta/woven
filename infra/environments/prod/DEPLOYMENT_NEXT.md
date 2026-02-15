# Container Apps Deployment (Hardened)

## What Changed (Stability Hardening)

### Root Cause of "Operation expired"
The `/health` endpoint called `db.Database.CanConnectAsync()` — if DB was slow to respond through VNet + private DNS, the probe returned 503, Azure killed the container, and the restart loop exceeded the provisioning timeout.

### Fixes Applied

**Backend (Program.cs)**
- Split health into 3 endpoints:
  - `/health/live` — returns 200 always (liveness probe, no external deps)
  - `/health/ready` — checks DB connectivity (readiness probe)
  - `/health` — lightweight 200 for general monitoring
- Added `EnableRetryOnFailure(5)` on Npgsql — tolerates transient DB failures during startup
- Added `UseForwardedHeaders()` — Container Apps terminates TLS at ingress
- Moved `UseHttpsRedirection()` to dev-only — not needed behind Container Apps ingress
- Added startup logging (environment, URLs, DB config status)

**Backend (Dockerfile)**
- Removed broken `HEALTHCHECK` — `curl` doesn't exist in `aspnet:10.0` image; Azure Container Apps uses its own probes

**Terraform (container_apps module)**
- Added `startup_probe` on backend: 150s boot window (30 failures x 5s interval)
- Changed `liveness_probe` to hit `/health/live` (no DB dependency)
- Changed `readiness_probe` to hit `/health/ready` (DB connectivity check)
- Added startup_probe on frontend (30s boot window)
- Added `ASPNETCORE_URLS=http://+:8080` env var (safety net for port binding)
- Added `Jwt__Key` as auto-generated secret (64-char random, stored in state)
- Added `Cors__AllowedOrigins` pointing to frontend FQDN (no circular dep)
- Added `backend_image_tag` / `frontend_image_tag` variables (defaults to `latest`)
- Removed redundant `ignore_changes` on computed-only environment attributes

---

## Architecture

### Backend (`woven-prod-backend`)
- **Ingress**: Internal only
- **Port**: 8080
- **Image**: `wovenprodacr.azurecr.io/woven-backend:<tag>`
- **Scaling**: 1-5 replicas
- **Resources**: 0.5 vCPU, 1Gi memory
- **Probes**:
  - Startup: `/health/live:8080` — 5s interval, 30 failures max (150s window)
  - Liveness: `/health/live:8080` — 30s interval, no external deps
  - Readiness: `/health/ready:8080` — 10s interval, checks DB
- **Secrets** (Container App secrets, not plain env vars):
  - `ConnectionStrings__DefaultConnection` — PostgreSQL (SSL Mode=Require)
  - `APPLICATIONINSIGHTS_CONNECTION_STRING` — App Insights
  - `Jwt__Key` — Auto-generated 64-char signing key
- **Plain env vars**:
  - `ASPNETCORE_URLS=http://+:8080`
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `Cors__AllowedOrigins=https://<frontend-fqdn>`

### Frontend (`woven-prod-frontend`)
- **Ingress**: External (public HTTPS)
- **Port**: 80
- **Image**: `wovenprodacr.azurecr.io/woven-frontend:<tag>`
- **Scaling**: 1-3 replicas
- **Resources**: 0.5 vCPU, 1Gi memory
- **Probes**: Startup + liveness + readiness on `/:80`
- **Env vars**: `API_BASE_URL` — Backend internal FQDN

### ACR Authentication
- Both apps use **system-assigned managed identity** with **AcrPull** role

---

## Deploy

```bash
cd infra

# Default (uses :latest tag)
terraform plan -var-file="environments/prod/terraform.tfvars" -out=prod.tfplan
terraform apply prod.tfplan

# With specific image tags (recommended for production)
terraform plan \
  -var-file="environments/prod/terraform.tfvars" \
  -var="backend_image_tag=abc1234" \
  -var="frontend_image_tag=abc1234" \
  -out=prod.tfplan
terraform apply prod.tfplan
```

## Get Outputs

```bash
terraform output frontend_url
terraform output backend_internal_url
terraform output -raw jwt_secret_key    # for local testing only
```

---

## CORS Configuration

CORS is now auto-configured in Terraform. The backend receives `Cors__AllowedOrigins` set to the frontend's Container App FQDN.

To add a custom domain later, update the env var in Terraform:
```hcl
env {
  name  = "Cors__AllowedOrigins"
  value = "https://${var.frontend_app_name}.${azurerm_container_app_environment.main.default_domain},https://wooven.me"
}
```

---

## Custom Domain (Future)

1. Add a CNAME record pointing to the frontend FQDN
2. Use `azurerm_container_app_custom_domain` resource in Terraform
3. Azure will provision a managed TLS certificate automatically
4. Update CORS origins to include `https://wooven.me`

---

## Troubleshooting

| Issue | Check |
|-------|-------|
| Image pull fails | Verify AcrPull role: `az role assignment list --scope <acr-id>` |
| Backend startup timeout | Check startup_probe gives 150s: `az containerapp logs show -n woven-prod-backend -g woven-prod-rg` |
| Backend unhealthy after boot | Check `/health/ready` — likely DB connectivity. Verify NSG allows container-subnet on 5432 |
| DB connection timeout | Private DNS zone link must exist. Check `az network private-dns link vnet list` |
| Frontend can't reach backend | Backend ingress must be internal. Frontend uses internal FQDN via `API_BASE_URL` |
| JWT errors | Key is auto-generated. Check: `terraform output -raw jwt_secret_key` |
| CORS errors | Check `Cors__AllowedOrigins` matches frontend URL exactly (including https://) |
