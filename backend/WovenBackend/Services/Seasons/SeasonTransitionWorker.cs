using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Seasons;

public class SeasonTransitionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notify;
    private readonly ILogger<SeasonTransitionWorker> _logger;

    public SeasonTransitionWorker(
        IServiceScopeFactory scopeFactory,
        INotificationService notify,
        ILogger<SeasonTransitionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _notify = notify;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SleepUntil1AmUtcAsync(ct);
            if (ct.IsCancellationRequested) break;

            try
            {
                await CheckAndTransitionSeasonAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SeasonWorker] Season transition check failed");
            }
        }
    }

    private static async Task SleepUntil1AmUtcAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next1Am = now.Date.AddDays(now.Hour >= 1 ? 1 : 0).AddHours(1);
        var delay = next1Am - now;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }

    private async Task CheckAndTransitionSeasonAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
        var openAi = scope.ServiceProvider.GetRequiredService<IOpenAiResilientClient>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Find the last season
        var lastSeason = await db.Seasons.AsNoTracking()
            .OrderByDescending(s => s.SeasonNumber)
            .FirstOrDefaultAsync(ct);

        // No transition needed if current season is still active
        if (lastSeason != null && lastSeason.EndDate >= today)
            return;

        _logger.LogInformation("[SeasonWorker] Season transition triggered — creating next season");

        // Generate prompt via OpenAI
        var promptText = await GenerateSeasonPromptAsync(openAi, ct);

        var nextNumber = (lastSeason?.SeasonNumber ?? 0) + 1;
        var newSeason = new Season
        {
            SeasonNumber = nextNumber,
            StartDate = today,
            EndDate = today.AddDays(21),
            PromptText = promptText,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Seasons.Add(newSeason);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[SeasonWorker] Created Season {Number}: {Prompt}", nextNumber, promptText);

        // Notify all COMPLETE users
        var activeUserIds = await db.Users.AsNoTracking()
            .Where(u => u.ProfileStatus == ProfileStatus.COMPLETE)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in activeUserIds)
        {
            try
            {
                await _notify.NewSeasonStartedAsync(userId, nextNumber, promptText, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SeasonWorker] NewSeasonStarted notification failed for user {UserId}", userId);
            }
        }
    }

    private async Task<string> GenerateSeasonPromptAsync(IOpenAiResilientClient openAi, CancellationToken ct)
    {
        const string aiPrompt = "Generate a short introspective question for a dating app season. Max 200 chars. No clichés. Return only the question text, nothing else.";

        var result = await openAi.ExecuteAsync("season_prompt", aiPrompt, useJsonMode: false, ct);

        if (string.IsNullOrWhiteSpace(result))
        {
            _logger.LogWarning("[SeasonWorker] OpenAI returned empty prompt — using fallback");
            return "What matters most to you in a connection right now?";
        }

        // Trim to 200 chars, strip quotes
        var cleaned = result.Trim().Trim('"').Trim();
        return cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }
}
