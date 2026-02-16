project_name   = "woven"
environment    = "prod"
primary_region = "centralindia"

vnet_address_space = ["10.0.0.0/16"]

subnet_cidrs = {
  container = "10.0.1.0/24"
  db        = "10.0.2.0/24"
  private   = "10.0.3.0/24"
}

db_admin_username        = "wovenadmin"
db_sku_name              = "B_Standard_B1ms"
db_storage_mb            = 32768
db_version               = "16"
db_backup_retention_days = 7
db_geo_redundant_backup  = false
db_high_availability     = false

acr_sku = "Basic"

container_apps_internal_only = false

custom_domain = "www.wooven.me"

log_retention_days = 30

allowed_ip_ranges = []

tags = {
  owner       = "woven-team"
  cost_center = "engineering"
}
