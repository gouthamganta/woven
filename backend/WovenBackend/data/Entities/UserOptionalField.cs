namespace WovenBackend.Data;

public class UserOptionalField
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // e.g. "job", "education", "pets", "religion"
    public string Key { get; set; } = "";

    // e.g. "Software Engineer", "Masters", "Dog"
    public string Value { get; set; } = "";

    public VisibilityLevel Visibility { get; set; } = VisibilityLevel.MatchingOnly;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
