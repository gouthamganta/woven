using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Security;

public class SecurityAuditCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityAuditCleanupWorker> _logger;

    public SecurityAuditCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SecurityAuditCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SleepUntilSunday5AmUtcAsync(ct);
            if (ct.IsCancellationRequested) break;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
                var deleted = await db.SecurityAuditLogs
                    .Where(l => l.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(ct);

                _logger.LogInformation(
                    "[SecurityAuditCleanup] Deleted {Count} audit log rows older than 90 days", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SecurityAuditCleanup] Cleanup failed");
            }
        }
    }

    private static async Task SleepUntilSunday5AmUtcAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilSunday == 0 && now.Hour >= 5) daysUntilSunday = 7;
        var next = now.Date.AddDays(daysUntilSunday).AddHours(5);
        var delay = next - now;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }
}
