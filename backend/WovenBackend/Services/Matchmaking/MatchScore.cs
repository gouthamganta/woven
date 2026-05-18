namespace WovenBackend.Services.Matchmaking;

public class MatchScore
{
    public int CandidateId { get; set; }

    // Phase 3D: 14-component scoring (PreferenceScore always NULL, redistributed)
    public double PillarScore { get; set; } = 50.0;
    public double IntentScore { get; set; } = 50.0;
    public double ExpressionScore { get; set; } = 50.0;
    public double StyleScore { get; set; } = 50.0;
    public double VisualScore { get; set; } = 50.0;
    public double VoiceScore { get; set; } = 50.0;
    public double HumorScore { get; set; } = 50.0;
    public double LifestyleScore { get; set; } = 50.0;
    public double BehavioralLifestyleScore { get; set; } = 50.0;
    public double EmotionalRhythmScore { get; set; } = 50.0;
    public double AttachmentScore { get; set; } = 50.0;
    public double OrbitGravityScore { get; set; } = 50.0;
    public double PulseScore { get; set; } = 50.0;
    public double CfScore { get; set; } = 50.0;

    // Backward-compat alias used by DeckSelectionService and MatchExplanationService
    public double FoundationalScore => PillarScore;

    // Depth and metadata
    public int DepthSignals { get; set; }  // count of non-null components beyond pillar+intent
    public double TotalScore { get; set; }
    public string Bucket { get; set; } = "EXPLORER";

    public MatchScore(int candidateId)
    {
        CandidateId = candidateId;
    }

    /// <summary>
    /// Computes TotalScore using dynamic weight redistribution.
    /// Weights for NULL components are redistributed proportionally to available ones.
    /// Applies depth boost, negative dampening, and sets Bucket.
    /// </summary>
    public void ComputeTotal(
        bool[] componentAvailable,
        double[] baseWeights,
        double[]? learnedWeights = null,
        double intentMultiplier = 1.0,
        double trustScore = 1.0,
        bool hasSeasonResponse = false)
    {
        var scores = new double[]
        {
            PillarScore, IntentScore, ExpressionScore, StyleScore, VisualScore,
            VoiceScore, HumorScore, LifestyleScore, BehavioralLifestyleScore,
            EmotionalRhythmScore, AttachmentScore, OrbitGravityScore, PulseScore, CfScore
        };

        // Use learned weights if sample_count >= 5, else base weights
        var weights = learnedWeights ?? baseWeights;

        // Dynamic redistribution: sum only available weights, then normalize
        double totalWeight = 0;
        for (int i = 0; i < weights.Length; i++)
            if (componentAvailable[i]) totalWeight += weights[i];

        if (totalWeight <= 0) { TotalScore = 50; Bucket = "EXPLORER"; return; }

        double rawScore = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            if (!componentAvailable[i]) continue;
            var normalizedWeight = weights[i] / totalWeight;
            rawScore += scores[i] * normalizedWeight;
        }

        // Depth boost: each additional signal beyond pillar+intent adds up to +15 total
        DepthSignals = componentAvailable.Skip(2).Count(a => a);
        var depthBoost = Math.Min(15.0, DepthSignals * 2.5);
        rawScore = Math.Min(100, rawScore + depthBoost);

        // Season freshness bonus
        if (hasSeasonResponse) rawScore = Math.Min(100, rawScore + 5);

        // Intent multiplier
        rawScore *= intentMultiplier;

        // Trust dampening (trust score as a multiplier)
        rawScore *= Math.Clamp((double)trustScore, 0, 1);

        TotalScore = Math.Clamp(rawScore, 0, 100);

        Bucket = TotalScore >= 80 ? "STRONG"
               : TotalScore >= 60 ? "GOOD"
               : TotalScore >= 40 ? "OK"
               : "EXPLORER";
    }
}
