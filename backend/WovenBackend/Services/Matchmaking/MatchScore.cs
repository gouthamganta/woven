namespace WovenBackend.Services.Matchmaking;

public class MatchScore
{
    public int CandidateId { get; set; }
    public double IntentScore { get; set; }
    public double FoundationalScore { get; set; }
    public double LifestyleScore { get; set; }
    public double PulseScore { get; set; }
    public double TotalScore { get; set; }

    public MatchScore(int candidateId)
    {
        CandidateId = candidateId;
    }

    public void ComputeTotal()
    {
        // Weights from blueprint: Intent 35%, Foundational 30%, Lifestyle 20%, Pulse 15%
        TotalScore = (IntentScore * 0.35) +
                     (FoundationalScore * 0.30) +
                     (LifestyleScore * 0.20) +
                     (PulseScore * 0.15);

        // Clamp to 0-100
        TotalScore = Math.Max(0, Math.Min(100, TotalScore));
    }
}