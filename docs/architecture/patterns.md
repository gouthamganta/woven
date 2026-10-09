# Code Patterns & Best Practices

**Last Updated:** 2026-10-07

---

## Overview

This document captures recurring code patterns used across Woven's codebase — backend (.NET) and frontend (Angular). Follow these patterns for consistency and maintainability.

---

## Backend Patterns (.NET)

### 1. Minimal API Endpoint Pattern

**File structure:**
```
Endpoints/
├── MomentsEndpoints.cs
├── ChatEndpoints.cs
└── ...
```

**Pattern:**
```csharp
public static class MomentsEndpoints
{
    public static void MapMomentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/moments")
            .RequireAuthorization()
            .RequireRateLimiting("user");

        group.MapGet("/deck", GetDeck);
        group.MapPost("/respond", RespondToMoment)
            .RequireRateLimiting("ai-heavy");
    }

    private static async Task<IResult> GetDeck(
        HttpContext http,
        WovenDbContext db,
        IDailyDeckOrchestrator deckService,
        ILogger<IDailyDeckOrchestrator> logger,
        CancellationToken ct)
    {
        // 1. Extract user ID (throws UnauthorizedAccessException if invalid)
        var userId = EndpointHelper.GetUserId(http.User);

        // 2. Delegate to service
        var deck = await deckService.GetOrBuildDeckAsync(userId, ct);

        // 3. Return response
        return Results.Ok(deck);
    }
}
```

**Registration (Program.cs):**
```csharp
app.MapMomentsEndpoints();
```

**Rules:**
- **Thin endpoints** — no business logic, only validation + delegation
- **Use `EndpointHelper.GetUserId()`** — never write local `GetUserId()` copy
- **Rate limiting** — apply `RequireRateLimiting()` where needed
- **CancellationToken** — always accept `ct` parameter for cancellable operations

---

### 2. Service Pattern

**Interface + implementation:**
```csharp
public interface IDailyDeckOrchestrator
{
    Task<DailyDeckResponse> GetOrBuildDeckAsync(long userId, CancellationToken ct);
}

public class DailyDeckOrchestrator : IDailyDeckOrchestrator
{
    private readonly WovenDbContext _db;
    private readonly ILogger<DailyDeckOrchestrator> _logger;
    private readonly ICorrelationService _correlation;

    public DailyDeckOrchestrator(
        WovenDbContext db,
        ILogger<DailyDeckOrchestrator> logger,
        ICorrelationService correlation)
    {
        _db = db;
        _logger = logger;
        _correlation = correlation;
    }

    public async Task<DailyDeckResponse> GetOrBuildDeckAsync(long userId, CancellationToken ct)
    {
        // 1. Log entry with correlation ID
        _logger.LogInformation(
            "[DailyDeck] Building deck | UserId={UserId} CorrelationId={Cid}",
            userId, _correlation.CorrelationId);

        // 2. Business logic...
        var deck = await _db.DailyDecks
            .Where(d => d.UserId == userId && d.GeneratedAt.Date == DateTime.UtcNow.Date)
            .FirstOrDefaultAsync(ct);

        if (deck == null)
        {
            deck = await BuildNewDeckAsync(userId, ct);
        }

        return MapToResponse(deck);
    }
}
```

**Registration (Program.cs):**
```csharp
builder.Services.AddScoped<IDailyDeckOrchestrator, DailyDeckOrchestrator>();
```

**Rules:**
- **Interface + implementation** — always define interface for testability
- **Constructor injection** — never `new` for dependencies
- **Structured logging** — `[ServiceName]` prefix + correlation ID
- **CancellationToken** — pass through all async calls

---

### 3. Structured Logging

**Format:**
```csharp
_logger.LogInformation(
    "[ServiceName] Action | Key1={Value1} Key2={Value2} CorrelationId={Cid}",
    value1, value2, _correlation.CorrelationId);
```

**Examples:**
```csharp
// Success
_logger.LogInformation(
    "[CandidatePool] Built pool | UserId={UserId} Count={Count} CorrelationId={Cid}",
    userId, pool.Count, _correlation.CorrelationId);

// Warning
_logger.LogWarning(
    "[ConnectionScore] Match not found | MatchId={MatchId} CorrelationId={Cid}",
    matchId, _correlation.CorrelationId);

// Error
_logger.LogError(
    "[OpenAiClient] Request failed | Purpose={Purpose} Error={Error} CorrelationId={Cid}",
    purpose, ex.Message, _correlation.CorrelationId);
```

**Rules:**
- **[ServiceName] prefix** — always start with bracketed service name
- **Structured properties** — use `{PropertyName}` placeholders, not string interpolation
- **Correlation ID** — always include `CorrelationId={Cid}`
- **Context, not code** — log business context (UserId, MatchId), not implementation details

