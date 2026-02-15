variable "resource_group_name" {
  description = "Resource group to deploy into"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "server_name" {
  description = "PostgreSQL Flexible Server name"
  type        = string
}

variable "database_name" {
  description = "Name of the database to create"
  type        = string
  default     = "woven"
}

variable "admin_username" {
  description = "Administrator username"
  type        = string
  sensitive   = true
}

variable "sku_name" {
  description = "Server SKU (compute tier)"
  type        = string
}

variable "storage_mb" {
  description = "Storage size in MB"
  type        = number
}

variable "postgres_version" {
  description = "PostgreSQL major version"
  type        = string
}

variable "backup_retention_days" {
  description = "Backup retention in days"
  type        = number
}

variable "geo_redundant_backup" {
  description = "Enable geo-redundant backups"
  type        = bool
}

variable "high_availability" {
  description = "Enable zone-redundant HA"
  type        = bool
}

variable "delegated_subnet_id" {
  description = "Subnet ID for PostgreSQL VNet integration"
  type        = string
}

variable "private_dns_zone_id" {
  description = "Private DNS Zone ID for PostgreSQL"
  type        = string
}

variable "tags" {
  description = "Tags to apply to resources"
  type        = map(string)
  default     = {}
}
