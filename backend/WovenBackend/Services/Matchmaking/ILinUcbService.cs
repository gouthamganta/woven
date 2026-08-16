namespace WovenBackend.Services.Matchmaking;

public interface ILinUcbService
{
    /// <summary>
    /// Returns a UCB exploration boost (0–10 scale, additive on TotalScore 0–100)
    /// for each candidate in the pool.
    /// Candidates with uncertain reward estimates get higher boosts (exploration).
    /// Falls back to a uniform exploration boost for users with no model yet.
    /// </summary>
    Task<Dictionary<int, double>> GetBoostMapAsync(
        int userId,
        List<int> candidateIds,
        float alpha = 1.0f,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the user's LinUCB model from a batch of (context, reward) observations.
    /// Uses Sherman-Morrison for O(d²) incremental A_inv updates.
    /// </summary>
    Task UpdateAsync(
        int userId,
        IReadOnlyList<(float[] Context, float Reward)> observations,
        CancellationToken ct = default);
}
