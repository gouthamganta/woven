using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("security_audit_log")]
public class SecurityAuditLog
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Column("event_type")]
    [MaxLength(50)]
    public string EventType { get; set; } = default!;

    [Column("resource_type")]
    [MaxLength(50)]
    public string ResourceType { get; set; } = default!;

    [Column("resource_id")]
    [MaxLength(100)]
    public string ResourceId { get; set; } = default!;

    [Column("actor_id")]
    public int? ActorId { get; set; }

    [Column("ip_hash")]
    [MaxLength(64)]
    public string? IpHash { get; set; }

    [Column("details_json", TypeName = "jsonb")]
    public string DetailsJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
