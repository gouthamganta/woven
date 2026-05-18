using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Analytics;

public class AnalyticsRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalyticsRetentionWorker> _logger;

    public AnalyticsRetentionWorker(IServiceScopeFactory scopeFactory, ILogger<AnalyticsRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = new DateTime(now.Year, now.Month, 1)
                .AddMonths(1)
                .AddHours(2).AddMinutes(15); // 1st of next month, 02:15 UTC (offset from TrustBatchWorker)

            var delay = nextRun - now;
            if (delay < TimeSpan.Zero) delay = TimeSpan.FromDays(28);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await AnonymizeOldEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalyticsRetention] Anonymization run failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task AnonymizeOldEventsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<ISecurityAuditService>();

        var cutoff = DateTimeOffset.UtcNow.AddMonths(-12);

        var rowsAffected = await db.AnalyticsEvents
            .Where(e => e.CreatedAt < cutoff && e.UserIdHash != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.UserIdHash, (string?)null)
                .SetProperty(e => e.SessionId, (string?)null), ct);

        _logger.LogInformation("[AnalyticsRetention] Anonymized {Count} events older than 12 months", rowsAffected);

        audit.Log("pii_access",
            service: "AnalyticsRetentionWorker",
            resourceType: "analytics_events",
            piiStripped: true);
    }
}
