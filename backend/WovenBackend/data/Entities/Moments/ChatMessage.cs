using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("chat_messages")]
public class ChatMessage
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("thread_id")]
    public Guid ThreadId { get; set; }

    [Column("sender_user_id")]
    public int SenderUserId { get; set; }

    [Column("body")]
    public string Body { get; set; } = "";

    // ✅ NEW
    [Column("message_type")]
    [MaxLength(20)]
    public string MessageType { get; set; } = ""; // USER | SYSTEM

    // ✅ NEW (Postgres)
    [Column("meta_json", TypeName = "jsonb")]
    public string MetaJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
