using System.Text.Json;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Security;

public class SecurityAuditService : ISecurityAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SecurityAuditService> _logger;

    public SecurityAuditService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<SecurityAuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public void Log(
        string eventType,
        int? userId = null,
        string? service = null,
        string? resourceType = null,
        string? resourceId = null,
        string? dataType = null,
        bool piiStripped = false,
        string? ipAddress = null)
    {
        // Fire-and-forget — never surfaces to caller
        _ = Task.Run(async () =>
        {
            try
            {
                var salt = _config["Encryption:PiiSalt"] ?? "default-salt";

                var ipHash = ipAddress != null
                    ? PiiSanitizer.HashForAudit(ipAddress, salt)
                    : null;

                var details = new Dictionary<string, object?>();
                if (service != null)    details["service"] = service;
                if (dataType != null)   details["dataType"] = dataType;
                if (piiStripped)        details["piiStripped"] = true;

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

                db.SecurityAuditLogs.Add(new SecurityAuditLog
                {
                    UserId = userId,
                    EventType = eventType,
                    ResourceType = resourceType ?? "system",
                    ResourceId = resourceId ?? "n/a",
                    IpHash = ipHash,
                    DetailsJson = details.Count > 0
                        ? JsonSerializer.Serialize(details)
                        : "{}",
                    CreatedAt = DateTimeOffset.UtcNow
                });

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SecurityAudit] Failed to write audit log for event {EventType}", eventType);
            }
        });
    }
}
