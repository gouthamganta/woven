namespace WovenBackend.Services.Matchmaking;

public interface IWeightLearningService
{
    /// <summary>
    /// Learns per-user component weights from match outcomes.
    /// Requires at least 5 outcomes. Persists learned weights to user_matching_weights.
    /// </summary>
    Task LearnWeightsAsync(int userId, CancellationToken ct = default);
}
