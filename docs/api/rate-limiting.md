# Rate Limiting API Reference

**Base URL:** `https://api.wooven.me` (prod) / `http://localhost:5135` (dev)

---

## Overview

Woven API implements per-user and per-IP rate limiting to prevent abuse and ensure fair resource allocation. Rate limits are enforced via Redis-backed counters with TTL-based expiration.

**Key Features:**
- Per-user limits on authenticated endpoints
- Per-IP limits on auth and public endpoints
- 429 status code with `Retry-After` header
- Sliding windows and calendar-day windows
- Redis-backed distributed rate limiting

---

## Rate Limit Configuration

### Global Rate Limiter

**Source:** [`backend/WovenBackend/Program.cs:97-123`](../../backend/WovenBackend/Program.cs)

**Configuration:**
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            retryAfter = 60
        }, ct);
    };

    // Default fixed window: 100 requests per minute per user
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var userId = ctx.User.FindFirstValue("uid") ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});
```

---

## Endpoint-Specific Limits

### Authentication Endpoints

| Endpoint | Limit | Window | Scope |
|----------|-------|--------|-------|
| `POST /auth/google` | 20 requests | Calendar day | Per IP |

**Implementation:**
```csharp
var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
var ipHash = PiiSanitizer.HashForAudit(ip, "rl-auth-v1");
var rlKey = $"rl:auth:{ipHash}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);

