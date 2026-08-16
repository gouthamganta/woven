using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Commons;


public class CommonsFeedService : ICommonsFeedService
{
    private const int DailyTileCap = 100;
    private const int PageSize = 20;
    private const int FeedPoolSize = 200;       // max tiles scored per session
    private const double ResonantThreshold = 0.65;
    private const double ResonantFraction = 0.70;

    private const int DwellSignalThresholdMs = 8000;

    private readonly WovenDbContext _db;
    private readonly ICacheService _cache;
    private readonly IMatchSignalService _signals;
    private readonly ILogger<CommonsFeedService> _logger;

    public CommonsFeedService(WovenDbContext db, ICacheService cache, IMatchSignalService signals, ILogger<CommonsFeedService> logger)
    {
        _db = db;
        _cache = cache;
        _signals = signals;
        _logger = logger;
    }

    // -------------------------------------------------------
    // GetFeedAsync
    // -------------------------------------------------------
    public async Task<CommonsFeedResult> GetFeedAsync(
        int userId, int page, Guid sessionId, CancellationToken ct = default)
    {
        var sessionKey = sessionId.ToString("N")[..8];
        var cacheKey = $"commons:feed:{userId}:{sessionKey}";

        // 1. Redis cache check — shared for all pages in a session
        var cached = await _cache.GetAsync<List<CommonsFeedTile>>(cacheKey, ct);
        if (cached != null)
        {
            var energyDepleted = await IsEnergyDepletedAsync(userId, ct);
            return new CommonsFeedResult(PageSlice(cached, page), energyDepleted);
        }

        // 2. Energy gate
        if (await IsEnergyDepletedAsync(userId, ct))
        {
            _logger.LogInformation("[Commons] Energy depleted for user {UserId}", userId);
            return new CommonsFeedResult(new List<CommonsFeedTile>(), EnergyDepleted: true);
        }

        // 3. Compute feed from DB
        var feed = await ComputeFeedAsync(userId, ct);

        // 4. Cache 2 hours
        await _cache.SetAsync(cacheKey, feed, TimeSpan.FromHours(2), ct);

        return new CommonsFeedResult(PageSlice(feed, page), EnergyDepleted: false);
    }

    // -------------------------------------------------------
    // RecordViewAsync
    // -------------------------------------------------------
    public async Task RecordViewAsync(int userId, Guid tileId, int? durationMs, CancellationToken ct = default)
    {
        // Insert view event
        _db.TileViews.Add(new TileView
        {
            UserId = userId,
            TileId = tileId,
            ViewedAt = DateTimeOffset.UtcNow,
            DurationMs = durationMs
        });
        await _db.SaveChangesAsync(ct);

        // Dwell signal: only for meaningful dwells on other users' tiles
        if (durationMs >= DwellSignalThresholdMs)
        {
            var tile = await _db.Tiles.AsNoTracking()
                .Where(t => t.Id == tileId)
                .Select(t => new { t.UserId, t.ContentType })
                .FirstOrDefaultAsync(ct);

            if (tile != null && tile.UserId != userId)
            {
                var eventType = tile.ContentType == "voice"
                    ? MatchSignalEventTypes.VoiceDwell
                    : MatchSignalEventTypes.TileDwell;
                try { await _signals.RecordAsync(userId, tile.UserId, eventType, durationMs.Value, ct: ct); }
                catch { /* non-critical */ }
            }
        }

        // Increment Redis energy counter; write-through to DB
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var redisKey = EnergyRedisKey(userId, today);
        var ttl = SecondsUntilEndOfUtcDay();

        var newCount = await _cache.IncrementAsync(redisKey, TimeSpan.FromSeconds(ttl), ct);

        // Write-through to user_energy_meter (upsert via EF)
        var row = await _db.UserEnergyMeters
            .FirstOrDefaultAsync(m => m.UserId == userId && m.DateUtc == today, ct);

        if (row == null)
        {
            _db.UserEnergyMeters.Add(new UserEnergyMeter
            {
                UserId = userId,
                DateUtc = today,
                TilesViewed = (int)newCount
            });
        }
        else
        {
            row.TilesViewed = (int)newCount;
        }

        await _db.SaveChangesAsync(ct);
    }

