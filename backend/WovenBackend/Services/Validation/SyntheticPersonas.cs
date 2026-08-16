namespace WovenBackend.Services.Validation;

/// <summary>
/// Step 15: 30 synthetic personas across 5 behavioural clusters (6 per cluster).
///
/// Pillar dimensions (0-100 scale, matches UserVector.PillarEmbedding semantic intent):
///   [0] Values alignment (ethics/worldview)
///   [1] Emotional openness
///   [2] Intellectual depth
///   [3] Creativity
///   [4] Adventure / novelty-seeking
///   [5] Social energy (introvert→extrovert)
///   [6] Commitment orientation (casual→serious)
///   [7] Family / domestic orientation
///
/// Fingerprint dimensions (0-1 scale, matches BehavioralFingerprintService output):
///   [0]  Response speed          [8]  Balloon pop rate
///   [1]  Night-owl tendency      [9]  Message anxiety (burst pattern)
///   [2]  Response consistency    [10] Game completion rate
///   [3]  Conversation depth      [11] Date progression rate
///   [4]  Engagement (reply rate) [12] Love reaction rate
///   [5]  Message frequency       [13] Conversation recovery rate
///   [6]  Trial affinity          [14] Emotional peak rate
///   [7]  Disclosure balance      [15] Attachment volatility
/// </summary>
public enum PersonaCluster { SecureCommitted, AdventurousSocial, IntellectualIntrospective, CaregiverWarmth, CreativeIndependent }

public record SyntheticPersona(
    int Id,
    string Name,
    PersonaCluster Cluster,
    float[] Pillar,       // float[8], 0-100
    float[] Fingerprint); // float[16], 0-1

public static class SyntheticPersonas
{
    // Cluster base vectors — each persona adds ±noise[i]
    private static readonly float[] _baseA = [85, 80, 70, 60, 40, 60, 90, 75];
    private static readonly float[] _baseB = [60, 60, 50, 70, 90, 85, 35, 40];
    private static readonly float[] _baseC = [75, 70, 95, 85, 35, 35, 65, 55];
    private static readonly float[] _baseD = [90, 90, 60, 55, 45, 65, 80, 90];
    private static readonly float[] _baseE = [65, 75, 80, 95, 70, 55, 45, 45];

    private static readonly float[] _fpA = [0.60f, 0.30f, 0.80f, 0.75f, 0.80f, 0.50f, 0.60f, 0.70f, 0.50f, 0.20f, 0.70f, 0.60f, 0.50f, 0.70f, 0.40f, 0.20f];
    private static readonly float[] _fpB = [0.85f, 0.50f, 0.50f, 0.40f, 0.70f, 0.80f, 0.80f, 0.40f, 0.80f, 0.40f, 0.50f, 0.30f, 0.70f, 0.50f, 0.60f, 0.50f];
    private static readonly float[] _fpC = [0.30f, 0.60f, 0.70f, 0.90f, 0.60f, 0.30f, 0.40f, 0.85f, 0.30f, 0.15f, 0.60f, 0.40f, 0.40f, 0.50f, 0.70f, 0.15f];
    private static readonly float[] _fpD = [0.70f, 0.40f, 0.85f, 0.70f, 0.85f, 0.60f, 0.50f, 0.75f, 0.60f, 0.25f, 0.75f, 0.80f, 0.65f, 0.80f, 0.50f, 0.20f];
    private static readonly float[] _fpE = [0.50f, 0.70f, 0.60f, 0.70f, 0.55f, 0.50f, 0.55f, 0.60f, 0.45f, 0.35f, 0.65f, 0.35f, 0.60f, 0.55f, 0.80f, 0.40f];

