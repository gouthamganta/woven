using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

public class LifestyleEmbeddingService : ILifestyleEmbeddingService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<LifestyleEmbeddingService> _logger;

    public LifestyleEmbeddingService(WovenDbContext db, ILogger<LifestyleEmbeddingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ComputeLifestyleEmbeddingAsync(int userId, CancellationToken ct = default)
    {
        // Check minimum app history (14 days)
        var firstActivity = await _db.Tiles.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => (DateTimeOffset?)t.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? await _db.MomentResponses.AsNoTracking()
            .Where(r => r.FromUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => (DateTimeOffset?)r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (firstActivity == null ||
            (DateTimeOffset.UtcNow - firstActivity.Value).TotalDays < 14)
        {
            _logger.LogInformation("[LifestyleEmbedding] Skipping user {UserId} — insufficient history", userId);
            return;
        }

        var features = new float[128];

        // --- Self-reported fields (features 0-31) ---
        var fields = await _db.UserOptionalFields.AsNoTracking()
            .Where(f => f.UserId == userId)
            .ToDictionaryAsync(f => f.Key, f => f.Value, ct);

        features[0] = EncodeLifestyleField(fields, "children", new[] { "None", "Have some", "Want someday", "Never" });
        features[1] = EncodeLifestyleField(fields, "pref_children", new[] { "None", "Have some", "Want someday", "Never" });
        features[2] = EncodeLifestyleField(fields, "smoking", new[] { "Never", "Socially", "Regularly" });
        features[3] = EncodeLifestyleField(fields, "pref_smoking", new[] { "Never", "Socially", "Regularly" });
        features[4] = EncodeLifestyleField(fields, "diet", new[] { "Omnivore", "Vegetarian", "Vegan", "Kosher", "Halal" });
        features[5] = EncodeLifestyleField(fields, "pref_drinking", new[] { "Never", "Socially", "Regularly" });
        features[6] = EncodeLifestyleField(fields, "pref_workout", new[] { "Never", "Sometimes", "Often", "Daily" });
        features[7] = EncodeLifestyleField(fields, "religion", new[] { "None", "Christian", "Jewish", "Muslim", "Hindu", "Buddhist", "Other" });
        features[8] = EncodeLifestyleField(fields, "pref_religion", new[] { "None", "Christian", "Jewish", "Muslim", "Hindu", "Buddhist", "Other" });

        // --- Behavioral signals (features 32-63) ---
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        var tiles = await _db.Tiles.AsNoTracking()
            .Where(t => t.UserId == userId && t.CreatedAt >= cutoff)
            .Select(t => new { t.ContentType, t.CreatedAt })
            .ToListAsync(ct);

        var totalTiles = (float)tiles.Count;
        if (totalTiles > 0)
        {
            features[32] = Math.Min(1f, totalTiles / 30f); // posting frequency
            features[33] = (float)tiles.Count(t => t.ContentType == "text") / totalTiles;
            features[34] = (float)tiles.Count(t => t.ContentType == "photo") / totalTiles;
            features[35] = (float)tiles.Count(t => t.ContentType == "voice") / totalTiles;
            features[36] = (float)tiles.Count(t => t.ContentType == "video") / totalTiles;

            // Variety: distinct content types / 4
            features[37] = tiles.Select(t => t.ContentType).Distinct().Count() / 4f;
        }

        // Commons browsing (tile views)
        var viewCount = await _db.TileViews.AsNoTracking()
            .Where(v => v.UserId == userId && v.ViewedAt >= cutoff)
            .CountAsync(ct);
        features[38] = Math.Min(1f, viewCount / 100f);

        // Gap signals
        features[64] = fields.ContainsKey("children") ? 1f : 0f;
        features[65] = fields.ContainsKey("diet") ? 1f : 0f;
        features[66] = fields.ContainsKey("pref_smoking") ? 1f : 0f;
        features[67] = fields.ContainsKey("pref_drinking") ? 1f : 0f;

        // Clamp all features to [0, 1]
        for (int i = 0; i < 128; i++)
            features[i] = Math.Clamp(features[i], 0f, 1f);

        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null) return;

        vector.LifestyleEmbedding = new Vector(features);
        vector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[LifestyleEmbedding] Computed for user {UserId}", userId);
    }

    private static float EncodeLifestyleField(
        Dictionary<string, string> fields,
        string key,
        string[] orderedValues)
    {
        if (!fields.TryGetValue(key, out var val) || string.IsNullOrWhiteSpace(val))
            return 0.5f; // neutral when missing

        var idx = Array.FindIndex(orderedValues, v =>
            string.Equals(v, val.Trim(), StringComparison.OrdinalIgnoreCase));

        return idx < 0 ? 0.5f : (float)idx / Math.Max(1, orderedValues.Length - 1);
    }
}
