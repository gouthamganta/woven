namespace WovenBackend.Services.Feedback;

public record DateFeedbackDto(
    bool MetInPerson,
    int? Stars,
    string? FeltRightText,
    string? FeltOffText,
    string? MeetAgain);

public interface IDateFeedbackService
{
    Task QueueFeedbackPromptsAsync(CancellationToken ct = default);
    Task SendDuePromptsAsync(CancellationToken ct = default);
    Task ReschedulePendingAsync(CancellationToken ct = default);
    Task SubmitFeedbackAsync(int userId, Guid matchId, DateFeedbackDto dto, CancellationToken ct = default);
}
