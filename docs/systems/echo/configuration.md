# ECHO Configuration

All ECHO configuration lives in `appsettings.json` and Azure Key Vault (production secrets).

**File:** `backend/WovenBackend/appsettings.json`

---

## OpenAI Configuration

```json
{
  "OpenAI": {
    "ApiKey": "OVERRIDE_IN_USER_SECRETS_OR_KEYVAULT"
  }
}
```

**Models used:**
- **Chat/Explanations:** `gpt-4.1-mini`
- **Embeddings:** `text-embedding-3-small`
- **TTS:** `tts-1`, voice `nova`

**Where configured:**
- `IOpenAiClient` hardcodes model names (not in appsettings)
- Rationale: Model changes are code changes (affect token budgets, prompts)

**API key storage:**
- **Local dev:** User Secrets (`dotnet user-secrets set "OpenAI:ApiKey" "sk-..."`)
- **Production:** Azure Key Vault (`OpenAI--ApiKey`)

---

## ConnectionScore Formula

```json
{
  "Echo": {
    "ConnectionScore": {
      "BalloonPopped": 0.05,
      "TrialRequested": 0.10,
      "TrialAccepted": 0.22,
      "ConversationDepth": 0.20,
      "DateAccepted": 0.15,
      "ExplicitFeedback": 0.13,
      "LoveReactions": 0.08,
      "VoiceExchange": 0.06,
      "VoiceCompleted": 0.03
    }
  }
}
```

**Why configurable:**
- Allows tuning without redeployment
- Can A/B test weight changes
- Validated against synthetic persona oracle (Phase 8)

**Read by:**
- `ConnectionScoreBatchWorker.W(string key, float fallback)`

**Constraints:**
- All weights must sum to 1.0 (enforced in code, not config validation)
- Each weight in [0.0, 1.0]

---

## Cinematic Narrator

```json
{
  "Cinematic": {
    "NarrationLaunchDate": null
  }
}
```

**Purpose:** Baseline protection for weight learning.

**When set:**
- Filters out ConnectionScores where `NarrationExposed == true` for 30 days post-launch
- Prevents ECHO weights from being biased by the cinematic UX itself

**Current status:** `null` (narration not launched yet, `NarrationExposed` always `false`)

**After launch:**
```json
{
  "Cinematic": {
    "NarrationLaunchDate": "2026-09-01T00:00:00Z"
  }
}
```

**Read by:**
- `WeightLearningService.LearnWeightsAsync`

---

## SpeechBrain (Voice Embedding)

```json
{
  "SpeechBrain": {
    "EndpointUrl": "https://speechbrain.woven.internal/embed"
  }
}
```

**Purpose:** HTTP endpoint for ECAPA-TDNN voice embedding (192-dim).

**Fallback:** If not configured, uses Python subprocess (dev only, not available in containers).

**Model:** ECAPA-TDNN speaker verification model (SpeechBrain)

**Read by:**
- `VoiceEmbeddingService.GetEmbeddingAsync`

---

## Moderation

```json
{
  "Moderation": {
    "IsModerationEnabled": false
  }
}
```

**Purpose:** Kill switch for content moderation (OpenAI Moderation API).

**When true:**
- All text tiles + chat messages sent to OpenAI Moderation API
- Flagged content rejected (HTTP 422)

**When false:**
- No moderation (dev environments)

**Read by:**
- `ModerationService.ModerateTextAsync`

---

## Web Push Notifications

```json
{
  "WebPush": {
    "PublicKey": "BATHPGvX-lNgkEe7Q7rqCs2LacaHaxm7RzCqtqMhH8DxmleUexzwEu_pdcB-eRepwCUhS3rJ6zMWdlnF8_WMPdU",
    "PrivateKey": "OVERRIDE_IN_USER_SECRETS_OR_KEYVAULT"
  }
}
```

**Purpose:** VAPID keys for Web Push API.

**PublicKey:**
- Safe to commit (sent to frontend, used by service worker)

**PrivateKey:**
- **Local dev:** User Secrets
- **Production:** Azure Key Vault

**Read by:**
- `WebPushService` (signs push notifications)

---

## Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

**Serilog configuration:**
- Structured logging (JSON in production)
- Console sink (development)
- Enrichers: Environment, Thread, CorrelationId

**Read by:**
- `Program.cs` (Serilog setup)

---

## Batch Worker Control

**Environment variable (not in appsettings.json):**
```bash
export WOVEN_DISABLE_BATCH_WORKERS=true
```

**Purpose:**
- Web pods: set to `true` (only handle HTTP)
- Worker pods: unset (run all 6 batch workers)

**Read by:**
- All `BackgroundService` workers check this flag in constructor

---

