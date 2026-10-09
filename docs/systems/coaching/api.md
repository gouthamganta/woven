# Coaching API

HTTP endpoints for coaching summary delivery and user preferences.

---

## Overview

**Base path:** `/coaching`  
**Authentication:** Required (JWT Bearer or HttpOnly cookie)  
**Endpoints:**
- `GET /coaching/current-summary` — Get latest unread summary
- `POST /coaching/{id}/dismiss` — Dismiss a summary
- `POST /coaching/opt-out` — Disable coaching summaries
- `POST /coaching/opt-in` — Re-enable coaching summaries

**Rate limiting:** Not applied (read-heavy, low abuse potential)

---

## GET /coaching/current-summary

**Purpose:** Fetch the user's latest unread coaching summary (if any).

**Auth:** Required  
**Method:** GET  
**Path:** `/coaching/current-summary`

### Request

**Headers:**
```
Authorization: Bearer <jwt>
```

**No query params, no body.**

### Response

#### Success (200 OK)

**When:** User has an unread summary (`DismissedAt == null`)

```json
{
  "id": 12345,
  "summaryText": "You showed up this week — not just scrolling, actually engaging. The fact that you're willing to send the first message when something feels right says a lot.",
  "deliveredAt": "2026-10-07T18:00:12Z",
  "weekStart": "2026-10-05"
}
```

**Fields:**
- `id` (long) — CoachingSummary.Id (needed for dismissal)
- `summaryText` (string) — GPT-generated summary (displayed to user)
- `deliveredAt` (ISO 8601) — When summary was created (not when user saw it)
- `weekStart` (YYYY-MM-DD) — Monday of the week this summary covers

---

#### No Content (204)

**When:** No unread summary exists

**Reasons:**
- User has no summaries (never qualified)
- All summaries dismissed (`DismissedAt != null`)
- User opted out (`User.CoachingOptedOut == true`)

**Body:** Empty (no JSON)

**Frontend handling:**
```typescript
try {
  const summary = await this.coachingService.getCurrent();
  this.coachingSummary = summary;
} catch (err: any) {
  if (err.status === 204) {
    this.coachingSummary = null; // Don't render card
  }
}
```

---

#### Unauthorized (401)

**When:** No valid JWT or cookie

**Body:**
```json
{
  "error": "Unauthorized",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T18:01:23Z"
}
```

---

### Implementation

**File:** `backend/WovenBackend/Endpoints/CoachingEndpoints.cs`

```csharp
group.MapGet("/current-summary", async (
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);

    var summary = await db.CoachingSummaries
        .Where(c => c.UserId == userId && c.DismissedAt == null && c.OptedOutAt == null)
        .OrderByDescending(c => c.DeliveredAt)
        .FirstOrDefaultAsync(ct);

    if (summary == null) return Results.NoContent();

    return Results.Ok(new
    {
        id          = summary.Id,
        summaryText = summary.SummaryText,
        deliveredAt = summary.DeliveredAt,
        weekStart   = summary.WeekStartDate.ToString("yyyy-MM-dd")
    });
});
```

**Query logic:**
- `DismissedAt == null` → user hasn't dismissed it yet
- `OptedOutAt == null` → not part of batch opt-out dismissal
- `OrderByDescending(DeliveredAt)` → most recent first (in case of duplicates)

---

## POST /coaching/{id}/dismiss

**Purpose:** Mark a coaching summary as dismissed (card disappears).

**Auth:** Required  
**Method:** POST  
**Path:** `/coaching/{id}/dismiss`

### Request

**Path params:**
- `id` (long) — CoachingSummary.Id (from `GET /current-summary` response)

**Headers:**
```
Authorization: Bearer <jwt>
```

**No body.**

### Response

#### Success (200 OK)

**When:** Summary found and dismissed

```json
{
  "dismissed": true
}
```

**Effect:**
- `CoachingSummaries.DismissedAt` set to `now`
- Next `GET /current-summary` returns 204 (card disappears)

---

#### Not Found (404)

**When:** 
- Summary ID doesn't exist
- Summary belongs to another user (security check)

**Body:**
```json
{
  "error": "Not Found",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T18:02:45Z"
}
```

---

#### Unauthorized (401)

**When:** No valid JWT or cookie

---

### Implementation

```csharp
group.MapPost("/{id:long}/dismiss", async (
    long id,
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);

    var summary = await db.CoachingSummaries
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

    if (summary == null) return Results.NotFound();

    summary.DismissedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { dismissed = true });
});
```

**Security:**
- `c.UserId == userId` ensures users can only dismiss their own summaries
- No admin override (dismissed summaries are user-scoped)

