# Backend Service Template

> Copy this template when creating new backend services.
> Follow all patterns exactly - deviations break consistency.

---

## File Location

```
backend/
└── Woven.Api/
    └── Services/
        ├── I{Feature}Service.cs    ← Interface
        └── {Feature}Service.cs     ← Implementation
```

---

## Interface Template

```csharp
// Services/I{Feature}Service.cs

namespace Woven.Api.Services;

/// <summary>
/// {Brief description of what this service handles}
/// </summary>
public interface I{Feature}Service
{
    /// <summary>
    /// {Method description}
    /// </summary>
    /// <param name="userId">The user performing the action</param>
    /// <returns>{What it returns}</returns>
    Task<{ReturnType}> {MethodName}(Guid userId);

    /// <summary>
    /// {Method description}
    /// </summary>
    Task<{ReturnType}> {MethodName}(Guid userId, {InputType} input);
}
```

---

## Implementation Template

```csharp
// Services/{Feature}Service.cs

using Microsoft.EntityFrameworkCore;
using Woven.Api.Data;
using Woven.Api.Models;

namespace Woven.Api.Services;

public class {Feature}Service : I{Feature}Service
{
    private readonly WovenDbContext _db;
    private readonly ILogger<{Feature}Service> _logger;

    public {Feature}Service(
        WovenDbContext db,
        ILogger<{Feature}Service> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<{ReturnType}> {MethodName}(Guid userId)
    {
        // 1. Validate inputs
        if (userId == Guid.Empty)
            throw new ArgumentException("Invalid user ID", nameof(userId));

        // 2. Fetch required data
        var entity = await _db.{Entities}
            .Where(e => e.UserId == userId)
            .FirstOrDefaultAsync();

        if (entity == null)
        {
            _logger.LogWarning("{Feature} not found for user {UserId}", userId);
            return null;
        }

        // 3. Business logic
        // ...

        // 4. Save changes if needed
        await _db.SaveChangesAsync();

        // 5. Return result
        return result;
    }
}
```

---

## Result Pattern Template

For operations that can fail gracefully, use typed result records:

```csharp
// In the service file or a separate Results folder

namespace Woven.Api.Services;

/// <summary>
/// Result of attempting to {action description}
/// </summary>
/// <param name="Success">Whether the operation succeeded</param>
/// <param name="{DataField}">The resulting data if successful</param>
/// <param name="Error">Error message if failed, null if successful</param>
public record {Feature}Result(
    bool Success,
    {DataType}? {DataField},
    string? Error
)
{
    public static {Feature}Result Ok({DataType} data) =>
        new(true, data, null);

    public static {Feature}Result Fail(string error) =>
        new(false, default, error);
}
```

**Usage:**

```csharp
public async Task<SpendResult> TrySpend(Guid userId, int cost)
{
    var budget = await GetBudget(userId);

    if (budget.Remaining < cost)
        return SpendResult.Fail("Not enough budget");

    budget.Remaining -= cost;
    await _db.SaveChangesAsync();

    return SpendResult.Ok(budget.Remaining);
}
```

---

## Registration Template

```csharp
// In Program.cs

// Choose appropriate lifetime:

// Singleton - One instance for entire app lifetime
// Use for: Caches, configuration, stateless utilities
builder.Services.AddSingleton<I{Feature}Service, {Feature}Service>();

// Scoped - One instance per HTTP request (MOST COMMON)
// Use for: Database operations, user-scoped services
builder.Services.AddScoped<I{Feature}Service, {Feature}Service>();

// Transient - New instance every time
// Use for: Stateless services, builders
builder.Services.AddTransient<I{Feature}Service, {Feature}Service>();
```

---

## Common Dependencies

```csharp
// Database context
private readonly WovenDbContext _db;

// Logging
private readonly ILogger<{Feature}Service> _logger;

// Configuration
private readonly IConfiguration _config;
// Or typed options:
private readonly FeatureOptions _options;

// Other services (inject via interface)
private readonly IOtherService _otherService;
```

---

## Error Handling Patterns

### Throw for Programming Errors

```csharp
if (userId == Guid.Empty)
    throw new ArgumentException("Invalid user ID", nameof(userId));
```

### Return Result for Business Failures

```csharp
if (budget.Remaining < cost)
    return SpendResult.Fail("Not enough budget");
```

### Log and Continue for Non-Critical

```csharp
try
{
    await _notificationService.Send(userId, message);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to send notification to {UserId}", userId);
    // Continue without failing the main operation
}
```

---

## Testing Considerations

```csharp
// Services should be easily testable:
// 1. Depend on interfaces, not implementations
// 2. Accept DbContext for easy mocking
// 3. Keep methods focused and single-purpose

// Example test setup:
var mockDb = new Mock<WovenDbContext>();
var mockLogger = new Mock<ILogger<FeatureService>>();
var service = new FeatureService(mockDb.Object, mockLogger.Object);
```

---

## Checklist

Before committing a new service:

- [ ] Interface defined with XML documentation
- [ ] Implementation follows single responsibility
- [ ] Appropriate DI lifetime chosen
- [ ] Registered in Program.cs
- [ ] Uses Result pattern for fallible operations
- [ ] Logging added for important operations
- [ ] No direct endpoint logic in service
- [ ] Async/await used correctly