---

### 4. OpenAI Client Pattern

**Never call HttpClient directly** — always use `IOpenAiClient`:

```csharp
public class MatchExplanationService
{
    private readonly IOpenAiClient _openAi;
    private readonly ILogger<MatchExplanationService> _logger;

    public async Task<string> GenerateExplanationAsync(Match match, CancellationToken ct)
    {
        var messages = new[]
        {
            new { role = "system", content = "You are a matchmaking assistant." },
            new { role = "user", content = $"Explain why {match.UserA} and {match.UserB} match." }
        };

        var response = await _openAi.ChatAsync(new OpenAiRequest(
            Model: "gpt-4.1-mini",
            Messages: messages,
            MaxTokens: 400,
            Purpose: "match_explanation"  // for log tracing
        ), ct);

        return response.Choices[0].Message.Content;
    }
}
```

**Benefits:**
- Exponential backoff + retry (3 attempts)
- 429 handling (respects `Retry-After` header)
- X-Correlation-ID on every outbound call
- Structured token usage logging

**Rule:** Never use `HttpClient` for OpenAI — always `IOpenAiClient`.

---

### 5. Idempotency Pattern

**Applied to critical mutations:**

```csharp
[HttpPost("/matches/{matchId}/pop")]
public async Task<IResult> PopBalloon(
    long matchId,
    HttpContext http,
    WovenDbContext db,
    IIdempotencyService idempotency,
    CancellationToken ct)
{
    var userId = EndpointHelper.GetUserId(http.User);
    var idempotencyKey = http.Request.Headers["X-Idempotency-Key"].ToString();

    if (string.IsNullOrEmpty(idempotencyKey))
        return Results.BadRequest("X-Idempotency-Key header required");

    // Check if already executed
    var cached = await idempotency.GetResponseAsync(userId, idempotencyKey, ct);
    if (cached != null)
        return Results.Ok(cached);  // Return cached response (no re-execution)

    // Execute once
    var result = await PopBalloonLogic(matchId, userId, db, ct);

    // Cache response for 24h
    await idempotency.StoreResponseAsync(userId, idempotencyKey, result, ct);

    return Results.Ok(result);
}
```

**Applied to:**
- `POST /matches/{matchId}/pop` (balloon pop → trial start)
- `POST /chats/{threadId}/trial-decision` (CONTINUE/END/BLOCK)
- `POST /moments/respond` (spark spend on LIKED_YOU actions only)

**Rule:** Use idempotency for non-idempotent mutations that spend resources (sparks) or change critical state (trial start).

---

### 6. EF Core Query Patterns

**Async queries only:**
```csharp
// ✅ Good
var user = await db.Users.FindAsync(userId);

// ❌ Bad (blocks thread)
var user = db.Users.Find(userId);
```

**Filtering in SQL, not C#:**
```csharp
// ✅ Good (SQL WHERE clause)
var matches = await db.Matches
    .Where(m => m.UserId == userId && m.BalloonState == BalloonState.ACTIVE)
    .ToListAsync(ct);

// ❌ Bad (loads all matches to memory, then filters)
var matches = await db.Matches.ToListAsync(ct);
matches = matches.Where(m => m.UserId == userId).ToList();
```

**Projections over full entities:**
```csharp
// ✅ Good (select only needed columns)
var userNames = await db.Users
    .Where(u => u.ProfileStatus == ProfileStatus.Complete)
    .Select(u => new { u.Id, u.Name })
    .ToListAsync(ct);

// ❌ Bad (loads full User entity)
var users = await db.Users
    .Where(u => u.ProfileStatus == ProfileStatus.Complete)
    .ToListAsync(ct);
var userNames = users.Select(u => new { u.Id, u.Name }).ToList();
```

**Avoid N+1 queries:**
```csharp
// ✅ Good (single query with JOIN)
var matches = await db.Matches
    .Include(m => m.MatchExplanation)
    .Where(m => m.UserId == userId)
    .ToListAsync(ct);

// ❌ Bad (N+1: 1 query for matches + N queries for explanations)
var matches = await db.Matches.Where(m => m.UserId == userId).ToListAsync(ct);
foreach (var match in matches)
{
    match.Explanation = await db.MatchExplanations.FindAsync(match.Id);
}
```

---

### 7. Background Worker Pattern

**Hosted service:**
```csharp
public class ConnectionScoreBatchWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ConnectionScoreBatchWorker> _logger;

    public ConnectionScoreBatchWorker(IServiceProvider services, ILogger<ConnectionScoreBatchWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait until 03:50 UTC
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddHours(3).AddMinutes(50);
            if (now > nextRun) nextRun = nextRun.AddDays(1);

            var delay = nextRun - now;
            await Task.Delay(delay, stoppingToken);

            // Execute batch job in scoped service provider
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

            try
            {
                await ProcessBatchAsync(db, stoppingToken);
                _logger.LogInformation("[ConnectionScore] Batch completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConnectionScore] Batch failed");
            }
        }
    }
}
```

