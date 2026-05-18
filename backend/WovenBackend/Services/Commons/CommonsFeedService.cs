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

    private readonly WovenDbContext _db;
    private readonly ICacheService _cache;
    private readonly ILogger<CommonsFeedService> _logger;

    public CommonsFeedService(WovenDbContext db, ICacheService cache, ILogger<CommonsFeedService> logger)
    {
        _db = db;
        _cache = cache;
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
        // Viewer's pillar embedding (8-dim)
        var viewerVector = await _db.UserVectors.AsNoTracking()
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .Select(v => new { v.PillarEmbedding })
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
                t.CreatedAt
            })
            .ToListAsync(ct);

        if (tiles.Count == 0)
            return new List<CommonsFeedTile>();

        // Batch-load owner pillar embeddings
        var ownerIds = tiles.Select(t => t.UserId).Distinct().ToList();
        var rawVectors = await _db.UserVectors.AsNoTracking()
            .Where(v => ownerIds.Contains(v.UserId))
            .Select(v => new { v.UserId, v.Version, v.PillarEmbedding })
            .ToListAsync(ct);

        var ownerVectors = rawVectors
            .GroupBy(v => v.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => v.Version).First().PillarEmbedding);

        // Batch-load CF scores for tile owners (tertiary ranking signal)
        var cfScoreMap = await _db.CfScores.AsNoTracking()
            .Where(c => c.UserId == userId && ownerIds.Contains(c.CandidateId))
            .ToDictionaryAsync(c => c.CandidateId, c => c.Score, ct);

        // Score each tile: 60% pillar similarity + 25% recency + 15% CF affinity
        var scored = new List<(CommonsFeedTile Tile, double CombinedScore)>(tiles.Count);
        foreach (var t in tiles)
        {
            ownerVectors.TryGetValue(t.UserId, out var ownerEmb);
            var sim = (viewerVector?.PillarEmbedding != null && ownerEmb != null)
                ? CosineSimilarity(viewerVector.PillarEmbedding, ownerEmb)
                : 0.5;

            var cfNormalized = cfScoreMap.TryGetValue(t.UserId, out var cfRaw)
                ? Math.Min(1.0, cfRaw)
                : 0.5; // neutral when no CF data yet

            var combinedScore = sim * 0.60
                              + RecencyScore(t.CreatedAt) * 0.25
                              + cfNormalized * 0.15;

            scored.Add((new CommonsFeedTile(t.Id, t.UserId, t.ContentType, t.ContentText, t.MediaUrl, t.CreatedAt, sim), combinedScore));
        }

        // 70/30 resonant/discovery split (threshold on raw cosine similarity stored in Tile record)
        var resonant = scored
            .Where(s => s.Tile.Similarity >= ResonantThreshold)
            .OrderByDescending(s => s.CombinedScore)
            .Select(s => s.Tile)
            .ToList();

        var discovery = scored
            .Where(s => s.Tile.Similarity < ResonantThreshold)
            .OrderByDescending(s => RecencyScore(s.Tile.CreatedAt))
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

    private static double RecencyScore(DateTimeOffset createdAt)
    {
        var ageHours = (DateTimeOffset.UtcNow - createdAt).TotalHours;
        return Math.Exp(-ageHours / 24.0);
    }
}
