using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("user_verifications")]
public class UserVerification
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("type")]
    [MaxLength(20)]
    public string Type { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [Column("submitted_at")]
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("verified_at")]
    public DateTimeOffset? VerifiedAt { get; set; }

    [Column("failure_reason")]
    [MaxLength(200)]
    public string? FailureReason { get; set; }
}
