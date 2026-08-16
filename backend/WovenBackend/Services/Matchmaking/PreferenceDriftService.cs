using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// Exponential moving average drift toward high-ConnectionScore candidates.
///
/// Per-pair drift rate:  α = min(MaxDriftRate, DriftScale × connectionScore)
/// Update rule:          pref[j] = (1−α) × pref[j] + α × candidate[j]
///
/// Voice:  drifts UserVoicePreference.PreferenceEmbedding (192-dim) toward
///         the candidate's UserVector.VoiceEmbedding.
///
/// Visual: drifts UserVisualPreference.PreferenceEmbedding (1536-dim) toward
///         the candidate's best available PhotoEmbedding.
///
/// Only applies to candidates with ConnectionScore ≥ MinScore and who have
/// the relevant embedding populated. Top-N connections taken per run.
/// </summary>
public class PreferenceDriftService : IPreferenceDriftService
{
    private const float MinScore    = 0.15f;
    private const float DriftScale  = 0.10f;
    private const float MaxDriftRate = 0.05f;
    private const int   TopN        = 20;

    private readonly WovenDbContext _db;
    private readonly ILogger<PreferenceDriftService> _logger;

    public PreferenceDriftService(WovenDbContext db, ILogger<PreferenceDriftService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task DriftForUserAsync(int userId, CancellationToken ct = default)
    {
        // Top-N positive connections for this viewer
        var topPairs = await _db.ConnectionScores.AsNoTracking()
            .Where(c => c.ViewerId == userId && c.Score >= MinScore)
            .OrderByDescending(c => c.Score)
            .Take(TopN)
            .Select(c => new { c.CandidateId, c.Score })
            .ToListAsync(ct);

        if (topPairs.Count == 0) return;

        var candidateIds = topPairs.Select(p => p.CandidateId).ToList();

        // Load viewer preferences (with tracking for update)
        var voicePref  = await _db.UserVoicePreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var visualPref = await _db.UserVisualPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        // Load candidate voice embeddings
        var candidateVoiceVecs = await _db.UserVectors.AsNoTracking()
            .Where(v => candidateIds.Contains(v.UserId) && v.VoiceEmbedding != null)
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .Select(v => new { v.UserId, v.VoiceEmbedding })
            .ToListAsync(ct);

        // Load candidate photo embeddings (earliest embedded = most stable)
        var candidatePhotoVecs = await _db.PhotoEmbeddings.AsNoTracking()
            .Where(p => candidateIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => g.OrderBy(p => p.EmbeddedAt).First())
            .Select(p => new { p.UserId, p.Embedding })
            .ToListAsync(ct);

        var voiceMap = candidateVoiceVecs.ToDictionary(v => v.UserId, v => v.VoiceEmbedding!);
        var photoMap = candidatePhotoVecs.ToDictionary(p => p.UserId, p => p.Embedding);

        var scoreMap = topPairs.ToDictionary(p => p.CandidateId, p => p.Score);

        // Cold start: seed voice preference from connection-score-weighted mean of available
        // candidate voice embeddings so new users aren't permanently skipped by the drift loop.
        if (voicePref == null && voiceMap.Count > 0)
        {
            var pairs = voiceMap
                .Where(kv => scoreMap.ContainsKey(kv.Key))
                .Select(kv => (vec: kv.Value.ToArray(), w: (double)scoreMap[kv.Key]))
                .ToList();

            if (pairs.Count > 0)
            {
                var seeded = WeightedMean(pairs);
                voicePref = new Data.Entities.UserVoicePreference
                {
                    UserId = userId,
                    PreferenceEmbedding = new Vector(seeded),
                    YesSampleCount = 0,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _db.UserVoicePreferences.Add(voicePref);
                _logger.LogInformation("[PreferenceDrift] Cold-start voice preference seeded for user {UserId} from {N} candidates", userId, pairs.Count);
            }
        }

        int voiceDrifts = 0, visualDrifts = 0;

        foreach (var candidateId in candidateIds)
        {
            var score = scoreMap[candidateId];
            var alpha = Math.Min(MaxDriftRate, DriftScale * score);

            // Voice drift
            if (voicePref?.PreferenceEmbedding != null && voiceMap.TryGetValue(candidateId, out var candVoice))
            {
                var drifted = Ema(voicePref.PreferenceEmbedding.ToArray(), candVoice.ToArray(), alpha);
                voicePref.PreferenceEmbedding = new Vector(drifted);
                voicePref.UpdatedAt = DateTimeOffset.UtcNow;
                voiceDrifts++;
            }

            // Visual drift
            if (visualPref?.PreferenceEmbedding != null && photoMap.TryGetValue(candidateId, out var candPhoto)
                && candPhoto != null)
            {
                var drifted = Ema(visualPref.PreferenceEmbedding.ToArray(), candPhoto.ToArray(), alpha);
                visualPref.PreferenceEmbedding = new Vector(drifted);
                visualPref.UpdatedAt = DateTimeOffset.UtcNow;
                visualDrifts++;
            }
        }

        if (voiceDrifts > 0 || visualDrifts > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogDebug(
                "[PreferenceDrift] user={UserId} voiceDrifts={V} visualDrifts={Vi}",
                userId, voiceDrifts, visualDrifts);
        }
    }

    // new_pref[j] = (1 − α) × old[j] + α × target[j]
    internal static float[] Ema(float[] old, float[] target, float alpha)
    {
        var len    = Math.Min(old.Length, target.Length);
        var result = new float[len];
        var keep   = 1f - alpha;
        for (int j = 0; j < len; j++)
            result[j] = keep * old[j] + alpha * target[j];
        return result;
    }

    // Weighted mean of float vectors; falls back to uniform mean when all weights are zero.
    private static float[] WeightedMean(List<(float[] vec, double w)> pairs)
    {
        var dim = pairs[0].vec.Length;
        var result = new float[dim];
        var totalW = pairs.Sum(p => p.w);
        if (totalW <= 0) totalW = pairs.Count; // uniform fallback

        foreach (var (vec, w) in pairs)
        {
            var wf = (float)(w / totalW);
            for (int j = 0; j < Math.Min(dim, vec.Length); j++)
                result[j] += wf * vec[j];
        }
        return result;
    }
}
