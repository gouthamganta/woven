using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("chat_availability_signals")]
public class ChatAvailabilitySignal
{
    [Key]
    [Column("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("ThreadId")]
    public Guid ThreadId { get; set; }

    [Column("UserId")]
    public int UserId { get; set; }

    [Column("SignalText")]
    [MaxLength(200)]
    public string SignalText { get; set; } = string.Empty;

    [Column("CreatedAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
