# Woven Production Environment

## Resources Deployed

| Resource | Name | SKU/Tier |
|----------|------|----------|
| Resource Group | woven-prod-rg | - |
| Virtual Network | woven-prod-vnet | 10.0.0.0/16 |
| PostgreSQL Flexible Server | woven-prod-pg | B_Standard_B1ms |
| Container Registry | wovenprodacr | Basic |
| Container Apps Environment | woven-prod-cae | Consumption |
| Log Analytics Workspace | woven-prod-law | PerGB2018 |
| Application Insights | woven-prod-ai | - |

## Deployment

```bash
cd infra

# Initialize with backend config
terraform init

# Plan with prod variables
terraform plan -var-file="environments/prod/terraform.tfvars" -out=prod.tfplan

# Apply
terraform apply prod.tfplan
```

## Post-Deployment

### Get Database Connection String

```bash
terraform output -raw postgres_connection_string
```

### Get ACR Login Server

```bash
terraform output acr_login_server
```

### Push Docker Images to ACR

```bash
ACR_NAME=$(terraform output -raw acr_login_server)
az acr login --name $ACR_NAME

docker tag woven-backend:latest $ACR_NAME/woven-backend:latest
docker push $ACR_NAME/woven-backend:latest

docker tag woven-frontend:latest $ACR_NAME/woven-frontend:latest
docker push $ACR_NAME/woven-frontend:latest
```

## Scaling

To upgrade database tier, update `db_sku_name` in terraform.tfvars:

| Tier | SKU | Use Case |
|------|-----|----------|
| Burstable | B_Standard_B1ms | Dev/early prod |
| Burstable | B_Standard_B2s | Growing traffic |
| General Purpose | GP_Standard_D2s_v3 | Production |
| Memory Optimized | MO_Standard_E2s_v3 | Heavy workloads |

To enable high availability:

```hcl
db_high_availability = true
db_sku_name          = "GP_Standard_D2s_v3"  # HA requires GP or MO tier
```
