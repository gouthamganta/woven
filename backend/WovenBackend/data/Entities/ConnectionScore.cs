namespace WovenBackend.Data.Entities;

public class ConnectionScore
{
    public long Id { get; set; }

    public int ViewerId { get; set; }
    public int CandidateId { get; set; }

    // Composite outcome score [0.0, 1.0] — see ConnectionScoreBatchWorker for formula
    public float Score { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
