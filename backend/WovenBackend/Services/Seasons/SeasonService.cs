using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Services.Seasons;

public class SeasonService : ISeasonService
{
    private readonly WovenDbContext _db;
    private readonly INotificationService _notify;
    private readonly ICacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<SeasonService> _logger;

    public SeasonService(
        WovenDbContext db,
        INotificationService notify,
        ICacheService cache,
        IServiceScopeFactory scopeFactory,
        IAnalyticsService analytics,
        ILogger<SeasonService> logger)
    {
        _db = db;
        _notify = notify;
        _cache = cache;
        _scopeFactory = scopeFactory;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<CurrentSeasonResult> GetCurrentSeasonAsync(int userId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var season = await _db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today, ct);

        if (season == null)
            return new CurrentSeasonResult(null, "not_started", 0);

        var responseCount = await _db.UserSeasonResponses.AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.SeasonId == season.Id, ct);

        var status = responseCount > 0 ? "answered" : "unanswered";

        var info = new SeasonInfo(season.Id, season.SeasonNumber, season.StartDate, season.EndDate, season.PromptText);
        return new CurrentSeasonResult(info, status, responseCount);
    }

    public async Task SubmitSeasonResponsesAsync(int userId, List<SeasonResponseRequest> responses, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var season = await _db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today, ct)
            ?? throw new InvalidOperationException("NO_ACTIVE_SEASON");

        // Load existing responses for this user+season in one query
        var existingMap = await _db.UserSeasonResponses
            .Where(r => r.UserId == userId && r.SeasonId == season.Id)
            .ToDictionaryAsync(r => r.PillarId, ct);

        foreach (var req in responses)
        {
            if (existingMap.TryGetValue(req.PillarId, out var existing))
            {
                existing.Response = req.Response;
                existing.QuestionId = req.QuestionId;
                existing.RespondedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _db.UserSeasonResponses.Add(new UserSeasonResponse
                {
                    UserId = userId,
                    SeasonId = season.Id,
                    PillarId = req.PillarId,
                    QuestionId = req.QuestionId,
                    Response = req.Response,
                    RespondedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.SeasonResponseSubmitted,
            new { seasonNumber = season.SeasonNumber });

        // Fire-and-forget: invalidate pillar embedding cache + best-effort re-embed
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            try
            {
                var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
                await cache.DeleteAsync(CacheKeys.PillarEmbedding(userId));

                var vectorBuilder = scope.ServiceProvider.GetRequiredService<IUserVectorBuilder>();
                await vectorBuilder.BuildAndSaveV1Async(userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Seasons] Post-submit re-embed failed for user {UserId}", userId);
            }
        });

        // Notify user
        _ = Task.Run(async () =>
        {
            try { await _notify.SeasonResponseSubmittedAsync(userId); }
            catch (Exception ex) { _logger.LogWarning(ex, "[Seasons] SeasonResponseSubmitted notification failed for user {UserId}", userId); }
        });
    }

    public async Task<string?> GetSignaturePromptAsync(int userId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var season = await _db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today, ct);
        return season?.PromptText;
    }
}
