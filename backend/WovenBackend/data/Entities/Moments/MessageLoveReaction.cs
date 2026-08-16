namespace WovenBackend.data.Entities.Moments;

public class MessageLoveReaction
{
    public int Id { get; set; }

    public Guid MessageId { get; set; }
    public ChatMessage Message { get; set; } = null!;

    public Guid ThreadId { get; set; }

    // Who loved the message
    public int FromUserId { get; set; }

    // Who sent the message (denormalized for fast MatchSignalLog writes)
    public int MessageAuthorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