**Idempotency:**
- Dismissing twice is safe (second call updates `DismissedAt` again)
- No error if already dismissed

---

## POST /coaching/opt-out

**Purpose:** Disable coaching summaries globally for this user.

**Auth:** Required  
**Method:** POST  
**Path:** `/coaching/opt-out`

### Request

**Headers:**
```
Authorization: Bearer <jwt>
```

**No body.**

### Response

#### Success (200 OK)

```json
{
  "optedOut": true
}
```

**Effect:**
1. `User.CoachingOptedOut` set to `true`
2. All unread summaries dismissed immediately:
   - `CoachingSummaries.DismissedAt` set to `now`
   - `CoachingSummaries.OptedOutAt` set to `now` (audit trail)
3. Future weekly runs skip this user (eligibility check fails)

---

#### Unauthorized (401)

**When:** No valid JWT or cookie

---

### Implementation

```csharp
group.MapPost("/opt-out", async (
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);
    var now    = DateTimeOffset.UtcNow;

    // Set user preference
    await db.Users
        .Where(u => u.Id == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.CoachingOptedOut, true), ct);

    // Dismiss all unread summaries
    await db.CoachingSummaries
        .Where(c => c.UserId == userId && c.DismissedAt == null)
        .ExecuteUpdateAsync(s =>
            s.SetProperty(c => c.OptedOutAt, now)
             .SetProperty(c => c.DismissedAt, now), ct);

    return Results.Ok(new { optedOut = true });
});
```

**Batch dismissal:**
- `ExecuteUpdateAsync` — bulk update (no load into memory)
- Sets both `OptedOutAt` and `DismissedAt` (allows distinguishing opt-out dismissals from manual dismissals)

**Idempotency:**
- Opting out twice is safe (no error if already opted out)

---

## POST /coaching/opt-in

**Purpose:** Re-enable coaching summaries for this user.

**Auth:** Required  
**Method:** POST  
**Path:** `/coaching/opt-in`

### Request

**Headers:**
```
Authorization: Bearer <jwt>
```

**No body.**

### Response

#### Success (200 OK)

```json
{
  "optedIn": true
}
```

**Effect:**
1. `User.CoachingOptedOut` set to `false`
2. Next Wednesday, user is eligible again
3. Previously dismissed summaries remain dismissed (no resurrection)

---

#### Unauthorized (401)

**When:** No valid JWT or cookie

---

### Implementation

```csharp
group.MapPost("/opt-in", async (
    WovenDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var userId = GetUserId(http.User);

    await db.Users
        .Where(u => u.Id == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.CoachingOptedOut, false), ct);

    return Results.Ok(new { optedIn = true });
});
```

**No summary resurrection:**
- Opting back in doesn't un-dismiss old summaries
- User must wait until next Wednesday for new summary

**Idempotency:**
- Opting in twice is safe (no error if already opted in)

---

## Authentication

**Dual-mode:** Bearer tokens + HttpOnly cookies both work.

### JWT Bearer (legacy)

**Header:**
```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

**Claim chain:**
```csharp
var userId = GetUserId(http.User); // Reads "uid" -> "sub" -> ClaimTypes.NameIdentifier
```

**Throws:** `UnauthorizedAccessException` if claim invalid/missing

---

### HttpOnly Cookie (preferred)

**Cookie name:** `woven-auth`  
**SameSite:** Strict  
**Secure:** true (HTTPS only)  
**HttpOnly:** true (XSS-resistant)

**Middleware:** JWT middleware reads from cookie if `Authorization` header missing.

**See:** `backend/WovenBackend/Auth/CookieAuthHelper.cs`

---

## Error Responses

All endpoints use `GlobalExceptionHandler` for consistent error format.

### 401 Unauthorized

```json
{
  "error": "Unauthorized",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T18:03:12Z"
}
```

**Trigger:** No valid JWT or cookie

---

### 404 Not Found

```json
{
  "error": "Not Found",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T18:03:12Z"
}
```

**Trigger:** Summary ID doesn't exist or belongs to another user

---

### 500 Internal Server Error

```json
{
  "error": "An error occurred while processing your request",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T18:03:12Z"
}
```

**Trigger:** Unhandled exception (DB down, etc.)

**CorrelationId:** Passed in response header `X-Correlation-ID` (matches log lines)

---

## Frontend Integration

**Service:** `frontend/src/app/services/coaching.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class CoachingService {
  private readonly baseUrl = `${environment.apiUrl}/coaching`;

  constructor(private http: HttpClient) {}

  getCurrent(): Observable<CoachingSummary> {
    return this.http.get<CoachingSummary>(`${this.baseUrl}/current-summary`);
  }

  dismiss(id: number): Observable<{ dismissed: boolean }> {
    return this.http.post<{ dismissed: boolean }>(`${this.baseUrl}/${id}/dismiss`, {});
  }

  optOut(): Observable<{ optedOut: boolean }> {
    return this.http.post<{ optedOut: boolean }>(`${this.baseUrl}/opt-out`, {});
  }

  optIn(): Observable<{ optedIn: boolean }> {
    return this.http.post<{ optedIn: boolean }>(`${this.baseUrl}/opt-in`, {});
  }
}
```

**Component:** `frontend/src/app/components/coaching-card/coaching-card.component.ts`

**Usage:**
```typescript
async ngOnInit() {
  try {
    this.summary = await firstValueFrom(this.coachingService.getCurrent());
  } catch (err: any) {
    if (err.status !== 204) {
      console.error('Failed to load coaching summary:', err);
    }
  }
}

