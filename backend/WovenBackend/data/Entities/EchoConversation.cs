using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities;

[Table("echo_conversations")]
public class EchoConversation
{
    [Key]
    [Column("conversation_id")]
    public Guid ConversationId { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }  // Nullable — public can chat without auth

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation property
    public virtual ICollection<EchoMessage> Messages { get; set; } = new List<EchoMessage>();
}

[Table("echo_messages")]
public class EchoMessage
{
    [Key]
    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("conversation_id")]
    public Guid ConversationId { get; set; }

    [ForeignKey(nameof(ConversationId))]
    public virtual EchoConversation? Conversation { get; set; }

    [Column("role")]
    [MaxLength(10)]
    public required string Role { get; set; }  // "user" or "assistant"

    [Column("content")]
    public required string Content { get; set; }

    [Column("voice_audio_url")]
    [MaxLength(500)]
    public string? VoiceAudioUrl { get; set; }

    [Column("echo_state")]
    [MaxLength(20)]
    public string? EchoState { get; set; }  // flow, friction, spark, drain, edge

    [Column("citations_json")]
    public string? CitationsJson { get; set; }  // JSON array of { file, line, snippet }

    [Column("live_stats_json")]
    public string? LiveStatsJson { get; set; }  // JSON object

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

[Table("echo_state")]
public class EchoState
{
    [Key]
    [Column("id")]
    public int Id { get; set; }  // Always 1 (singleton)

    [Column("current_state")]
    [MaxLength(20)]
    public required string CurrentState { get; set; }  // flow, friction, spark, drain, edge

    [Column("state_description")]
    [MaxLength(500)]
    public string? StateDescription { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
