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

    // Phase 2B: trust scoring (0.0–1.0, default 0.5)
    public float TrustScore { get; set; } = 0.5f;
    public DateTime? TrustUpdatedAt { get; set; }

    // Phase 4A: anti-ghosting signals
    public float GhostScore { get; set; } = 0.5f;
    public DateTimeOffset? LastActiveAt { get; set; }

    // Phase 5A: identity verification
    public bool IsVerified { get; set; } = false;
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationType { get; set; }
}