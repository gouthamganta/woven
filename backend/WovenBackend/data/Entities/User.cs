namespace WovenBackend.Data;

public class User
{
    public int Id { get; set; }

    public required string Email { get; set; }

    // OAuth-first MVP
    public string? PasswordHash { get; set; }
    public string? FullName { get; set; }
    public string? ProfilePhoto { get; set; }

    public ProfileStatus ProfileStatus { get; set; } = ProfileStatus.INCOMPLETE;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}