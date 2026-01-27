namespace WovenBackend.Data;

public class UserIntent
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Single select: Long-term / Short-term / Casual / Exploring
    public string PrimaryIntent { get; set; } = "";

    // Multi select stored as JSON string for MVP: ["Open to slow burn","Open to casual"]
    public string OpennessJson { get; set; } = "[]";

    // One sentence, editable
    public string ReflectionSentence { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
