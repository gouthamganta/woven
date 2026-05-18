using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

public class HumorEmbeddingService : IHumorEmbeddingService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<HumorEmbeddingService> _logger;

    public HumorEmbeddingService(WovenDbContext db, ILogger<HumorEmbeddingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ComputeHumorEmbeddingAsync(int userId, CancellationToken ct = default)
    {
        var completedGames = await _db.GameOutcomes.AsNoTracking()
            .Where(g => g.InitiatorUserId == userId || g.PartnerUserId == userId)
            .CountAsync(ct);

        if (completedGames < 2)
        {
            _logger.LogInformation("[HumorEmbedding] Skipping user {UserId} — only {N} completed games", userId, completedGames);
            return;
        }

        var features = new float[64];

        // Feature cluster 0-15: game round signal (RedGreenFlag choices)
        var rounds = await _db.GameRounds.AsNoTracking()
            .Where(r => r.GuesserUserId == userId || r.TargetUserId == userId)
            .Select(r => new { r.GuesserUserId, r.Score })
            .ToListAsync(ct);

        var totalRounds = rounds.Count;
        if (totalRounds > 0)
        {
            // Feature 0: fraction of rounds where user scored > 0
            features[0] = (float)rounds.Count(r => r.Score is > 0) / totalRounds;
            // Feature 1: fraction where user was guesser
            features[1] = (float)rounds.Count(r => r.GuesserUserId == userId) / totalRounds;
            // Feature 2: avg normalized score
            features[2] = Math.Min(1f, (float)(rounds.Where(r => r.Score.HasValue).DefaultIfEmpty()
                .Average(r => r?.Score ?? 0)) / 10f);
        }

        // Feature cluster 16-31: chat emoji density
        var messages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.SenderUserId == userId)
            .Select(m => m.Body)
            .Take(200)
            .ToListAsync(ct);


        if (messages.Count > 0)
        {
            var totalChars = (float)messages.Sum(m => m.Length);
            var emojiChars = (float)messages.Sum(m => m.Count(c => c > 0x1F300));
            features[16] = totalChars > 0 ? Math.Min(1f, emojiChars / totalChars * 50f) : 0f;

            // Avg message length normalized by 100
            features[17] = Math.Min(1f, (float)messages.Average(m => m.Length) / 100f);

            // Question messages ratio
            features[18] = (float)messages.Count(m => m.TrimEnd().EndsWith('?')) / messages.Count;

            // Exclamation messages ratio
            features[19] = (float)messages.Count(m => m.TrimEnd().EndsWith('!')) / messages.Count;
        }

        // Feature cluster 32-47: game outcome stats
        var outcomes = await _db.GameOutcomes.AsNoTracking()
            .Where(g => g.InitiatorUserId == userId || g.PartnerUserId == userId)
            .Take(50)
            .ToListAsync(ct);

        if (outcomes.Count > 0)
        {
            features[32] = Math.Min(1f, completedGames / 20f);
            features[33] = (float)outcomes.Count(o => o.InitiatorUserId == userId) / outcomes.Count;
        }

        // Clamp all to [0, 1]
        for (int i = 0; i < 64; i++)
            features[i] = Math.Clamp(features[i], 0f, 1f);

        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null) return;

        vector.HumorEmbedding = new Vector(features);
        vector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[HumorEmbedding] Computed for user {UserId}", userId);
    }
}
