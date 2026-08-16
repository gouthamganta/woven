namespace WovenBackend.Services.Validation;

/// <summary>
/// Step 16: Research-grounded compatibility oracle.
///
/// Cluster-level compatibility matrix (symmetric) — scores in [0, 1]:
///
///                  Secure  Advent  Intell  Caregiv  Creative
///   Secure        [ 0.88,  0.32,   0.55,   0.72,    0.42 ]
///   Adventurous   [ 0.32,  0.86,   0.28,   0.38,    0.62 ]
///   Intellectual  [ 0.55,  0.28,   0.90,   0.48,    0.66 ]
///   Caregiver     [ 0.72,  0.38,   0.48,   0.88,    0.42 ]
///   Creative      [ 0.42,  0.62,   0.66,   0.42,    0.86 ]
///
/// Research basis:
///   - Attachment theory: Secure attaches well across styles, especially with Caregiver.
///   - Similarity-attraction: Same-cluster pairs score 0.86-0.90.
///   - Complementarity: Adventurous × Creative (freedom-seeking); Intellectual × Creative (depth + expression).
///   - Conflict: Adventurous × Intellectual (pace/depth mismatch); Secure × Adventurous (commitment gap).
///
/// Intra-cluster: ±0.04 jitter per pair to reflect within-cluster variation.
/// </summary>
public static class PersonaOracle
{
    // [from_cluster, to_cluster] → base compatibility
    private static readonly float[,] ClusterMatrix = new float[5, 5]
    {
        // Sec    Adv    Int    Car    Cre
        { 0.88f, 0.32f, 0.55f, 0.72f, 0.42f }, // SecureCommitted
        { 0.32f, 0.86f, 0.28f, 0.38f, 0.62f }, // AdventurousSocial
        { 0.55f, 0.28f, 0.90f, 0.48f, 0.66f }, // IntellectualIntrospective
        { 0.72f, 0.38f, 0.48f, 0.88f, 0.42f }, // CaregiverWarmth
        { 0.42f, 0.62f, 0.66f, 0.42f, 0.86f }, // CreativeIndependent
    };

    // Returns oracle compatibility [0,1] for a (viewer, candidate) persona pair.
    // A small deterministic jitter is applied so same-cluster pairs have slight variance.
    public static float Compatibility(SyntheticPersona viewer, SyntheticPersona candidate)
    {
        var base_ = ClusterMatrix[(int)viewer.Cluster, (int)candidate.Cluster];

        // Deterministic per-pair jitter so pairs within same cluster aren't identical
        var jitter = (float)((MathHelper.StableHash(viewer.Id, candidate.Id) % 400 - 200) / 10_000.0);

        return Math.Clamp(base_ + jitter, 0f, 1f);
    }

    // Returns the oracle-ranked list of candidate IDs for a given viewer (descending compatibility)
    public static List<int> OracleRanking(SyntheticPersona viewer, IReadOnlyList<SyntheticPersona> all)
        => all
            .Where(p => p.Id != viewer.Id)
            .OrderByDescending(p => Compatibility(viewer, p))
            .Select(p => p.Id)
            .ToList();
}

internal static class MathHelper
{
    // Deterministic hash for (a, b) pair — same regardless of argument order so it's symmetric
    internal static int StableHash(int a, int b)
    {
        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);
        return lo * 31 + hi * 17;
    }
}
