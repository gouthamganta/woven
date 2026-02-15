output "resource_group_name" {
  description = "Name of the resource group"
  value       = azurerm_resource_group.main.name
}

output "resource_group_id" {
  description = "ID of the resource group"
  value       = azurerm_resource_group.main.id
}

output "vnet_id" {
  description = "Virtual Network resource ID"
  value       = module.networking.vnet_id
}

output "vnet_name" {
  description = "Virtual Network name"
  value       = module.networking.vnet_name
}

output "container_subnet_id" {
  description = "Container Apps subnet ID"
  value       = module.networking.container_subnet_id
}

output "db_subnet_id" {
  description = "Database subnet ID"
  value       = module.networking.db_subnet_id
}

output "postgres_fqdn" {
  description = "PostgreSQL Flexible Server FQDN"
  value       = module.postgres.server_fqdn
}

output "postgres_server_id" {
  description = "PostgreSQL Flexible Server resource ID"
  value       = module.postgres.server_id
}

output "postgres_database_name" {
  description = "PostgreSQL database name"
  value       = module.postgres.database_name
}

output "postgres_admin_username" {
  description = "PostgreSQL admin username"
  value       = module.postgres.admin_username
  sensitive   = true
}

output "postgres_admin_password" {
  description = "PostgreSQL admin password"
  value       = module.postgres.admin_password
  sensitive   = true
}

output "postgres_connection_string" {
  description = "PostgreSQL connection string for backend"
  value       = module.postgres.connection_string
  sensitive   = true
}

output "acr_login_server" {
  description = "ACR login server URL"
  value       = module.acr.login_server
}

output "acr_id" {
  description = "ACR resource ID"
  value       = module.acr.acr_id
}

output "container_apps_environment_id" {
  description = "Container Apps Environment resource ID"
  value       = module.container_apps.environment_id
}

output "container_apps_default_domain" {
  description = "Container Apps Environment default domain"
  value       = module.container_apps.default_domain
}

output "frontend_url" {
  description = "Frontend public URL"
  value       = "https://${module.container_apps.frontend_fqdn}"
}

output "backend_internal_url" {
  description = "Backend internal URL (accessible within Container Apps Environment)"
  value       = "https://${module.container_apps.backend_fqdn}"
}

output "backend_container_app_id" {
  description = "Backend Container App resource ID"
  value       = module.container_apps.backend_container_app_id
}

output "frontend_container_app_id" {
  description = "Frontend Container App resource ID"
  value       = module.container_apps.frontend_container_app_id
}

output "log_analytics_workspace_id" {
  description = "Log Analytics Workspace resource ID"
  value       = module.monitoring.log_analytics_workspace_id
}

output "app_insights_connection_string" {
  description = "Application Insights connection string"
  value       = module.monitoring.app_insights_connection_string
  sensitive   = true
}

output "app_insights_instrumentation_key" {
  description = "Application Insights instrumentation key"
  value       = module.monitoring.app_insights_instrumentation_key
  sensitive   = true
}

output "jwt_secret_key" {
  description = "Auto-generated JWT signing key used by the backend"
  value       = module.container_apps.jwt_secret_key
  sensitive   = true
}
