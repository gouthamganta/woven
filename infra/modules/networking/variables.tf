variable "resource_group_name" {
  description = "Resource group to deploy into"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "vnet_name" {
  description = "Virtual Network name"
  type        = string
}

variable "address_space" {
  description = "VNet address space"
  type        = list(string)
}

variable "subnet_cidrs" {
  description = "CIDR blocks for each subnet"
  type = object({
    container = string
    db        = string
    private   = string
  })
}

variable "container_subnet_name" {
  description = "Name for the container apps subnet"
  type        = string
}

variable "db_subnet_name" {
  description = "Name for the database subnet"
  type        = string
}

variable "private_subnet_name" {
  description = "Name for the private endpoint subnet"
  type        = string
}

variable "container_nsg_name" {
  description = "Name for the container NSG"
  type        = string
}

variable "db_nsg_name" {
  description = "Name for the database NSG"
  type        = string
}

variable "tags" {
  description = "Tags to apply to resources"
  type        = map(string)
  default     = {}
}
