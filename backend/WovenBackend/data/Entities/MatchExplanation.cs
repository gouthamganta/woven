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

    // Date idea shown after Find Love unlocks (single idea, legacy)
    public string? DateIdea { get; set; } = null;

    // JSON array of 3 date ideas; falls back to [DateIdea] for old rows
    public string? DateIdeasJson { get; set; } = null;

    // ECHO-generated opening question specific to this pair.
    // Shown to the viewer post-choice. Pre-loaded as chat opener if they match.
    public string? BridgeQuestion { get; set; } = null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetDateIdeas()
    {
        if (!string.IsNullOrEmpty(DateIdeasJson))
        {
            try
            {
                var ideas = System.Text.Json.JsonSerializer.Deserialize<List<string>>(DateIdeasJson);
                if (ideas?.Count > 0) return ideas;
            }
            catch { }
        }
        return string.IsNullOrEmpty(DateIdea) ? [] : [DateIdea];
    }
}