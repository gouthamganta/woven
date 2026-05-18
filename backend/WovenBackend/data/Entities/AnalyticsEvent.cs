using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities;

[Table("analytics_events")]
public class AnalyticsEvent
{
    [Key][Column("id")] public long Id { get; set; }
    [Column("user_id_hash")][MaxLength(64)] public string? UserIdHash { get; set; }
    [Column("session_id")][MaxLength(100)] public string? SessionId { get; set; }
    [Column("event_type")][MaxLength(100)] public string EventType { get; set; } = string.Empty;
    [Column("properties", TypeName = "jsonb")] public string? Properties { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
