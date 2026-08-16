namespace WovenBackend.data.Entities.Moments;

public class ChatNoteLoveReaction
{
    public int Id { get; set; }

    // The note being loved
    public Guid NoteId { get; set; }
    public ChatNote Note { get; set; } = null!;

    // Who loved the note
    public int FromUserId { get; set; }

    // Who wrote the note (denormalized for fast MatchSignalLog writes without extra join)
    public int NoteAuthorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
