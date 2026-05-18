using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Services.Matchmaking;

public class WeightLearningService : IWeightLearningService
{
    private const int MinOutcomes = 5;

    // Component names matching the scoring formula
    private static readonly string[] Components = new[]
    {
        "pillar", "intent", "expression", "style", "visual",
        "voice", "humor", "lifestyle", "behavioral_lifestyle",
        "emotional_rhythm", "attachment", "orbit_gravity", "pulse", "cf"
    };

    private static readonly double[] DefaultWeights = new[]
    {
        0.20, 0.13, 0.10, 0.09, 0.10,
        0.08, 0.07, 0.08, 0.05,
        0.04, 0.04, 0.08, 0.06, 0.03
    };

    private readonly WovenDbContext _db;
    private readonly IMatchScoringService _scoring;
    private readonly IAnalyticsService _analytics;
    private readonly ILogger<WeightLearningService> _logger;

    public WeightLearningService(
        WovenDbContext db,
        IMatchScoringService scoring,
        IAnalyticsService analytics,
        ILogger<WeightLearningService> logger)
    {
        _db = db;
        _scoring = scoring;
        _analytics = analytics;
        _logger = logger;
    }

    public async Task LearnWeightsAsync(int userId, CancellationToken ct = default)
    {
        var outcomes = await _db.MatchOutcomes.AsNoTracking()
            .Where(o => o.UserId == userId && !o.Expired)
            .Select(o => new { o.CandidateId, o.Messages24h, o.ChatStarted, o.Blocked, o.Unmatched })
            .ToListAsync(ct);

        if (outcomes.Count < MinOutcomes)
        {
            _logger.LogInformation("[WeightLearning] Skipping user {UserId} — only {N} outcomes", userId, outcomes.Count);
            return;
        }

        // Compute scores for all candidate pairs
        var candidateIds = outcomes.Select(o => o.CandidateId).Distinct().ToList();
        var scores = await _scoring.ScoreCandidatesAsync(userId, candidateIds, ct);
        if (scores.Count == 0) return;

        var scoreMap = scores.ToDictionary(s => s.CandidateId);

        // Build feature matrix and labels
        var featureMatrix = new List<double[]>();
        var labels = new List<double>();

        foreach (var outcome in outcomes)
        {
            if (!scoreMap.TryGetValue(outcome.CandidateId, out var score)) continue;

            // Positive signal: engaged meaningfully
            var positive = (outcome.Messages24h > 15 || outcome.ChatStarted) && !outcome.Blocked;
            // Negative signal: blocked or unmatched quickly
            var negative = outcome.Blocked || outcome.Unmatched;

            if (!positive && !negative) continue; // no clear signal

            featureMatrix.Add(new[]
            {
                score.PillarScore / 100.0,
                score.IntentScore / 100.0,
                score.ExpressionScore / 100.0,
                score.StyleScore / 100.0,
                score.VisualScore / 100.0,
                score.VoiceScore / 100.0,
                score.HumorScore / 100.0,
                score.LifestyleScore / 100.0,
                score.BehavioralLifestyleScore / 100.0,
                score.EmotionalRhythmScore / 100.0,
                score.AttachmentScore / 100.0,
                score.OrbitGravityScore / 100.0,
                score.PulseScore / 100.0,
                score.CfScore / 100.0
            });
            labels.Add(positive ? 1.0 : 0.0);
        }

        if (featureMatrix.Count < MinOutcomes) return;

        // Compute per-component correlation with positive label
        var gradients = ComputeCorrelationGradients(featureMatrix, labels);

        // Update weights via gradient: learned_weight = default + 0.1 * gradient
        var existingWeights = await _db.UserMatchingWeights.AsNoTracking()
            .Where(w => w.UserId == userId)
            .ToDictionaryAsync(w => w.Component, ct);

        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < Components.Length; i++)
        {
            var component = Components[i];
            var defaultW = DefaultWeights[i];
            var learnedW = (float)Math.Clamp(defaultW + 0.1 * gradients[i], 0.01, 0.5);

            if (existingWeights.TryGetValue(component, out _))
            {
                var row = await _db.UserMatchingWeights
                    .FirstAsync(w => w.UserId == userId && w.Component == component, ct);
                row.LearnedWeight = learnedW;
                row.SampleCount = featureMatrix.Count;
                row.UpdatedAt = now;
            }
            else
            {
                _db.UserMatchingWeights.Add(new UserMatchingWeight
                {
                    UserId = userId,
                    Component = component,
                    LearnedWeight = learnedW,
                    SampleCount = featureMatrix.Count,
                    UpdatedAt = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[WeightLearning] Learned weights for user {UserId} from {N} outcomes", userId, featureMatrix.Count);

        var topComponent = Components.Length > 0 ? Components[0] : "pillar";
        _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.WeightLearningRun,
            new { sampleCount = featureMatrix.Count, topComponent });
    }

    private static double[] ComputeCorrelationGradients(List<double[]> features, List<double> labels)
    {
        int n = features.Count;
        int d = features[0].Length;
        var gradients = new double[d];

        var labelMean = labels.Average();

        for (int j = 0; j < d; j++)
        {
            var featureValues = features.Select(f => f[j]).ToList();
            var featureMean = featureValues.Average();

            double cov = 0, varF = 0, varL = 0;
            for (int i = 0; i < n; i++)
            {
                var df = featureValues[i] - featureMean;
                var dl = labels[i] - labelMean;
                cov += df * dl;
                varF += df * df;
                varL += dl * dl;
            }

            // Pearson correlation coefficient
            var denom = Math.Sqrt(varF * varL);
            gradients[j] = denom > 0 ? cov / denom : 0;
        }

        return gradients;
    }
}
