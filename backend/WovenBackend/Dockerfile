# ================================
# Woven Backend - Multi-stage Dockerfile
# ================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["WovenBackend.csproj", "./"]
RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN useradd -m -s /bin/bash appuser
# Copy published app
COPY --from=build /app/publish .

# Set ownership
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Azure Container Apps uses its own liveness/readiness/startup probes.
# Docker HEALTHCHECK is not used in Container Apps and the aspnet image lacks curl.
EXPOSE 8080

# Explicit port binding — must match Container Apps ingress target_port and probe ports.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "WovenBackend.dll"]
