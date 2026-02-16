# ==============================================================
# Container Apps Environment
# ==============================================================

resource "azurerm_container_app_environment" "main" {
  name                           = var.environment_name
  location                       = var.location
  resource_group_name            = var.resource_group_name
  log_analytics_workspace_id     = var.log_analytics_workspace_id
  infrastructure_subnet_id       = var.subnet_id
  internal_load_balancer_enabled = var.internal_only
  tags                           = var.tags

  # Azure auto-creates a managed infrastructure resource group (ME_<name>_<rg>_<region>).
  # This value is NOT set in our config, so Terraform sees it go from "<value>" -> null
  # and forces a REPLACEMENT of the entire environment (~20 min downtime).
  # ignore_changes prevents this destructive drift.
  lifecycle {
    ignore_changes = [
      infrastructure_resource_group_name,
    ]
  }
}

# ==============================================================
# User-Assigned Managed Identity for ACR Pull
# ==============================================================
# Created BEFORE container apps so AcrPull role is ready when
# the container app tries to pull images. This breaks the
# chicken-and-egg problem with system-assigned identity:
#   system-assigned: app must exist → identity created → role assigned → but app creation needs image pull → fails
#   user-assigned:   identity created → role assigned → app references identity → image pull succeeds

resource "azurerm_user_assigned_identity" "acr_pull" {
  name                = "${var.backend_app_name}-acr-identity"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_role_assignment" "acr_pull" {
  principal_id                     = azurerm_user_assigned_identity.acr_pull.principal_id
  role_definition_name             = "AcrPull"
  scope                            = var.acr_id
  skip_service_principal_aad_check = true
}

# ==============================================================
# JWT Signing Key (auto-generated, stored in Terraform state)
# ==============================================================

resource "random_password" "jwt_key" {
  length           = 64
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
  min_lower        = 8
  min_upper        = 8
  min_numeric      = 8
  min_special      = 4
}

# ==============================================================
# Backend Container App
# ==============================================================

resource "azurerm_container_app" "backend" {
  name                         = var.backend_app_name
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.acr_pull.id]
  }

  # Use the user-assigned identity for ACR pulls.
  # The role assignment is created before this resource (dependency via identity_ids).
  registry {
    server   = var.acr_login_server
    identity = azurerm_user_assigned_identity.acr_pull.id
  }

  # ----------------------------------------------------------
  # Secrets — sensitive values injected via Container App secrets,
  # never as plain environment variables.
  # ----------------------------------------------------------

  secret {
    name  = "db-conn"
    value = var.postgres_connection_string
  }

  secret {
    name  = "appinsights-conn"
    value = var.app_insights_connection_string
  }

  secret {
    name  = "jwt-key"
    value = random_password.jwt_key.result
  }

  template {
    min_replicas = 1
    max_replicas = 5

    container {
      name   = "backend"
      image  = "${var.acr_login_server}/woven-backend:${var.backend_image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      # --- Secrets as env vars ---
      env {
        name        = "ConnectionStrings__DefaultConnection"
        secret_name = "db-conn"
      }

      env {
        name        = "APPLICATIONINSIGHTS_CONNECTION_STRING"
        secret_name = "appinsights-conn"
      }

      env {
        name        = "Jwt__Key"
        secret_name = "jwt-key"
      }

      # --- Plain env vars ---

      # Explicit port binding. Must match EXPOSE in Dockerfile and probe ports below.
      # Safety net in case Dockerfile ENV gets overridden.
      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:8080"
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      # CORS: allow the frontend Container App origin + custom domain if set.
      # Constructed from the environment default domain to avoid circular dependency
      # (frontend depends_on backend, so backend cannot reference frontend resource).
      env {
        name = "Cors__AllowedOrigins"
        value = var.custom_domain != "" ? "https://${var.frontend_app_name}.${azurerm_container_app_environment.main.default_domain},https://${var.custom_domain}" : "https://${var.frontend_app_name}.${azurerm_container_app_environment.main.default_domain}"
      }

      # ----------------------------------------------------------
      # Probes — tuned for Azure Container Apps + .NET cold start.
      #
      # startup_probe:  Runs only during initial boot. Gives the container
      #                 up to 100s (10 failures x 10s interval) to become live.
      #                 Max failure_count_threshold is 10 (Azure limit).
      #                 While running, liveness and readiness probes are paused.
      #
      # liveness_probe: After startup succeeds, checks every 30s that the
      #                 process is still alive. Uses /health/live which returns
      #                 200 unconditionally (no external deps). Failure kills
      #                 the container.
      #
      # readiness_probe: Checks if the app can serve traffic (DB connected).
      #                  Uses /health/ready which verifies DB connectivity.
      #                  Failure removes the container from the load balancer
      #                  but does NOT kill it.
      # ----------------------------------------------------------

      startup_probe {
        transport               = "HTTP"
        path                    = "/health/live"
        port                    = 8080
        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = 10
      }

      liveness_probe {
        transport               = "HTTP"
        path                    = "/health/live"
        port                    = 8080
        initial_delay           = 1
        interval_seconds        = 30
        timeout                 = 5
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        path                    = "/health/ready"
        port                    = 8080
        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = 3
        success_count_threshold = 1
      }
    }
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  # Ensure role assignment exists before attempting image pull
  depends_on = [azurerm_role_assignment.acr_pull]
}

# ==============================================================
# Frontend Container App
# ==============================================================

resource "azurerm_container_app" "frontend" {
  name                         = var.frontend_app_name
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "SystemAssigned, UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.acr_pull.id]
  }

  registry {
    server   = var.acr_login_server
    identity = azurerm_user_assigned_identity.acr_pull.id
  }

  template {
    min_replicas = 1
    max_replicas = 3

    container {
      name   = "frontend"
      image  = "${var.acr_login_server}/woven-frontend:${var.frontend_image_tag}"
      cpu    = 0.5
      memory = "1Gi"

      # BACKEND_URL is used by envsubst in the nginx.conf.template at container startup.
      # It replaces the Docker Compose default (http://backend:8080) with the Container Apps internal FQDN.
      # Note: Internal Container Apps communication uses HTTP, not HTTPS
      env {
        name  = "BACKEND_URL"
        value = "http://${azurerm_container_app.backend.ingress[0].fqdn}"
      }

      # Frontend is nginx serving static files — starts fast, no external deps.

      startup_probe {
        transport               = "HTTP"
        path                    = "/"
        port                    = 80
        interval_seconds        = 3
        timeout                 = 3
        failure_count_threshold = 10
      }

      liveness_probe {
        transport               = "HTTP"
        path                    = "/"
        port                    = 80
        initial_delay           = 1
        interval_seconds        = 30
        timeout                 = 5
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        path                    = "/"
        port                    = 80
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
        success_count_threshold = 1
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 80
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  depends_on = [
    azurerm_container_app.backend,
    azurerm_role_assignment.acr_pull,
  ]
}
