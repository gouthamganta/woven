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

# Install Python 3 + pip for speechbrain voice embedding (Phase 3D)
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    python3-venv \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Install SpeechBrain + PyTorch CPU (no CUDA needed for inference)
RUN pip3 install --no-cache-dir --break-system-packages \
    speechbrain \
    torch \
    torchaudio \
    --extra-index-url https://download.pytorch.org/whl/cpu

# Create non-root user for security
RUN useradd -m -s /bin/bash appuser

# Copy published app
COPY --from=build /app/publish .

# Copy scripts (speechbrain_embed.py)
COPY --from=build /src/scripts ./scripts

# Set ownership
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Verify python script loads (downloads model on first run; --test uses zero vector)
# RUN python3 scripts/speechbrain_embed.py --test

# Azure Container Apps uses its own liveness/readiness/startup probes.
# Docker HEALTHCHECK is not used in Container Apps and the aspnet image lacks curl.
EXPOSE 8080

# Explicit port binding — must match Container Apps ingress target_port and probe ports.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "WovenBackend.dll"]
