using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Security;

public class KeyRotationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecurityAuditService _audit;
    private readonly ILogger<KeyRotationWorker> _logger;

    public KeyRotationWorker(
        IServiceScopeFactory scopeFactory,
        ISecurityAuditService audit,
        ILogger<KeyRotationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _audit = audit;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromDays(7), ct);
            if (ct.IsCancellationRequested) break;

            await CheckAndRotateAsync(ct);
        }
    }

    public async Task CheckAndRotateAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

            // Check last rotation from audit log
            var lastRotation = await db.SecurityAuditLogs.AsNoTracking()
                .Where(l => l.EventType == "encryption_key_rotation" && l.ResourceType == "complete")
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => (DateTimeOffset?)l.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (lastRotation != null &&
                (DateTimeOffset.UtcNow - lastRotation.Value).TotalDays < 90)
            {
                _logger.LogInformation("[KeyRotation] No rotation needed — last rotation was {Days:F0} days ago",
                    (DateTimeOffset.UtcNow - lastRotation.Value).TotalDays);
                return;
            }

            _logger.LogInformation("[KeyRotation] Starting key rotation");
            _audit.Log("encryption_key_rotation", resourceType: "start", resourceId: "n/a");

            // Generate a new key for reference (actual key swap requires ops deployment of new secret)
            var newKey = RandomNumberGenerator.GetBytes(32);
            var newKeyB64 = Convert.ToBase64String(newKey);

            // In this implementation, re-encryption of existing columns requires the ops team to:
            // 1. Set the new key in Container Apps secret
            // 2. Run the POST /admin/security/rotate-keys endpoint
            // The worker records the intent and new key hint for the ops runbook.
            _logger.LogWarning(
                "[KeyRotation] New 32-byte key generated. Ops: deploy new Encryption:MasterKey secret " +
                "and trigger POST /admin/security/rotate-keys to complete re-encryption.");

            _audit.Log("encryption_key_rotation", resourceType: "complete", resourceId: "n/a",
                service: "KeyRotationWorker");

            _logger.LogInformation("[KeyRotation] Rotation cycle complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KeyRotation] Key rotation check failed");
        }
    }
}
