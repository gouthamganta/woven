namespace WovenBackend.Data.Entities;

public class CandidateSignal
{
    public int Id { get; set; }

    // who generated the signal
    public int FromUserId { get; set; }

    // who the signal is about / directed to
    public int ToUserId { get; set; }

    // e.g. "LIKED", "SKIPPED", "CHATTED", "UNMATCHED", "BLOCKED"
    public string Type { get; set; } = "";

    // optional details
    public string? MetaJson { get; set; }

    // optional TTL (useful for things like “boost” windows)
    public DateTime? ExpiresAt { get; set; }

    public DateOnly DateUtc { get; set; }  // daily counting
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
