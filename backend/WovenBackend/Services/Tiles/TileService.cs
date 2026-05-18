using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Services.Tiles;

public class TileService : ITileService
{
    private static readonly HashSet<string> ValidContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "text", "photo", "video", "voice" };

    private const int MaxActiveTiles = 10;
    private static readonly TimeSpan TileLifetime = TimeSpan.FromHours(48);

    private readonly WovenDbContext _db;
    private readonly TileEmbeddingService _embeddings;
    private readonly IConfiguration _config;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<TileService> _logger;

    public TileService(
        WovenDbContext db,
        TileEmbeddingService embeddings,
        IConfiguration config,
        IAnalyticsService analytics,
        ILogger<TileService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _config = config;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<CreateTileResult> CreateAsync(int userId, CreateTileRequest req, CancellationToken ct = default)
    {
        if (!ValidContentTypes.Contains(req.ContentType))
            return new CreateTileResult(false, null, "INVALID_CONTENT_TYPE");

        if (req.ContentType.Equals("text", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(req.ContentText))
            return new CreateTileResult(false, null, "TEXT_REQUIRED");

        if (!req.ContentType.Equals("text", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(req.MediaUrl))
            return new CreateTileResult(false, null, "MEDIA_URL_REQUIRED");

        var activeCount = await _db.Tiles
            .CountAsync(t => t.UserId == userId && !t.IsExpired, ct);

        if (activeCount >= MaxActiveTiles)
            return new CreateTileResult(false, null, "ACTIVE_TILE_LIMIT_REACHED");

        var isModerationEnabled = _config.GetValue<bool>("Moderation:IsModerationEnabled");
        var now = DateTimeOffset.UtcNow;
        var isText = req.ContentType.Equals("text", StringComparison.OrdinalIgnoreCase);

        // Text tiles: auto-approve inline (ModerationService handles them synchronously in Phase 2B).
        // Media tiles: stay unmoderated until ModerationWorker runs (Phase 2B).
        // When moderation is disabled (dev), all tiles auto-approve.
        var isModerated = !isModerationEnabled || isText;

        var tile = new Tile
        {
            UserId      = userId,
            ContentType = req.ContentType.ToLowerInvariant(),
            ContentText = req.ContentText?.Trim(),
            MediaUrl    = req.MediaUrl,
            CreatedAt   = now,
            ExpiresAt   = now + TileLifetime,
            IsExpired   = false,
            IsHighlighted = false,
            IsModerated = isModerated
        };

        _db.Tiles.Add(tile);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[TileService] Created tile {TileId} for user {UserId} (type={Type}, moderated={Mod})",
            tile.Id, userId, tile.ContentType, isModerated);

        // Fire embedding pipeline without blocking the response.
        // TileEmbeddingService is a singleton using IServiceScopeFactory — safe after request scope ends.
        if (!string.IsNullOrEmpty(tile.ContentText))
        {
            var tileId = tile.Id;
            _ = Task.Run(async () =>
            {
                try   { await _embeddings.EmbedTileAsync(tileId); }
                catch (Exception ex)
                { _logger.LogError(ex, "[TileService] Embedding failed for tile {TileId}", tileId); }
            });
        }

        _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.TilePosted,
            new { contentType = tile.ContentType });

        return new CreateTileResult(true, tile.Id, null);
    }

    public async Task<List<TileDto>> GetMineAsync(int userId, CancellationToken ct = default)
    {
        var tiles = await _db.Tiles
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var tileIds = tiles.Select(t => t.Id).ToList();

        var slotMap = await _db.Highlights
            .Where(h => h.UserId == userId && tileIds.Contains(h.TileId))
            .ToDictionaryAsync(h => h.TileId, h => h.SlotNumber, ct);

        return tiles.Select(t => new TileDto(
            t.Id, t.ContentType, t.ContentText, t.MediaUrl,
            t.CreatedAt, t.ExpiresAt, t.IsExpired, t.IsHighlighted, t.IsModerated,
            slotMap.TryGetValue(t.Id, out var slot) ? slot : null)).ToList();
    }

    public async Task<HighlightResult> HighlightAsync(int userId, Guid tileId, int slot, CancellationToken ct = default)
    {
        if (slot < 1 || slot > 9)
            return new HighlightResult(false, "INVALID_SLOT");

        var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId && t.UserId == userId, ct);
        if (tile is null)
            return new HighlightResult(false, "TILE_NOT_FOUND");

        if (!tile.IsExpired)
            return new HighlightResult(false, "TILE_MUST_BE_EXPIRED");

        // Evict any OTHER tile that currently occupies the target slot
        var occupant = await _db.Highlights
            .FirstOrDefaultAsync(h => h.UserId == userId && h.SlotNumber == slot && h.TileId != tileId, ct);

        if (occupant is not null)
        {
            var evictedTileId = occupant.TileId;
            _db.Highlights.Remove(occupant);
            await _db.SaveChangesAsync(ct);

            // Clear is_highlighted on evicted tile if it no longer has any slots
            var evictedTile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == evictedTileId, ct);
            if (evictedTile is not null)
            {
                var stillPinned = await _db.Highlights.AnyAsync(h => h.TileId == evictedTileId, ct);
                if (!stillPinned)
                {
                    evictedTile.IsHighlighted = false;
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        // Move existing slot or add new slot for this tile
        var existing = await _db.Highlights
            .FirstOrDefaultAsync(h => h.TileId == tileId && h.UserId == userId, ct);

        if (existing is not null)
        {
            existing.SlotNumber = slot;
            existing.PinnedAt   = DateTimeOffset.UtcNow;
        }
        else
        {
            _db.Highlights.Add(new Highlight
            {
                UserId     = userId,
                TileId     = tileId,
                SlotNumber = slot,
                PinnedAt   = DateTimeOffset.UtcNow
            });
        }

        tile.IsHighlighted = true;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return new HighlightResult(false, "SLOT_CONFLICT");
        }

        return new HighlightResult(true, null);
    }

    public async Task<bool> UnhighlightAsync(int userId, Guid tileId, CancellationToken ct = default)
    {
        var highlight = await _db.Highlights
            .FirstOrDefaultAsync(h => h.TileId == tileId && h.UserId == userId, ct);

        if (highlight is null) return false;

        _db.Highlights.Remove(highlight);
        await _db.SaveChangesAsync(ct);

        // Clear is_highlighted if this was the only slot
        var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
        if (tile is not null)
        {
            var stillPinned = await _db.Highlights.AnyAsync(h => h.TileId == tileId, ct);
            if (!stillPinned)
            {
                tile.IsHighlighted = false;
                await _db.SaveChangesAsync(ct);
            }
        }

        return true;
    }

    public async Task<bool> DeleteAsync(int userId, Guid tileId, CancellationToken ct = default)
    {
        var tile = await _db.Tiles
            .FirstOrDefaultAsync(t => t.Id == tileId && t.UserId == userId, ct);

        if (tile is null)   return false;
        if (tile.IsExpired) return false;       // already gone
        if (tile.IsHighlighted) return false;   // must unhighlight first

        // Soft-delete: mark expired so MediaLifecycleWorker can clean up the blob
        tile.IsExpired = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