    // Per-persona noise offsets (pillar[8]) — deliberate, not random, so results are reproducible
    // Each row: 6 personas per cluster
    private static readonly float[][] _noiseA =
    [
        [  0,  0,  0,  0,  0,  0,  0,  0],
        [  5, -5,  8, -3,  7, -5,  3,  5],
        [ -5,  8, -3,  5, -6,  7, -3, -8],
        [ 10, -3,  5,  8, -8,  5, -5,  6],
        [ -8,  6, -6, -5,  5, -8,  5, -5],
        [  3, 10, -5,  3,  6,  3, -6,  3],
    ];
    private static readonly float[][] _noiseB =
    [
        [  0,  0,  0,  0,  0,  0,  0,  0],
        [  5,  5, -5,  8, -3, -5,  5, -5],
        [ -5,  8,  5, -5,  5,  5, -5,  6],
        [  8, -5, -3,  5,  5, -6,  5,  3],
        [ -3, -6,  6, -3,  8,  6, -3, -5],
        [  5,  3, -8,  6, -5,  3,  3,  5],
    ];
    private static readonly float[][] _noiseC =
    [
        [  0,  0,  0,  0,  0,  0,  0,  0],
        [  5, -5,  3, -5,  5,  5, -5,  5],
        [ -5,  6, -5,  5, -5, -5,  5, -5],
        [  8,  5,  5, -5,  5,  8, -3,  8],
        [ -5, -3, -3,  5,  5,  3,  5, -3],
        [  3,  5,  5, -3, -5, -3, -5,  3],
    ];
    private static readonly float[][] _noiseD =
    [
        [  0,  0,  0,  0,  0,  0,  0,  0],
        [ -5, -5,  5,  8,  5, -5,  5, -5],
        [  5,  5, -5, -5, -5,  5, -5,  5],
        [ -5,  8,  5,  5,  5,  5, -5,  3],
        [  5, -5, -5, -5, -5,  3,  5, -5],
        [ -3,  3,  5,  3,  5, -5, -3,  5],
    ];
    private static readonly float[][] _noiseE =
    [
        [  0,  0,  0,  0,  0,  0,  0,  0],
        [  5,  5, -5, -5,  5,  5, -5,  5],
        [ -5, -5,  5,  5, -5, -5,  5, -5],
        [  8,  3,  3,  5,  5,  3,  5,  3],
        [ -5,  8, -5,  5,  5, -3, -5,  5],
        [  3, -3,  5, -3, -3,  5,  3, -5],
    ];

    private static readonly string[] _namesA = ["Morgan", "Riley", "Jordan", "Cameron", "Avery", "Quinn"];
    private static readonly string[] _namesB = ["Skylar", "Blake", "Phoenix", "Rowan", "Sage", "Finley"];
    private static readonly string[] _namesC = ["Elliot", "Emery", "Remy", "Lennox", "Sterling", "Caspian"];
    private static readonly string[] _namesD = ["Rowan", "Harlow", "Indigo", "Eden", "Sage", "Lumen"];
    private static readonly string[] _namesE = ["River", "Briar", "Lark", "Cove", "Ash", "Dune"];

    private static float[] Add(float[] base_, float[] noise) =>
        base_.Zip(noise, (b, n) => Math.Clamp(b + n, 0f, 100f)).ToArray();

    private static float[] FpJitter(float[] base_, int seed)
    {
        var r = new Random(seed);
        return base_.Select(v => Math.Clamp(v + (float)(r.NextDouble() - 0.5) * 0.1f, 0f, 1f)).ToArray();
    }

    public static readonly IReadOnlyList<SyntheticPersona> All = BuildAll();

    private static List<SyntheticPersona> BuildAll()
    {
        var list = new List<SyntheticPersona>(30);
        int id = 1;

        for (int i = 0; i < 6; i++, id++)
            list.Add(new(id, _namesA[i], PersonaCluster.SecureCommitted,        Add(_baseA, _noiseA[i]), FpJitter(_fpA, id)));
        for (int i = 0; i < 6; i++, id++)
            list.Add(new(id, _namesB[i], PersonaCluster.AdventurousSocial,       Add(_baseB, _noiseB[i]), FpJitter(_fpB, id)));
        for (int i = 0; i < 6; i++, id++)
            list.Add(new(id, _namesC[i], PersonaCluster.IntellectualIntrospective, Add(_baseC, _noiseC[i]), FpJitter(_fpC, id)));
        for (int i = 0; i < 6; i++, id++)
            list.Add(new(id, _namesD[i], PersonaCluster.CaregiverWarmth,          Add(_baseD, _noiseD[i]), FpJitter(_fpD, id)));
        for (int i = 0; i < 6; i++, id++)
            list.Add(new(id, _namesE[i], PersonaCluster.CreativeIndependent,      Add(_baseE, _noiseE[i]), FpJitter(_fpE, id)));

        return list;
    }
}
