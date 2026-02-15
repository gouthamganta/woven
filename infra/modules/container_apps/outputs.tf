output "environment_id" {
  description = "Container Apps Environment resource ID"
  value       = azurerm_container_app_environment.main.id
}

output "environment_name" {
  description = "Container Apps Environment name"
  value       = azurerm_container_app_environment.main.name
}

output "default_domain" {
  description = "Default domain for the environment"
  value       = azurerm_container_app_environment.main.default_domain
}

output "static_ip_address" {
  description = "Static IP address of the environment"
  value       = azurerm_container_app_environment.main.static_ip_address
}

output "backend_container_app_id" {
  description = "Backend Container App resource ID"
  value       = azurerm_container_app.backend.id
}

output "backend_fqdn" {
  description = "Backend Container App internal FQDN"
  value       = azurerm_container_app.backend.ingress[0].fqdn
}

output "frontend_container_app_id" {
  description = "Frontend Container App resource ID"
  value       = azurerm_container_app.frontend.id
}

output "frontend_fqdn" {
  description = "Frontend Container App public FQDN"
  value       = azurerm_container_app.frontend.ingress[0].fqdn
}

output "jwt_secret_key" {
  description = "Auto-generated JWT signing key (store securely, do not share)"
  value       = random_password.jwt_key.result
  sensitive   = true
}
