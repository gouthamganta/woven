using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

public class StyleEmbeddingService : IStyleEmbeddingService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<StyleEmbeddingService> _logger;

    public StyleEmbeddingService(WovenDbContext db, ILogger<StyleEmbeddingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ComputeStyleEmbeddingAsync(int userId, CancellationToken ct = default)
    {
        // Collect text corpus
        var foundational = await _db.UserFoundationalV1s.AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => f.AnswersJson)
            .FirstOrDefaultAsync(ct) ?? "";

        var seasonText = await _db.UserSeasonResponses.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.Response)
            .ToListAsync(ct);

        var tileCutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var tileText = await _db.Tiles.AsNoTracking()
            .Where(t => t.UserId == userId && t.ContentText != null && t.CreatedAt >= tileCutoff)
            .OrderByDescending(t => t.CreatedAt)
            .Take(30)
            .Select(t => t.ContentText!)
            .ToListAsync(ct);

        var corpus = string.Join(" ", new[] { foundational }
            .Concat(seasonText)
            .Concat(tileText)
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var wordCount = corpus.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 200)
        {
            _logger.LogInformation("[StyleEmbedding] Skipping user {UserId} — only {Words} words", userId, wordCount);
            return;
        }

        var features = ExtractStyleFeatures(corpus);

        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null) return;

        vector.StyleEmbedding = new Vector(features);
        vector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[StyleEmbedding] Computed style embedding for user {UserId}", userId);
    }

    private static float[] ExtractStyleFeatures(string text)
    {
        var features = new float[128];
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+").Where(s => s.Length > 0).ToList();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = new HashSet<string>(words.Select(w => w.ToLowerInvariant()));

        // Feature 0: avg sentence length (words), normalized by 30
        features[0] = sentences.Count > 0
            ? Math.Min(1f, (float)words.Length / sentences.Count / 30f)
            : 0f;

        // Feature 1: vocabulary richness (unique/total), already 0-1
        features[1] = words.Length > 0 ? (float)uniqueWords.Count / words.Length : 0f;

        // Feature 2: question ratio
        features[2] = sentences.Count > 0
            ? (float)sentences.Count(s => s.TrimEnd().EndsWith('?')) / sentences.Count
            : 0f;

        // Feature 3: emoji usage rate (emoji chars / total chars), normalized
        var emojiCount = text.Count(c => c > 0x1F300);
        features[3] = text.Length > 0 ? Math.Min(1f, (float)emojiCount / text.Length * 100f) : 0f;

        // Feature 4: avg word length, normalized by 8
        features[4] = words.Length > 0
            ? Math.Min(1f, (float)words.Average(w => w.Length) / 8f)
            : 0f;

        // Feature 5: exclamation rate
        features[5] = sentences.Count > 0
            ? (float)sentences.Count(s => s.TrimEnd().EndsWith('!')) / sentences.Count
            : 0f;

        // Feature 6: capitalization consistency (sentences starting with uppercase)
        features[6] = sentences.Count > 0
            ? (float)sentences.Count(s => s.Length > 0 && char.IsUpper(s[0])) / sentences.Count
            : 0f;

        // Feature 7: avg paragraph length (sentences per paragraph), normalized by 5
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        features[7] = paragraphs.Length > 0
            ? Math.Min(1f, (float)sentences.Count / paragraphs.Length / 5f)
            : 0f;

        // Feature 8: punctuation variety (number of distinct punctuation chars / 10)
        var distinctPunct = new HashSet<char>(text.Where(char.IsPunctuation));
        features[8] = Math.Min(1f, distinctPunct.Count / 10f);

        // Features 9-127: pad with zero (reserved for future style dimensions)
        // Features 9-15: character bigram frequency proxies
        var totalChars = (float)Math.Max(1, text.Length);
        features[9]  = text.Count(c => c == ',') / totalChars * 100f;
        features[10] = text.Count(c => c == '.') / totalChars * 100f;
        features[11] = text.Count(c => c == '-') / totalChars * 100f;
        features[12] = text.Count(c => c == '"') / totalChars * 100f;
        features[13] = text.Count(c => c == '(') / totalChars * 100f;
        features[14] = text.Count(c => c == ':') / totalChars * 100f;
        features[15] = text.Count(c => c == ';') / totalChars * 100f;

        // Normalize all features to [0, 1]
        for (int i = 0; i < 128; i++)
            features[i] = Math.Clamp(features[i], 0f, 1f);

        return features;
    }
}