async dismiss() {
  if (!this.summary) return;
  await firstValueFrom(this.coachingService.dismiss(this.summary.id));
  this.dismissed.emit();
}
```

---

## Correlation IDs

**Middleware:** `CorrelationIdMiddleware` adds `X-Correlation-ID` to every request/response.

**Request header:**
```
X-Correlation-ID: a1b2c3d4e5f67890
```

**Response header:**
```
X-Correlation-ID: a1b2c3d4e5f67890
```

**Logging:**
```
[CoachingSummary] Processing user 1234 | CorrelationId=a1b2c3d4e5f67890
```

**Why:**
- Trace request across frontend → backend → OpenAI
- Debugging: user reports error → search logs by correlationId
- Incident response: group all log lines for one request

---

## Rate Limiting

**Status:** Not applied (as of 2026-10-07)

**Why:**
- Coaching endpoints are read-heavy (`GET /current-summary`)
- Low abuse potential (1 summary/user/week)
- No costly operations (no OpenAI calls in request path)

**Future consideration:**
- If abuse detected, add rate limit: 10 req/min/user

---

## Monitoring

### Key Metrics

**Request-level:**
- `GET /current-summary` hit rate (pulls/day)
- `POST /dismiss` hit rate (dismissals/week)
- Opt-out rate (% of users who disable coaching)

**Response-level:**
- 204 rate (% of `GET /current-summary` returning no content)
- 404 rate (should be ~0% — indicates bad frontend state)
- 500 rate (should be ~0% — indicates DB/infra issue)

### Alerts

**Warning:**
- Opt-out rate >20% → coaching quality issue
- 500 rate >1% → DB degradation

---

## Testing

### Integration Test: GET current-summary

```csharp
[Test]
public async Task GetCurrentSummary_UnreadExists_Returns200()
{
    // Arrange
    var user = CreateTestUser();
    var summary = CreateCoachingSummary(user.Id, dismissedAt: null);
    
    // Act
    var response = await client.GetAsync("/coaching/current-summary", 
        headers: AuthHeaders(user.Id));
    
    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    var body = await response.Content.ReadAsJsonAsync<CoachingSummaryResponse>();
    Assert.That(body.Id, Is.EqualTo(summary.Id));
}

[Test]
public async Task GetCurrentSummary_NoneExists_Returns204()
{
    // Arrange
    var user = CreateTestUser();
    
    // Act
    var response = await client.GetAsync("/coaching/current-summary", 
        headers: AuthHeaders(user.Id));
    
    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
}
```

### Integration Test: POST dismiss

```csharp
[Test]
public async Task DismissSummary_ValidId_SetsDismissedAt()
{
    // Arrange
    var user = CreateTestUser();
    var summary = CreateCoachingSummary(user.Id, dismissedAt: null);
    
    // Act
    var response = await client.PostAsync($"/coaching/{summary.Id}/dismiss", 
        content: null, headers: AuthHeaders(user.Id));
    
    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    await db.Entry(summary).ReloadAsync();
    Assert.That(summary.DismissedAt, Is.Not.Null);
}

[Test]
public async Task DismissSummary_OtherUsersSummary_Returns404()
{
    // Arrange
    var user1 = CreateTestUser();
    var user2 = CreateTestUser();
    var summary = CreateCoachingSummary(user2.Id, dismissedAt: null);
    
    // Act
    var response = await client.PostAsync($"/coaching/{summary.Id}/dismiss", 
        content: null, headers: AuthHeaders(user1.Id));
    
    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
}
```

---

## See Also

- [summary-generation.md](./summary-generation.md) — How summaries are generated
- [delivery.md](./delivery.md) — When summaries are delivered
- [workers.md](./workers.md) — CoachingSummaryWorker schedule
- [docs/systems/auth/](../auth/) — JWT + cookie authentication
