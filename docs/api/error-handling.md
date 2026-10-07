# Error Handling API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

All Woven API errors return structured JSON responses with correlation IDs for request tracing. Error handling is implemented via centralized exception handlers that ensure consistent formatting across all endpoints.

**Key Features:**
- Correlation IDs on every request/response (`X-Correlation-ID` header)
- Structured error responses with timestamps
- Domain-specific error codes (422 for business logic failures)
- Auth error mapping (401 for unauthorized access)
- Global exception handler (500 for unexpected errors)

---

## Error Response Format

### Standard Error Response

```json
{
  "error": "ERROR_CODE",
  "message": "Human-readable error description",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

**Fields:**
- `error` — Machine-readable error code (uppercase snake_case)
- `message` — Optional human-readable description
- `correlationId` — 16-character hex request tracking ID
- `timestamp` — ISO 8601 UTC timestamp

---

## HTTP Status Codes

| Code | Meaning | Handler | Example |
|------|---------|---------|---------|
| 400 | Bad Request | Validation failure | Missing required field |
| 401 | Unauthorized | `AuthExceptionHandler` | Invalid/expired JWT |
| 403 | Forbidden | Authorization logic | User cannot access resource |
| 404 | Not Found | Endpoint logic | Resource does not exist |
| 422 | Unprocessable Entity | `DomainExceptionHandler` | Business rule violation |
| 429 | Too Many Requests | Rate limiter | Rate limit exceeded |
| 500 | Internal Server Error | `GlobalExceptionHandler` | Unexpected exception |

---

## Exception Handlers

### 1. AuthExceptionHandler (401)

**Purpose:** Maps `UnauthorizedAccessException` to HTTP 401.

**Source:** [`backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs`](../../backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs)

**Implementation:**
```csharp
public async ValueTask<bool> TryHandleAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken)
{
    if (exception is not UnauthorizedAccessException ex)
        return false;

    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

    httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await httpContext.Response.WriteAsJsonAsync(new
    {
        error = "Unauthorized",
        message = ex.Message,
        correlationId,
        timestamp = DateTimeOffset.UtcNow
    }, cancellationToken);

    return true;
}
```

**Triggered By:**
- `EndpointHelper.GetUserId()` when JWT claim is missing/invalid
- Auth middleware failures

**Example Response:**
```json
{
  "error": "Unauthorized",
  "message": "Missing user id claim",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

---

### 2. DomainExceptionHandler (422)

**Purpose:** Maps `DomainException` to HTTP 422 for business rule violations.

**Source:** [`backend/WovenBackend/Infrastructure/GlobalExceptionHandler.cs`](../../backend/WovenBackend/Infrastructure/GlobalExceptionHandler.cs)

**Implementation:**
```csharp
public class DomainException : Exception
{
    public string ErrorCode { get; }
    
    public DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

public class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(...)
    {
        if (exception is not DomainException ex)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            error = ex.ErrorCode,
            message = ex.Message,
            correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? "unknown",
            timestamp = DateTimeOffset.UtcNow
        }, cancellationToken);

        return true;
    }
}
```

**Example Usage:**
```csharp
if (match.BalloonState != BalloonState.ACTIVE)
    throw new DomainException("BALLOON_NOT_ACTIVE", "Match balloon has already closed");
```

**Example Response:**
```json
{
  "error": "BALLOON_NOT_ACTIVE",
  "message": "Match balloon has already closed",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

---

### 3. GlobalExceptionHandler (500)

**Purpose:** Catches all unhandled exceptions and returns safe error response.

**Source:** [`backend/WovenBackend/Infrastructure/GlobalExceptionHandler.cs`](../../backend/WovenBackend/Infrastructure/GlobalExceptionHandler.cs)

**Implementation:**
```csharp
public async ValueTask<bool> TryHandleAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken)
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

    _logger.LogError(exception,
        "[GlobalExceptionHandler] Unhandled exception | CorrelationId={CorrelationId}",
        correlationId);

    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await httpContext.Response.WriteAsJsonAsync(new
    {
        error = "Internal server error",
        correlationId,
        timestamp = DateTimeOffset.UtcNow
    }, cancellationToken);

    return true;
}
```

**Example Response:**
```json
{
  "error": "Internal server error",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

**Note:** Exception details are logged but NOT exposed to clients in production.

---

## Correlation IDs

### Purpose

Correlation IDs enable request tracing across:
- Client → API → Database → External services (OpenAI, Azure, etc.)
- Logs → Exceptions → Responses
- Multi-service distributed systems

### Implementation

**Middleware:** [`backend/WovenBackend/Infrastructure/CorrelationIdMiddleware.cs`](../../backend/WovenBackend/Infrastructure/CorrelationIdMiddleware.cs)

```csharp
public async Task InvokeAsync(HttpContext context, RequestDelegate next)
{
    // Read from incoming header or generate new 16-char hex ID
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                        ?? GenerateCorrelationId();

    // Store in HttpContext.Items for handlers to access
    context.Items["CorrelationId"] = correlationId;

    // Push into Serilog LogContext so every log line carries it
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        // Echo back in response header
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        await next(context);
    }
}

private static string GenerateCorrelationId()
{
    return Guid.NewGuid().ToString("N")[..16]; // 16-char hex
}
```

**Registered In:** [`backend/WovenBackend/Program.cs:86-88`](../../backend/WovenBackend/Program.cs)

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationService, CorrelationService>();
// Middleware added first in pipeline (before exception handlers)
```

---

### Correlation ID Flow

**1. Client Request:**
```http
GET /moments HTTP/1.1
X-Correlation-ID: client-generated-id
Authorization: Bearer <token>
```

**2. Server Logs:**
```json
{
  "timestamp": "2026-10-07T12:34:56Z",
  "level": "Information",
  "message": "[Moments] Fetching deck | UserId=123 CorrelationId=a1b2c3d4e5f67890",
  "correlationId": "a1b2c3d4e5f67890"
}
```

**3. External Service Calls (OpenAI, etc.):**
```csharp
// OpenAI client automatically includes correlation ID
var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
request.Headers.Add("X-Correlation-ID", correlationService.CorrelationId);
```

**Source:** [`backend/WovenBackend/Services/OpenAiClient.cs`](../../backend/WovenBackend/Services/OpenAiClient.cs)

**4. Error Response:**
```http
HTTP/1.1 500 Internal Server Error
X-Correlation-ID: a1b2c3d4e5f67890

{
  "error": "Internal server error",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

---

## Common Error Codes

### Authentication & Authorization

| Code | Status | Description |
|------|--------|-------------|
| `Unauthorized` | 401 | Missing or invalid JWT token |
| `FORBIDDEN` | 403 | User lacks permission for resource |

### Resource Errors

| Code | Status | Description |
|------|--------|-------------|
| `MATCH_NOT_FOUND` | 404 | Match does not exist |
| `THREAD_NOT_FOUND` | 404 | Chat thread does not exist |
| `TILE_NOT_FOUND` | 404 | Content tile does not exist |

### Business Logic Errors (422)

| Code | Status | Description |
|------|--------|-------------|
| `BALLOON_NOT_ACTIVE` | 422 | Match balloon has closed |
| `TRIAL_NOT_ACTIVE` | 422 | Trial period has ended |
| `INSUFFICIENT_SPARKS` | 422 | Not enough sparks in wallet |
| `DAILY_LIMIT_REACHED` | 422 | Daily interaction cap hit |
| `ALREADY_RESPONDED` | 422 | User already responded to this moment |

### Rate Limiting

| Code | Status | Description |
|------|--------|-------------|
| `Rate limit exceeded` | 429 | Too many requests (see `retryAfter`) |

---

## Client Error Handling

### TypeScript Example

```typescript
interface ApiError {
  error: string;
  message?: string;
  correlationId: string;
  timestamp: string;
  retryAfter?: number; // 429 only
}

async function handleApiError(response: Response) {
  const error: ApiError = await response.json();
  
  console.error(`API Error [${error.correlationId}]:`, error.error);
  
  switch (response.status) {
    case 401:
      // Redirect to login
      window.location.href = '/login';
      break;
    
    case 422:
      // Show domain-specific error to user
      showToast(error.message || error.error);
      break;
    
    case 429:
      // Handle rate limit with retry
      const retryAfter = error.retryAfter || 60;
      showToast(`Please wait ${retryAfter} seconds`);
      break;
    
    case 500:
      // Log correlation ID for support
      showToast(`Something went wrong. Reference: ${error.correlationId}`);
      logToSentry(error);
      break;
  }
}
```

---

## Logging & Observability

### Structured Logs

**Every log line includes:**
- `CorrelationId` — Request tracking ID
- `UserId` — User ID (if authenticated)
- `Timestamp` — UTC timestamp
- `Level` — Log level (Information, Warning, Error)

**Example:**
```json
{
  "timestamp": "2026-10-07T12:34:56Z",
  "level": "Error",
  "message": "[GlobalExceptionHandler] Unhandled exception | CorrelationId=a1b2c3d4e5f67890",
  "correlationId": "a1b2c3d4e5f67890",
  "exception": "System.NullReferenceException: ..."
}
```

### Querying Logs by Correlation ID

**Azure Log Analytics:**
```kusto
traces
| where customDimensions.CorrelationId == "a1b2c3d4e5f67890"
| project timestamp, message, severityLevel
| order by timestamp asc
```

**Serilog (local dev):**
```bash
cat logs/app.log | grep "a1b2c3d4e5f67890"
```

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Infrastructure/GlobalExceptionHandler.cs`
- `backend/WovenBackend/Infrastructure/AuthExceptionHandler.cs`
- `backend/WovenBackend/Infrastructure/CorrelationIdMiddleware.cs`
- `backend/WovenBackend/Services/CorrelationService.cs`
- `backend/WovenBackend/Program.cs` (exception handler registration)
