using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// Nightly batch (03:50 UTC) — aggregates all MatchSignalLog events per (viewer, candidate) pair
/// into a single composite ConnectionScore [0, 1] used by ECHO's weight learning.
///
/// Formula weights are read from appsettings.json under Echo:ConnectionScore so they can be
/// tuned without redeployment and validated against the Phase 8 synthetic persona oracle.
/// </summary>
public class ConnectionScoreBatchWorker : BackgroundService
{
    private const string LockKey = "lock:connection-score-batch";
    private const string LastRunKey = "connection-score-batch:last-run";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(2);

    // Look back 90 days of signals for scoring
    private static readonly TimeSpan SignalWindow = TimeSpan.FromDays(90);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<ConnectionScoreBatchWorker> _logger;

    public ConnectionScoreBatchWorker(
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        IConfiguration config,
        ILogger<ConnectionScoreBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    private float W(string key, float fallback) =>
        _config.GetValue<float?>($"Echo:ConnectionScore:{key}") ?? fallback;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[ConnectionScore] Started — nightly run at 03:50 UTC");

        while (!ct.IsCancellationRequested)
        {
            await WaitForNextRunAsync(ct);
            if (ct.IsCancellationRequested) break;

            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
            {
                _logger.LogInformation("[ConnectionScore] Skipping — another pod holds the lock");
                continue;
            }

            var start = DateTime.UtcNow;
            _logger.LogInformation("[ConnectionScore] Starting nightly batch at {Time}", start);

            try
            {
                await RunBatchAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ConnectionScore] Batch failed");
            }
            finally
            {
                await _cache.ReleaseLockAsync(LockKey, ct);
                _logger.LogInformation("[ConnectionScore] Done in {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
        }
    }

    private static async Task WaitForNextRunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next = now.Date.AddHours(3).AddMinutes(50);
        if (now >= next) next = next.AddDays(1);
        await Task.Delay(next - now, ct);
    }

    private async Task RunBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var now = DateTime.UtcNow;
        var cutoff = now - SignalWindow;

        // Incremental processing: find pairs with new signals since last run
        var lastRunStr = await _cache.GetAsync<string>(LastRunKey, ct);
        DateTime? lastRun = lastRunStr != null && DateTime.TryParse(lastRunStr, out var dt) ? dt : null;

        List<(int ViewerId, int CandidateId)> affectedPairs;

        if (lastRun.HasValue)
        {
            // Find distinct pairs with signals created/updated since last run
            affectedPairs = await db.MatchSignalLogs.AsNoTracking()
                .Where(s => s.OccurredAt >= lastRun.Value && s.OccurredAt >= cutoff)
                .Select(s => new { s.ViewerId, s.CandidateId })
                .Distinct()
                .Select(p => ValueTuple.Create(p.ViewerId, p.CandidateId))
                .ToListAsync(ct);

            if (affectedPairs.Count == 0)
            {
                _logger.LogInformation("[ConnectionScore] No new signals since {LastRun} — nothing to update", lastRun.Value);
                return;
            }

            _logger.LogInformation("[ConnectionScore] Incremental mode: {Count} pairs affected since {LastRun}",
                affectedPairs.Count, lastRun.Value);
        }
        else
        {
            // First run or cache cleared — process all pairs in the 90-day window
            affectedPairs = await db.MatchSignalLogs.AsNoTracking()
                .Where(s => s.OccurredAt >= cutoff)
                .Select(s => new { s.ViewerId, s.CandidateId })
                .Distinct()
                .Select(p => ValueTuple.Create(p.ViewerId, p.CandidateId))
                .ToListAsync(ct);

            _logger.LogInformation("[ConnectionScore] Full scan mode: {Count} pairs in 90-day window",
                affectedPairs.Count);
        }

        if (affectedPairs.Count == 0) return;

        // For each affected pair, re-aggregate ALL signals from the 90-day window
        var aggs = await db.MatchSignalLogs.AsNoTracking()
            .Where(s => s.OccurredAt >= cutoff
                     && affectedPairs.Contains(new ValueTuple<int, int>(s.ViewerId, s.CandidateId)))
            .GroupBy(s => new { s.ViewerId, s.CandidateId, s.EventType })
            .Select(g => new
            {
                g.Key.ViewerId,
                g.Key.CandidateId,
                g.Key.EventType,
                Count    = g.Count(),
                MaxValue = g.Max(s => s.EventValue)
            })
            .ToListAsync(ct);

        if (aggs.Count == 0)
        {
            _logger.LogWarning("[ConnectionScore] Affected pairs found but no aggregates returned");
            return;
        }

