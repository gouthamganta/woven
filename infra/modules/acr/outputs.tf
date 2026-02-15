output "acr_id" {
  description = "Container Registry resource ID"
  value       = azurerm_container_registry.main.id
}

output "login_server" {
  description = "ACR login server URL"
  value       = azurerm_container_registry.main.login_server
}

output "acr_name" {
  description = "Container Registry name"
  value       = azurerm_container_registry.main.name
}

output "identity_principal_id" {
  description = "System-assigned managed identity principal ID"
  value       = azurerm_container_registry.main.identity[0].principal_id
}
