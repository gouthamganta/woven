namespace WovenBackend.Data;

public class UserWeeklyVibe
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    public string Text { get; set; } = "";

    // expires automatically
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
