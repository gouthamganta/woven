using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Tiles;

public class TileExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TileExpiryWorker> _logger;

    public TileExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<TileExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TileExpiryWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TileExpiryWorker failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ExpireOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var now = DateTimeOffset.UtcNow;

        var expired = await db.Tiles
            .Where(t => !t.IsExpired && t.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var t in expired)
            t.IsExpired = true;

        await db.SaveChangesAsync(ct);

        // MediaLifecycleWorker handles blob cleanup for non-highlighted tiles (Phase 1D).
        _logger.LogInformation("TileExpiryWorker marked {Count} tiles expired", expired.Count);
    }
}
