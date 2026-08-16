using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("user_push_subscriptions")]
public class UserPushSubscription
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("endpoint")]
    [MaxLength(2048)]
    public string Endpoint { get; set; } = default!;

    [Column("p256dh")]
    [MaxLength(512)]
    public string P256dh { get; set; } = default!;

    [Column("auth")]
    [MaxLength(256)]
    public string Auth { get; set; } = default!;

    [Column("user_agent")]
    [MaxLength(512)]
    public string? UserAgent { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