## Azure Configuration (Not in appsettings.json)

**Configured via environment variables or Azure App Configuration:**

### PostgreSQL
```bash
ConnectionStrings__WovenDb="Host=woven-prod-db.postgres.database.azure.com;Database=woven_db;Username=woven_admin;Password=***"
```

### Redis
```bash
Redis__ConnectionString="woven-prod-redis.redis.cache.windows.net:6380,password=***,ssl=True"
```

### Azure Blob Storage
```bash
AzureBlobStorage__ConnectionString="DefaultEndpointsProtocol=https;AccountName=wovenprodblob;AccountKey=***"
```

### Azure Service Bus
```bash
AzureServiceBus__ConnectionString="Endpoint=sb://woven-prod-bus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=***"
```

### Application Insights
```bash
ApplicationInsights__ConnectionString="InstrumentationKey=***;IngestionEndpoint=https://westus2-1.in.applicationinsights.azure.com/"
```

---

## User Secrets (Local Development)

**Initialize:**
```bash
cd backend/WovenBackend
dotnet user-secrets init
```

**Set secrets:**
```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "WebPush:PrivateKey" "..."
dotnet user-secrets set "ConnectionStrings:WovenDb" "Host=localhost;..."
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
```

**Location:**
- Windows: `%APPDATA%\Microsoft\UserSecrets\<user-secrets-id>\secrets.json`
- macOS/Linux: `~/.microsoft/usersecrets/<user-secrets-id>/secrets.json`

**Reference:**
- [SECRETS_SETUP.md](../../../backend/WovenBackend/SECRETS_SETUP.md)

---

## Azure Key Vault (Production)

**Key Vault:** `woven-prod-kv`

**Secrets:**
- `OpenAI--ApiKey`
- `WebPush--PrivateKey`
- `ConnectionStrings--WovenDb`
- `Redis--ConnectionString`
- `AzureBlobStorage--ConnectionString`
- `AzureServiceBus--ConnectionString`

**Wiring in `Program.cs`:**
```csharp
if (builder.Environment.IsProduction())
{
    var keyVaultUri = new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/");
    builder.Configuration.AddAzureKeyVault(
        keyVaultUri,
        new DefaultAzureCredential()
    );
}
```

**Authentication:**
- Managed Identity (Azure Container Apps → Key Vault)
- No keys/passwords needed (Azure AD RBAC)

---

## Configuration Validation

**Startup checks (Program.cs):**
```csharp
// Ensure OpenAI key is set
var openAiKey = builder.Configuration["OpenAI:ApiKey"];
if (string.IsNullOrWhiteSpace(openAiKey))
    throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

// Ensure ConnectionString is set
var connStr = builder.Configuration.GetConnectionString("WovenDb");
if (string.IsNullOrWhiteSpace(connStr))
    throw new InvalidOperationException("ConnectionStrings:WovenDb is not configured.");
```

**Effect:** App fails fast at startup if critical config missing (prevents silent failures in prod).

---

## Feature Flags (Future)

**Planned:**
```json
{
  "FeatureFlags": {
    "EnableCinematicNarrator": false,
    "EnableVoiceMatching": true,
    "EnableCoaching": true,
    "EnableWeightLearning": true
  }
}
```

**Purpose:**
- Gradual rollout of ECHO features
- A/B testing (50% users see narrator, 50% don't)
- Kill switch for problematic features

**Library:** LaunchDarkly or Azure App Configuration (not yet implemented)

---

## Environment-Specific Overrides

**appsettings.Development.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "OpenAI": {
    "ApiKey": "sk-test-key"
  }
}
```

**appsettings.Production.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**Load order:**
1. `appsettings.json` (base)
2. `appsettings.{Environment}.json` (environment override)
3. User Secrets (dev only)
4. Environment variables
5. Azure Key Vault (production only)

**Later overrides earlier** (e.g., Key Vault wins over appsettings.json).

---

## CORS Configuration

```csharp
// Program.cs
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4202", "https://wooven.me")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});
```

**Why hardcoded (not in appsettings.json):**
- Security: CORS origins are deployment-specific, not environment config
- Prevents accidental exposure (e.g., "*.wooven.me" wildcard)

---

## Rate Limiting Configuration

```csharp
// Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

**Why hardcoded:**
- Rate limits are security controls (should be reviewed in code)
- Prevents accidental loosening via config change

---

## See Also

- [SECRETS_SETUP.md](../../../backend/WovenBackend/SECRETS_SETUP.md) — Local dev + Key Vault setup
- [workers.md](./workers.md) — Batch worker configuration (WOVEN_DISABLE_BATCH_WORKERS)
- [signals.md](./signals.md) — ConnectionScore formula weights
