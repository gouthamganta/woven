namespace WovenBackend.Services.Seasons;

public record SeasonInfo(int Id, int SeasonNumber, DateOnly StartDate, DateOnly EndDate, string PromptText);

public record CurrentSeasonResult(SeasonInfo? Season, string UserStatus, int ResponseCount);

public record SeasonResponseRequest(string PillarId, string QuestionId, string Response);

public interface ISeasonService
{
    Task<CurrentSeasonResult> GetCurrentSeasonAsync(int userId, CancellationToken ct = default);
    Task SubmitSeasonResponsesAsync(int userId, List<SeasonResponseRequest> responses, CancellationToken ct = default);
    Task<string?> GetSignaturePromptAsync(int userId, CancellationToken ct = default);
}
