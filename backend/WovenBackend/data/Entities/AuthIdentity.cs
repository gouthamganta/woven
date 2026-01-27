namespace WovenBackend.Data;

public class AuthIdentity
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    public required string Provider { get; set; }          // "google"
    public required string ProviderSubject { get; set; }   // Google "sub"

    public string? Email { get; set; }                     // optional copy for debugging
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
