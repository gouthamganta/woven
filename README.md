# Woven

A modern matchmaking and social interaction platform with AI-powered games, dynamic user profiling, and a unique balloon-based connection system.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Getting Started](#getting-started)
- [Development](#development)
- [Docker Deployment](#docker-deployment)
- [Production Deployment](#production-deployment)
- [API Documentation](#api-documentation)
- [Configuration](#configuration)
- [CI/CD](#cicd)
- [Contributing](#contributing)

---

## Overview

Woven is a matchmaking platform that takes a fresh approach to online connections:

- **Moments System**: Daily themed choices (Brunch vs Dinner) that create meaningful matches
- **Balloon Matches**: Time-limited connections (36 hours) that encourage genuine interaction
- **Trial Period**: 1-minute trial after "popping" a balloon to decide if the connection continues
- **AI-Powered Games**: Interactive games (Know Me, Red/Green Flag) to break the ice
- **Rating System**: Community-driven quality signals for better matching
- **Find Love Stage**: Unlocked after mutual engagement with personalized date ideas

---

## Architecture

```
woven/
├── backend/
│   └── WovenBackend/          # ASP.NET Core 10.0 Web API
│       ├── Auth/              # JWT + Google OAuth
│       ├── Endpoints/         # Minimal API endpoints
│       ├── Services/          # Business logic
│       │   ├── Games/         # Game agents (Know Me, Red/Green Flag)
│       │   ├── Matchmaking/   # Daily deck, scoring, explanations
│       │   └── Moments/       # Match creation, balloon expiry
│       ├── data/              # EF Core entities & context
│       └── Migrations/        # Database migrations
├── frontend/
│   └── woven-frontend/        # Angular 21 SPA
│       └── src/app/
│           ├── pages/         # Route components
│           ├── services/      # HTTP services
│           ├── components/    # Reusable UI components
│           └── core/          # Auth interceptors, guards
├── docs/                      # Documentation
├── docker-compose.yml         # Production compose
└── docker-compose.dev.yml     # Development compose
```

---

## Tech Stack

### Backend
| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime |
| ASP.NET Core | 10.0 | Web API framework |
| Entity Framework Core | 10.0 | ORM |
| PostgreSQL | 16 | Database |
| JWT Bearer | - | Authentication |
| OpenAI API | - | AI features |

### Frontend
| Technology | Version | Purpose |
|------------|---------|---------|
| Angular | 21 | SPA framework |
| TypeScript | 5.x | Language |
| RxJS | - | Reactive programming |
| PrimeNG | - | UI components |
| nginx | alpine | Production server |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- [PostgreSQL 16+](https://www.postgresql.org/download/)
- [Docker](https://www.docker.com/) (optional, for containerized deployment)

### Quick Start (Local Development)

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-org/woven.git
   cd woven
   ```

2. **Set up the database**
   ```bash
   # Create PostgreSQL database
   createdb -U postgres woven

   # Or use Docker
   docker run -d --name woven-db \
     -e POSTGRES_USER=woven \
     -e POSTGRES_PASSWORD=woven \
     -e POSTGRES_DB=woven \
     -p 5433:5432 \
     postgres:16-alpine
   ```

3. **Configure the backend**
   ```bash
   cd backend/WovenBackend

   # Update appsettings.Development.json with your settings
   # - Database connection string
   # - JWT secret key
   # - Google OAuth client ID
   # - OpenAI API key
   ```

4. **Run database migrations**
   ```bash
   cd backend/WovenBackend
   dotnet ef database update
   ```

5. **Start the backend**
   ```bash
   cd backend/WovenBackend
   dotnet run
   # API available at http://localhost:5135
   # Swagger UI at http://localhost:5135/swagger
   ```

6. **Start the frontend**
   ```bash
   cd frontend/woven-frontend
   npm install
   npm start
   # App available at http://localhost:4200
   ```

---

## Development

### Backend Development

```bash
cd backend/WovenBackend

# Run with hot reload
dotnet watch run

# Run tests
dotnet test

# Add a new migration
dotnet ef migrations add MigrationName

# Apply migrations
dotnet ef database update

# Revert last migration
dotnet ef migrations remove
```

### Frontend Development

```bash
cd frontend/woven-frontend

# Install dependencies
npm install

# Start dev server with hot reload
npm start

# Run tests
npm test

# Build for production
npm run build -- --configuration=production

# Lint code
npm run lint
```

---

## Docker Deployment

### Quick Start with Docker Compose

```bash
# Copy environment template
cp .env.example .env

# Edit .env with your values
nano .env

# Start all services
docker-compose up -d

# View logs
docker-compose logs -f

# Stop services
docker-compose down
```

### Development with Docker

```bash
# Start with dev overrides (includes pgAdmin)
docker-compose -f docker-compose.yml -f docker-compose.dev.yml up -d

# pgAdmin available at http://localhost:5050
# Login: admin@woven.local / admin
```

### Build Individual Images

```bash
# Build backend
docker build -t woven-backend ./backend/WovenBackend

# Build frontend
docker build -t woven-frontend ./frontend/woven-frontend

# Run backend
docker run -d -p 5135:8080 \
  -e ConnectionStrings__DefaultConnection="Host=host.docker.internal;..." \
  woven-backend

# Run frontend
docker run -d -p 80:80 woven-frontend
```

---

## Production Deployment

### Hosting Options Comparison

| Option | Pros | Cons | Best For |
|--------|------|------|----------|
| **Azure App Service** | Easy setup, auto-scaling, managed SSL | Higher cost at scale, less control | Small-medium apps, quick deployment |
| **Azure Container Apps** | Serverless containers, cost-effective | Learning curve, fewer features | Microservices, event-driven |
| **Azure Kubernetes (AKS)** | Full control, highly scalable | Complex setup, requires expertise | Large scale, multiple services |
| **AWS ECS/Fargate** | Serverless containers, AWS ecosystem | AWS lock-in | AWS-centric teams |
| **DigitalOcean App Platform** | Simple, affordable | Limited regions | Budget-conscious startups |
| **Self-hosted Docker** | Full control, cheapest | Manual maintenance | Technical teams, full control |

### Recommended: Azure Container Apps

For Woven, I recommend **Azure Container Apps** because:
- Cost-effective (pay per use, scales to zero)
- Built-in HTTPS and custom domains
- Easy CI/CD integration
- Supports multiple containers (frontend, backend, db)
- No Kubernetes complexity

#### Azure Container Apps Deployment

```bash
# Install Azure CLI
az login

# Create resource group
az group create --name woven-rg --location eastus

# Create Container Apps environment
az containerapp env create \
  --name woven-env \
  --resource-group woven-rg \
  --location eastus

# Deploy backend
az containerapp create \
  --name woven-backend \
  --resource-group woven-rg \
  --environment woven-env \
  --image your-registry/woven-backend:latest \
  --target-port 8080 \
  --ingress external \
  --env-vars "ConnectionStrings__DefaultConnection=secretref:db-connection"

# Deploy frontend
az containerapp create \
  --name woven-frontend \
  --resource-group woven-rg \
  --environment woven-env \
  --image your-registry/woven-frontend:latest \
  --target-port 80 \
  --ingress external
```

### Alternative: Azure App Service

```bash
# Create App Service plan
az appservice plan create \
  --name woven-plan \
  --resource-group woven-rg \
  --sku B1 \
  --is-linux

# Create backend web app
az webapp create \
  --name woven-api \
  --resource-group woven-rg \
  --plan woven-plan \
  --runtime "DOTNET|10.0"

# Create frontend web app (static)
az webapp create \
  --name woven-app \
  --resource-group woven-rg \
  --plan woven-plan \
  --runtime "NODE|22-lts"
```

### Database Options

| Option | Pros | Cons |
|--------|------|------|
| **Azure Database for PostgreSQL** | Managed, automatic backups, HA | Cost |
| **Amazon RDS** | Mature, reliable | AWS ecosystem |
| **Supabase** | Free tier, real-time features | Limited customization |
| **Self-hosted** | Cheapest, full control | Manual maintenance |

---

## API Documentation

### Authentication

All protected endpoints require a JWT token in the Authorization header:
```
Authorization: Bearer <token>
```

### Core Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/auth/google` | Authenticate with Google ID token |
| GET | `/moments` | Get daily deck of candidates |
| POST | `/moments/respond` | Respond to a candidate (YES/NO/PENDING) |
| GET | `/chats` | List active chat threads |
| GET | `/chats/{threadId}` | Get chat messages |
| POST | `/chats/{threadId}/messages` | Send a message |
| POST | `/chats/{threadId}/trial-decision` | Submit trial decision |
| POST | `/matches/{matchId}/pop` | Start trial period |
| POST | `/matches/{matchId}/unmatch` | Unmatch with optional rating |
| GET | `/health` | Health check with DB status |

### Swagger UI

When running in development, access Swagger UI at:
```
http://localhost:5135/swagger
```

---

## Configuration

### Backend (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=woven;Username=woven;Password=woven"
  },
  "Jwt": {
    "Issuer": "WovenBackend",
    "Audience": "WovenFrontend",
    "Key": "YOUR_SECRET_KEY_MIN_32_CHARACTERS",
    "ExpiryMinutes": 60,
    "ClockSkewMinutes": 1
  },
  "GoogleAuth": {
    "ClientId": "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com"
  },
  "OpenAI": {
    "ApiKey": "sk-your-openai-api-key"
  },
  "Cors": {
    "AllowedOrigins": "http://localhost:4200,https://your-domain.com"
  }
}
```

### Frontend (environment.ts)

```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5135',
  googleClientId: 'YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com'
};
```

---

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `ConnectionStrings__DefaultConnection` | Yes | PostgreSQL connection string |
| `Jwt__Key` | Yes | JWT signing key (min 32 chars) |
| `GoogleAuth__ClientId` | Yes | Google OAuth client ID |
| `OpenAI__ApiKey` | No | OpenAI API key for AI features |
| `Cors__AllowedOrigins` | Yes | Comma-separated allowed origins |

---

## CI/CD

Three GitHub Actions workflows automate testing, deployment, and infrastructure management.

### Workflows

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **CI** | `ci.yml` | PR + push to main | Build & test backend (.NET) and frontend (Angular) |
| **Deploy** | `deploy.yml` | Push to main | Build images, push to ACR, update Container Apps, smoke check |
| **Terraform** | `terraform.yml` | `infra/**` changes on PR/push; manual dispatch | Plan on PR, apply only via manual approval |

### Required GitHub Secrets

| Secret | Description |
|--------|-------------|
| `AZURE_CLIENT_ID` | Service principal app ID (OIDC federated credential) |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `OPENAI_API_KEY` | OpenAI API key (injected as Container App secret at deploy time) |

### Required GitHub Environment

Create a **`production`** environment in GitHub repo settings with **required reviewers** enabled. This gates:
- Container App deployments (deploy workflow)
- Terraform apply (manual dispatch only)

### How it works

1. **On PR**: CI runs backend + frontend builds/tests. Terraform plans `infra/` changes and comments the plan on the PR.
2. **On merge to main**: CI runs first, then Deploy builds images tagged with the commit SHA, pushes to ACR via `az acr build`, updates both Container Apps, and runs smoke checks against `/health/live`, `/health/ready`, and the frontend root.
3. **Terraform apply**: Triggered manually via `workflow_dispatch` with `action: apply`. Requires production environment approval.
4. **OpenAI key**: Stored only in GitHub Secrets. During deploy, injected into the backend Container App as a secret (`openai-api-key`) and referenced via `OpenAI__ApiKey=secretref:openai-api-key`. Never in Terraform state or Docker images.

### Azure OIDC Setup

Create a service principal with federated credentials for GitHub Actions:

```bash
# Create SP and note the appId
az ad sp create-for-rbac --name "woven-github-cicd" --role Contributor \
  --scopes /subscriptions/<SUB_ID>/resourceGroups/woven-prod-rg

# Add federated credential for main branch
az ad app federated-credential create --id <APP_OBJECT_ID> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<OWNER>/Woven:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Add federated credential for PRs
az ad app federated-credential create --id <APP_OBJECT_ID> --parameters '{
  "name": "github-pr",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<OWNER>/Woven:pull_request",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Assign additional role for ACR push:
```bash
az role assignment create --assignee <APP_ID> --role AcrPush \
  --scope /subscriptions/<SUB_ID>/resourceGroups/woven-prod-rg/providers/Microsoft.ContainerRegistry/registries/wovenprodacr
```

---

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Code Style

- **Backend**: Follow C# coding conventions, use nullable reference types
- **Frontend**: Follow Angular style guide, use strict TypeScript
- **Commits**: Use conventional commits (feat:, fix:, docs:, etc.)

---

## License

This project is proprietary. All rights reserved.

---

## Support

For questions or issues, please open a GitHub issue or contact the development team.
