namespace WovenBackend.Data.Entities;

public class CoachingSummary
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = default!;
    public DateOnly WeekStartDate { get; set; }
    public string SummaryText { get; set; } = "";
    public string InterpretedNarrative { get; set; } = "";
    public DateTimeOffset DeliveredAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public DateTimeOffset? OptedOutAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
