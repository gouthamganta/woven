# Backend Endpoint Template

> Copy this template when creating new API endpoints.
> Always use Minimal API pattern - never use Controllers.

---

## File Location

```
backend/
└── Woven.Api/
    └── Endpoints/
        └── {Feature}Endpoints.cs
```

---

## Basic Endpoint Template

```csharp
// Endpoints/{Feature}Endpoints.cs

using Microsoft.AspNetCore.Mvc;
using Woven.Api.Services;
using Woven.Api.DTOs;

namespace Woven.Api.Endpoints;

public static class {Feature}Endpoints
{
    public static void Map{Feature}Endpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/{feature}")
            .RequireAuthorization()  // ← MANDATORY for all endpoints
            .WithTags("{Feature}");  // ← For OpenAPI/Swagger grouping

        // GET list
        group.MapGet("/", GetAll);

        // GET single
        group.MapGet("/{id:guid}", GetById);

        // POST create
        group.MapPost("/", Create);

        // PUT update
        group.MapPut("/{id:guid}", Update);

        // DELETE
        group.MapDelete("/{id:guid}", Delete);
    }

    // ═══════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════

    private static async Task<IResult> GetAll(
        I{Feature}Service service,
        HttpContext http)
    {
        var userId = GetUserId(http);
        var result = await service.GetAll(userId);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetById(
        Guid id,
        I{Feature}Service service,
        HttpContext http)
    {
        var userId = GetUserId(http);
        var result = await service.GetById(userId, id);

        if (result == null)
            return Results.NotFound();

        return Results.Ok(result);
    }

    private static async Task<IResult> Create(
        [FromBody] Create{Feature}Request request,
        I{Feature}Service service,
        HttpContext http)
    {
        var userId = GetUserId(http);
        var result = await service.Create(userId, request);

        if (!result.Success)
            return Results.BadRequest(new { error = result.Error });

        return Results.Created($"/api/{feature}/{result.Data.Id}", result.Data);
    }

    private static async Task<IResult> Update(
        Guid id,
        [FromBody] Update{Feature}Request request,
        I{Feature}Service service,
        HttpContext http)
    {
        var userId = GetUserId(http);
        var result = await service.Update(userId, id, request);

        if (!result.Success)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(result.Data);
    }

    private static async Task<IResult> Delete(
        Guid id,
        I{Feature}Service service,
        HttpContext http)
    {
        var userId = GetUserId(http);
        var result = await service.Delete(userId, id);

        if (!result.Success)
            return Results.BadRequest(new { error = result.Error });

        return Results.NoContent();
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static Guid GetUserId(HttpContext http)
    {
        var claim = http.User.FindFirst("sub")?.Value
            ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }
}
```

---

## Registration in Program.cs

```csharp
// In Program.cs, after app is built:

app.Map{Feature}Endpoints();
```

---

## Request/Response DTOs

```csharp
// DTOs/{Feature}Dtos.cs

namespace Woven.Api.DTOs;

// Request DTOs
public record Create{Feature}Request(
    string Name,
    string Description,
    int? OptionalField
);

public record Update{Feature}Request(
    string Name,
    string Description
);

// Response DTOs
public record {Feature}Response(
    Guid Id,
    string Name,
    string Description,
    DateTime CreatedAt
);

public record {Feature}ListResponse(
    IReadOnlyList<{Feature}Response> Items,
    int Total
);
```

---

## Action-Based Endpoints

For non-CRUD operations, use action-based routes:

```csharp
public static void MapBalloonsEndpoints(this IEndpointRouteBuilder routes)
{
    var group = routes.MapGroup("/api/balloons")
        .RequireAuthorization()
        .WithTags("Balloons");

    // Standard CRUD
    group.MapGet("/", GetAll);
    group.MapGet("/{id:guid}", GetById);

    // Actions - use verbs for non-CRUD operations
    group.MapPost("/{id:guid}/pop", Pop);
    group.MapPost("/{id:guid}/unmatch", Unmatch);
    group.MapPost("/{id:guid}/block", Block);
}

private static async Task<IResult> Pop(
    Guid id,
    IBalloonsService service,
    HttpContext http)
{
    var userId = GetUserId(http);
    var result = await service.Pop(userId, id);

    if (!result.Success)
        return Results.BadRequest(new { error = result.Error });

    return Results.Ok(new { threadId = result.ThreadId });
}
```

