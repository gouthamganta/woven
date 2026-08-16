using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Coaching;

internal record CoachingSignalStat(string EventType, int Count, double AvgValue);

/// <summary>
/// Runs Wednesday 18:00 UTC. Generates a private, trusted-friend AI coaching summary
/// per qualified user using ONLY permitted behavioral signals.
///
/// Eligibility: account ≥14 days, ≥3 deck interactions in prior 7 days, not opted out,
/// no existing CoachingSummary row for this week_start_date.
///
/// Model: gpt-4.1-mini, temperature 0.8. Max 3–5 sentences, plain text, no headers.
/// If model returns "SUPPRESS" or <50 chars → no summary written.
///
/// 90-day TTL: stale rows deleted in preamble.
/// </summary>
public class CoachingSummaryWorker : BackgroundService
{
    private const string LockKey = "lock:coaching-summary-batch";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(4);
    private const string OpenAiEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string Model = "gpt-4.1-mini";
    private const float Temperature = 0.8f;
    private const int MinSummaryChars = 50;
    private const int MinDeckInteractions = 3;
    private const int MinAccountAgeDays = 14;

    private const string SystemPrompt =
        "You are a warm, perceptive friend who happens to understand how dating apps work. " +
        "You know this person has been putting themselves out there this week. " +
        "Your job is to reflect back what you noticed — genuine, specific, encouraging without being hollow. " +
        "Never mention numbers directly. Never compare them to others. " +
        "Never imply they should change who they are. " +
        "If there is nothing genuinely positive or actionable to say, return the string \"SUPPRESS\".";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ICacheService _cache;
    private readonly ILogger<CoachingSummaryWorker> _logger;

    public CoachingSummaryWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ICacheService cache,
        ILogger<CoachingSummaryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory  = httpFactory;
        _config       = config;
        _cache        = cache;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await SleepUntilWednesdayAsync(ct);
            if (ct.IsCancellationRequested) break;

            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
            {
                _logger.LogInformation("[CoachingSummary] Skipping — another pod holds the lock");
                continue;
            }

            var start = DateTime.UtcNow;
            _logger.LogInformation("[CoachingSummary] Starting batch at {Time}", start);

            try { await RunAsync(ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CoachingSummary] Batch failed after {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
            finally
            {
                await _cache.ReleaseLockAsync(LockKey, ct);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[CoachingSummary] OpenAI:ApiKey not configured — skipping");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var now        = DateTimeOffset.UtcNow;
        var weekStart  = DateOnly.FromDateTime(now.DateTime).AddDays(-(int)now.DayOfWeek);
        var cutoffDate = now.AddDays(-MinAccountAgeDays);
        var signalFrom = now.AddDays(-7);

        // Cleanup: delete coaching summaries older than 90 days
        await db.CoachingSummaries
            .Where(c => c.CreatedAt < now.AddDays(-90))
            .ExecuteDeleteAsync(ct);

        // Eligible users: account ≥14 days, not opted out
        var eligibleUserIds = await db.Users.AsNoTracking()
            .Where(u => u.CreatedAt <= cutoffDate.DateTime && !u.CoachingOptedOut)
            .Select(u => u.Id)
            .ToListAsync(ct);

        // Filter: ≥3 deck interactions in prior 7 days
        var activeUserIds = await db.DailyInteractions.AsNoTracking()
            .Where(d => d.DateUtc >= DateOnly.FromDateTime(signalFrom.DateTime) &&
                        eligibleUserIds.Contains(d.UserId))
            .GroupBy(d => d.UserId)
            .Where(g => g.Sum(d => d.TotalUsed) >= MinDeckInteractions)
            .Select(g => g.Key)
            .ToListAsync(ct);

        // Filter: no existing summary for this week
        var alreadySummarized = await db.CoachingSummaries.AsNoTracking()
            .Where(c => c.WeekStartDate == weekStart && activeUserIds.Contains(c.UserId))
            .Select(c => c.UserId)
            .ToListAsync(ct);

        var toProcess = activeUserIds.Except(alreadySummarized).ToList();
        _logger.LogInformation("[CoachingSummary] Processing {Count} eligible users", toProcess.Count);

        int written = 0;
        foreach (var userId in toProcess)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var innerScope = _scopeFactory.CreateScope();
                var innerDb = innerScope.ServiceProvider.GetRequiredService<WovenDbContext>();
                var saved = await ProcessUserAsync(innerDb, apiKey, userId, weekStart, signalFrom, now, ct);
                if (saved) written++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CoachingSummary] Failed for user {UserId}", userId);
            }
        }

