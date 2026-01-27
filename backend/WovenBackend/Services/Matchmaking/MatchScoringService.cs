using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services.Matchmaking;

public class MatchScoringService : IMatchScoringService
{
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

        var userVectorData = ParseVector(userVector.VectorJson);
        var userPillarScores = ParsePillarScores(userVector.PillarScoresJson);

        // ✅ Load each candidate's LATEST vector correctly
        var candidateVectors = await _db.UserVectors
            .Where(v => candidateIds.Contains(v.UserId))
            .GroupBy(v => v.UserId)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .ToListAsync(ct);

        if (candidateVectors.Count == 0)
        {
            _logger.LogWarning("[Scoring] No candidate vectors found for user {UserId}", userId);
            return new List<MatchScore>();
        }

        var scores = new List<MatchScore>(candidateVectors.Count);

        foreach (var candidateVector in candidateVectors)
        {
            var score = new MatchScore(candidateVector.UserId);

            var candidateVectorData = ParseVector(candidateVector.VectorJson);
            var candidatePillarScores = ParsePillarScores(candidateVector.PillarScoresJson);

            // 1) Intent
            score.IntentScore = ComputeIntentScore(userVectorData, candidateVectorData);

            // 2) Foundational
            score.FoundationalScore = ComputeFoundationalScore(
                userPillarScores,
                candidatePillarScores,
                userVectorData,
                candidateVectorData);

            // 3) Lifestyle
            score.LifestyleScore = await ComputeLifestyleScoreAsync(userId, candidateVector.UserId, ct);

            // 4) Pulse
            score.PulseScore = ComputePulseScore(userVectorData, candidateVectorData);

            score.ComputeTotal();
            scores.Add(score);
        }

        _logger.LogInformation("[Scoring] Scored {Count} candidates, avg score: {AvgScore:F2}",
            scores.Count, scores.Average(s => s.TotalScore));