        // Group by pair and compute composite score
        var byPair = aggs.GroupBy(a => (a.ViewerId, a.CandidateId));

        var scores = new List<(int ViewerId, int CandidateId, float Score)>(byPair.Count());
        foreach (var pair in byPair)
        {
            var byType = pair.ToDictionary(a => a.EventType);

            float Get(string t) => byType.TryGetValue(t, out var r) ? r.MaxValue : 0f;
            int Count(string t) => byType.TryGetValue(t, out var r) ? r.Count : 0;

            var balloonPopped  = Get(MatchSignalEventTypes.BalloonPop) > 0 ? 1f : 0f;
            var trialRequested = Get(MatchSignalEventTypes.TrialRequested) > 0 ? 1f : 0f;
            var trialAccepted  = Get(MatchSignalEventTypes.TrialAccepted) > 0 ? 1f : 0f;
            var convDepth      = Math.Min(1f, Count(MatchSignalEventTypes.MessageSent) / 20f);
            var dateAccepted   = Get(MatchSignalEventTypes.DateIdeaAccepted) > 0 ? 1f : 0f;
            var explicitFb     = Get(MatchSignalEventTypes.ExplicitFeedback); // already [0,1]
            var loveReactions  = Math.Min(1f,
                (Count(MatchSignalEventTypes.ChatNoteLove) + Count(MatchSignalEventTypes.MessageLove)) / 3f);
            // Voice signals: mutual exchange = strong vulnerability/trust indicator
            var voiceExchange  = Get(MatchSignalEventTypes.MutualVoiceExchange) > 0 ? 1f : 0f;
            var voiceCompleted = Math.Min(1f, Count(MatchSignalEventTypes.VoiceNoteListenComplete) / 2f);

            var score = W("BalloonPopped",       0.05f) * balloonPopped
                      + W("TrialRequested",      0.08f) * trialRequested   // reduced: 0.10→0.08
                      + W("TrialAccepted",       0.22f) * trialAccepted    // reduced: 0.25→0.22
                      + W("ConversationDepth",   0.20f) * convDepth
                      + W("DateAccepted",        0.15f) * dateAccepted
                      + W("ExplicitFeedback",    0.13f) * explicitFb       // reduced: 0.15→0.13
                      + W("LoveReactions",       0.08f) * loveReactions    // reduced: 0.10→0.08
                      + W("VoiceExchange",       0.06f) * voiceExchange    // NEW
                      + W("VoiceCompleted",      0.03f) * voiceCompleted;  // NEW — total stays 1.0

            scores.Add((pair.Key.ViewerId, pair.Key.CandidateId, Math.Clamp(score, 0f, 1f)));
        }

        // Upsert via raw SQL — avoids loading all existing rows into memory.
        // INSERT ... ON CONFLICT DO UPDATE is an atomic upsert in PostgreSQL.
        // Chunks of 500 to stay well inside parameter limits.
        const int chunkSize = 500;
        int upserted = 0;

        for (int i = 0; i < scores.Count; i += chunkSize)
        {
            var chunk = scores.GetRange(i, Math.Min(chunkSize, scores.Count - i));

            // Build a VALUES list: ($1,$2,$3,$4), ($5,$6,$7,$8), ...
            var sb    = new System.Text.StringBuilder();
            var param = new List<object>();
            int p     = 0;

            sb.Append(
                @"INSERT INTO ""ConnectionScores"" (""ViewerId"", ""CandidateId"", ""Score"", ""ComputedAt"") VALUES ");

            for (int j = 0; j < chunk.Count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append($"({{{p}}},{{{p+1}}},{{{p+2}}},{{{p+3}}})");
                param.Add(chunk[j].ViewerId);
                param.Add(chunk[j].CandidateId);
                param.Add((double)chunk[j].Score);
                param.Add(now);
                p += 4;
            }

            sb.Append(
                @" ON CONFLICT (""ViewerId"", ""CandidateId"") " +
                @"DO UPDATE SET ""Score""=EXCLUDED.""Score"", ""ComputedAt""=EXCLUDED.""ComputedAt""");

            await db.Database.ExecuteSqlRawAsync(sb.ToString(), param.ToArray(), ct);
            upserted += chunk.Count;
        }

        // Store completion timestamp for next incremental run
        await _cache.SetAsync(LastRunKey, now.ToString("O"), TimeSpan.FromDays(7), ct);

        _logger.LogInformation("[ConnectionScore] Upserted {Total} scores via bulk SQL | Mode={Mode}",
            upserted, lastRun.HasValue ? "incremental" : "full");
    }
}
