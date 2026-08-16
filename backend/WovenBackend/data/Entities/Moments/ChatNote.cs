using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.data.Entities.Moments;

[Table("chat_notes")]
public class ChatNote
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("from_user_id")]
    public int FromUserId { get; set; }

    [Column("to_user_id")]
    public int ToUserId { get; set; }

    [Column("choice")]
    public MomentChoice Choice { get; set; }

    [Column("note_text")]
    [MaxLength(150)]
    public string NoteText { get; set; } = "";

    // Set when the balloon is created — links both notes to the match
    [Column("match_id")]
    public Guid? MatchId { get; set; }

    [Column("source")]
    [MaxLength(20)]
    public string? Source { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
