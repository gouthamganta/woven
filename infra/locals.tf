locals {
  name_prefix = "${var.project_name}-${var.environment}"

  resource_names = {
    resource_group   = "${local.name_prefix}-rg"
    vnet             = "${local.name_prefix}-vnet"
    container_subnet = "${local.name_prefix}-container-subnet"
    db_subnet        = "${local.name_prefix}-db-subnet"
    private_subnet   = "${local.name_prefix}-private-subnet"
    container_nsg    = "${local.name_prefix}-container-nsg"
    db_nsg           = "${local.name_prefix}-db-nsg"
    postgres         = "${local.name_prefix}-pg"
    acr              = replace("${var.project_name}${var.environment}acr", "-", "")
    container_env    = "${local.name_prefix}-cae"
    backend_app      = "${local.name_prefix}-backend"
    frontend_app     = "${local.name_prefix}-frontend"
    log_analytics    = "${local.name_prefix}-law"
    app_insights     = "${local.name_prefix}-ai"
    private_dns_zone = "privatelink.postgres.database.azure.com"
  }

  common_tags = merge(
    {
      environment = var.environment
      project     = var.project_name
      managed_by  = "terraform"
      region      = var.primary_region
    },
    var.tags
  )
}
