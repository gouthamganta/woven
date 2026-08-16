namespace WovenBackend.Data.Entities;

/// <summary>
/// Normalized deck item - replaces DailyDeck.ItemsJson JSON blob.
/// Each row represents one candidate shown in a user's daily deck.
/// </summary>
public class DailyDeckItem
{
    public long Id { get; set; }

    public int DeckId { get; set; }
    public DailyDeck Deck { get; set; } = default!;

    public int CandidateId { get; set; }
    public User Candidate { get; set; } = default!;

    /// <summary>
    /// Total match score (0-100) at time of selection
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Selection bucket: CORE_FIT, LIFESTYLE_FIT, CONVERSATION_FIT, EXPLORER, WILDCARD
    /// </summary>
    public string Bucket { get; set; } = "";

    /// <summary>
    /// Position in deck (1-5, where 1 is first shown)
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// When this deck item was created (deck generation time)
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
