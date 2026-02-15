output "vnet_id" {
  description = "Virtual Network resource ID"
  value       = azurerm_virtual_network.main.id
}

output "vnet_name" {
  description = "Virtual Network name"
  value       = azurerm_virtual_network.main.name
}

output "container_subnet_id" {
  description = "Container Apps subnet ID"
  value       = azurerm_subnet.container.id
}

output "db_subnet_id" {
  description = "Database subnet ID"
  value       = azurerm_subnet.db.id
}

output "private_subnet_id" {
  description = "Private endpoint subnet ID"
  value       = azurerm_subnet.private.id
}

output "container_nsg_id" {
  description = "Container NSG ID"
  value       = azurerm_network_security_group.container.id
}

output "db_nsg_id" {
  description = "Database NSG ID"
  value       = azurerm_network_security_group.db.id
}
