variable "project_name" {
  description = "Project name used in resource naming"
  type        = string
  default     = "woven"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,20}$", var.project_name))
    error_message = "Project name must be lowercase alphanumeric with hyphens, 2-21 characters."
  }
}

variable "environment" {
  description = "Deployment environment"
  type        = string

  validation {
    condition     = contains(["prod", "staging", "dev"], var.environment)
    error_message = "Environment must be one of: prod, staging, dev."
  }
}

variable "primary_region" {
  description = "Primary Azure region for resource deployment"
  type        = string
  default     = "centralindia"
}

variable "vnet_address_space" {
  description = "Address space for the Virtual Network"
  type        = list(string)
  default     = ["10.0.0.0/16"]

  validation {
    condition     = length(var.vnet_address_space) > 0
    error_message = "At least one address space must be provided."
  }
}

variable "subnet_cidrs" {
  description = "CIDR blocks for each subnet"
  type = object({
    container = string
    db        = string
    private   = string
  })
  default = {
    container = "10.0.1.0/24"
    db        = "10.0.2.0/24"
    private   = "10.0.3.0/24"
  }
}

variable "db_admin_username" {
  description = "PostgreSQL administrator username"
  type        = string
  default     = "wovenadmin"

  validation {
    condition     = !contains(["admin", "administrator", "root", "postgres", "azure_superuser"], var.db_admin_username)
    error_message = "Database admin username cannot be a reserved name."
  }
}

variable "db_sku_name" {
  description = "PostgreSQL Flexible Server SKU (compute tier)"
  type        = string
  default     = "B_Standard_B1ms"

  validation {
    condition     = can(regex("^(B_Standard_|GP_Standard_|MO_Standard_)", var.db_sku_name))
    error_message = "SKU must start with B_Standard_, GP_Standard_, or MO_Standard_."
  }
}

variable "db_storage_mb" {
  description = "PostgreSQL storage size in MB"
  type        = number
  default     = 32768

  validation {
    condition     = var.db_storage_mb >= 32768
    error_message = "Minimum storage is 32768 MB (32 GB)."
  }
}

variable "db_version" {
  description = "PostgreSQL major version"
  type        = string
  default     = "16"

  validation {
    condition     = contains(["14", "15", "16"], var.db_version)
    error_message = "PostgreSQL version must be 14, 15, or 16."
  }
}

variable "db_backup_retention_days" {
  description = "Backup retention period in days"
  type        = number
  default     = 7

  validation {
    condition     = var.db_backup_retention_days >= 7 && var.db_backup_retention_days <= 35
    error_message = "Backup retention must be between 7 and 35 days."
  }
}

variable "db_geo_redundant_backup" {
  description = "Enable geo-redundant backups for PostgreSQL"
  type        = bool
  default     = false
}

variable "db_high_availability" {
  description = "Enable zone-redundant high availability for PostgreSQL"
  type        = bool
  default     = false
}

variable "acr_sku" {
  description = "Azure Container Registry SKU"
  type        = string
  default     = "Basic"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.acr_sku)
    error_message = "ACR SKU must be Basic, Standard, or Premium."
  }
}

variable "container_apps_internal_only" {
  description = "Whether Container Apps Environment uses internal-only load balancer"
  type        = bool
  default     = false
}

variable "backend_image_tag" {
  description = "Docker image tag for the backend container app (use git SHA or semver for stable deploys)"
  type        = string
  default     = "latest"
}

variable "frontend_image_tag" {
  description = "Docker image tag for the frontend container app (use git SHA or semver for stable deploys)"
  type        = string
  default     = "latest"
}

variable "custom_domain" {
  description = "Custom domain for the frontend (e.g. www.wooven.me). Added to backend CORS."
  type        = string
  default     = ""
}

variable "log_retention_days" {
  description = "Log Analytics workspace retention in days"
  type        = number
  default     = 30

  validation {
    condition     = var.log_retention_days >= 30 && var.log_retention_days <= 730
    error_message = "Log retention must be between 30 and 730 days."
  }
}

variable "allowed_ip_ranges" {
  description = "IP CIDR ranges allowed for management access (future use)"
  type        = list(string)
  default     = []
}

variable "tags" {
  description = "Additional tags to apply to all resources"
  type        = map(string)
  default     = {}
}
