namespace WovenBackend.Services.Matchmaking;

public interface IDailyDeckOrchestrator
{
    Task<DailyDeckResult> GetOrCreateDeckAsync(int userId, DateOnly dateUtc, CancellationToken ct = default);
}

public class DailyDeckResult
{
    public List<DeckItem> Items { get; set; } = new();
    public bool Generated { get; set; } // True if freshly generated
    public string? MoodLine { get; set; }
}

public class DeckItem
{
    public int CandidateId { get; set; }
    public double Score { get; set; }
    public string Bucket { get; set; } = "";
    public int ExplanationId { get; set; }

    // Cinematic intro fields (Build N+1 populates; null → silent skip)
    public string[]? KenBurnsPhotoUrls { get; set; }
    public string? CuratedQuote { get; set; }
    public string? NarrationUrl { get; set; }
    public bool NarrationExposed { get; set; } = false;
}