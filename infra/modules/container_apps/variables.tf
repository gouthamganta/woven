variable "resource_group_name" {
  description = "Resource group to deploy into"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "environment_name" {
  description = "Container Apps Environment name"
  type        = string
}

variable "log_analytics_workspace_id" {
  description = "Log Analytics Workspace ID for diagnostics"
  type        = string
}

variable "subnet_id" {
  description = "Subnet ID for VNet integration"
  type        = string
}

variable "internal_only" {
  description = "Whether the environment uses internal-only load balancer"
  type        = bool
  default     = false
}

variable "backend_app_name" {
  description = "Name for the backend container app"
  type        = string
}

variable "frontend_app_name" {
  description = "Name for the frontend container app"
  type        = string
}

variable "acr_login_server" {
  description = "ACR login server URL for image pulls"
  type        = string
}

variable "acr_id" {
  description = "ACR resource ID for role assignment scope"
  type        = string
}

variable "postgres_connection_string" {
  description = "PostgreSQL connection string (stored as container app secret)"
  type        = string
  sensitive   = true
}

variable "app_insights_connection_string" {
  description = "Application Insights connection string"
  type        = string
  sensitive   = true
}

variable "backend_image_tag" {
  description = "Docker image tag for the backend container (use a git SHA or semver for stable deployments)"
  type        = string
  default     = "latest"
}

variable "frontend_image_tag" {
  description = "Docker image tag for the frontend container (use a git SHA or semver for stable deployments)"
  type        = string
  default     = "latest"
}

variable "tags" {
  description = "Tags to apply to resources"
  type        = map(string)
  default     = {}
}