    // -------------------------------------------------------
    // RefreshFeedAsync
    // -------------------------------------------------------
    public async Task RefreshFeedAsync(int userId, CancellationToken ct = default)
    {
        // Invalidate all session cache keys for this user by deleting the known pattern.
        // Since we can't enumerate Redis keys easily, we ask the caller to include
        // the sessionId — on refresh the frontend generates a new sessionId, which
        // naturally produces a cache miss and recomputes the feed.
        // Here we do a best-effort delete of any key if caller passes Guid.Empty.
        var cacheKey = $"commons:feed:{userId}:00000000";
        await _cache.DeleteAsync(cacheKey, ct);
        _logger.LogInformation("[Commons] Feed cache invalidated for user {UserId}", userId);
    }

    // -------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------

    private async Task<List<CommonsFeedTile>> ComputeFeedAsync(int userId, CancellationToken ct)
    {
        // Viewer's full vector — all 5 scoring dimensions
        var viewerVector = await _db.UserVectors.AsNoTracking()
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .Select(v => new { v.PillarScoresJson, v.VectorJson, v.ReceptionEmbedding, v.ExpressionEmbedding, v.PreferenceEmbedding })
            .FirstOrDefaultAsync(ct);

        // Blocked user IDs (both directions)
        var blockedIds = await _db.Blocks.AsNoTracking()
            .Where(b => b.BlockerId == userId)
            .Select(b => b.BlockedId)
            .Union(_db.Blocks.Where(b => b.BlockedId == userId).Select(b => b.BlockerId))
            .ToListAsync(ct);

        // Tiles already viewed today
        var todayStart = DateTimeOffset.UtcNow.Date;
        var viewedTileIds = await _db.TileViews.AsNoTracking()
            .Where(v => v.UserId == userId && v.ViewedAt >= todayStart)
            .Select(v => v.TileId)
            .Distinct()
            .ToListAsync(ct);

        // Eligible tiles: moderated, not expired, not self, not blocked, not viewed today
        var tiles = await _db.Tiles.AsNoTracking()
            .Where(t =>
                t.IsModerated &&
                !t.IsExpired &&
                t.UserId != userId &&
                !blockedIds.Contains(t.UserId) &&
                !viewedTileIds.Contains(t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .Take(FeedPoolSize * 3) // over-fetch; will prune after scoring
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.ContentType,
                t.ContentText,
                t.MediaUrl,
                t.CreatedAt,
                t.Embedding
            })
            .ToListAsync(ct);

        if (tiles.Count == 0)
            return new List<CommonsFeedTile>();

        // Batch-load owner vectors — all dimensions needed for scoring
        var ownerIds = tiles.Select(t => t.UserId).Distinct().ToList();
        var rawVectors = await _db.UserVectors.AsNoTracking()
            .Where(v => ownerIds.Contains(v.UserId))
            .Select(v => new { v.UserId, v.Version, v.PillarScoresJson, v.VectorJson, v.ExpressionEmbedding })
            .ToListAsync(ct);

        var ownerVectors = rawVectors
            .GroupBy(v => v.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => v.Version).First());

        // Batch-load CF scores for tile owners (tertiary ranking signal)
        var cfScoreMap = await _db.CfScores.AsNoTracking()
            .Where(c => c.UserId == userId && ownerIds.Contains(c.CandidateId))
            .ToDictionaryAsync(c => c.CandidateId, c => c.Score, ct);

