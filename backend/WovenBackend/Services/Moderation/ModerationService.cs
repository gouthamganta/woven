using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Moderation;

public class ModerationService : IModerationService
{
    private readonly WovenDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly ILogger<ModerationService> _logger;

    public ModerationService(WovenDbContext db, IConfiguration config, HttpClient http, ILogger<ModerationService> logger)
    {
        _db = db;
        _config = config;
        _http = http;
        _logger = logger;

        var apiKey = _config["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task EnqueueAsync(Guid tileId, int userId, CancellationToken ct = default)
    {
        var isModerationEnabled = _config.GetValue<bool>("Moderation:IsModerationEnabled");
        if (!isModerationEnabled)
        {
            // Dev: auto-approve media tiles immediately
            var tile = await _db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
            if (tile is not null && !tile.IsModerated)
            {
                tile.IsModerated = true;
                await _db.SaveChangesAsync(ct);
            }
            return;
        }

        // Avoid duplicate queue entries
        var exists = await _db.ModerationQueues
            .AnyAsync(q => q.TileId == tileId && q.ReviewedAt == null, ct);
        if (exists) return;

        _db.ModerationQueues.Add(new ModerationQueue
        {
            TileId    = tileId,
            UserId    = userId,
            QueuedAt  = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[Moderation] Queued tile {TileId} for user {UserId}", tileId, userId);
    }

    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        var isModerationEnabled = _config.GetValue<bool>("Moderation:IsModerationEnabled");

        // When moderation is off, auto-approve all pending items in bulk
        if (!isModerationEnabled)
        {
            var pending = await _db.ModerationQueues
                .Where(q => q.ReviewedAt == null)
                .ToListAsync(ct);

            if (pending.Count == 0) return;

            var now = DateTimeOffset.UtcNow;
            var tileIds = pending.Select(q => q.TileId).ToList();

            var tiles = await _db.Tiles
                .Where(t => tileIds.Contains(t.Id) && !t.IsExpired)
                .ToListAsync(ct);

            foreach (var tile in tiles)
                tile.IsModerated = true;

            foreach (var item in pending)
            {
                item.ReviewedAt = now;
                item.Decision   = "approved";
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[Moderation] Auto-approved {Count} pending tiles (moderation disabled)", pending.Count);
            return;
        }

        var queue = await _db.ModerationQueues
            .Include(q => q.Tile)
            .Where(q => q.ReviewedAt == null && !q.Tile.IsExpired)
            .OrderBy(q => q.QueuedAt)
            .Take(50)
            .ToListAsync(ct);

        _logger.LogInformation("[Moderation] {Count} tiles pending AI review", queue.Count);

        var reviewedAt = DateTimeOffset.UtcNow;
        foreach (var item in queue)
        {
            try
            {
                var flagged = await CheckOpenAiModerationAsync(item.Tile.ContentText, ct);
                item.ReviewedAt = reviewedAt;

                if (flagged)
                {
                    item.Decision        = "rejected";
                    item.RejectReason    = "openai_moderation_flagged";
                    item.Tile.IsExpired  = true;
                    _logger.LogWarning("[Moderation] Tile {TileId} flagged by OpenAI moderation", item.TileId);
                }
                else
                {
                    item.Decision         = "approved";
                    item.Tile.IsModerated = true;
                    _logger.LogDebug("[Moderation] Tile {TileId} approved by OpenAI moderation", item.TileId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Moderation] OpenAI check failed for tile {TileId} — skipping", item.TileId);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> ApproveAsync(Guid queueItemId, int reviewerId, CancellationToken ct = default)
    {
        var item = await _db.ModerationQueues
            .Include(q => q.Tile)
            .FirstOrDefaultAsync(q => q.Id == queueItemId, ct);

        if (item is null || item.ReviewedAt is not null) return false;

        item.ReviewedAt = DateTimeOffset.UtcNow;
        item.ReviewerId = reviewerId;
        item.Decision   = "approved";
        item.Tile.IsModerated = true;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Moderation] Approved tile {TileId} by reviewer {ReviewerId}", item.TileId, reviewerId);
        return true;
    }

    public async Task<bool> RejectAsync(Guid queueItemId, int reviewerId, string reason, CancellationToken ct = default)
    {
        var item = await _db.ModerationQueues
            .Include(q => q.Tile)
            .FirstOrDefaultAsync(q => q.Id == queueItemId, ct);

        if (item is null || item.ReviewedAt is not null) return false;

        item.ReviewedAt   = DateTimeOffset.UtcNow;
        item.ReviewerId   = reviewerId;
        item.Decision     = "rejected";
        item.RejectReason = reason[..Math.Min(reason.Length, 200)];

        // Soft-expire the tile so it's hidden and blob-cleaned
        item.Tile.IsExpired = true;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[Moderation] Rejected tile {TileId} by reviewer {ReviewerId}: {Reason}",
            item.TileId, reviewerId, reason);
        return true;
    }

    public async Task<List<ModerationQueueDto>> GetPendingAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _db.ModerationQueues
            .AsNoTracking()
            .Include(q => q.Tile)
            .Where(q => q.ReviewedAt == null && !q.Tile.IsExpired)
            .OrderBy(q => q.QueuedAt)
            .Take(limit)
            .Select(q => new ModerationQueueDto(
                q.Id,
                q.TileId,
                q.UserId,
                q.Tile.ContentType,
                q.Tile.ContentText,
                q.Tile.MediaUrl,
                q.QueuedAt))
            .ToListAsync(ct);
    }

    public async Task<List<TileReportDto>> GetReportsAsync(Guid tileId, CancellationToken ct = default)
    {
        return await _db.TileReports
            .AsNoTracking()
            .Where(r => r.TileId == tileId)
            .OrderByDescending(r => r.ReportedAt)
            .Select(r => new TileReportDto(r.Id, r.TileId, r.ReporterId, r.Reason, r.ReportedAt))
            .ToListAsync(ct);
    }

    public async Task<ModerationImageResult> ModerateImageAsync(int userId, string imageUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return ModerationImageResult.APPROVED;

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = "omni-moderation-latest",
                input = new[] { new { type = "image_url", image_url = new { url = imageUrl } } }
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync("https://api.openai.com/v1/moderations", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Moderation] Image moderation API returned {Status} for user {UserId}",
                    response.StatusCode, userId);
                return ModerationImageResult.ESCALATED;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var flagged = doc.RootElement
                .GetProperty("results")[0]
                .GetProperty("flagged")
                .GetBoolean();

            if (flagged)
            {
                _logger.LogWarning("[Moderation] Image auto-rejected for user {UserId}: {Url}", userId, imageUrl);
                return ModerationImageResult.AUTO_REJECTED;
            }

            return ModerationImageResult.APPROVED;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Moderation] Image moderation failed for user {UserId} — escalating", userId);
            return ModerationImageResult.ESCALATED;
        }
    }

    // Returns true if OpenAI Moderation API flags the content. Null/empty content auto-passes.
    private async Task<bool> CheckOpenAiModerationAsync(string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var body = JsonSerializer.Serialize(new { input = text, model = "omni-moderation-latest" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/moderations", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Moderation] OpenAI moderation API returned {Status}", response.StatusCode);
            return false;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement
            .GetProperty("results")[0]
            .GetProperty("flagged")
            .GetBoolean();
    }
}
