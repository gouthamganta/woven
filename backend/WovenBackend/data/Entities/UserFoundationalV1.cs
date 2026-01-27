namespace WovenBackend.Data;

public class UserFoundationalV1
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Store max 5 Q/A as JSON string:
    // [{ "q": "...", "a": "..." }, ...]
    public string AnswersJson { get; set; } = "[]";

    // Later AI extraction. For now keep as {}
    // { "values": [...], "lifestyle": {...}, "eq": {...}, "cognitive": {...} }
    public string SignalsJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
