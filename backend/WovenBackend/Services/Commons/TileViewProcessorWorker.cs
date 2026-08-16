using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Commons;

/// <summary>
/// Runs every 30 minutes. For users who dwelled on tiles (≥8s) in the last hour,
/// computes a weighted mean of those tile embeddings and writes it as ReceptionEmbedding
/// on the user's UserVector. This is the revealed-interest signal that feeds into
/// Commons feed scoring and (eventually) matchmaking.
/// </summary>
public class TileViewProcessorWorker : BackgroundService
{
    private const int DwellThresholdMs = 8_000;
    private const int MaxDwells = 50;
    private const int EmbeddingDims = 1536;
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TileViewProcessorWorker> _logger;

    public TileViewProcessorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<TileViewProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TileViewProcessor] Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(RunInterval, stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[TileViewProcessor] Run failed");
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        // Users who had qualifying dwells in the last RunInterval window + small buffer
        var since = DateTimeOffset.UtcNow - RunInterval - TimeSpan.FromMinutes(5);
        var activeUserIds = await db.TileViews.AsNoTracking()
            .Where(v => v.DurationMs >= DwellThresholdMs && v.ViewedAt >= since)
            .Select(v => v.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (activeUserIds.Count == 0) return;

        _logger.LogInformation("[TileViewProcessor] Processing {Count} users", activeUserIds.Count);

        var receptionUpdated = 0;
        var voicePrefUpdated = 0;
        foreach (var userId in activeUserIds)
        {
            if (await UpdateReceptionEmbeddingAsync(db, userId, ct))
                receptionUpdated++;
            if (await UpdateVoicePreferenceAsync(db, userId, ct))
                voicePrefUpdated++;
        }

        _logger.LogInformation(
            "[TileViewProcessor] Updated ReceptionEmbedding={Reception}, VoicePreference={Voice} for {Total} users",
            receptionUpdated, voicePrefUpdated, activeUserIds.Count);
    }

    private async Task<bool> UpdateReceptionEmbeddingAsync(WovenDbContext db, int userId, CancellationToken ct)
    {
        // Most-recent-first list of tile IDs from qualifying dwells
        var qualifyingTileIds = await db.TileViews.AsNoTracking()
            .Where(v => v.UserId == userId && v.DurationMs >= DwellThresholdMs)
            .OrderByDescending(v => v.ViewedAt)
            .Take(MaxDwells)
            .Select(v => v.TileId)
            .ToListAsync(ct);

        if (qualifyingTileIds.Count == 0) return false;

        // Load embeddings only for tiles that have them
        var embeddingMap = await db.Tiles.AsNoTracking()
            .Where(t => qualifyingTileIds.Contains(t.Id) && t.Embedding != null)
            .Select(t => new { t.Id, t.Embedding })
            .ToListAsync(ct);

        if (embeddingMap.Count == 0) return false;

        var lookupMap = embeddingMap.ToDictionary(e => e.Id, e => e.Embedding!);

        // Compute exponentially-weighted mean (index 0 = most recent = highest weight)
        var mean = new float[EmbeddingDims];
        double totalWeight = 0;

        for (int i = 0; i < qualifyingTileIds.Count; i++)
        {
            if (!lookupMap.TryGetValue(qualifyingTileIds[i], out var emb)) continue;

            var weight = Math.Exp(-i * 0.1); // e^(-0.1 * position)
            var span = emb.Memory.Span;
            for (int d = 0; d < EmbeddingDims && d < span.Length; d++)
                mean[d] += (float)(span[d] * weight);
            totalWeight += weight;
        }

        if (totalWeight == 0) return false;

        for (int d = 0; d < EmbeddingDims; d++)
            mean[d] = (float)(mean[d] / totalWeight);

        var userVector = await db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (userVector is null) return false;

        userVector.ReceptionEmbedding = new Vector(mean);
        userVector.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Updates UserVoicePreference with an exponentially-weighted mean of voice tile embeddings
    // the user has dwelled on that belong to other users. This is the behavioral attraction signal:
    // what voices does this user actually spend time listening to?
    private async Task<bool> UpdateVoicePreferenceAsync(WovenDbContext db, int userId, CancellationToken ct)
    {
        const int VoiceDims = 192;

        // Ordered list of recently-dwelled tile IDs (broad, we'll filter to voice below)
        var dwelledTileIds = await db.TileViews.AsNoTracking()
            .Where(v => v.UserId == userId && v.DurationMs >= DwellThresholdMs)
            .OrderByDescending(v => v.ViewedAt)
            .Take(200)
            .Select(v => v.TileId)
            .ToListAsync(ct);

        if (dwelledTileIds.Count == 0) return false;

        // Voice tiles from other users that have been embedded
        var voiceTileData = await db.Tiles.AsNoTracking()
            .Where(t => dwelledTileIds.Contains(t.Id) && t.UserId != userId
                        && t.ContentType == "voice" && t.VoiceEmbedding != null)
            .Select(t => new { t.Id, t.VoiceEmbedding })
            .ToListAsync(ct);

        if (voiceTileData.Count == 0) return false;

        // Reconstruct dwell order (most recently listened = highest weight)
        var voiceMap = voiceTileData.ToDictionary(t => t.Id, t => t.VoiceEmbedding!);
        var ordered = dwelledTileIds
            .Where(id => voiceMap.ContainsKey(id))
            .Take(50)
            .ToList();

        // Exponentially-weighted mean (same decay as ReceptionEmbedding)
        var mean = new float[VoiceDims];
        double totalWeight = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            var emb = voiceMap[ordered[i]];
            var weight = Math.Exp(-i * 0.1);
            var span = emb.Memory.Span;
            for (int d = 0; d < VoiceDims && d < span.Length; d++)
                mean[d] += (float)(span[d] * weight);
            totalWeight += weight;
        }

        if (totalWeight == 0) return false;

        for (int d = 0; d < VoiceDims; d++)
            mean[d] = (float)(mean[d] / totalWeight);

        var pref = await db.UserVoicePreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (pref == null)
        {
            db.UserVoicePreferences.Add(new UserVoicePreference
            {
                UserId              = userId,
                PreferenceEmbedding = new Vector(mean),
                YesSampleCount      = ordered.Count,
                UpdatedAt           = DateTimeOffset.UtcNow
            });
        }
        else
        {
            pref.PreferenceEmbedding = new Vector(mean);
            pref.YesSampleCount      = ordered.Count;
            pref.UpdatedAt           = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
