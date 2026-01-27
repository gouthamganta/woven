namespace WovenBackend.Data.Entities;

public class UserFoundationalQuestionSet
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // v1, v2, v3, ...
    public int Version { get; set; }

    // [{ "id": "FND_V1_VALUES_INTENT", "text": "..." }, ...] (exactly 5)
    public string QuestionsJson { get; set; } = "[]";

    // [{ "id": "FND_V1_VALUES_INTENT", "a": "..." }, ...] (exactly 5)
    public string AnswersJson { get; set; } = "[]";

    // Later AI extraction (your 8-pillars signals)
    // { "values": {...}, "ops": {...}, "heart": {...}, ... }
    public string SignalsJson { get; set; } = "{}";

    // ✅ NEW (optional but highly recommended)
    // "base" | "ai"
    public string QuestionsSource { get; set; } = "base";

    // hash of QuestionsJson (for debugging + drift detection)
    public string? QuestionsHash { get; set; }

    // metadata about generation: { "model": "...", "promptVersion": 1, ... }
    public string GenerationMetaJson { get; set; } = "{}";

    // NULL = active (unanswered)
    public DateTime? AnsweredAt { get; set; }

    // For "Later" functionality - defer showing this question set
    public DateTime? DeferredUntil { get; set; }

    // When next version becomes eligible
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
