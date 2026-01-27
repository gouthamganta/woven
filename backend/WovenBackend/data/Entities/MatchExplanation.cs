namespace WovenBackend.Data.Entities;
using WovenBackend.Data; 

public class MatchExplanation
{
    public int Id { get; set; }

    // Viewer (who sees the explanation)
    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Match candidate
    public int CandidateId { get; set; }

    // Date this explanation was generated (matches DailyDeck date)
    public DateOnly DateUtc { get; set; }

    // One sentence headline
    // e.g., "You both want something that can grow, with an easy pace."
    public string Headline { get; set; } = "";

    // Max 2 bullet points as JSON array:
    // [
    //   "Shared: relationship-forward but flexible mindset",
    //   "Lifestyle: similar approach to health + routines"
    // ]
    public string BulletsJson { get; set; } = "[]";

    // Tone used for generation: "playful", "calm", "serious"
    public string Tone { get; set; } = "calm";

    // ✅ NEW: Date idea shown after Find Love unlocks
    // e.g., "Grab coffee and explore a new neighborhood together"
    public string? DateIdea { get; set; } = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}