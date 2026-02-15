# Woven Infrastructure

Azure production infrastructure managed by Terraform.

**Region:** Central India | **State:** Azure Storage (`wooventfstate84732`) | **Provider:** azurerm ~> 3.100

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  woven-prod-rg (Central India)                                   │
│                                                                  │
│  ┌──────────── woven-prod-vnet (10.0.0.0/16) ─────────────────┐ │
│  │                                                             │ │
│  │  ┌─────────────────────┐   container-nsg                    │ │
│  │  │  container-subnet   │◄── Allow 80/443 inbound            │ │
│  │  │  10.0.1.0/24        │    Deny all other inbound          │ │
│  │  │                     │                                    │ │
│  │  │  Container Apps Env │                                    │ │
│  │  │  (backend+frontend) │                                    │ │
│  │  └──────────┬──────────┘                                    │ │
│  │             │ port 5432 (private)                            │ │
│  │  ┌──────────▼──────────┐   db-nsg                           │ │
│  │  │  db-subnet          │◄── Allow 5432 from container-subnet│ │
│  │  │  10.0.2.0/24        │    Deny all other inbound          │ │
│  │  │                     │                                    │ │
│  │  │  PostgreSQL 16      │    Private DNS:                    │ │
│  │  │  Flexible Server    │    privatelink.postgres.database.  │ │
│  │  │  (no public access) │    azure.com                       │ │
│  │  └─────────────────────┘                                    │ │
│  │                                                             │ │
│  │  ┌─────────────────────┐                                    │ │
│  │  │  private-subnet     │   Reserved for future              │ │
│  │  │  10.0.3.0/24        │   private endpoints                │ │
│  │  └─────────────────────┘                                    │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌────────────┐  ┌─────────────────┐  ┌───────────────────────┐ │
│  │ wovenprod  │  │ woven-prod-law  │  │ woven-prod-ai         │ │
│  │ acr        │  │ Log Analytics   │  │ Application Insights  │ │
│  │ (Basic)    │  │ (30-day retain) │  │                       │ │
│  └────────────┘  └─────────────────┘  └───────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

## Resources

| Resource | Name | Config | Purpose |
|----------|------|--------|---------|
| Resource Group | `woven-prod-rg` | Central India | All resources live here |
| VNet | `woven-prod-vnet` | 10.0.0.0/16 | Network isolation |
| Container Subnet | `woven-prod-container-subnet` | 10.0.1.0/24, delegated to `Microsoft.App/environments` | Container Apps |
| DB Subnet | `woven-prod-db-subnet` | 10.0.2.0/24, delegated to `Microsoft.DBforPostgreSQL/flexibleServers` | PostgreSQL |
| Private Subnet | `woven-prod-private-subnet` | 10.0.3.0/24 | Future private endpoints |
| PostgreSQL | `woven-prod-pg` | B_Standard_B1ms, 32GB, v16, SSL required, TLS 1.2+ | Primary database |
| ACR | `wovenprodacr` | Basic, admin disabled, managed identity | Docker image registry |
| Container Apps Env | `woven-prod-cae` | VNet-integrated, consumption plan | Runs backend + frontend |
| Log Analytics | `woven-prod-law` | 30-day retention | Centralized logging |
| App Insights | `woven-prod-ai` | Connected to Log Analytics | APM + telemetry |

## Security

- **Database**: Zero public access. Only reachable from container-subnet via private DNS. SSL enforced, TLS 1.2 minimum.
- **NSGs**: Container subnet allows 80/443 inbound only. DB subnet allows 5432 from container subnet only. Everything else denied.
- **ACR**: Admin disabled. Uses system-assigned managed identity with AcrPull role.
- **Secrets**: DB password generated via `random_password` (32 chars). Exposed only as sensitive Terraform outputs.

## Quick Start

```bash
cd infra

# First time
terraform init

# Deploy
terraform plan -var-file="environments/prod/terraform.tfvars" -out=prod.tfplan
terraform apply prod.tfplan

# Get outputs
terraform output acr_login_server
terraform output -raw postgres_connection_string
```

## Push Images to ACR

```bash
ACR=$(terraform output -raw acr_login_server)
az acr login --name $ACR

docker tag woven-backend:latest $ACR/woven-backend:latest
docker push $ACR/woven-backend:latest

docker tag woven-frontend:latest $ACR/woven-frontend:latest
docker push $ACR/woven-frontend:latest
```

## Scaling

Edit `environments/prod/terraform.tfvars`, then plan + apply.

| Change | Variable | Example |
|--------|----------|---------|
| Upgrade DB compute | `db_sku_name` | `"GP_Standard_D2s_v3"` |
| Enable DB HA | `db_high_availability` | `true` (requires GP/MO tier) |
| More DB storage | `db_storage_mb` | `65536` (64GB) |
| Upgrade ACR | `acr_sku` | `"Standard"` or `"Premium"` |
| Geo-redundant backups | `db_geo_redundant_backup` | `true` |
| Longer log retention | `log_retention_days` | `90` |

## Add New Environment

1. Create `environments/dev/terraform.tfvars` with different CIDR (`10.1.0.0/16`) and environment name
2. Use a different state key (e.g., `woven-dev.tfstate`)
3. Run `terraform init -reconfigure` then plan/apply

## Troubleshooting

| Problem | Fix |
|---------|-----|
| State lock stuck | `terraform force-unlock <lock-id>` |
| DB connection timeout | Check db-nsg allows container subnet on 5432. Verify private DNS zone link exists. |
| ACR auth failed | Run `az acr login --name wovenprodacr` |
| Container Apps won't deploy | Verify container-subnet delegation to `Microsoft.App/environments` |
| Terraform drift | Run `terraform plan` to see differences, then apply or import |

## Estimated Monthly Cost (Central India)

| Resource | Estimate |
|----------|----------|
| PostgreSQL B_Standard_B1ms | ~$13 |
| ACR Basic | ~$5 |
| Container Apps (consumption) | Pay-per-use (~$0-20) |
| Log Analytics (30 days) | ~$2-5/GB ingested |
| VNet + NSGs | Free |
| **Total (light usage)** | **~$25-50/month** |

## Module Structure

```
infra/
├── backend.tf              # State backend (Azure Storage)
├── versions.tf             # Terraform + provider versions
├── provider.tf             # azurerm provider features
├── variables.tf            # All inputs with validation
├── locals.tf               # Naming conventions + tags
├── main.tf                 # Orchestrates modules + DNS + role assignments
├── outputs.tf              # All exported values
├── modules/
│   ├── networking/         # VNet, 3 subnets, 2 NSGs, associations
│   ├── postgres/           # Flexible Server, SSL config, database
│   ├── acr/                # Container Registry + managed identity
│   ├── container_apps/     # Container Apps Environment
│   └── monitoring/         # Log Analytics + Application Insights
└── environments/
    └── prod/
        └── terraform.tfvars
```
