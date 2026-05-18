namespace WovenBackend.Services.Embeddings;

public interface IEmotionalRhythmService
{
    /// <summary>
    /// Builds a 48-dim emotional rhythm embedding from temporal app-usage patterns.
    /// Skips users with fewer than 28 days of app history.
    /// </summary>
    Task ComputeEmotionalRhythmAsync(int userId, CancellationToken ct = default);
}
