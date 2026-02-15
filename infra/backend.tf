terraform {
  backend "azurerm" {
    resource_group_name  = "wooven-tf-rg"
    storage_account_name = "wooventfstate84732"
    container_name       = "tfstate"
    key                  = "wooven-prod.tfstate"
    use_azuread_auth     = true
  }
}
