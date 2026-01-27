namespace WovenBackend.Services.Matchmaking;

public interface IDailyDeckOrchestrator
{
    Task<DailyDeckResult> GetOrCreateDeckAsync(int userId, DateOnly dateUtc, CancellationToken ct = default);
}

public class DailyDeckResult
{
    public List<DeckItem> Items { get; set; } = new();
    public bool Generated { get; set; } // True if freshly generated
}

public class DeckItem
{
    public int CandidateId { get; set; }
    public double Score { get; set; }
    public string Bucket { get; set; } = "";
    public int ExplanationId { get; set; }
}