using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Analytics;

namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// ECHO Phase 4 — logistic regression weight learner.
///
/// Label source: ConnectionScore.Score [0,1] per (viewer, candidate) pair.
///   Replaces the old MatchOutcomes binary label (positive/negative).
///   ConnectionScore is a richer composite from 7 behavioral signals
///   (balloon pop, trial accepted, message depth, date interest, feedback, love reactions).
///
/// Algorithm: mini-batch gradient ascent on log-likelihood (equivalent to minimising
/// binary cross-entropy when labels are treated as pseudo-probabilities):
///   ŷ  = σ(w · x)
///   Δw = lr × [Σ (y − ŷ) × x] − 2λw    (L2 regularisation term)
///   w  ← clip(w, 0.01, 0.50)
///
/// Weights are normalised to sum to 1 after learning so the scoring formula
/// remains a proper weighted average.
/// </summary>
public class WeightLearningService : IWeightLearningService
{
    private const int MinSamples = 10;
    private const double LearningRate = 0.01;
    private const int Iterations = 100;
    private const double L2Lambda = 0.01;
    private const double MinWeight = 0.01;
    private const double MaxWeight = 0.50;

    // Minimum ConnectionScore to include a pair as a training sample.
    // Pairs with only a BalloonPop (score ≈ 0.05) give almost no signal about
    // component compatibility — we need at least some conversation depth.
    private const float MinConnectionScore = 0.08f;

    private static readonly string[] Components = new[]
    {
        "pillar", "intent", "expression", "style", "visual",
        "voice", "humor", "lifestyle", "behavioral_lifestyle",
        "emotional_rhythm", "attachment", "orbit_gravity", "pulse", "cf",
        "shared_tile_affinity", "preference_affinity"
    };

    private static readonly double[] DefaultWeights = new[]
    {
        0.19, 0.12, 0.09, 0.09, 0.10,
        0.08, 0.07, 0.08, 0.05,
        0.04, 0.04, 0.08, 0.06, 0.03, 0.05, 0.04
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
        // NarrationExposed baseline protection:
        // When Cinematic:NarrationLaunchDate is set, this block should filter out
        // ConnectionScores for pairs where the choice was made with NarrationExposed=true,
        // for 30 days post-launch. This prevents ECHO weights from being biased by
        // the cinematic experience itself.
        // Currently NarrationExposed is always false → no-op. Wire this once narration goes live.

        // Load ConnectionScores for this viewer — ECHO's ground-truth outcome labels
        var connectionScores = await _db.ConnectionScores.AsNoTracking()
            .Where(c => c.ViewerId == userId && c.Score >= MinConnectionScore)
            .Select(c => new { c.CandidateId, c.Score })
            .ToListAsync(ct);

        if (connectionScores.Count < MinSamples)
        {
            _logger.LogInformation("[WeightLearning] Skipping user {UserId} — only {N} qualifying connection scores (need {Min})",
                userId, connectionScores.Count, MinSamples);
            return;
        }

        // Score the candidates to get component feature vectors
        var candidateIds = connectionScores.Select(c => c.CandidateId).Distinct().ToList();
        var matchScores = await _scoring.ScoreCandidatesAsync(userId, candidateIds, ct);
        if (matchScores.Count == 0) return;

        var scoreMap = matchScores.ToDictionary(s => s.CandidateId);

        // Build feature matrix X and label vector y
        var X = new List<double[]>();
        var y = new List<double>();

        foreach (var cs in connectionScores)
        {
            if (!scoreMap.TryGetValue(cs.CandidateId, out var ms)) continue;

            X.Add(new[]
            {
                ms.PillarScore              / 100.0,
                ms.IntentScore              / 100.0,
                ms.ExpressionScore          / 100.0,
                ms.StyleScore               / 100.0,
                ms.VisualScore              / 100.0,
                ms.VoiceScore               / 100.0,
                ms.HumorScore               / 100.0,
                ms.LifestyleScore           / 100.0,
                ms.BehavioralLifestyleScore / 100.0,
                ms.EmotionalRhythmScore     / 100.0,
                ms.AttachmentScore          / 100.0,
                ms.OrbitGravityScore        / 100.0,
                ms.PulseScore               / 100.0,
                ms.CfScore                  / 100.0,
                ms.SharedTileAffinityScore  / 100.0,
                ms.PreferenceAffinityScore  / 100.0
            });
            y.Add(cs.Score); // ConnectionScore [0,1] as pseudo-probability label
        }

        if (X.Count < MinSamples)
        {
            _logger.LogInformation("[WeightLearning] Skipping user {UserId} — only {N} matched samples after scoring", userId, X.Count);
            return;
        }

        // Initialise weights from existing learned weights (warm start) or defaults
        var existingWeights = await _db.UserMatchingWeights.AsNoTracking()
            .Where(w => w.UserId == userId)
            .ToDictionaryAsync(w => w.Component, w => (double)w.LearnedWeight, ct);

        var w = new double[Components.Length];
        for (int j = 0; j < Components.Length; j++)
            w[j] = existingWeights.TryGetValue(Components[j], out var lw) ? lw : DefaultWeights[j];

        // Gradient ascent on log-likelihood with L2 regularisation
        int n = X.Count;
        for (int iter = 0; iter < Iterations; iter++)
        {
            var grad = new double[Components.Length];

            for (int i = 0; i < n; i++)
            {
                var predicted = Sigmoid(Dot(w, X[i]));
                var residual  = y[i] - predicted;
                for (int j = 0; j < Components.Length; j++)
                    grad[j] += residual * X[i][j];
            }

            // Apply gradient + L2 penalty
            for (int j = 0; j < Components.Length; j++)
            {
                w[j] += LearningRate * (grad[j] / n - 2 * L2Lambda * w[j]);
                w[j]  = Math.Clamp(w[j], MinWeight, MaxWeight);
            }
        }

        // Normalise so weights sum to 1 (scoring formula stays a proper weighted average)
        var total = w.Sum();
        if (total > 0)
            for (int j = 0; j < Components.Length; j++)
                w[j] = Math.Clamp(w[j] / total, MinWeight, MaxWeight);

        // Persist
        var now = DateTimeOffset.UtcNow;
        var dbWeights = await _db.UserMatchingWeights
            .Where(ww => ww.UserId == userId)
            .ToDictionaryAsync(ww => ww.Component, ct);

        for (int j = 0; j < Components.Length; j++)
        {
            var comp   = Components[j];
            var learned = (float)w[j];

            if (dbWeights.TryGetValue(comp, out var row))
            {
                row.LearnedWeight = learned;
                row.SampleCount   = n;
                row.UpdatedAt     = now;
            }
            else
            {
                _db.UserMatchingWeights.Add(new UserMatchingWeight
                {
                    UserId        = userId,
                    Component     = comp,
                    LearnedWeight = learned,
                    SampleCount   = n,
                    UpdatedAt     = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        // Log which component got the highest learned weight for analytics
        var topIdx       = Array.IndexOf(w, w.Max());
        var topComponent = Components[topIdx];

        _logger.LogInformation(
            "[WeightLearning] User {UserId}: learned weights from {N} samples, top={Top} ({Val:F3})",
            userId, n, topComponent, w[topIdx]);

        _ = _analytics.TrackAsync(userId, null, AnalyticsEvents.WeightLearningRun,
            new { sampleCount = n, topComponent });
    }

    private static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
