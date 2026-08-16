namespace WovenBackend.Data.Entities;

/// <summary>
/// Per-user LinUCB bandit model (disjoint, 24-dim context).
/// Context = [8 pillar scores ÷ 100, 16 behavioural fingerprint].
///
/// Stored as the *inverse* of matrix A (Sherman-Morrison updated nightly)
/// so UCB scores can be computed without an O(d³) solve at deck-generation time.
///
/// A_inv: float[Dim * Dim], row-major  — starts as identity
/// B    : float[Dim]                   — starts as zeros
/// θ    : A_inv · B  (derived at read time, not stored)
/// </summary>
public class LinUcbUserModel
{
    public int UserId { get; set; }

    public int Dim { get; set; } = 24;

    // Row-major float[Dim²] stored as JSON
    public string AInvJson { get; set; } = "[]";

    // float[Dim] stored as JSON
    public string BJson { get; set; } = "[]";

    // Observation count — model is trusted once N ≥ threshold
    public int ObservationCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