        // 5-component similarity — weights are renormalized to whichever components are available.
        // Components ranked by signal strength; all are gender-blind except intent tags (capped at 0.10).
        //   pillar (0.28)     — values alignment from onboarding pillar scores (float cosine, more precise than 8-dim embedding)
        //   reception (0.32)  — what viewer dwells on vs this tile's content (revealed behavioral taste)
        //   expression (0.18) — what viewer posts vs what owner posts (creative wavelength)
        //   preference (0.12) — viewer's ChatNote preferences vs tile content (stated attraction patterns)
        //   intent (0.10)     — intent tag Jaccard; kept low so gender-correlated tags can't dominate
        var viewerPillarScores = ParsePillarScores(viewerVector?.PillarScoresJson);
        var viewerIntentTags   = ExtractIntentTags(viewerVector?.VectorJson);

        var scored = new List<(CommonsFeedTile Tile, double CombinedScore, double CfScore)>(tiles.Count);
        foreach (var t in tiles)
        {
            ownerVectors.TryGetValue(t.UserId, out var owner);
            var ownerPillarScores = ParsePillarScores(owner?.PillarScoresJson);
            var ownerIntentTags   = ExtractIntentTags(owner?.VectorJson);

            double sim = 0, totalW = 0;

            // 1. Pillar scores (always present with neutral fallback)
            var pillarSim = (viewerPillarScores.Length > 0 && ownerPillarScores.Length > 0)
                ? FloatCosineSimilarity(viewerPillarScores, ownerPillarScores)
                : 0.5;
            sim += pillarSim * 0.28; totalW += 0.28;

            // 2. Reception vs tile embedding
            if (viewerVector?.ReceptionEmbedding != null && t.Embedding != null)
            { sim += CosineSimilarity(viewerVector.ReceptionEmbedding, t.Embedding) * 0.32; totalW += 0.32; }

            // 3. Expression vs owner expression
            if (viewerVector?.ExpressionEmbedding != null && owner?.ExpressionEmbedding != null)
            { sim += CosineSimilarity(viewerVector.ExpressionEmbedding, owner.ExpressionEmbedding) * 0.18; totalW += 0.18; }

            // 4. Preference vs tile embedding
            if (viewerVector?.PreferenceEmbedding != null && t.Embedding != null)
            { sim += CosineSimilarity(viewerVector.PreferenceEmbedding, t.Embedding) * 0.12; totalW += 0.12; }

            // 5. Intent tag Jaccard (low weight — soft signal, won't create demographic silos)
            if (viewerIntentTags.Count > 0 && ownerIntentTags.Count > 0)
            {
                var intentSim = JaccardSimilarity(viewerIntentTags, ownerIntentTags);
                sim += intentSim * 0.10; totalW += 0.10;
            }

            sim = totalW > 0 ? sim / totalW : 0.5;

            var cfNormalized = cfScoreMap.TryGetValue(t.UserId, out var cfRaw)
                ? Math.Min(1.0, cfRaw)
                : 0.5;

            var combinedScore = sim * 0.60
                              + RecencyScore(t.CreatedAt) * 0.25
                              + cfNormalized * 0.15;

            scored.Add((new CommonsFeedTile(t.Id, t.UserId, t.ContentType, t.ContentText, t.MediaUrl, t.CreatedAt, sim), combinedScore, cfNormalized));
        }

        // 70/30 resonant/discovery split
        var resonant = scored
            .Where(s => s.Tile.Similarity >= ResonantThreshold)
            .OrderByDescending(s => s.CombinedScore)
            .Select(s => s.Tile)
            .ToList();

        // Discovery bucket: rank by CF affinity + recency (not recency-only)
        // This surfaces tiles from behaviorally similar users even when pillar-distant.
        var discovery = scored
            .Where(s => s.Tile.Similarity < ResonantThreshold)
            .OrderByDescending(s => s.CfScore * 0.5 + RecencyScore(s.Tile.CreatedAt) * 0.5)
            .Select(s => s.Tile)
            .ToList();

