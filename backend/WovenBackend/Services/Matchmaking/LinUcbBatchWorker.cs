using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Embeddings;

namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// Nightly at 04:20 UTC — updates per-user LinUCB bandit models from accumulated
/// ConnectionScores.  For each user with ≥1 scored candidate, builds context vectors
/// (8 pillar + 16 behavioural fingerprint) and calls LinUcbService.UpdateAsync
/// using Sherman-Morrison so A_inv stays invertible without an O(d³) solve.
/// </summary>
public class LinUcbBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<LinUcbBatchWorker> _logger;

    private const string LockKey = "lock:linucb-batch";

    public LinUcbBatchWorker(
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        ILogger<LinUcbBatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache        = cache;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now    = DateTime.UtcNow;
            var target = DateTime.UtcNow.Date.AddHours(4).AddMinutes(20);
            if (target <= now) target = target.AddDays(1);

            await Task.Delay(target - now, stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            await RunAsync(stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        if (!await _cache.AcquireLockAsync(LockKey, TimeSpan.FromMinutes(30), ct))
        {
            _logger.LogInformation("[LinUCBBatch] Lock held by another pod — skipping");
            return;
        }

        try
        {
            _logger.LogInformation("[LinUCBBatch] Starting LinUCB model update pass");

            using var scope = _scopeFactory.CreateScope();
            var db          = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
            var linUcb      = scope.ServiceProvider.GetRequiredService<ILinUcbService>();
            var fingerprints = scope.ServiceProvider.GetRequiredService<IBehavioralFingerprintService>();

            // All distinct viewer IDs that have at least one connection score
            var viewerIds = await db.ConnectionScores.AsNoTracking()
                .Select(c => c.ViewerId)
                .Distinct()
                .ToListAsync(ct);

            _logger.LogInformation("[LinUCBBatch] Processing {Count} users", viewerIds.Count);

            int updated = 0;

            foreach (var userId in viewerIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await UpdateUserAsync(db, linUcb, fingerprints, userId, ct);
                    updated++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LinUCBBatch] Failed for user {UserId}", userId);
                }
            }

            _logger.LogInformation("[LinUCBBatch] Completed — {Updated}/{Total} users updated", updated, viewerIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LinUCBBatch] Fatal error");
        }
        finally
        {
            await _cache.ReleaseLockAsync(LockKey, ct);
        }
    }

    private static async Task UpdateUserAsync(
        WovenDbContext db,
        ILinUcbService linUcb,
        IBehavioralFingerprintService fingerprints,
        int userId,
        CancellationToken ct)
    {
        // Load all connection scores for this viewer (reward labels)
        var scores = await db.ConnectionScores.AsNoTracking()
            .Where(c => c.ViewerId == userId)
            .Select(c => new { c.CandidateId, c.Score })
            .ToListAsync(ct);

        if (scores.Count == 0) return;

        var candidateIds = scores.Select(s => s.CandidateId).ToList();

        // Load candidate pillar embeddings (latest version)
        var pillarVecs = await db.UserVectors.AsNoTracking()
            .Where(v => candidateIds.Contains(v.UserId) && v.PillarEmbedding != null)
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .Select(v => new { v.UserId, v.PillarEmbedding })
            .ToListAsync(ct);

        // Load candidate behavioral fingerprints
        var fpRows = await db.UserBehavioralFingerprints.AsNoTracking()
            .Where(f => candidateIds.Contains(f.UserId))
            .Select(f => new { f.UserId, f.VectorJson })
            .ToListAsync(ct);

        var pillarMap = pillarVecs.ToDictionary(v => v.UserId, v => v.PillarEmbedding!.ToArray());
        var fpMap     = fpRows.ToDictionary(
            f => f.UserId,
            f => DeserializeFloats(f.VectorJson));

        // Build (context, reward) observations
        var observations = new List<(float[] Context, float Reward)>(scores.Count);
        foreach (var s in scores)
        {
            pillarMap.TryGetValue(s.CandidateId, out var pillar);
            fpMap.TryGetValue(s.CandidateId, out var fp);
            var context = LinUcbService.BuildContext(pillar, fp);
            observations.Add((context, s.Score));
        }

        await linUcb.UpdateAsync(userId, observations, ct);
    }

    private static float[]? DeserializeFloats(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<float[]>(json); }
        catch { return null; }
    }
}
