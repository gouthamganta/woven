# Backend Patterns Reference

> Quick reference for common backend patterns in Woven.
> Use these patterns consistently across all backend code.

---

## 1. Dependency Injection

### Service Registration

```csharp
// Program.cs

// Scoped (per-request) - Most common for DB-related services
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<IBalloonService, BalloonService>();
builder.Services.AddScoped<IChatService, ChatService>();

// Singleton (app lifetime) - For caches, configuration
builder.Services.AddSingleton<IAppConfig, AppConfig>();
builder.Services.AddSingleton<ICacheService, MemoryCacheService>();

// Transient (new instance each time) - For stateless utilities
builder.Services.AddTransient<IEmailBuilder, EmailBuilder>();
```

### Constructor Injection

```csharp
public class MatchService : IMatchService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<MatchService> _logger;
    private readonly INotificationService _notifications;

    public MatchService(
        WovenDbContext db,
        ILogger<MatchService> logger,
        INotificationService notifications)
    {
        _db = db;
        _logger = logger;
        _notifications = notifications;
    }
}
```

---

## 2. Repository/Query Patterns

### Basic Queries

```csharp
// Get single by ID
var user = await _db.Users.FindAsync(userId);

// Get single with condition
var balloon = await _db.Balloons
    .FirstOrDefaultAsync(b => b.Id == id && b.UserAId == userId);

// Get list with filters
var matches = await _db.Matches
    .Where(m => m.UserId == userId)
    .Where(m => m.Status == MatchStatus.ACTIVE)
    .OrderByDescending(m => m.CreatedAt)
    .Take(20)
    .ToListAsync();
```

### Include Related Data

```csharp
// Include navigation properties
var balloon = await _db.Balloons
    .Include(b => b.UserA)
    .Include(b => b.UserB)
    .Include(b => b.Moment)
    .FirstOrDefaultAsync(b => b.Id == id);

// Selective includes
var balloon = await _db.Balloons
    .Include(b => b.UserA)
        .ThenInclude(u => u.Photos)
    .FirstOrDefaultAsync(b => b.Id == id);
```

### Projections (Recommended for DTOs)

```csharp
// Project to DTO directly - more efficient
var balloons = await _db.Balloons
    .Where(b => b.UserAId == userId || b.UserBId == userId)
    .Where(b => b.Status == BalloonStatus.ACTIVE)
    .Select(b => new BalloonDto
    {
        Id = b.Id,
        MatchType = b.MatchType,
        OtherUser = b.UserAId == userId
            ? new UserSummaryDto { Id = b.UserBId, Name = b.UserB.FirstName }
            : new UserSummaryDto { Id = b.UserAId, Name = b.UserA.FirstName },
        ExpiresAt = b.ExpiresAt
    })
    .ToListAsync();
```

### Pagination

```csharp
public async Task<PagedResult<T>> GetPaged<T>(
    IQueryable<T> query,
    int page,
    int pageSize)
{
    var total = await query.CountAsync();

    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return new PagedResult<T>
    {
        Items = items,
        Total = total,
        Page = page,
        PageSize = pageSize,
        TotalPages = (int)Math.Ceiling(total / (double)pageSize)
    };
}
```

---

## 3. Result Pattern

### Typed Results for Fallible Operations

```csharp
// Define result types
public record ServiceResult<T>(bool Success, T? Data, string? Error)
{
    public static ServiceResult<T> Ok(T data) => new(true, data, null);
    public static ServiceResult<T> Fail(string error) => new(false, default, error);
}

// Specific result types
public record SpendResult(bool Success, int Remaining, string? Error);
public record MatchResult(bool Success, Guid? MatchId, MatchType? Type, string? Error);
```

### Usage in Services

```csharp
public async Task<SpendResult> TrySpend(Guid userId, int cost)
{
    var budget = await GetOrCreateBudget(userId);

    if (budget.Remaining < cost)
        return new SpendResult(false, budget.Remaining, "Insufficient budget");

    budget.Remaining -= cost;
    budget.LastSpent = DateTime.UtcNow;

    await _db.SaveChangesAsync();
    return new SpendResult(true, budget.Remaining, null);
}
```

### Usage in Endpoints

```csharp
private static async Task<IResult> MakeChoice(
    [FromBody] ChoiceRequest request,
    IMomentsService service,
    HttpContext http)
{
    var userId = GetUserId(http);
    var result = await service.MakeChoice(userId, request);

    if (!result.Success)
        return Results.BadRequest(new { error = result.Error });

    return Results.Ok(new
    {
        budgetRemaining = result.BudgetRemaining,
        matched = result.Matched,
        matchId = result.MatchId
    });
}
```

---

## 4. Validation Patterns

### Input Validation

```csharp
public async Task<ValidationResult> ValidateProfile(UpdateProfileRequest request)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(request.FirstName))
        errors.Add("First name is required");

    if (request.FirstName?.Length > 50)
        errors.Add("First name must be 50 characters or less");

    if (request.Bio?.Length > 500)
        errors.Add("Bio must be 500 characters or less");

    if (request.BirthDate > DateTime.UtcNow.AddYears(-18))
        errors.Add("Must be 18 or older");

    return errors.Count == 0
        ? ValidationResult.Success()
        : ValidationResult.Fail(errors);
}
```

### FluentValidation (if using)

