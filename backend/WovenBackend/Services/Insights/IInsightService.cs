namespace WovenBackend.Services.Insights;

public record InsightDeliveryDto(string Insight, bool ShouldAskOpinion, string? OpinionPrompt);

public interface IInsightService
{
    Task ComputeInsightsAsync(int userId, CancellationToken ct = default);
    Task<(bool ShouldAsk, string? Trigger, string? Prompt)> ShouldAskOpinionAsync(int userId, CancellationToken ct = default);
    Task SubmitOpinionAsync(int userId, string text, string trigger, CancellationToken ct = default);
    Task<InsightDeliveryDto?> DeliverInsightAtMomentAsync(int userId, string moment, CancellationToken ct = default);
}
