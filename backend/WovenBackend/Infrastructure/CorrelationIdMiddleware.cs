using Serilog.Context;

namespace WovenBackend.Infrastructure;

/// <summary>
/// Every request gets a correlation ID.
/// Order of precedence:
///   1. X-Correlation-ID header already on the request (forwarded from client / upstream)
///   2. X-Request-ID header (alternative convention)
///   3. Generate a new GUID
///
/// The ID is:
///   - Added to the response header so the client can log it
///   - Pushed into Serilog LogContext so every log line in this request carries {CorrelationId}
///   - Stored in HttpContext.Items so any code in the pipeline can read it
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemsKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    private static bool IsValidCorrelationId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Length > 64) return false;
        // Allow alphanumeric, dash, underscore — reject everything else (prevent log injection)
        return id.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? context.Request.Headers["X-Request-ID"].FirstOrDefault();

        // Validate incoming correlation ID: <= 64 chars, alphanumeric + dash/underscore only
        // Invalid or missing → generate a new one
        var correlationId = IsValidCorrelationId(incoming)
            ? incoming!
            : Guid.NewGuid().ToString("N")[..16];   // 16-char hex — short but collision-proof at scale

        context.Items[ItemsKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // Push into Serilog's ambient context — every log in this request gets CorrelationId
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path))
        using (LogContext.PushProperty("RequestMethod", context.Request.Method))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Accessor that any service can inject to read the current request's correlation ID.
/// Scoped lifetime — one instance per request, resolved from HttpContext.Items.
/// Returns "no-context" when called outside an HTTP request (e.g. from a background worker).
/// </summary>
public interface ICorrelationService
{
    string CorrelationId { get; }
}

public class CorrelationService : ICorrelationService
{
    private readonly IHttpContextAccessor _accessor;

    public CorrelationService(IHttpContextAccessor accessor) => _accessor = accessor;

    public string CorrelationId =>
        _accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemsKey] as string
        ?? "no-context";
}
