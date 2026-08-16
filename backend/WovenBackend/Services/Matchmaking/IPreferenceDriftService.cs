namespace WovenBackend.Services.Matchmaking;

/// <summary>
/// ECHO Phase 6 — revealed-preference drift.
/// Nudges a user's voice and visual preference embeddings toward candidates
/// they've had positive behavioural connections with (ConnectionScore ≥ threshold).
/// This lets ECHO learn who the user is actually attracted to, not just who
/// they said they wanted in their stated-preference profile.
/// </summary>
public interface IPreferenceDriftService
{
    Task DriftForUserAsync(int userId, CancellationToken ct = default);
}
