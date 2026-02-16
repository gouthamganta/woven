# ==============================================================
# Root Orchestration
# ==============================================================

resource "azurerm_resource_group" "main" {
  name     = local.resource_names.resource_group
  location = var.primary_region
  tags     = local.common_tags
}

# --------------------------------------------------------------
# Monitoring (deployed first — other modules depend on it)
# --------------------------------------------------------------

module "monitoring" {
  source = "./modules/monitoring"

  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  log_analytics_name  = local.resource_names.log_analytics
  app_insights_name   = local.resource_names.app_insights
  retention_days      = var.log_retention_days
  tags                = local.common_tags
}

# --------------------------------------------------------------
# Networking
# --------------------------------------------------------------

module "networking" {
  source = "./modules/networking"

  resource_group_name   = azurerm_resource_group.main.name
  location              = azurerm_resource_group.main.location
  vnet_name             = local.resource_names.vnet
  address_space         = var.vnet_address_space
  subnet_cidrs          = var.subnet_cidrs
  container_subnet_name = local.resource_names.container_subnet
  db_subnet_name        = local.resource_names.db_subnet
  private_subnet_name   = local.resource_names.private_subnet
  container_nsg_name    = local.resource_names.container_nsg
  db_nsg_name           = local.resource_names.db_nsg
  tags                  = local.common_tags
}

# --------------------------------------------------------------
# Private DNS Zone (for PostgreSQL private access)
# --------------------------------------------------------------

resource "azurerm_private_dns_zone" "postgres" {
  name                = local.resource_names.private_dns_zone
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.common_tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "postgres" {
  name                  = "${local.name_prefix}-pg-dns-link"
  private_dns_zone_name = azurerm_private_dns_zone.postgres.name
  resource_group_name   = azurerm_resource_group.main.name
  virtual_network_id    = module.networking.vnet_id
  registration_enabled  = false
  tags                  = local.common_tags
}

# --------------------------------------------------------------
# PostgreSQL
# --------------------------------------------------------------

module "postgres" {
  source = "./modules/postgres"

  resource_group_name   = azurerm_resource_group.main.name
  location              = azurerm_resource_group.main.location
  server_name           = local.resource_names.postgres
  database_name         = var.project_name
  admin_username        = var.db_admin_username
  sku_name              = var.db_sku_name
  storage_mb            = var.db_storage_mb
  postgres_version      = var.db_version
  backup_retention_days = var.db_backup_retention_days
  geo_redundant_backup  = var.db_geo_redundant_backup
  high_availability     = var.db_high_availability
  delegated_subnet_id   = module.networking.db_subnet_id
  private_dns_zone_id   = azurerm_private_dns_zone.postgres.id
  tags                  = local.common_tags

  depends_on = [azurerm_private_dns_zone_virtual_network_link.postgres]
}

# --------------------------------------------------------------
# Azure Container Registry
# --------------------------------------------------------------

module "acr" {
  source = "./modules/acr"

  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  acr_name            = local.resource_names.acr
  sku                 = var.acr_sku
  tags                = local.common_tags
}

# --------------------------------------------------------------
# Container Apps Environment
# --------------------------------------------------------------

module "container_apps" {
  source = "./modules/container_apps"

  resource_group_name            = azurerm_resource_group.main.name
  location                       = azurerm_resource_group.main.location
  environment_name               = local.resource_names.container_env
  log_analytics_workspace_id     = module.monitoring.log_analytics_workspace_id
  subnet_id                      = module.networking.container_subnet_id
  internal_only                  = var.container_apps_internal_only
  backend_app_name               = local.resource_names.backend_app
  frontend_app_name              = local.resource_names.frontend_app
  acr_login_server               = module.acr.login_server
  acr_id                         = module.acr.acr_id
  postgres_connection_string     = module.postgres.connection_string
  app_insights_connection_string = module.monitoring.app_insights_connection_string
  backend_image_tag              = var.backend_image_tag
  frontend_image_tag             = var.frontend_image_tag
  custom_domain                  = var.custom_domain
  tags                           = local.common_tags
}
