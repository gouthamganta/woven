namespace WovenBackend.Services.Nudges;

public record NudgeDto(string Type, string Text, string Action);

public interface INudgeService
{
    Task<NudgeDto?> GetConversationNudgeAsync(int userId, Guid threadId, CancellationToken ct = default);
}
