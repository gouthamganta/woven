output "server_id" {
  description = "PostgreSQL Flexible Server resource ID"
  value       = azurerm_postgresql_flexible_server.main.id
}

output "server_fqdn" {
  description = "PostgreSQL Flexible Server FQDN"
  value       = azurerm_postgresql_flexible_server.main.fqdn
}

output "database_name" {
  description = "Database name"
  value       = azurerm_postgresql_flexible_server_database.main.name
}

output "admin_username" {
  description = "Administrator username"
  value       = azurerm_postgresql_flexible_server.main.administrator_login
  sensitive   = true
}

output "admin_password" {
  description = "Administrator password"
  value       = random_password.db_password.result
  sensitive   = true
}

output "connection_string" {
  description = "Connection string for .NET backend"
  value       = "Host=${azurerm_postgresql_flexible_server.main.fqdn};Port=5432;Database=${azurerm_postgresql_flexible_server_database.main.name};Username=${azurerm_postgresql_flexible_server.main.administrator_login};Password=${random_password.db_password.result};SSL Mode=Require;Trust Server Certificate=true"
  sensitive   = true
}
