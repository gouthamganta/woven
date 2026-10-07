# Monitoring Guide

Logs, Application Insights, and observability for Woven.

---

## Overview

Woven uses **Serilog** for structured logging with correlation IDs for request tracing. Production logs flow to **Azure Application Insights** for monitoring and alerting.

---

## Structured Logging

### Log Format

**Development (console):**
```
[12:34:56 INF] [MatchService] Pool built | UserId=123 Count=15 CorrelationId=a1b2c3d4e5f67890
```

**Production (JSON):**
```json
{
  "timestamp": "2026-10-07T12:34:56Z",
  "level": "Information",
  "message": "[MatchService] Pool built | UserId=123 Count=15 CorrelationId=a1b2c3d4e5f67890",
  "correlationId": "a1b2c3d4e5f67890",
  "userId": 123,
  "count": 15
}
```

---

### Logging Conventions

**All log lines include:**
- `[ServiceName]` prefix
- User ID (if authenticated)
- Correlation ID (from `X-Correlation-ID` header)
- Structured properties (not string interpolation)

**Example:**
```csharp
_logger.LogInformation(
    "[CandidatePool] Built pool | UserId={UserId} Count={Count} CorrelationId={Cid}",
    userId, pool.Count, _correlation.CorrelationId);
```

**Source:** All services in `backend/WovenBackend/Services/`

---

## Correlation IDs

### Purpose

Correlation IDs enable request tracing across:
- Client → API → Database → External services
- Logs → Exceptions → Responses
- Multi-service distributed systems

---

### Implementation

**Middleware:** [`Infrastructure/CorrelationIdMiddleware.cs`](../../backend/WovenBackend/Infrastructure/CorrelationIdMiddleware.cs)

**Flow:**
1. Read `X-Correlation-ID` from incoming request header (or generate 16-char hex ID)
2. Store in `HttpContext.Items["CorrelationId"]`
3. Push into Serilog LogContext (every log line carries it)
4. Echo back in response header

**Example client request:**
```http
GET /moments HTTP/1.1
X-Correlation-ID: client-generated-id
Authorization: Bearer <token>
```

**Example server logs:**
```
[Moments] Fetching deck | UserId=123 CorrelationId=a1b2c3d4e5f67890
[DeckOrchestrator] Pool size: 50 | CorrelationId=a1b2c3d4e5f67890
[OpenAiClient] Chat completion | Model=gpt-4.1-mini Tokens=350 CorrelationId=a1b2c3d4e5f67890
```

**Example error response:**
```json
{
  "error": "Internal server error",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

---

## Log Levels

| Level | Usage |
|---|---|
| `Trace` | Extremely detailed (disabled in production) |
| `Debug` | Developer-facing (disabled in production) |
| `Information` | Normal operation, key events |
| `Warning` | Recoverable errors, degraded behavior |
| `Error` | Unhandled exceptions, failures |
| `Critical` | System-wide failures |

**Production minimum level:** `Information`

---

## Querying Logs

### Azure Log Analytics (Production)

**Find all logs for a correlation ID:**
```kusto
traces
| where customDimensions.CorrelationId == "a1b2c3d4e5f67890"
| project timestamp, message, severityLevel
| order by timestamp asc
```

**Find errors in last hour:**
```kusto
traces
| where timestamp > ago(1h)
| where severityLevel >= 3  // Error or Critical
| project timestamp, message, customDimensions
| order by timestamp desc
```

**Count requests by user:**
```kusto
traces
| where message contains "[Moments]"
| extend UserId = tostring(customDimensions.UserId)
| summarize count() by UserId
```

---

### Local Development (Console)

**Filter logs by correlation ID:**
```bash
# Pipe backend output to file
dotnet run > logs.txt 2>&1

# Search by correlation ID
grep "a1b2c3d4e5f67890" logs.txt
```

**Watch logs in real-time:**
```bash
dotnet run | grep --line-buffered "CorrelationId"
```

---

## Application Insights

### Telemetry Types

| Type | What It Tracks |
|---|---|
| **Requests** | HTTP requests (duration, status code) |
| **Dependencies** | External calls (OpenAI, Azure Blob, Redis) |
| **Exceptions** | Unhandled exceptions |
| **Traces** | Serilog log lines |
| **Custom Events** | Analytics events via `IAnalyticsService` |

---

### Key Metrics

**Request metrics:**
- Request rate (requests/second)
- Response time (p50, p95, p99)
- Failure rate (5xx responses)

**Dependency metrics:**
- OpenAI call duration
- Redis latency
- PostgreSQL query time

**Custom metrics:**
- Daily active users
- Match creation rate
- Spark transactions

---

### Alerts (Configured in Azure)

**Recommended alerts:**
- **Error rate > 5%** — Alert if >5% of requests fail
- **Response time > 2s** — Alert if p95 > 2 seconds
- **Exception count** — Alert on any unhandled exception
- **Dependency failures** — Alert if OpenAI or Redis fails

---

## Health Checks

### Backend Health Endpoint

**URL:** `http://localhost:5135/health` (local) / `https://<backend-url>/health` (prod)

**Response:**
```json
{
  "status": "Healthy",
  "checks": {
    "database": "Healthy",
    "redis": "Healthy"
  }
}
```

**Source:** Configured in `Program.cs`

---

### Monitoring Health

**Local:**
```bash
curl http://localhost:5135/health
```

**Production (from Azure CLI):**
```bash
az containerapp revision list \
  --name woven-prod-backend \
  --resource-group woven-prod-rg \
  --query "[?properties.healthState=='Healthy']"
```

---

## Analytics Events

**Custom events tracked via `IAnalyticsService`:**

| Event | Trigger |
|---|---|
| `UserRegistered` | New user signs up |
| `OnboardingStepCompleted` | Each onboarding step |
| `MomentResponded` | User responds to a moment |
| `MatchCreated` | New match formed |
| `ChatMessageSent` | Message sent in chat |
| `TrialDecision` | Trial period decision (CONTINUE/END) |
| `VoiceNoteRecorded` | Voice note sent |
| `GameStarted` | In-match game initiated |

**Usage:**
```csharp
await _analytics.TrackAsync(
    userId,
    sessionId: null,
    eventName: AnalyticsEvents.MatchCreated,
    properties: new { matchType = "PURE" },
    ct: ct
);
```

**Source:** `backend/WovenBackend/Services/Analytics/IAnalyticsService.cs`

---

## Troubleshooting with Logs

### Find slow requests

**Azure Log Analytics:**
```kusto
requests
| where duration > 2000  // >2 seconds
| project timestamp, name, duration, resultCode
| order by duration desc
```

---

### Trace a user's journey

```kusto
traces
| where customDimensions.UserId == "123"
| project timestamp, message
| order by timestamp asc
```

---

### Find OpenAI token usage

```kusto
traces
| where message contains "[OpenAiClient]"
| extend Tokens = toint(customDimensions.Tokens)
| summarize TotalTokens = sum(Tokens) by bin(timestamp, 1h)
```

---

## Best Practices

1. **Always log with correlation ID** — Use `_correlation.CorrelationId`
2. **Use structured properties** — Not string interpolation
3. **Prefix with service name** — `[ServiceName]` convention
4. **Include user ID when available** — For user-specific issues
5. **Log actions, not values** — "Pool built" not "Pool = [...]"
6. **Warning for recoverable errors** — Error for failures
7. **Include source file references** — For traceability

---

**Last Updated:** 2026-10-07