if (!allowed) {
    http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

**Source:** [`backend/WovenBackend/Endpoints/AuthEndpoints.cs:30-38`](../../backend/WovenBackend/Endpoints/AuthEndpoints.cs)

---

### Media Upload Endpoints

| Endpoint | Limit | Window | Scope |
|----------|-------|--------|-------|
| `POST /media/upload-token` | 20 tokens | Calendar day | Per user |

**Implementation:**
```csharp
var rlKey = $"rl:upload:{userId}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 20, CacheTtl.UntilMidnightUtc(), ct);

if (!allowed) {
    http.Response.Headers["Retry-After"] = ((int)CacheTtl.UntilMidnightUtc().TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

**Source:** [`backend/WovenBackend/Endpoints/MediaEndpoints.cs:41-47`](../../backend/WovenBackend/Endpoints/MediaEndpoints.cs)

---

### User Insights / Opinion Submission

| Endpoint | Limit | Window | Scope |
|----------|-------|--------|-------|
| `POST /me/insights/opinion` | 1 request | Calendar month | Per user |

**Implementation:**
```csharp
var monthYear = DateTime.UtcNow.ToString("yyyy-MM");
var rlKey = $"rl:opinion:{userId}:{monthYear}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 1, TimeSpan.FromDays(31), ct);

if (!allowed) {
    var now = DateTime.UtcNow;
    var nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
    http.Response.Headers["Retry-After"] = ((int)(nextMonth - now).TotalSeconds).ToString();
    return Results.StatusCode(429);
}
```

**Source:** [`backend/WovenBackend/Endpoints/MeEndpoints.cs:69-78`](../../backend/WovenBackend/Endpoints/MeEndpoints.cs)

---

### Game Answer Submissions (AI-gated)

| Endpoint | Limit | Window | Scope |
|----------|-------|--------|-------|
| `POST /games/sessions/{sessionId}/answers` | 10 requests | 10 minutes | Per user |

**Implementation:**
```csharp
var rlKey = $"rl:game-ai:{userId}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 10, TimeSpan.FromMinutes(10), ct);

if (!allowed) {
    http.Response.Headers["Retry-After"] = "600";
    return Results.StatusCode(429);
}
```

**Source:** [`backend/WovenBackend/Endpoints/GameEndpoints.cs:150`](../../backend/WovenBackend/Endpoints/GameEndpoints.cs) (referenced)

---

### Chat Message AI Features

| Feature | Limit | Window | Scope |
|---------|-------|--------|-------|
| AI Conversation Starters | 5 requests | 10 minutes | Per user |
| AI Message Suggestions | 10 requests | 10 minutes | Per user |

**Implementation pattern (referenced in services):**
```csharp
var rlKey = $"rl:ai-chat:{userId}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 5, TimeSpan.FromMinutes(10), ct);
```

**Note:** Actual limits managed by `ICacheService.CheckRateLimitAsync()` in service layer.

---

## Response Format

### 429 Rate Limit Exceeded

**Headers:**
```http
HTTP/1.1 429 Too Many Requests
Retry-After: 3600
X-Correlation-ID: a1b2c3d4e5f67890
Content-Type: application/json
```

**Body:**
```json
{
  "error": "Rate limit exceeded",
  "retryAfter": 3600,
  "correlationId": "a1b2c3d4e5f67890"
}
```

**Fields:**
- `error` — Human-readable error message
- `retryAfter` — Seconds until next allowed request
- `correlationId` — Request tracking ID (see [error-handling.md](error-handling.md))

---

## Retry-After Header

The `Retry-After` header indicates how many **seconds** the client should wait before retrying.

**Examples:**
- `Retry-After: 60` — Retry after 1 minute
- `Retry-After: 3600` — Retry after 1 hour
- `Retry-After: 43200` — Retry after 12 hours (until midnight UTC)

**Calendar-day limits:**
```csharp
// Retry-After set to seconds until midnight UTC
CacheTtl.UntilMidnightUtc().TotalSeconds
```

---

## Rate Limit Implementation

### Redis-Backed Counters

**Service:** [`backend/WovenBackend/Services/CacheService.cs`](../../backend/WovenBackend/Services/CacheService.cs)

**Method Signature:**
```csharp
Task<bool> CheckRateLimitAsync(string key, int limit, TimeSpan window, CancellationToken ct = default);
```

**Logic:**
1. Increment Redis counter at `key`
2. Set TTL to `window` if counter is new
3. Return `true` if count ≤ `limit`, else `false`

**Example:**
```csharp
// 100 requests per hour
var allowed = await cache.CheckRateLimitAsync(
    key: $"rl:api:{userId}",
    limit: 100,
    window: TimeSpan.FromHours(1),
    ct: cancellationToken
);
```

---

## Client Handling

### Exponential Backoff (Recommended)

```typescript
async function fetchWithRetry(url: string, maxRetries = 3): Promise<Response> {
  for (let attempt = 0; attempt < maxRetries; attempt++) {
    const response = await fetch(url);
    
    if (response.status !== 429) {
      return response;
    }
    
    const retryAfter = parseInt(response.headers.get('Retry-After') || '60');
    const backoff = Math.min(retryAfter, 300) * 1000; // Cap at 5 minutes
    
    console.log(`Rate limited. Retrying after ${backoff}ms...`);
    await new Promise(resolve => setTimeout(resolve, backoff));
  }
  
  throw new Error('Max retries exceeded');
}
```

### Respect Retry-After

```typescript
if (response.status === 429) {
  const retryAfter = parseInt(response.headers.get('Retry-After') || '60');
  const retryTime = Date.now() + (retryAfter * 1000);
  
  // Store in localStorage or state
  localStorage.setItem('nextAllowedRequest', retryTime.toString());
  
  // Show user-friendly message
  showToast(`Please wait ${retryAfter} seconds before trying again`);
}
```

---

## Monitoring & Observability

### Rate Limit Logs

**Logged on rejection:**
```log
[RateLimit] Rejected | UserId={UserId} IP={IpHash} Endpoint={Path} CorrelationId={CorrelationId}
```

**Structured log fields:**
- `UserId` — User ID (or `anon` for unauthenticated)
- `IpHash` — Hashed IP address (PII-sanitized)
- `Endpoint` — Request path
- `CorrelationId` — Request tracking ID

**Source:** [`backend/WovenBackend/Program.cs:100-108`](../../backend/WovenBackend/Program.cs)

---

## Future Enhancements

**Planned improvements:**
- Sliding window rate limiters (more flexible than fixed windows)
- Per-endpoint custom limits (beyond global limiter)
- Rate limit quotas in API responses (`X-RateLimit-Remaining`)
- Burst allowances for authenticated users

---

**Last Updated:** 2026-10-07  
**Source Files:**
- `backend/WovenBackend/Program.cs` (global rate limiter)
- `backend/WovenBackend/Services/CacheService.cs` (Redis implementation)
- `backend/WovenBackend/Endpoints/*.cs` (per-endpoint limits)