```csharp
public class CreateProfileRequestValidator : AbstractValidator<CreateProfileRequest>
{
    public CreateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(50).WithMessage("First name must be 50 characters or less");

        RuleFor(x => x.BirthDate)
            .LessThan(DateTime.UtcNow.AddYears(-18))
            .WithMessage("Must be 18 or older");
    }
}
```

---

## 5. Transaction Patterns

### Implicit Transactions (DbContext SaveChanges)

```csharp
// SaveChanges is already transactional
public async Task<MatchResult> CreateMatch(Guid userA, Guid userB, Guid momentId)
{
    // All changes are atomic
    var match = new Match { /* ... */ };
    _db.Matches.Add(match);

    var balloon = new Balloon { /* ... */ };
    _db.Balloons.Add(balloon);

    await _db.SaveChangesAsync();  // Both saved in one transaction
    return MatchResult.Ok(match.Id);
}
```

### Explicit Transactions (Cross-Service)

```csharp
public async Task<Result> ComplexOperation(Guid userId)
{
    await using var transaction = await _db.Database.BeginTransactionAsync();

    try
    {
        // Operation 1
        var budget = await _budgetService.TrySpend(userId, 1);
        if (!budget.Success)
        {
            await transaction.RollbackAsync();
            return Result.Fail(budget.Error);
        }

        // Operation 2
        var match = await _matchService.Create(userId, /* ... */);
        if (!match.Success)
        {
            await transaction.RollbackAsync();
            return Result.Fail(match.Error);
        }

        await transaction.CommitAsync();
        return Result.Ok();
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        _logger.LogError(ex, "Complex operation failed for user {UserId}", userId);
        throw;
    }
}
```

---

## 6. Caching Patterns

### Memory Cache

```csharp
public class CachedConfigService : IConfigService
{
    private readonly IMemoryCache _cache;
    private readonly WovenDbContext _db;

    public async Task<AppConfig> GetConfig()
    {
        return await _cache.GetOrCreateAsync("app-config", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            return await _db.AppConfigs.FirstOrDefaultAsync()
                ?? AppConfig.Default;
        });
    }

    public async Task InvalidateConfig()
    {
        _cache.Remove("app-config");
    }
}
```

### User-Specific Cache Keys

```csharp
public async Task<UserBudget> GetBudget(Guid userId)
{
    var cacheKey = $"budget:{userId}:{DateTime.UtcNow:yyyy-MM-dd}";

    return await _cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
        return await LoadBudgetFromDb(userId);
    });
}
```

---

## 7. Background Jobs Pattern

### Fire-and-Forget

```csharp
// Using IHostedService for background work
public class NotificationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<NotificationJob> _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<INotificationService>();

            try
            {
                await service.Send(job.UserId, job.Message);
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }
}

// Queueing a job
await _notificationChannel.Writer.WriteAsync(new NotificationJob(userId, message));
```

---

## 8. Logging Patterns

### Structured Logging

```csharp
// Good - structured with named parameters
_logger.LogInformation(
    "User {UserId} created match {MatchId} with type {MatchType}",
    userId, matchId, matchType);

// Good - warning for expected failures
_logger.LogWarning(
    "Budget exceeded for user {UserId}, required {Required}, had {Available}",
    userId, cost, budget.Remaining);

// Good - error with exception
_logger.LogError(ex,
    "Failed to process match for users {UserA} and {UserB}",
    userAId, userBId);
```

### What to Log

```csharp
// DO log:
// - Important business events
// - Warnings for edge cases
// - Errors with context

// DON'T log:
// - Sensitive data (passwords, tokens)
// - PII in production (can be enabled in dev)
// - Every routine operation (too noisy)
```

---

## 9. Configuration Patterns

### Options Pattern

```csharp
// appsettings.json
{
  "Budget": {
    "DailyLimit": 10,
    "ResetHourUtc": 0
  }
}

// Options class
public class BudgetOptions
{
    public const string Section = "Budget";
    public int DailyLimit { get; set; } = 10;
    public int ResetHourUtc { get; set; } = 0;
}

// Registration
builder.Services.Configure<BudgetOptions>(
    builder.Configuration.GetSection(BudgetOptions.Section));

// Usage
public class BudgetService
{
    private readonly BudgetOptions _options;

    public BudgetService(IOptions<BudgetOptions> options)
    {
        _options = options.Value;
    }
}
```

---

## 10. Testing Patterns

### Service Testing Setup

```csharp
public class MatchServiceTests
{
    private readonly WovenDbContext _db;
    private readonly MatchService _service;

    public MatchServiceTests()
    {
        var options = new DbContextOptionsBuilder<WovenDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new WovenDbContext(options);
        _service = new MatchService(
            _db,
            Mock.Of<ILogger<MatchService>>(),
            Mock.Of<INotificationService>());
    }

    [Fact]
    public async Task CreateMatch_WhenBothUsersChose_ReturnsMatch()
    {
        // Arrange
        var userA = await CreateTestUser();
        var userB = await CreateTestUser();
        var moment = await CreateTestMoment();

        // Act
        var result = await _service.CreateMatch(userA.Id, userB.Id, moment.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MatchId);
    }
}
```

---

## Quick Reference

| Pattern | When to Use |
|---------|-------------|
| Result<T> | Operations that can fail gracefully |
| Repository query | Data access with filters/includes |
| Projection | When returning DTOs (prefer over Include) |
| Memory cache | Frequently accessed, rarely changed data |
| Background job | Operations that don't need immediate result |
| Options pattern | Configuration values |
| Explicit transaction | Multi-service operations |
