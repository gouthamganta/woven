using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

public class EmotionalRhythmService : IEmotionalRhythmService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<EmotionalRhythmService> _logger;

    public EmotionalRhythmService(WovenDbContext db, ILogger<EmotionalRhythmService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ComputeEmotionalRhythmAsync(int userId, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-28);

        // Check 28-day minimum history via message activity
        var messageCount = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.SenderUserId == userId && m.CreatedAt >= cutoff)
            .CountAsync(ct);

        var tileCount = await _db.Tiles.AsNoTracking()
            .Where(t => t.UserId == userId && t.CreatedAt >= cutoff)
            .CountAsync(ct);

        if (messageCount + tileCount < 10)
        {
            _logger.LogInformation("[EmotionalRhythm] Skipping user {UserId} — insufficient 28-day activity", userId);
            return;
        }

        var features = new float[48];

        // --- Tile posting patterns ---
        var tileHours = await _db.Tiles.AsNoTracking()
            .Where(t => t.UserId == userId && t.CreatedAt >= cutoff)
            .Select(t => t.CreatedAt)
            .ToListAsync(ct);

        if (tileHours.Count > 0)
        {
            // Feature 0: morning activity fraction (6-12)
            features[0] = (float)tileHours.Count(t => t.Hour >= 6 && t.Hour < 12) / tileHours.Count;
            // Feature 1: afternoon activity (12-18)
            features[1] = (float)tileHours.Count(t => t.Hour >= 12 && t.Hour < 18) / tileHours.Count;
            // Feature 2: evening activity (18-24)
            features[2] = (float)tileHours.Count(t => t.Hour >= 18 && t.Hour < 24) / tileHours.Count;
            // Feature 3: night activity (0-6)
            features[3] = (float)tileHours.Count(t => t.Hour >= 0 && t.Hour < 6) / tileHours.Count;

            // Feature 4: weekday vs weekend ratio
            features[4] = (float)tileHours.Count(t => t.DayOfWeek != DayOfWeek.Saturday && t.DayOfWeek != DayOfWeek.Sunday)
                / tileHours.Count;

            // Feature 5: posting regularity (days with activity / 28)
            var activeDays = tileHours.Select(t => t.Date).Distinct().Count();
            features[5] = Math.Min(1f, activeDays / 28f);
        }

        // --- Chat message patterns ---
        var messages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.SenderUserId == userId && m.CreatedAt >= cutoff)
            .Select(m => new { m.CreatedAt, BodyLen = m.Body.Length })
            .ToListAsync(ct);

        if (messages.Count > 0)
        {
            // Feature 8: morning chat fraction
            features[8]  = (float)messages.Count(m => m.CreatedAt.Hour >= 6 && m.CreatedAt.Hour < 12) / messages.Count;
            // Feature 9: evening chat fraction
            features[9]  = (float)messages.Count(m => m.CreatedAt.Hour >= 18 && m.CreatedAt.Hour < 24) / messages.Count;
            // Feature 10: night chat fraction
            features[10] = (float)messages.Count(m => m.CreatedAt.Hour >= 0 && m.CreatedAt.Hour < 6) / messages.Count;

            // Feature 11: weekend chat ratio
            features[11] = (float)messages.Count(m =>
                m.CreatedAt.DayOfWeek == DayOfWeek.Saturday || m.CreatedAt.DayOfWeek == DayOfWeek.Sunday)
                / messages.Count;

            // Feature 12: message length variance proxy (avg length normalized)
            features[12] = Math.Min(1f, (float)messages.Average(m => m.BodyLen) / 200f);
        }

        // --- Commons energy variance (features 16-19) ---
        var energyRows = await _db.UserEnergyMeters.AsNoTracking()
            .Where(e => e.UserId == userId && e.DateUtc >= DateOnly.FromDateTime(cutoff.DateTime))
            .Select(e => (float)e.TilesViewed)
            .ToListAsync(ct);

        if (energyRows.Count > 0)
        {
            var avg = energyRows.Average();
            var variance = energyRows.Select(v => (v - avg) * (v - avg)).Average();
            features[16] = Math.Min(1f, avg / 100f);
            features[17] = Math.Min(1f, (float)Math.Sqrt(variance) / 50f);

            // Trend: compare first half vs second half avg
            var half = energyRows.Count / 2;
            if (half > 0)
            {
                var firstHalf = energyRows.Take(half).Average();
                var secondHalf = energyRows.Skip(half).Average();
                features[18] = Math.Clamp(0.5f + (secondHalf - firstHalf) / 100f, 0f, 1f);
            }
        }

        // Clamp all to [0, 1]
        for (int i = 0; i < 48; i++)
            features[i] = Math.Clamp(features[i], 0f, 1f);

        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null) return;

        vector.EmotionalRhythmEmbedding = new Vector(features);
        vector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[EmotionalRhythm] Computed for user {UserId}", userId);
    }
}
