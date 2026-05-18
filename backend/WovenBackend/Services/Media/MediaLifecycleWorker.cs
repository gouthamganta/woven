using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Media;

public class MediaLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaLifecycleWorker> _logger;

    public MediaLifecycleWorker(IServiceScopeFactory scopeFactory, ILogger<MediaLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MediaLifecycleWorker started (runs every 6 hours)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            await RunLifecyclePassAsync(stoppingToken);
        }
    }

    private async Task RunLifecyclePassAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MediaLifecycle] Running lifecycle pass");

        using var scope = _scopeFactory.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();

        try
        {
            var expiredUrls = await db.Database
                .SqlQueryRaw<string>(
                    "SELECT media_url FROM tiles " +
                    "WHERE is_expired = TRUE AND is_highlighted = FALSE " +
                    "AND expires_at < NOW() - INTERVAL '1 hour'")
                .ToListAsync(ct);

            if (expiredUrls.Count == 0)
            {
                _logger.LogInformation("[MediaLifecycle] No expired tile media found");
                return;
            }

            _logger.LogInformation("[MediaLifecycle] Deleting {Count} expired tile blobs", expiredUrls.Count);

            foreach (var url in expiredUrls)
            {
                try
                {
                    // URL format: https://{account}.blob.core.windows.net/tile-media/{blobPath}
                    var uri = new Uri(url);
                    var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
                    if (segments.Length < 2) continue;

                    await media.DeleteMediaAsync(segments[1], MediaContainerType.TileMedia, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[MediaLifecycle] Failed to delete blob from URL {Url}", url);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediaLifecycle] Lifecycle pass failed");
        }
    }
}