        _logger.LogInformation("[CoachingSummary] Done — {Written}/{Total} summaries written",
            written, toProcess.Count);
    }

    private async Task<bool> ProcessUserAsync(
        WovenDbContext db,
        string apiKey,
        int userId,
        DateOnly weekStart,
        DateTimeOffset signalFrom,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Aggregate permitted signals for the prior 7 days
        var deckInteractions = await db.DailyInteractions.AsNoTracking()
            .Where(d => d.UserId == userId && d.DateUtc >= DateOnly.FromDateTime(signalFrom.DateTime))
            .SumAsync(d => d.TotalUsed, ct);

        var signalCounts = await db.MatchSignalLogs.AsNoTracking()
            .Where(s => s.ViewerId == userId && s.OccurredAt >= signalFrom)
            .GroupBy(s => s.EventType)
            .Select(g => new CoachingSignalStat(g.Key, g.Count(), (double)g.Average(s => s.EventValue)))
            .ToListAsync(ct);

        var signalMap = signalCounts.ToDictionary(s => s.EventType, s => s);

        // Build interpretedNarrative — deterministic C# paragraph, never raw numbers
        var narrative = BuildNarrative(userId, deckInteractions, signalMap);

        // Call GPT-4.1-mini
        var summaryText = await GenerateSummaryAsync(apiKey, narrative, ct);

        // Suppress if model says so or text is too short
        if (string.IsNullOrWhiteSpace(summaryText) ||
            summaryText.Trim().Equals("SUPPRESS", StringComparison.OrdinalIgnoreCase) ||
            summaryText.Trim().Length < MinSummaryChars)
        {
            _logger.LogInformation("[CoachingSummary] Suppressed for user {UserId}", userId);
            return false;
        }

        db.CoachingSummaries.Add(new CoachingSummary
        {
            UserId               = userId,
            WeekStartDate        = weekStart,
            SummaryText          = summaryText.Trim(),
            InterpretedNarrative = narrative,
            DeliveredAt          = now
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Signal aggregation (permitted signals only — see spec) ────────────────

    private static string BuildNarrative(
        int userId,
        int deckInteractions,
        Dictionary<string, CoachingSignalStat> signalMap)
    {
        var sb = new StringBuilder();
        sb.Append("This person has been active on the app this week. ");

        // Deck engagement (bucketed)
        if (deckInteractions >= 10)
            sb.Append("They showed up consistently, reviewing a solid set of potential connections. ");
        else if (deckInteractions >= 3)
            sb.Append("They engaged with their daily deck, taking their time with the profiles they saw. ");

        // Conversations started (TimeToFirstMessageMs = they initiated)
        var firstMessages = GetCount(signalMap, MatchSignalEventTypes.TimeToFirstMessageMs);
        if (firstMessages > 1)
            sb.Append("When they felt a connection, they were willing to make the first move. ");
        else if (firstMessages == 1)
            sb.Append("They took the step of sending the first message in at least one conversation. ");

        // Conversation depth (TrialMessageCount as proxy for chat depth)
        var avgMessages = GetAvg(signalMap, MatchSignalEventTypes.TrialMessageCount);
        if (avgMessages >= 15)
            sb.Append("Their conversations have gone deep — there's real exchange happening, not just surface pleasantries. ");
        else if (avgMessages >= 5)
            sb.Append("They're having real conversations, not just exchanging pleasantries. ");

        // Date ideas (positive signal of intent)
        var dateIdeasAccepted = GetCount(signalMap, MatchSignalEventTypes.DateIdeaAccepted);
        if (dateIdeasAccepted > 0)
            sb.Append("They showed genuine interest in meeting — that takes courage. ");

        // Voice exchange (vulnerability signal)
        var voiceExchanges = GetCount(signalMap, MatchSignalEventTypes.MutualVoiceExchange);
        if (voiceExchanges > 0)
            sb.Append("Voice notes were exchanged — something about that connection felt worth hearing in a real voice. ");

        // Trial continuation (positive intent)
        var trialsAccepted = GetCount(signalMap, MatchSignalEventTypes.TrialAccepted);
        if (trialsAccepted > 0)
            sb.Append("They chose to continue at least one trial — they're not just going through motions. ");

        return sb.ToString().Trim();
    }

    private static int GetCount(Dictionary<string, CoachingSignalStat> map, string key)
        => map.TryGetValue(key, out var s) ? s.Count : 0;

    private static double GetAvg(Dictionary<string, CoachingSignalStat> map, string key)
        => map.TryGetValue(key, out var s) ? s.AvgValue : 0.0;

    // ── GPT-4.1-mini call ─────────────────────────────────────────────────────

    private async Task<string?> GenerateSummaryAsync(
        string apiKey, string narrative, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model       = Model,
            temperature = Temperature,
            max_tokens  = 200,
            messages    = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user",   content = narrative }
            }
        });

        try
        {
            using var http    = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CoachingSummary] OpenAI call failed");
            return null;
        }
    }

    // ── Schedule ─────────────────────────────────────────────────────────────

    private static async Task SleepUntilWednesdayAsync(CancellationToken ct)
    {
        var now      = DateTime.UtcNow;
        var daysUntilWed = ((int)DayOfWeek.Wednesday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilWed == 0 && now.Hour >= 18) daysUntilWed = 7;
        var nextRun  = now.Date.AddDays(daysUntilWed).AddHours(18);
        var delay    = nextRun - now;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }
}
