namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// ECHO hard filter contract.
/// Only two criteria are binary exclusions — everything else is scored.
///   1. Reciprocal age range  — both users must fall inside each other's stated age window
///   2. Reciprocal distance   — both users must be within each other's stated distance limit
///
/// Anything else (relationship structure, intent seriousness, kids, smoking, religion…)
/// flows into ECHO's weighted scoring so behavioral learning can override stated preferences.
/// </summary>
public interface IHardFilterService
{
    Task<List<int>> ApplyAsync(int userId, List<int> candidateIds, CancellationToken ct = default);
}
