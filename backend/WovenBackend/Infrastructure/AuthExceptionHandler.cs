using Microsoft.AspNetCore.Diagnostics;

namespace WovenBackend.Infrastructure;

/// <summary>
/// Maps UnauthorizedAccessException → HTTP 401 with correlation ID.
/// Runs before GlobalExceptionHandler in the pipeline.
/// </summary>
public class AuthExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        if (exception is not UnauthorizedAccessException) return false;

        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey] as string ?? "unknown";

        context.Response.StatusCode  = 401;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            error         = "Unauthorized",
            correlationId = correlationId
        }, ct);

        return true;
    }
}