**Registration (Program.cs):**
```csharp
if (!batchWorkersDisabled)
    builder.Services.AddHostedService<ConnectionScoreBatchWorker>();
```

**Rules:**
- **BackgroundService base class** — inherits `ExecuteAsync` loop
- **Scoped services** — create scope per iteration (EF Core DbContext is scoped)
- **Exception handling** — catch exceptions, log, continue loop
- **Cancellation** — respect `stoppingToken` for graceful shutdown

---

## Frontend Patterns (Angular)

### 1. Standalone Component Pattern

**Component:**
```typescript
@Component({
  selector: 'app-moments-page',
  standalone: true,
  imports: [CommonModule, MomentsCardComponent, TabViewModule],
  templateUrl: './moments.page.html',
  styleUrls: ['./moments.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MomentsPage implements OnInit {
  private cdr = inject(ChangeDetectorRef);
  private momentsService = inject(MomentsService);

  deck: DailyDeck | null = null;

  async ngOnInit() {
    this.deck = await firstValueFrom(this.momentsService.getDeck());
    this.cdr.markForCheck();  // Trigger change detection
  }
}
```

**Rules:**
- **Standalone: true** — no NgModule
- **OnPush change detection** — always
- **inject() over constructor** — Angular 21 best practice
- **markForCheck() after async** — required for OnPush

---

### 2. Service Pattern

**Service:**
```typescript
@Injectable({ providedIn: 'root' })
export class MomentsService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  getDeck(): Observable<DailyDeck> {
    return this.http.get<DailyDeck>(`${this.baseUrl}/moments/deck`);
  }

  respond(candidateId: number, choice: 'MAGICAL' | 'LOGICAL' | 'SKIP'): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/moments/respond`, { candidateId, choice });
  }
}
```

**Rules:**
- **providedIn: 'root'** — singleton service
- **inject(HttpClient)** — Angular 21 best practice
- **Return Observable<T>** — let component decide subscribe vs firstValueFrom

---

### 3. Optimistic UI Pattern

**Example: Sending a message**
```typescript
async sendMessage(text: string) {
  const tempId = crypto.randomUUID();
  const tempMessage = { id: tempId, text, sentAt: new Date(), status: 'sending' };

  // 1. Optimistic add
  this.messages.push(tempMessage);
  this.cdr.markForCheck();

  try {
    // 2. Send to backend
    const confirmed = await firstValueFrom(this.chatService.sendMessage(this.threadId, text));

    // 3. Replace temp with confirmed
    const idx = this.messages.findIndex(m => m.id === tempId);
    this.messages[idx] = confirmed;
  } catch (err) {
    // 4. Silent rollback (or show error toast)
    this.messages = this.messages.filter(m => m.id !== tempId);
  }

  this.cdr.markForCheck();
}
```

**Used for:**
- Sending messages
- Swiping on Moments cards
- Popping balloons
- Submitting game answers

**Rule:** Show instant feedback, silently correct errors. Don't block user with loading spinners.

---

### 4. Reactive State Pattern

**Service with BehaviorSubject:**
```typescript
@Injectable({ providedIn: 'root' })
export class ChatService {
  private threadsSubject = new BehaviorSubject<ChatThread[]>([]);
  public threads$ = this.threadsSubject.asObservable();

  async loadThreads() {
    const threads = await firstValueFrom(this.http.get<ChatThread[]>('/chats'));
    this.threadsSubject.next(threads);
  }

  addThread(thread: ChatThread) {
    const current = this.threadsSubject.value;
    this.threadsSubject.next([thread, ...current]);
  }
}
```

**Component subscribes:**
```typescript
export class ChatsListComponent implements OnInit {
  private chatService = inject(ChatService);
  threads: ChatThread[] = [];

  ngOnInit() {
    this.chatService.threads$.subscribe(threads => {
      this.threads = threads;
      this.cdr.markForCheck();
    });
  }
}
```

**Or use async pipe (no markForCheck needed):**
```html
<div *ngFor="let thread of chatService.threads$ | async">
  {{ thread.lastMessage }}
</div>
```

**Rule:** Service holds state (BehaviorSubject), components subscribe. Async pipe preferred (automatic change detection).

---

### 5. SSR-Safe Pattern

**Check platform before browser-only code:**
```typescript
export class SomeComponent implements OnInit {
  private platformId = inject(PLATFORM_ID);