        // Interleave at 70/30: track resonant slots added; add discovery when behind target
        var result = new List<CommonsFeedTile>(FeedPoolSize);
        int ri = 0, di = 0, resonantAdded = 0;
        while (result.Count < FeedPoolSize && (ri < resonant.Count || di < discovery.Count))
        {
            int resonantTarget = (int)((result.Count + 1) * ResonantFraction);
            if (ri < resonant.Count && resonantAdded < resonantTarget)
            {
                result.Add(resonant[ri++]);
                resonantAdded++;
            }
            else if (di < discovery.Count)
                result.Add(discovery[di++]);
            else if (ri < resonant.Count)
            {
                result.Add(resonant[ri++]);
                resonantAdded++;
            }
        }

        return result;
    }

    private async Task<bool> IsEnergyDepletedAsync(int userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var redisKey = EnergyRedisKey(userId, today);

        var redisCount = await _cache.GetCounterAsync(redisKey, ct);
        if (redisCount >= DailyTileCap) return true;

        // Redis miss — check DB (counter may have expired from Redis before the day ended)
        if (redisCount == 0)
        {
            var dbCount = await _db.UserEnergyMeters.AsNoTracking()
                .Where(m => m.UserId == userId && m.DateUtc == today)
                .Select(m => (int?)m.TilesViewed)
                .FirstOrDefaultAsync(ct) ?? 0;

            if (dbCount >= DailyTileCap) return true;
        }

        return false;
    }

    private static string EnergyRedisKey(int userId, DateOnly date)
        => $"commons:energy:{userId}:{date:yyyy-MM-dd}";

    private static double SecondsUntilEndOfUtcDay()
    {
        var now = DateTime.UtcNow;
        var midnight = now.Date.AddDays(1);
        return (midnight - now).TotalSeconds;
    }

    private static List<CommonsFeedTile> PageSlice(List<CommonsFeedTile> feed, int page)
    {
        var skip = (Math.Max(1, page) - 1) * PageSize;
        return feed.Skip(skip).Take(PageSize).ToList();
    }

    private static double CosineSimilarity(Vector a, Vector b)
    {
        var aSpan = a.Memory.Span;
        var bSpan = b.Memory.Span;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < aSpan.Length && i < bSpan.Length; i++)
        {
            dot += aSpan[i] * bSpan[i];
            normA += aSpan[i] * aSpan[i];
            normB += bSpan[i] * bSpan[i];
        }
        return (normA == 0 || normB == 0) ? 0.0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static double FloatCosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0 || normB == 0) ? 0.5 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static float[] ParsePillarScores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<float>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var keys = new[] { "Lifestyle", "Energy", "Values", "Communication", "Ambition", "Stability", "Curiosity", "Affection" };
            var scores = new float[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                if (doc.RootElement.TryGetProperty(keys[i], out var v))
                    scores[i] = v.GetSingle();
            return scores;
        }
        catch { return Array.Empty<float>(); }
    }

    private static HashSet<string> ExtractIntentTags(string? vectorJson)
    {
        if (string.IsNullOrWhiteSpace(vectorJson)) return new HashSet<string>();
        try
        {
            using var doc = JsonDocument.Parse(vectorJson);
            if (!doc.RootElement.TryGetProperty("intent", out var intent)) return new HashSet<string>();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (intent.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
                foreach (var t in tagsEl.EnumerateArray())
                    if (t.GetString() is string s) tags.Add(s);
            return tags;
        }
        catch { return new HashSet<string>(); }
    }

    private static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        int intersection = a.Intersect(b).Count();
        int union = a.Count + b.Count - intersection;
        return union == 0 ? 0.5 : (double)intersection / union;
    }

    private static double RecencyScore(DateTimeOffset createdAt)
    {
        var ageHours = (DateTimeOffset.UtcNow - createdAt).TotalHours;
        return Math.Exp(-ageHours / 24.0);
    }
}
