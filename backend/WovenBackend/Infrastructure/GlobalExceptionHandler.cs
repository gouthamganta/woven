using System.Net;
using Microsoft.AspNetCore.Diagnostics;

namespace WovenBackend.Infrastructure;

/// <summary>
/// Catches all unhandled exceptions, logs them with full context, and returns a
/// structured JSON error response that always includes the correlation ID.
///
/// The correlation ID ties the user's error report to the exact server-side log entry.
/// Response body:
/// {
///   "error":         "An unexpected error occurred",
///   "correlationId": "a3f9c2b1e8d04a1f",
///   "timestamp":     "2026-06-04T14:22:01.123Z"
/// }
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey] as string ?? "unknown";
        var userId        = context.User.FindFirst("uid")?.Value ?? "anon";

        _logger.LogError(exception,
            "[UNHANDLED] {ExceptionType}: {Message} | " +
            "CorrelationId={CorrelationId} UserId={UserId} Path={Path} Method={Method}",
            exception.GetType().Name, exception.Message,
            correlationId, userId,
            context.Request.Path, context.Request.Method);

        context.Response.StatusCode  = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            error         = "An unexpected error occurred",
            correlationId = correlationId,
            timestamp     = DateTimeOffset.UtcNow
        }, ct);

        return true;
    }
}

/// <summary>
/// Domain exception — thrown when a business rule is violated.
/// Maps to HTTP 422 Unprocessable Entity, not 500.
/// Carries the correlation ID automatically via the pipeline.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }
    public DomainException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// Converts DomainExceptions to 422 responses before they reach GlobalExceptionHandler.
/// </summary>
public class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        if (exception is not DomainException domain) return false;

        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey] as string ?? "unknown";

        context.Response.StatusCode  = 422;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            error         = domain.Message,
            code          = domain.Code,
            correlationId = correlationId
        }, ct);

        return true;
    }
}