        return scores;
    }

    private double ComputeIntentScore(
        Dictionary<string, JsonElement> userVector,
        Dictionary<string, JsonElement> candidateVector)
    {
        var userIntent = GetIntentMetadata(userVector);
        var candidateIntent = GetIntentMetadata(candidateVector);

        if (userIntent == null || candidateIntent == null)
            return 50.0;

        var seriousnessDiff = Math.Abs(userIntent.Seriousness - candidateIntent.Seriousness);
        var seriousnessScore = 100 * (1 - seriousnessDiff);

        var commitmentDiff = Math.Abs(userIntent.CommitmentReadiness - candidateIntent.CommitmentReadiness);
        var commitmentScore = 100 * (1 - commitmentDiff);

        var userTags = new HashSet<string>(userIntent.Tags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var candidateTags = new HashSet<string>(candidateIntent.Tags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var overlap = userTags.Intersect(candidateTags).Count();
        var tagBonus = Math.Min(20, overlap * 10);

        var intentScore = (seriousnessScore * 0.5) + (commitmentScore * 0.3) + tagBonus;
        return Math.Max(0, Math.Min(100, intentScore));
    }

    private double ComputeFoundationalScore(
        PillarScores userPillars,
        PillarScores candidatePillars,
        Dictionary<string, JsonElement> userVector,
        Dictionary<string, JsonElement> candidateVector)
    {
        // Calculate signal strength (variance from neutral 0.5)
        var userVariance = CalculatePillarVariance(userPillars);
        var candidateVariance = CalculatePillarVariance(candidatePillars);

        // If either user has very low signal (all pillars near 0.5), return neutral score
        // This prevents the "everyone matches everyone at 95%" problem
        const double LowSignalThreshold = 0.05;
        if (userVariance < LowSignalThreshold || candidateVariance < LowSignalThreshold)
        {
            _logger.LogInformation("[Scoring] Low signal detected - user variance: {UserVar:F4}, candidate variance: {CandVar:F4}",
                userVariance, candidateVariance);
            return 50.0; // Neutral score when we don't have enough data
        }

        var similarity = userPillars.CosineSimilarity(candidatePillars);

        // Dampen similarity by signal strength to avoid over-scoring low-data profiles
        var signalStrength = Math.Min(userVariance, candidateVariance) / 0.15; // Normalize: 0.15 variance = full signal
        signalStrength = Math.Min(1.0, signalStrength); // Cap at 1.0

        var pillarScore = similarity * 100 * signalStrength;

        var userTags = GetFoundationalTags(userVector);
        var candidateTags = GetFoundationalTags(candidateVector);

        var overlapCount = 0;

        foreach (var category in userTags.Keys)
        {
            if (!candidateTags.ContainsKey(category)) continue;

            var userCategoryTags = new HashSet<string>(userTags[category], StringComparer.OrdinalIgnoreCase);
            var candidateCategoryTags = new HashSet<string>(candidateTags[category], StringComparer.OrdinalIgnoreCase);

            overlapCount += userCategoryTags.Intersect(candidateCategoryTags).Count();
        }

        var tagBonus = Math.Min(20, overlapCount * 3);
        var foundationalScore = pillarScore + tagBonus;

        return Math.Max(0, Math.Min(100, foundationalScore));
    }

    /// <summary>
    /// Calculates the variance of pillar scores from the neutral value (0.5).
    /// Higher variance = more meaningful signal from the user's answers.
    /// </summary>
    private double CalculatePillarVariance(PillarScores pillars)
    {
        var values = pillars.ToArray();
        const double Neutral = 0.5;

        double sumSquaredDiff = 0;
        foreach (var value in values)
        {
            var diff = value - Neutral;
            sumSquaredDiff += diff * diff;
        }

        return sumSquaredDiff / values.Length;
    }

    private async Task<double> ComputeLifestyleScoreAsync(int userId, int candidateId, CancellationToken ct)
    {
        var userFields = await _db.UserOptionalFields
            .Where(f => f.UserId == userId)
            .ToDictionaryAsync(f => f.Key, f => f.Value, ct);

        var candidateFields = await _db.UserOptionalFields
            .Where(f => f.UserId == candidateId)
            .ToDictionaryAsync(f => f.Key, f => f.Value, ct);

        double score = 50.0;

        // ✅ Helpers to avoid key-mismatch silent failures
        string? Get(Dictionary<string, string> dict, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (dict.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v;
            }
            return null;
        }

        // Kids
        var userKids = Get(userFields, "children", "pref_children");
        var candidateKids = Get(candidateFields, "children", "pref_children");
        if (userKids != null && candidateKids != null)
        {
            if (IsMismatch(userKids, candidateKids, new[] { "Never", "Want someday" }))
                score -= 30;
            else if (IsMatch(userKids, candidateKids))
                score += 20;
        }

        // Smoking
        var userSmoking = Get(userFields, "pref_smoking", "smoking");
        var candidateSmoking = Get(candidateFields, "pref_smoking", "smoking");
        if (userSmoking != null && candidateSmoking != null)
        {
            if (IsMatch(userSmoking, candidateSmoking))
                score += 15;
            else
                score -= 15;
        }

        // Diet
        var userDiet = Get(userFields, "diet");
        var candidateDiet = Get(candidateFields, "diet");
        if (userDiet != null && candidateDiet != null)
        {
            if (IsMatch(userDiet, candidateDiet))
                score += 10;
            else if (IsMismatch(userDiet, candidateDiet, new[] { "Vegan", "Vegetarian" }))
                score -= 10;
        }

        // Religion (bonus only)
        var userReligion = Get(userFields, "pref_religion", "religion");
        var candidateReligion = Get(candidateFields, "pref_religion", "religion");
        if (userReligion != null && candidateReligion != null)
        {
            if (IsMatch(userReligion, candidateReligion))
                score += 10;
        }

        // Drinking
        if (userFields.TryGetValue("pref_drinking", out var userDrink) &&
            candidateFields.TryGetValue("pref_drinking", out var candDrink))
        {
            if (IsMatch(userDrink, candDrink)) score += 10;
            else score -= 10;
        }

        // Workout
        if (userFields.TryGetValue("pref_workout", out var userWorkout) &&
            candidateFields.TryGetValue("pref_workout", out var candWorkout))
        {
            if (IsMatch(userWorkout, candWorkout)) score += 8;
            else score -= 8;
        }

        // Height (bonus-only)
        if (userFields.TryGetValue("pref_height", out var userHeightPref) &&
            candidateFields.TryGetValue("pref_height", out var candHeightPref))
        {
            if (IsMatch(userHeightPref, candHeightPref)) score += 5;
        }

        // Work (bonus-only)
        if (userFields.TryGetValue("pref_work", out var userWorkPref) &&
            candidateFields.TryGetValue("pref_work", out var candWorkPref))
        {
            if (IsMatch(userWorkPref, candWorkPref)) score += 5;
        }

        // Ethnicity (bonus-only)
        if (userFields.TryGetValue("pref_ethnicity", out var userEthPref) &&
            candidateFields.TryGetValue("pref_ethnicity", out var candEthPref))
        {
            if (IsMatch(userEthPref, candEthPref)) score += 3;
        }

        return Math.Max(0, Math.Min(100, score));
    }

    private double ComputePulseScore(
        Dictionary<string, JsonElement> userVector,
        Dictionary<string, JsonElement> candidateVector)
    {
        var userPulse = GetPulseFeatures(userVector);
        var candidatePulse = GetPulseFeatures(candidateVector);

        if (userPulse.Count == 0 || candidatePulse.Count == 0)
            return 50.0;

        double score = 50.0;

        if (userPulse.TryGetValue("socialCapacity", out var userCapacity) &&
            candidatePulse.TryGetValue("socialCapacity", out var candidateCapacity))
        {
            var diff = Math.Abs(userCapacity - candidateCapacity);
            score += 20 * (1 - diff);
        }

        if (userPulse.TryGetValue("initiative", out var userInitiative) &&
            candidatePulse.TryGetValue("initiative", out var candidateInitiative))
        {
            var diff = Math.Abs(userInitiative - candidateInitiative);

            if (diff > 0.4) score += 15;
            else if (diff < 0.2) score += 5;
        }

        if (candidatePulse.TryGetValue("ghostRisk", out var ghostRisk) && ghostRisk > 0.6)
            score -= 10;

        return Math.Max(0, Math.Min(100, score));
    }

    private Dictionary<string, JsonElement> ParseVector(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private PillarScores ParsePillarScores(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PillarScores>(json) ?? new PillarScores();
        }
        catch
        {
            return new PillarScores();
        }
    }

    private IntentMetadata? GetIntentMetadata(Dictionary<string, JsonElement> vector)
    {
        try
        {
            if (vector.TryGetValue("intent", out var intentElement))
                return JsonSerializer.Deserialize<IntentMetadata>(intentElement.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { }

        return null;
    }

    private Dictionary<string, List<string>> GetFoundationalTags(Dictionary<string, JsonElement> vector)
    {
        try
        {
            if (vector.TryGetValue("foundational", out var foundationalElement))
            {
                var foundational = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    foundationalElement.GetRawText());

                if (foundational != null && foundational.TryGetValue("tags", out var tagsElement))
                {
                    return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                               tagsElement.GetRawText(),
                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? new Dictionary<string, List<string>>();
                }
            }
        }
        catch { }

        return new Dictionary<string, List<string>>();
    }

    private Dictionary<string, double> GetPulseFeatures(Dictionary<string, JsonElement> vector)
    {
        try
        {
            if (vector.TryGetValue("pulse", out var pulseElement))
            {
                return JsonSerializer.Deserialize<Dictionary<string, double>>(
                           pulseElement.GetRawText(),
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new Dictionary<string, double>();
            }
        }
        catch { }

        return new Dictionary<string, double>();
    }

    private bool IsMatch(string value1, string value2)
        => string.Equals(value1?.Trim(), value2?.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool IsMismatch(string value1, string value2, string[] incompatibleValues)
    {
        var set = new HashSet<string>(incompatibleValues, StringComparer.OrdinalIgnoreCase);
        var v1 = value1?.Trim() ?? "";
        var v2 = value2?.Trim() ?? "";

        return (set.Contains(v1) && !set.Contains(v2)) ||
               (set.Contains(v2) && !set.Contains(v1));
    }
}