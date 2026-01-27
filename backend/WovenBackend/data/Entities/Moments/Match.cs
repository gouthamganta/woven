using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

public enum MatchType { PURE = 1, EDGE = 2 }
public enum BalloonState { ACTIVE = 1, CLOSED = 2 }
public enum ClosedReason 
{
    POP = 1,
    EXPIRE = 2,
    UNMATCH = 3,
    BLOCK = 4
}

[Table("matches")]
public class Match
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_a_id")]
    public int UserAId { get; set; }

    [Column("user_b_id")]
    public int UserBId { get; set; }

    [Column("match_type")]
    public MatchType MatchType { get; set; }

    [Column("edge_owner_id")]
    public int? EdgeOwnerId { get; set; }

    [Column("balloon_state")]
    public BalloonState BalloonState { get; set; }

    [Column("closed_reason")]
    public ClosedReason? ClosedReason { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("closed_at")]
    public DateTimeOffset? ClosedAt { get; set; }

    [Column("both_messaged_at")]
    public DateTimeOffset? BothMessagedAt { get; set; }

    [Column("find_love_at")]
    public DateTimeOffset? FindLoveAt { get; set; }

    // Trial match fields
    [Column("is_trial")]
    public bool IsTrial { get; set; } = false;

    [Column("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; set; }

    [Column("trial_ends_at")]
    public DateTimeOffset? TrialEndsAt { get; set; }

    [Column("user_a_decision")]
    [MaxLength(20)]
    public string? UserADecision { get; set; }

    [Column("user_b_decision")]
    [MaxLength(20)]
    public string? UserBDecision { get; set; }
}
