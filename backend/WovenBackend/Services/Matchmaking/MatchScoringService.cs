using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Matchmaking;

public class MatchScoringService : IMatchScoringService
{
    // Base weights for 14 components (PreferenceScore permanently excluded → redistributed)
    // pillar, intent, expression, style, visual, voice, humor, lifestyle, behavioral,
    // emotional_rhythm, attachment, orbit_gravity, pulse, cf
    private static readonly double[] BaseWeights = new[]
    {
        0.20, 0.13, 0.10, 0.09, 0.10,
        0.08, 0.07, 0.08, 0.05,
        0.04, 0.04, 0.08, 0.06, 0.03
    };

    private static readonly string[] ComponentNames = new[]
    {
        "pillar", "intent", "expression", "style", "visual",
        "voice", "humor", "lifestyle", "behavioral_lifestyle",
        "emotional_rhythm", "attachment", "orbit_gravity", "pulse", "cf"
    };

    private readonly WovenDbContext _db;
    private readonly ILogger<MatchScoringService> _logger;

    public MatchScoringService(WovenDbContext db, ILogger<MatchScoringService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<MatchScore>> ScoreCandidatesAsync(
        int userId,
        List<int> candidateIds,
        CancellationToken ct = default)
    {
        if (candidateIds.Count == 0)
            return new List<MatchScore>();

        _logger.LogInformation("[Scoring] Scoring {Count} candidates for user {UserId}",
            candidateIds.Count, userId);

        // Load user vector (latest)
        var userVector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (userVector == null)
        {
            _logger.LogWarning("[Scoring] No vector found for user {UserId}", userId);
            return new List<MatchScore>();
        }

        // Parse user data
        var userVectorData = ParseVector(userVector.VectorJson);
        var userPillarScores = ParsePillarScores(userVector.PillarScoresJson);
        var userBehavioralLifestyle = ParseFloatArray(userVector.BehavioralLifestyleJson);

        // Load candidate vectors (latest per user)
        var candidateVectors = await _db.UserVectors
            .Where(v => candidateIds.Contains(v.UserId))
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .ToListAsync(ct);

        if (candidateVectors.Count == 0) return new List<MatchScore>();

        var scoredIds = candidateVectors.Select(v => v.UserId).ToList();

        // Batch load all auxiliary data in parallel
        var cfScoreMapTask = _db.CfScores.AsNoTracking()
            .Where(c => c.UserId == userId && scoredIds.Contains(c.CandidateId))
            .ToDictionaryAsync(c => c.CandidateId, c => c.Score, ct);

        var orbitGravityTask = _db.OrbitGravities.AsNoTracking()
            .Where(g => scoredIds.Contains(g.UserId) && g.CandidateId == userId)
            .Select(g => new { g.UserId, g.Score, g.LastOrbitAt })
            .ToListAsync(ct);

        var visualPrefTask = _db.UserVisualPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(ct);

        var voicePrefTask = _db.UserVoicePreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(ct);

        var userOptionalTask = _db.UserOptionalFields.AsNoTracking()
            .Where(f => f.UserId == userId)
            .ToDictionaryAsync(f => f.Key, f => f.Value, ct);

        var learnedWeightsTask = _db.UserMatchingWeights.AsNoTracking()
            .Where(w => w.UserId == userId && w.SampleCount >= 5)
            .ToDictionaryAsync(w => w.Component, w => (double)w.LearnedWeight, ct);

        var seasonTask = _db.UserSeasonResponses.AsNoTracking()
            .AnyAsync(r => r.UserId == userId, ct);

        // Batch load candidate optional fields
        var candidateOptionalTask = _db.UserOptionalFields.AsNoTracking()
            .Where(f => scoredIds.Contains(f.UserId))
            .ToListAsync(ct);

        // Batch load candidate photo embeddings (primary photo per candidate)
        var candidatePhotosTask = _db.PhotoEmbeddings.AsNoTracking()
            .Where(p => scoredIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => g.OrderBy(p => p.EmbeddedAt).First())
            .ToListAsync(ct);

        // Batch load candidate trust scores
        var candidateTrustTask = _db.Users.AsNoTracking()
            .Where(u => scoredIds.Contains(u.Id))
            .Select(u => new { u.Id, u.TrustScore })
            .ToDictionaryAsync(u => u.Id, u => (double)u.TrustScore, ct);

        // Preload latest voice tile per candidate to avoid N+1 queries
        var candidateVoiceTilesTask = _db.Tiles.AsNoTracking()
            .Where(t => scoredIds.Contains(t.UserId) && t.VoiceEmbedding != null)
            .GroupBy(t => t.UserId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).First())
            .Select(t => new { t.UserId, t.VoiceEmbedding })
            .ToListAsync(ct);

        await Task.WhenAll(cfScoreMapTask, orbitGravityTask, visualPrefTask,
            voicePrefTask, userOptionalTask, learnedWeightsTask, seasonTask,
            candidateOptionalTask, candidatePhotosTask, candidateTrustTask,
            candidateVoiceTilesTask);

        var cfScoreMap = await cfScoreMapTask;
        var orbitGravities = await orbitGravityTask;
        var visualPref = await visualPrefTask;
        var voicePref = await voicePrefTask;
        var userFields = await userOptionalTask;
        var learnedWeightMap = await learnedWeightsTask;
        var hasSeasonResponse = await seasonTask;
        var allCandidateFields = await candidateOptionalTask;
        var candidatePhotos = (await candidatePhotosTask).ToDictionary(p => p.UserId);
        var trustScores = await candidateTrustTask;
        var candidateVoiceTiles = (await candidateVoiceTilesTask)
            .ToDictionary(t => t.UserId, t => t.VoiceEmbedding);

        // Group candidate fields by userId
        var candidateFieldsMap = allCandidateFields
            .GroupBy(f => f.UserId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(f => f.Key, f => f.Value));

        // Orbit gravity lookup
        var now = DateTimeOffset.UtcNow;
        var orbitMap = orbitGravities.ToDictionary(g => g.UserId, g => g);

        // Build learned weights array (or null to use base weights)
        double[]? learnedWeightsArray = null;
        if (learnedWeightMap.Count >= ComponentNames.Length / 2)
        {
            learnedWeightsArray = new double[ComponentNames.Length];
            for (int i = 0; i < ComponentNames.Length; i++)
                learnedWeightsArray[i] = learnedWeightMap.TryGetValue(ComponentNames[i], out var lw)
                    ? lw : BaseWeights[i];
        }

        var scores = new List<MatchScore>(candidateVectors.Count);

        foreach (var candidateVec in candidateVectors)
        {
            var score = new MatchScore(candidateVec.UserId);
            var available = new bool[14];
            var cvData = ParseVector(candidateVec.VectorJson);
            var cvPillarScores = ParsePillarScores(candidateVec.PillarScoresJson);
            var cvBehavioralLifestyle = ParseFloatArray(candidateVec.BehavioralLifestyleJson);

            // ── 0. PillarScore ────────────────────────────────────────────────────
            if (userVector.PillarEmbedding != null && candidateVec.PillarEmbedding != null)
            {
                score.PillarScore = CosineSimilarityToScore(
                    userVector.PillarEmbedding, candidateVec.PillarEmbedding);
                available[0] = true;
            }
            else
            {
                var pillarSim = userPillarScores.CosineSimilarity(cvPillarScores);
                score.PillarScore = pillarSim * 100;
                available[0] = true;
            }

            // ── 1. IntentScore ────────────────────────────────────────────────────
            var intentScore = ComputeIntentScore(userVectorData, cvData);
            if (intentScore.HasValue)
            {
                score.IntentScore = intentScore.Value;
                available[1] = true;
            }

            // ── 2. ExpressionScore ────────────────────────────────────────────────
            if (userVector.ExpressionEmbedding != null && candidateVec.ExpressionEmbedding != null)
            {
                score.ExpressionScore = CosineSimilarityToScore(
                    userVector.ExpressionEmbedding, candidateVec.ExpressionEmbedding);
                available[2] = true;
            }

            // ── 3. StyleScore ─────────────────────────────────────────────────────
            if (userVector.StyleEmbedding != null && candidateVec.StyleEmbedding != null)
            {
                score.StyleScore = CosineSimilarityToScore(
                    userVector.StyleEmbedding, candidateVec.StyleEmbedding);
                available[3] = true;
            }

            // ── 4. VisualPreference ───────────────────────────────────────────────
            if (visualPref?.PreferenceEmbedding != null && candidatePhotos.TryGetValue(candidateVec.UserId, out var candidatePhoto)
                && candidatePhoto.Embedding != null)
            {
                var dotProduct = DotProduct(visualPref.PreferenceEmbedding, candidatePhoto.Embedding);
                score.VisualScore = Math.Clamp(50 + dotProduct * 50, 0, 100);
                available[4] = true;
            }

            // ── 5. VoicePreference ────────────────────────────────────────────────
            if (voicePref?.PreferenceEmbedding != null &&
                candidateVoiceTiles.TryGetValue(candidateVec.UserId, out var candVoiceEmb) &&
                candVoiceEmb != null)
            {
                var dotProduct = DotProduct(voicePref.PreferenceEmbedding, candVoiceEmb);
                score.VoiceScore = Math.Clamp(50 + dotProduct * 50, 0, 100);
                available[5] = true;
            }

            // ── 6. HumorScore ─────────────────────────────────────────────────────
            if (userVector.HumorEmbedding != null && candidateVec.HumorEmbedding != null)
            {
                score.HumorScore = CosineSimilarityToScore(
                    userVector.HumorEmbedding, candidateVec.HumorEmbedding);
                available[6] = true;
            }

            // ── 7. LifestyleScore ─────────────────────────────────────────────────
            if (userVector.LifestyleEmbedding != null && candidateVec.LifestyleEmbedding != null)
            {
                score.LifestyleScore = CosineSimilarityToScore(
                    userVector.LifestyleEmbedding, candidateVec.LifestyleEmbedding);
                available[7] = true;
            }
            else
            {
                var cFields = candidateFieldsMap.GetValueOrDefault(candidateVec.UserId) ?? new Dictionary<string, string>();
                var lifestyleScore = ComputeLifestyleScore(userFields, cFields);
                score.LifestyleScore = lifestyleScore;
                available[7] = true;
            }

            // ── 8. BehavioralLifestyle ────────────────────────────────────────────
            if (userBehavioralLifestyle != null && cvBehavioralLifestyle != null)
            {
                score.BehavioralLifestyleScore = VectorCosineSimilarityToScore(userBehavioralLifestyle, cvBehavioralLifestyle);
                available[8] = true;
            }

            // ── 9. EmotionalRhythm ────────────────────────────────────────────────
            if (userVector.EmotionalRhythmEmbedding != null && candidateVec.EmotionalRhythmEmbedding != null)
            {
                score.EmotionalRhythmScore = CosineSimilarityToScore(
                    userVector.EmotionalRhythmEmbedding, candidateVec.EmotionalRhythmEmbedding);
                available[9] = true;
            }

            // ── 10. AttachmentScore ───────────────────────────────────────────────
            if (userVector.AttachmentProxyEmbedding != null && candidateVec.AttachmentProxyEmbedding != null)
            {
                score.AttachmentScore = CosineSimilarityToScore(
                    userVector.AttachmentProxyEmbedding, candidateVec.AttachmentProxyEmbedding);
                available[10] = true;
            }

            // ── 11. OrbitGravityScore ─────────────────────────────────────────────
            if (orbitMap.TryGetValue(candidateVec.UserId, out var gravity))
            {
                var daysSince = (now - gravity.LastOrbitAt).TotalDays;
                var decayed = gravity.Score * Math.Exp(-0.1 * daysSince);
                score.OrbitGravityScore = Math.Clamp(decayed, 0, 100);
                available[11] = true;
            }

            // ── 12. PulseScore ────────────────────────────────────────────────────
            var pulseScore = ComputePulseScore(userVectorData, cvData);
            if (pulseScore.HasValue)
            {
                score.PulseScore = pulseScore.Value;
                available[12] = true;
            }

            // ── 13. CFScore ───────────────────────────────────────────────────────
            if (cfScoreMap.TryGetValue(candidateVec.UserId, out var cfRaw))
            {
                score.CfScore = Math.Min(100.0, cfRaw * 100.0);
                available[13] = true;
            }

            // Intent multiplier
            var intentMult = ComputeIntentMultiplier(userVectorData, cvData);

            // Trust score
            var trust = trustScores.TryGetValue(candidateVec.UserId, out var t) ? t : 0.5;

            score.ComputeTotal(
                available,
                BaseWeights,
                learnedWeightsArray,
                intentMult,
                trust,
                hasSeasonResponse);

            scores.Add(score);
        }

        _logger.LogInformation("[Scoring] Scored {Count} candidates, avg={AvgScore:F2}",
            scores.Count, scores.Count > 0 ? scores.Average(s => s.TotalScore) : 0);

        return scores;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static double CosineSimilarityToScore(Vector a, Vector b)
    {
        var sim = CosineSimilarity(a.Memory.Span, b.Memory.Span);
        // Map [-1, 1] → [0, 100]
        return Math.Clamp((sim + 1.0) / 2.0 * 100.0, 0, 100);
    }

    private static double VectorCosineSimilarityToScore(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var sim = (normA == 0 || normB == 0) ? 0.0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Math.Clamp((sim + 1.0) / 2.0 * 100.0, 0, 100);
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return (normA == 0 || normB == 0) ? 0.0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static double DotProduct(Vector a, Vector b)
    {
        var aSpan = a.Memory.Span;
        var bSpan = b.Memory.Span;
        double dot = 0;
        for (int i = 0; i < aSpan.Length && i < bSpan.Length; i++)
            dot += aSpan[i] * bSpan[i];
        return dot;
    }

    private double? ComputeIntentScore(
        Dictionary<string, JsonElement> userVector,
        Dictionary<string, JsonElement> candidateVector)
    {
        var userIntent = GetIntentMetadata(userVector);
        var candidateIntent = GetIntentMetadata(candidateVector);
        if (userIntent == null || candidateIntent == null) return null;

        var seriousnessDiff = Math.Abs(userIntent.Seriousness - candidateIntent.Seriousness);
        var seriousnessScore = 100 * (1 - seriousnessDiff);

        var commitmentDiff = Math.Abs(userIntent.CommitmentReadiness - candidateIntent.CommitmentReadiness);
        var commitmentScore = 100 * (1 - commitmentDiff);

        var userTags = new HashSet<string>(userIntent.Tags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var candidateTags = new HashSet<string>(candidateIntent.Tags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var tagBonus = Math.Min(20, userTags.Intersect(candidateTags).Count() * 10);

        return Math.Clamp(seriousnessScore * 0.5 + commitmentScore * 0.3 + tagBonus, 0, 100);
    }

    private static double ComputeIntentMultiplier(
        Dictionary<string, JsonElement> userVector,
        Dictionary<string, JsonElement> candidateVector)
    {
        // Openness proxy: if intent seriousness difference is very low → full alignment
        try
        {
            if (userVector.TryGetValue("intent", out var ui) && candidateVector.TryGetValue("intent", out var ci))
            {
                var uIntent = JsonSerializer.Deserialize<IntentMetadata>(ui.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var cIntent = JsonSerializer.Deserialize<IntentMetadata>(ci.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (uIntent != null && cIntent != null)
                {
                    var diff = Math.Abs(uIntent.Seriousness - cIntent.Seriousness);
                    if (diff < 0.05) return 1.05; // exact alignment
                    var openness = 1.0 - diff;
                    if (openness > 0.6) return 1.00;
                    if (openness > 0.3) return 0.92;
                    if (openness > 0.0) return 0.82;
                }
            }
        }
        catch { }

        return 0.70; // no intent data
    }

    private double? ComputePulseScore(
        Dictionary<string, JsonElement> userVector,
        Dictionary<string, JsonElement> candidateVector)
    {
        var userPulse = GetPulseFeatures(userVector);
        var candidatePulse = GetPulseFeatures(candidateVector);
        if (userPulse.Count == 0 || candidatePulse.Count == 0) return null;

        double score = 50.0;

        if (userPulse.TryGetValue("socialCapacity", out var uCap) &&
            candidatePulse.TryGetValue("socialCapacity", out var cCap))
            score += 20 * (1 - Math.Abs(uCap - cCap));

        if (userPulse.TryGetValue("initiative", out var uInit) &&
            candidatePulse.TryGetValue("initiative", out var cInit))
        {
            var diff = Math.Abs(uInit - cInit);
            score += diff > 0.4 ? 15 : diff < 0.2 ? 5 : 0;
        }

        if (candidatePulse.TryGetValue("ghostRisk", out var ghostRisk) && ghostRisk > 0.6)
            score -= 10;

        return Math.Clamp(score, 0, 100);
    }

    private static double ComputeLifestyleScore(
        Dictionary<string, string> userFields,
        Dictionary<string, string> candidateFields)
    {
        double score = 50.0;

        string? Get(Dictionary<string, string> d, params string[] keys)
        {
            foreach (var k in keys)
                if (d.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
            return null;
        }

        bool IsMatch(string? a, string? b) =>
            a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        bool IsMismatch(string? a, string? b, string[] incompatible)
        {
            if (a == null || b == null) return false;
            var set = new HashSet<string>(incompatible, StringComparer.OrdinalIgnoreCase);
            return (set.Contains(a.Trim()) && !set.Contains(b.Trim())) ||
                   (set.Contains(b.Trim()) && !set.Contains(a.Trim()));
        }

        var uKids = Get(userFields, "children", "pref_children");
        var cKids = Get(candidateFields, "children", "pref_children");
        if (IsMismatch(uKids, cKids, new[] { "Never", "Want someday" })) score -= 30;
        else if (IsMatch(uKids, cKids)) score += 20;

        var uSmoke = Get(userFields, "pref_smoking", "smoking");
        var cSmoke = Get(candidateFields, "pref_smoking", "smoking");
        if (IsMatch(uSmoke, cSmoke)) score += 15; else if (uSmoke != null && cSmoke != null) score -= 15;

        var uDiet = Get(userFields, "diet");
        var cDiet = Get(candidateFields, "diet");
        if (IsMatch(uDiet, cDiet)) score += 10;
        else if (IsMismatch(uDiet, cDiet, new[] { "Vegan", "Vegetarian" })) score -= 10;

        if (IsMatch(Get(userFields, "pref_religion", "religion"), Get(candidateFields, "pref_religion", "religion"))) score += 10;

        if (userFields.TryGetValue("pref_drinking", out var uDrink) && candidateFields.TryGetValue("pref_drinking", out var cDrink))
        { if (IsMatch(uDrink, cDrink)) score += 10; else score -= 10; }

        if (userFields.TryGetValue("pref_workout", out var uWork) && candidateFields.TryGetValue("pref_workout", out var cWork))
        { if (IsMatch(uWork, cWork)) score += 8; else score -= 8; }

        return Math.Clamp(score, 0, 100);
    }

    // ── JSON parsers ──────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> ParseVector(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new(); }
        catch { return new(); }
    }

    private static PillarScores ParsePillarScores(string json)
    {
        try { return JsonSerializer.Deserialize<PillarScores>(json) ?? new(); }
        catch { return new(); }
    }

    private static float[]? ParseFloatArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<float[]>(json); }
        catch { return null; }
    }

    private static IntentMetadata? GetIntentMetadata(Dictionary<string, JsonElement> vector)
    {
        try
        {
            if (vector.TryGetValue("intent", out var el))
                return JsonSerializer.Deserialize<IntentMetadata>(el.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { }
        return null;
    }

    private static Dictionary<string, double> GetPulseFeatures(Dictionary<string, JsonElement> vector)
    {
        try
        {
            if (vector.TryGetValue("pulse", out var el))
                return JsonSerializer.Deserialize<Dictionary<string, double>>(el.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { }
        return new();
    }
}
