namespace WovenBackend.Data.Entities;
using WovenBackend.Data; 
public class UserVector
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Version 1, 2, 3... (incremented on major profile changes)
    public int Version { get; set; }

    // Complete matchable state as JSON:
    // {
    //   "intent": { "style": {...}, "tags": [...] },
    //   "foundational": { "pillars": {...}, "tags": {...} },
    //   "lifestyle": { "kids": "...", "smoking": "...", ... },
    //   "pulse": { "features": {...} }
    // }
    public string VectorJson { get; set; } = "{}";

    // 8 pillar scores (0.0-1.0) as JSON:
    // {
    //   "Lifestyle": 0.78,
    //   "Energy": 0.62,
    //   "Values": 0.85,
    //   "Communication": 0.55,
    //   "Ambition": 0.43,
    //   "Stability": 0.70,
    //   "Curiosity": 0.66,
    //   "Affection": 0.58
    // }
    public string PillarScoresJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}