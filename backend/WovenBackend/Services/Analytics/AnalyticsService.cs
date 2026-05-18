using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Analytics;

public class AnalyticsService : IAnalyticsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly string _hashSalt;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        IConfiguration configuration,
        ILogger<AnalyticsService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _hashSalt = configuration["Analytics:HashSalt"] ?? "woven-analytics-salt-v1";
        _logger = logger;
    }

    public Task TrackAsync(int? userId, string? sessionId, string eventType, object? properties = null, CancellationToken ct = default)
    {
        var userIdHash = userId.HasValue
            ? PiiSanitizer.HashForAudit(userId.Value.ToString(), _hashSalt)
            : null;

        var propsJson = properties != null
            ? JsonSerializer.Serialize(properties)
            : null;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

                var resolvedSession = sessionId;
                if (resolvedSession == null && userId.HasValue)
                    resolvedSession = await GetOrCreateSessionIdAsync(userId.Value, CancellationToken.None);

                db.AnalyticsEvents.Add(new AnalyticsEvent
                {
                    UserIdHash = userIdHash,
                    SessionId = resolvedSession,
                    EventType = eventType,
                    Properties = propsJson,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Analytics] TrackAsync failed for event {EventType}", eventType);
            }
        });

        return Task.CompletedTask;
    }

    public async Task<string> GetOrAssignVariantAsync(int userId, string experimentId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var existing = await db.AbAssignments.AsNoTracking()
            .Where(a => a.UserId == userId && a.ExperimentId == experimentId)
            .Select(a => a.Variant)
            .FirstOrDefaultAsync(ct);

        if (existing != null) return existing;

        var experiment = await db.AbExperiments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == experimentId && e.IsActive, ct);

        if (experiment == null) return "control";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString() + experimentId));
        var variant = bytes[0] % 2 == 0 ? "control" : "treatment";

        try
        {
            db.AbAssignments.Add(new AbAssignment
            {
                UserId = userId,
                ExperimentId = experimentId,
                Variant = variant
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Race condition: another process already inserted — re-read
            var raced = await db.AbAssignments.AsNoTracking()
                .Where(a => a.UserId == userId && a.ExperimentId == experimentId)
                .Select(a => a.Variant)
                .FirstOrDefaultAsync(ct);
            if (raced != null) return raced;
        }

        _ = TrackAsync(userId, null, AnalyticsEvents.AbExperimentAssigned,
            new { experimentId, variant }, ct);

        return variant;
    }

    public async Task TrackAbConversionAsync(int userId, string experimentId, string conversionType, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var assignment = await db.AbAssignments.AsNoTracking()
            .Where(a => a.UserId == userId && a.ExperimentId == experimentId)
            .Select(a => new { a.Variant })
            .FirstOrDefaultAsync(ct);

        if (assignment == null) return;

        db.AbConversions.Add(new AbConversion
        {
            UserId = userId,
            ExperimentId = experimentId,
            ConversionType = conversionType
        });

        await db.SaveChangesAsync(ct);

        _ = TrackAsync(userId, null, AnalyticsEvents.AbConversion,
            new { experimentId, variant = assignment.Variant, conversionType }, ct);
    }

    private async Task<string?> GetOrCreateSessionIdAsync(int userId, CancellationToken ct)
    {
        try
        {
            var key = $"analytics:session:{userId}";
            var existing = await _cache.GetAsync<string>(key, ct);
            if (existing != null) return existing;

            var newSessionId = Guid.NewGuid().ToString("N");
            await _cache.SetAsync(key, newSessionId, TimeSpan.FromHours(2), ct);
            return newSessionId;
        }
        catch
        {
            return null;
        }
    }
}
