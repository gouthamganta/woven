# Code Patterns

Common architectural patterns used in Woven codebase.

---

## Backend Patterns

### MapEndpoints Pattern

All endpoints use the MapEndpoints extension method pattern for route registration.

**File structure:**
```csharp
// Endpoints/XxxEndpoints.cs
namespace WovenBackend.Endpoints;

public static class XxxEndpoints
{
    public static void MapXxxEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/xxx");
        group.RequireAuthorization();
        
        group.MapGet("", async (HttpContext http, CancellationToken ct) =>
        {
            var userId = EndpointHelper.GetUserId(http.User);
            // ... endpoint logic
            return Results.Ok(new { data });
        });
    }
}
```

**Registration in Program.cs:**
```csharp
app.MapXxxEndpoints();
```

**Key principles:**
- One file per feature group (Moments, Chats, Matches, etc.)
- Route groups for common prefixes
- `EndpointHelper.GetUserId()` for auth
- Named endpoints with `.WithName()`

**Source:** All files in `backend/WovenBackend/Endpoints/`

---

### Service Layer Pattern

Business logic lives in services, not endpoints.

**Interface + Implementation:**
```csharp
// Services/IMatchService.cs
public interface IMatchService
{
    Task<Match?> GetMatchAsync(Guid matchId, int userId, CancellationToken ct);
}

// Services/MatchService.cs
public class MatchService : IMatchService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<MatchService> _logger;
    
    public MatchService(WovenDbContext db, ILogger<MatchService> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public async Task<Match?> GetMatchAsync(Guid matchId, int userId, CancellationToken ct)
    {
        // Business logic here
    }
}
```

**DI Registration in Program.cs:**
```csharp
builder.Services.AddScoped<IMatchService, MatchService>();
```

---

### GetUserId Pattern

**Always use EndpointHelper.GetUserId():**
```csharp
var userId = EndpointHelper.GetUserId(http.User);
```

**Never parse JWT directly:**
```csharp
// ❌ Bad
var claim = http.User.FindFirstValue("uid");
var userId = int.Parse(claim);

// ✅ Good
var userId = EndpointHelper.GetUserId(http.User);
```

**Source:** `backend/WovenBackend/Endpoints/EndpointHelper.cs`

---

### Signal Recording Pattern

All behavioral events must be recorded:

```csharp
await _matchSignalService.RecordAsync(
    viewerId: userId,
    candidateId: targetUserId,
    eventType: MatchSignalEventTypes.TimeToFirstMessageMs,
    eventValue: elapsedMs,
    metadataJson: null,
    ct: ct
);
```

**Source:** `backend/WovenBackend/Services/IMatchSignalService.cs`

---

## Frontend Patterns

### OnPush Change Detection

**All page components use OnPush:**
```typescript
@Component({
  selector: 'app-moments',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule]
})
export class MomentsPage {
  constructor(private cdr: ChangeDetectorRef) {}
  
  async loadData() {
    this.data = await this.service.fetchData();
    this.cdr.markForCheck(); // Required for OnPush!
  }
}
```

**Why:** Improves performance, forces explicit state change handling.

---

### Service Layer Pattern

**HTTP calls belong in services:**
```typescript
// services/moments.service.ts
@Injectable({ providedIn: 'root' })
export class MomentsService {
  private apiUrl = 'http://localhost:5135';
  
  constructor(private http: HttpClient) {}
  
  async getDeck(): Promise<DeckResponse> {
    return firstValueFrom(
      this.http.get<DeckResponse>(`${this.apiUrl}/moments`)
    );
  }
}
```

**Component usage:**
```typescript
constructor(private momentsService: MomentsService) {}

async ngOnInit() {
  this.deck = await this.momentsService.getDeck();
  this.cdr.markForCheck();
}
```

**Source:** All files in `frontend/woven-frontend/src/app/services/`

---

### Optimistic UI Pattern

For chat messages and immediate feedback:

```typescript
async sendMessage(body: string) {
  // 1. Add temporary message immediately
  const tempMsg = {
    id: crypto.randomUUID(),
    body,
    senderUserId: this.meUserId,
    createdAt: new Date(),
    _temp: true
  };
  this.messages.push(tempMsg);
  this.cdr.markForCheck();
  
  try {
    // 2. Send to server
    const confirmed = await this.chatService.sendMessage(this.threadId, body);
    
    // 3. Replace temp with confirmed
    const idx = this.messages.findIndex(m => m.id === tempMsg.id);
    if (idx >= 0) {
      this.messages[idx] = confirmed;
      this.cdr.markForCheck();
    }
  } catch (error) {
    // 4. Remove temp on error
    this.messages = this.messages.filter(m => m.id !== tempMsg.id);
    this.cdr.markForCheck();
    this.showError();
  }
}
```

---

### Route Parameter Extraction

**Walk the route tree for nested params:**
```typescript
private getThreadIdFromRouteTree(route: ActivatedRoute): string | null {
  while (route) {
    if (route.snapshot.paramMap.has('threadId')) {
      return route.snapshot.paramMap.get('threadId');
    }
    route = route.parent!;
  }
  return null;
}
```

**Source:** `frontend/woven-frontend/src/app/pages/chats/chat-thread.component.ts`

---

## Common Patterns

### Timestamp Handling

**Always use MomentsRules.NowUtc():**
```csharp
var now = MomentsRules.NowUtc();
match.CreatedAt = now;
```

**Never:**
```csharp
var now = DateTime.UtcNow; // ❌ Wrong
```

---

### Error Responses

**Use structured Results:**
```csharp
return Results.NotFound(new { error = "MATCH_NOT_FOUND" });
return Results.BadRequest(new { error = "INVALID_INPUT", message = "Age must be 18+" });
return Results.UnprocessableEntity(new { error = "BALLOON_NOT_ACTIVE" });
```

---

### CSS Tokens

**Always use CSS variables:**
```scss
// ✅ Good
.card {
  background: var(--surface-1);
  color: var(--text-primary);
  padding: var(--space-4);
}

// ❌ Bad
.card {
  background: #ffffff;
  color: #333333;
  padding: 16px;
}
```

**Tokens defined in:** `frontend/woven-frontend/src/styles.scss`

---

**Last Updated:** 2026-10-07