---

## Query Parameters

```csharp
group.MapGet("/", GetAll);

private static async Task<IResult> GetAll(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string? status = null,
    I{Feature}Service service,
    HttpContext http)
{
    var userId = GetUserId(http);

    var filter = new {Feature}Filter
    {
        Page = Math.Max(1, page),
        PageSize = Math.Clamp(pageSize, 1, 100),
        Status = status
    };

    var result = await service.GetAll(userId, filter);
    return Results.Ok(result);
}
```

---

## Error Response Format

Always return consistent error format:

```csharp
// Success responses
return Results.Ok(data);                           // 200
return Results.Created($"/api/x/{id}", data);      // 201
return Results.NoContent();                        // 204

// Error responses - ALWAYS include error field
return Results.BadRequest(new { error = "Invalid input" });           // 400
return Results.NotFound();                                            // 404
return Results.Conflict(new { error = "Already exists" });            // 409

// Structured error
return Results.BadRequest(new
{
    error = "VALIDATION_FAILED",
    message = "One or more fields are invalid",
    details = new { field = "name", issue = "Required" }
});
```

---

## Authorization Patterns

```csharp
// Require authentication (default for all endpoints)
.RequireAuthorization()

// Require specific role
.RequireAuthorization(policy => policy.RequireRole("Admin"))

// Require specific claim
.RequireAuthorization(policy => policy.RequireClaim("feature:premium"))

// Anonymous endpoint (rare - use sparingly)
group.MapGet("/public-info", GetPublicInfo)
    .AllowAnonymous();
```

---

## File Upload Endpoint

```csharp
group.MapPost("/photos", UploadPhoto)
    .DisableAntiforgery();  // Required for file uploads

private static async Task<IResult> UploadPhoto(
    IFormFile file,
    IPhotoService photoService,
    HttpContext http)
{
    var userId = GetUserId(http);

    // Validate file
    if (file.Length == 0)
        return Results.BadRequest(new { error = "No file provided" });

    if (file.Length > 10 * 1024 * 1024)  // 10MB limit
        return Results.BadRequest(new { error = "File too large" });

    var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
    if (!allowedTypes.Contains(file.ContentType))
        return Results.BadRequest(new { error = "Invalid file type" });

    var result = await photoService.Upload(userId, file);
    return Results.Ok(result);
}
```

---

## Real-Time Endpoint (WebSocket/SSE)

```csharp
// Server-Sent Events for real-time updates
group.MapGet("/stream", StreamUpdates);

private static async Task StreamUpdates(
    HttpContext http,
    INotificationService notifications,
    CancellationToken cancellationToken)
{
    var userId = GetUserId(http);

    http.Response.Headers.Append("Content-Type", "text/event-stream");
    http.Response.Headers.Append("Cache-Control", "no-cache");

    await foreach (var update in notifications.GetUpdates(userId, cancellationToken))
    {
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(update)}\n\n");
        await http.Response.Body.FlushAsync();
    }
}
```

---

## Checklist

Before committing new endpoints:

- [ ] Uses Minimal API pattern (NOT Controllers)
- [ ] `.RequireAuthorization()` is present on the group
- [ ] All handlers extract userId from HttpContext
- [ ] Request DTOs defined for POST/PUT bodies
- [ ] Response DTOs defined (not returning entities directly)
- [ ] Error responses use consistent `{ error: "..." }` format
- [ ] Registered in Program.cs
- [ ] No business logic in handlers (delegate to services)
- [ ] Route naming follows REST conventions
