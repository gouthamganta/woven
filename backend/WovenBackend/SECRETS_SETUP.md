# Secrets Management Setup

## Overview

All sensitive credentials have been moved out of `appsettings.json` to prevent accidental commits to source control.

## Local Development (User Secrets)

Secrets are stored in your local user profile using .NET User Secrets:

```powershell
# View all secrets
dotnet user-secrets list

# Set a secret
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=woven_db;Username=woven;Password=YOUR_PASSWORD"

# Remove a secret
dotnet user-secrets remove "Jwt:Key"

# Clear all secrets
dotnet user-secrets clear
```

### Required Secrets for Local Development

```powershell
cd backend/WovenBackend

# Database connection
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=woven_db;Username=woven;Password=woven"

# JWT signing key (generate a strong random key for production)
dotnet user-secrets set "Jwt:Key" "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_MIN_32_CHARS"

# Azure Storage (local emulator)
dotnet user-secrets set "Azure:Storage:ConnectionString" "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://localhost:10000"

# VAPID private key for Web Push
dotnet user-secrets set "Vapid:PrivateKey" "YOUR_VAPID_PRIVATE_KEY_HERE"

# OpenAI API key (if not already in environment)
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
```

## Production (Azure Key Vault)

In production, secrets are loaded from Azure Key Vault using Managed Identity:

### Setup Steps

1. **Create Azure Key Vault** (if not exists):
```bash
az keyvault create \
  --name woven-prod-kv \
  --resource-group woven-prod-rg \
  --location eastus
```

2. **Enable Managed Identity** on Container Apps:
```bash
az containerapp identity assign \
  --name woven-backend \
  --resource-group woven-prod-rg \
  --system-assigned
```

3. **Grant Key Vault access** to the managed identity:
```bash
# Get the managed identity principal ID
PRINCIPAL_ID=$(az containerapp show \
  --name woven-backend \
  --resource-group woven-prod-rg \
  --query identity.principalId -o tsv)

# Grant access
az keyvault set-policy \
  --name woven-prod-kv \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list
```

4. **Add secrets to Key Vault**:
```bash
# Database connection string
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "ConnectionStrings--DefaultConnection" \
  --value "Host=woven-prod-pg.postgres.database.azure.com;Port=5432;Database=woven_db;Username=woven;Password=PROD_PASSWORD;SSL Mode=Require"

# JWT key (generate with: openssl rand -base64 64)
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "Jwt--Key" \
  --value "GENERATE_A_STRONG_RANDOM_KEY_HERE"

# Azure Storage
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "Azure--Storage--ConnectionString" \
  --value "DefaultEndpointsProtocol=https;AccountName=wovenprod;AccountKey=...;EndpointSuffix=core.windows.net"

# VAPID private key
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "Vapid--PrivateKey" \
  --value "YOUR_PROD_VAPID_PRIVATE_KEY"

# OpenAI API key
az keyvault secret set \
  --vault-name woven-prod-kv \
  --name "OpenAI--ApiKey" \
  --value "sk-..."
```

5. **Configure Container App** to use Key Vault:
```bash
az containerapp update \
  --name woven-backend \
  --resource-group woven-prod-rg \
  --set-env-vars "KeyVault__Name=woven-prod-kv"
```

### Key Vault Secret Naming Convention

Azure Key Vault doesn't support `:` in secret names. Use `--` (double dash) as a replacement:

| Configuration Path | Key Vault Secret Name |
|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings--DefaultConnection` |
| `Jwt:Key` | `Jwt--Key` |
| `Azure:Storage:ConnectionString` | `Azure--Storage--ConnectionString` |

The Azure Key Vault configuration provider automatically converts `--` back to `:` when loading.

## Security Best Practices

✅ **DO:**
- Rotate secrets quarterly
- Use strong, randomly generated keys (min 256 bits)
- Enable Key Vault soft-delete and purge protection
- Monitor Key Vault access logs
- Use separate Key Vaults for staging and production

❌ **DON'T:**
- Commit secrets to source control
- Share secrets via email or Slack
- Reuse secrets across environments
- Use weak or predictable keys
- Disable audit logging

## Troubleshooting

### "Secret not found" errors locally
```powershell
# Verify secrets are set
dotnet user-secrets list

# Re-initialize if needed
dotnet user-secrets init
```

### Key Vault access denied in production
```bash
# Verify managed identity is assigned
az containerapp identity show \
  --name woven-backend \
  --resource-group woven-prod-rg

# Verify Key Vault access policy
az keyvault show \
  --name woven-prod-kv \
  --query properties.accessPolicies
```

### Connection string not loading
- Check `KeyVault__Name` environment variable is set
- Verify secret name uses `--` not `:`
- Check managed identity has `get` and `list` permissions

## Migration Checklist

- [x] Move secrets to user-secrets (local)
- [x] Remove secrets from appsettings.json
- [x] Add Azure Key Vault support to Program.cs
- [x] Install Azure.Identity and Azure.Extensions.AspNetCore.Configuration.Secrets
- [ ] Create Azure Key Vault (production)
- [ ] Enable managed identity on Container Apps
- [ ] Grant Key Vault access to managed identity
- [ ] Add all secrets to Key Vault
- [ ] Rotate all current credentials (database password, JWT key, etc.)
- [ ] Test production deployment
- [ ] Enable Key Vault audit logging
- [ ] Document secret rotation procedures
