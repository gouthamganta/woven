variable "resource_group_name" {
  description = "Resource group to deploy into"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "log_analytics_name" {
  description = "Log Analytics Workspace name"
  type        = string
}

variable "app_insights_name" {
  description = "Application Insights name"
  type        = string
}

variable "retention_days" {
  description = "Data retention in days"
  type        = number
}

variable "tags" {
  description = "Tags to apply to resources"
  type        = map(string)
  default     = {}
}
