using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("friend_bridges")]
public class FriendBridge
{
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_a_id")]
    public int UserAId { get; set; }

    [Column("user_b_id")]
    public int UserBId { get; set; }

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = default!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("accepted_at")]
    public DateTimeOffset? AcceptedAt { get; set; }
}