  ngOnInit() {
    if (isPlatformBrowser(this.platformId)) {
      // Browser-only code (localStorage, window, etc.)
      const token = localStorage.getItem('jwt');
    }
  }
}
```

**Rule:** Wrap browser APIs (`localStorage`, `window`, `document`) in `isPlatformBrowser()` check to avoid SSR errors.

---

### 6. HTTP Interceptor Pattern

**Interceptor:**
```typescript
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('jwt');
  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }
  return next(req);
};
```

**Registration (app.config.ts):**
```typescript
export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([authInterceptor, correlationIdInterceptor])
    )
  ]
};
```

**Rule:** Use functional interceptors (`HttpInterceptorFn`) over class-based (`HttpInterceptor`) in Angular 21.

---

## Cross-Cutting Patterns

### 1. Correlation ID Pattern

**Backend (middleware):**
```csharp
public class CorrelationIdMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? GenerateCorrelationId();

        context.Items[ItemsKey] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
```

**Frontend (interceptor):**
```typescript
export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
  const correlationId = generateCorrelationId();  // 16-char hex
  req = req.clone({
    setHeaders: { 'X-Correlation-ID': correlationId }
  });
  return next(req);
};
```

**Rule:** Every request carries a correlation ID. Backend echoes it in response, logs it in every log line.

---

### 2. Error Handling Pattern

**Backend (exception handlers):**
```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey] as string ?? "?";

        var response = new
        {
            error = exception.Message,
            correlationId,
            timestamp = DateTime.UtcNow
        };

        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(response, ct);
        return true;
    }
}
```

**Frontend (error display):**
```typescript
try {
  await this.doSomething();
} catch (error: any) {
  const correlationId = error.headers?.get('X-Correlation-ID');
  console.error(`Error [${correlationId}]:`, error.error?.message || error.message);
  this.showToast(`Something went wrong (${correlationId})`);
}
```

**Rule:** Always include correlation ID in errors. User reports error with correlation ID → developer traces full request in logs.

---

## Anti-Patterns (Avoid These)

### Backend

❌ **Local `GetUserId()` copy**  
```csharp
// BAD
private static long GetUserId(ClaimsPrincipal user)
{
    return long.Parse(user.FindFirst("uid")?.Value ?? "0");
}
```
✅ **Use `EndpointHelper.GetUserId()`** — throws exception if invalid.

---

❌ **Calling HttpClient for OpenAI directly**  
```csharp
// BAD
var response = await _http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", payload);
```
✅ **Use `IOpenAiClient`** — handles retries, 429, correlation IDs.

---

❌ **EF Core synchronous queries**  
```csharp
// BAD (blocks thread)
var user = db.Users.Find(userId);
```
✅ **Always async** — `FindAsync()`, `ToListAsync()`, `FirstOrDefaultAsync()`.

---

❌ **Loading all to memory, then filtering in C#**  
```csharp
// BAD (N+1, loads everything)
var allMatches = await db.Matches.ToListAsync();
var filtered = allMatches.Where(m => m.UserId == userId).ToList();
```
✅ **Filter in SQL** — `Where()` before `ToListAsync()`.

---

### Frontend

❌ **Default change detection**  
```typescript
// BAD
changeDetection: ChangeDetectionStrategy.Default
```
✅ **OnPush + markForCheck()** — explicit, performant.

---

❌ **HttpClient calls in components**  
```typescript
// BAD
export class SomeComponent {
  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.http.get('/api/data').subscribe(data => this.data = data);
  }
}
```
✅ **Service layer** — components call services, services call HttpClient.

---

❌ **Browser APIs without platform check (SSR errors)**  
```typescript
// BAD (crashes on SSR)
const token = localStorage.getItem('jwt');
```
✅ **isPlatformBrowser() check** — wrap browser APIs.

---

❌ **Unsubscribed observables (memory leaks)**  
```typescript
// BAD (memory leak)
ngOnInit() {
  this.service.data$.subscribe(data => this.data = data);
}
```
✅ **Async pipe or unsubscribe** — `takeUntilDestroyed()` or store subscription + unsubscribe in `ngOnDestroy()`.

---

## Summary

**Backend:**
- Minimal API endpoints (thin, delegate to services)
- Service pattern (interface + implementation, constructor injection)
- Structured logging ([ServiceName] + correlation ID)
- `IOpenAiClient` (never HttpClient directly)
- EF Core async queries (filter in SQL, not C#)

**Frontend:**
- Standalone components (OnPush + markForCheck())
- Service layer (components never call HttpClient)
- Optimistic UI (instant feedback, silent rollback)
- Reactive state (BehaviorSubject + async pipe)
- SSR-safe (isPlatformBrowser check)

**Cross-cutting:**
- Correlation IDs (every request)
- Error handling (correlation ID in errors)
- Idempotency (critical mutations)

---

**Last Updated:** 2026-10-07  
**Evidence:** [backend/](../../backend/WovenBackend/), [frontend/](../../frontend/woven-frontend/src/app/)
