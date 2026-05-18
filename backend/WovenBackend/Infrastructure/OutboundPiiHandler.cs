using WovenBackend.Services.Security;

namespace WovenBackend.Infrastructure;

public class OutboundPiiHandler : DelegatingHandler
{
    private readonly ISecurityAuditService _audit;
    private readonly ILogger<OutboundPiiHandler> _logger;

    private static readonly string[] _blockedHeaders = ["X-User-Id", "X-UserId", "User-Id"];

    public OutboundPiiHandler(ISecurityAuditService audit, ILogger<OutboundPiiHandler> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Block any attempt to forward raw user identity to external services.
        foreach (var header in _blockedHeaders)
        {
            if (request.Headers.Contains(header))
            {
                _logger.LogWarning("[OutboundPii] Stripped disallowed header {Header} from outbound request to {Host}",
                    header, request.RequestUri?.Host);
                request.Headers.Remove(header);
            }
        }

        // Add anonymous token so external services can correlate without PII.
        request.Headers.TryAddWithoutValidation("X-Anonymous-Token", GenerateToken());

        _audit.Log("external_api_call", resourceType: "HttpClient",
            resourceId: $"{request.Method} {request.RequestUri?.GetLeftPart(UriPartial.Path)}");

        return await base.SendAsync(request, ct);
    }

    private static string GenerateToken()
    {
        Span<byte> buf = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }
}
