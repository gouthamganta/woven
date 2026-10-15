using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

/// <summary>
/// Private rating collected when a match closes, for any reason.
/// Never shown to users. Fed to ECHO as a pair-outcome label.
/// </summary>
[Table("match_afterthoughts")]
public class MatchAfterthought
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("match_id")]
    public int MatchId { get; set; }
    public Match? Match { get; set; }

    /// <summary>
    /// User who provided this rating
    /// </summary>
    [Column("from_user_id")]
    public int FromUserId { get; set; }

    /// <summary>
    /// The other user in the match (target of rating)
    /// </summary>
    [Column("about_user_id")]
    public int AboutUserId { get; set; }

    /// <summary>
    /// How the match closed: trial_end, unmatch, expiry, block
    /// </summary>
    [Column("close_path")]
    [MaxLength(20)]
    public string ClosePath { get; set; } = "";

    /// <summary>
    /// 1-5 star rating (null for expiry and block paths)
    /// </summary>
    [Column("rating")]
    public short? Rating { get; set; }

    /// <summary>
    /// Reason tag selected (e.g., "no_spark", "timing", "not_my_type", "felt_off", "busy", "forgot", etc.)
    /// For block: safety reason
    /// </summary>
    [Column("reason_tag")]
    [MaxLength(50)]
    public string? ReasonTag { get; set; }

    /// <summary>
    /// Optional free-text note (max 300 chars)
    /// </summary>
    [Column("note_text")]
    [MaxLength(300)]
    public string? NoteText { get; set; }

    /// <summary>
    /// Whether this is a safety-related rating (from block path)
    /// Safety ratings go to TrustService, never to matching
    /// </summary>
    [Column("is_safety")]
    public bool IsSafety { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
