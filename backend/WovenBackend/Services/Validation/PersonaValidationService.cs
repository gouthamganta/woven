namespace WovenBackend.Services.Validation;

/// <summary>
/// Step 17: NDCG validation — measures how well the ECHO engine's ranking
///           of personas matches the research oracle.
/// Step 18: Weight convergence simulation — shows logistic regression on
///           synthetic (oracle-derived) observations converges toward better
///           pillar/fingerprint weights, improving NDCG over iterations.
///
/// Engine score for a (viewer, candidate) pair uses cosine similarity of:
///   - Pillar vectors (8-dim, 0-100 scale, normalised before cosine)
///   - Fingerprint vectors (16-dim, 0-1 scale)
/// Combined: engineScore = wP * cosinePillar + wF * cosineFingerprint
///
/// Default weights: wP = 0.60, wF = 0.40.
/// Learned weights are updated via gradient descent on binary oracle labels.
/// </summary>
public class PersonaValidationService : IPersonaValidationService
{
    private const float DefaultPillarWeight      = 0.60f;
    private const float DefaultFingerprintWeight = 0.40f;
    private const float LearningRate             = 0.05f;
    private const float L2Lambda                 = 0.01f;
    private const int   SimulationEpochs         = 100;

    // ---------------------------------------------------------------
    // Step 17: NDCG validation
    // ---------------------------------------------------------------

    public ValidationReport RunValidation()
    {
        var personas = SyntheticPersonas.All;

        // Initial NDCG with default weights
        var (ndcg5Init, ndcg10Init) = ComputeNdcg(personas, DefaultPillarWeight, DefaultFingerprintWeight);

        // Run weight convergence simulation → Step 18
        var convergence = SimulateWeightConvergence(personas);

        var (wPFinal, wFFinal) = convergence.Last();
        var (ndcg5Final, ndcg10Final) = ComputeNdcg(personas, wPFinal, wFFinal);

        // Build cluster-level compatibility table (oracle vs engine at default weights)
        var clusterReport = BuildClusterCompat(personas);

        return new ValidationReport(
            PersonaCount: personas.Count,
            Clusters: 5,
            NdcgAt5_Initial:  Math.Round(ndcg5Init,  4),
            NdcgAt10_Initial: Math.Round(ndcg10Init, 4),
            NdcgAt5_Final:    Math.Round(ndcg5Final,  4),
            NdcgAt10_Final:   Math.Round(ndcg10Final, 4),
            LearnedPillarWeight:      Math.Round(wPFinal, 4),
            LearnedFingerprintWeight: Math.Round(wFFinal, 4),
            ConvergenceCurve: SampleConvergenceCurve(personas, convergence),
            ClusterCompatibility: clusterReport,
            Personas: BuildPersonaSummaries(personas));
    }

    // ---------------------------------------------------------------
    // Step 18: Logistic regression on synthetic (oracle-derived) pairs
    // ---------------------------------------------------------------

    private static List<(float wP, float wF)> SimulateWeightConvergence(IReadOnlyList<SyntheticPersona> personas)
    {
        float wP = DefaultPillarWeight;
        float wF = DefaultFingerprintWeight;

        var history = new List<(float, float)>(SimulationEpochs + 1) { (wP, wF) };

        // Build observation list: (cosinePillar, cosineFingerprint, label)
        // Label = 1 if oracle > 0.55, 0 if oracle < 0.35 (skip ambiguous middle)
        var obs = new List<(float cp, float cf, float label)>();
        foreach (var a in personas)
        {
            foreach (var b in personas)
            {
                if (a.Id >= b.Id) continue;
                var oracle = PersonaOracle.Compatibility(a, b);
                if (oracle > 0.55f)
                    obs.Add((CosinePillar(a, b), CosineFingerprint(a, b), 1f));
                else if (oracle < 0.35f)
                    obs.Add((CosinePillar(a, b), CosineFingerprint(a, b), 0f));
            }
        }

        for (int epoch = 0; epoch < SimulationEpochs; epoch++)
        {
            float gradP = 0, gradF = 0;
            foreach (var (cp, cf, label) in obs)
            {
                var score = wP * cp + wF * cf;
                var pred  = Sigmoid(score);
                var err   = pred - label;
                gradP += err * cp;
                gradF += err * cf;
            }

            gradP = gradP / obs.Count + L2Lambda * wP;
            gradF = gradF / obs.Count + L2Lambda * wF;

            wP = Math.Max(0.05f, wP - LearningRate * gradP);
            wF = Math.Max(0.05f, wF - LearningRate * gradF);

            // Normalise so they sum to 1
            var total = wP + wF;
            wP /= total;
            wF /= total;

            history.Add((wP, wF));
        }

        return history;
    }

    // ---------------------------------------------------------------
    // NDCG computation
    // ---------------------------------------------------------------

    private static (double ndcg5, double ndcg10) ComputeNdcg(
        IReadOnlyList<SyntheticPersona> personas,
        float wP, float wF)
    {
        double sum5 = 0, sum10 = 0, count = 0;

        foreach (var viewer in personas)
        {
            var others = personas.Where(p => p.Id != viewer.Id).ToList();

            // Oracle ranking (ground truth gains)
            var oracleScores = others.ToDictionary(p => p.Id, p => PersonaOracle.Compatibility(viewer, p));

            // Engine ranking
            var engineRanked = others
                .OrderByDescending(p => wP * CosinePillar(viewer, p) + wF * CosineFingerprint(viewer, p))
                .Select(p => p.Id)
                .ToList();

            // Ideal ranking (sort by oracle score desc)
            var idealRanked = others
                .OrderByDescending(p => oracleScores[p.Id])
                .Select(p => p.Id)
                .ToList();

            sum5  += NdcgAt(engineRanked, idealRanked, oracleScores, 5);
            sum10 += NdcgAt(engineRanked, idealRanked, oracleScores, 10);
            count++;
        }

        return (sum5 / count, sum10 / count);
    }

    private static double NdcgAt(
        List<int> engineRanked,
        List<int> idealRanked,
        Dictionary<int, float> oracleScores,
        int k)
    {
        double dcg  = 0;
        double idcg = 0;

        for (int i = 0; i < Math.Min(k, engineRanked.Count); i++)
        {
            var gain = (double)oracleScores[engineRanked[i]];
            dcg  += gain / Math.Log2(i + 2);
        }

        for (int i = 0; i < Math.Min(k, idealRanked.Count); i++)
        {
            var gain = (double)oracleScores[idealRanked[i]];
            idcg += gain / Math.Log2(i + 2);
        }

        return idcg > 0 ? dcg / idcg : 1.0;
    }

    // ---------------------------------------------------------------
    // Report builders
    // ---------------------------------------------------------------

    private static List<ConvergencePoint> SampleConvergenceCurve(
        IReadOnlyList<SyntheticPersona> personas,
        List<(float wP, float wF)> history)
    {
        var samples = new[] { 0, 10, 25, 50, 75, 100 };
        return samples.Select(epoch =>
        {
            var (wP, wF) = history[Math.Min(epoch, history.Count - 1)];
            var (n5, n10) = ComputeNdcg(personas, wP, wF);
            return new ConvergencePoint(epoch, Math.Round(wP, 4), Math.Round(wF, 4), Math.Round(n5, 4), Math.Round(n10, 4));
        }).ToList();
    }

    private static List<ClusterCompatRow> BuildClusterCompat(IReadOnlyList<SyntheticPersona> personas)
    {
        var clusters = Enum.GetValues<PersonaCluster>();
        var rows = new List<ClusterCompatRow>();

        foreach (var from in clusters)
        {
            var fromPersonas = personas.Where(p => p.Cluster == from).ToList();
            foreach (var to in clusters)
            {
                var toPersonas   = personas.Where(p => p.Cluster == to).ToList();
                var oracleMean   = fromPersonas.SelectMany(a => toPersonas.Select(b => a.Id == b.Id ? 0f : PersonaOracle.Compatibility(a, b))).Where(s => s > 0).Average();
                var engineMean   = fromPersonas.SelectMany(a => toPersonas.Select(b => a.Id != b.Id
                    ? DefaultPillarWeight * CosinePillar(a, b) + DefaultFingerprintWeight * CosineFingerprint(a, b)
                    : 0f)).Where(s => s > 0).Average();
                rows.Add(new ClusterCompatRow(from.ToString(), to.ToString(), Math.Round(oracleMean, 3), Math.Round(engineMean, 3)));
            }
        }

        return rows;
    }

    private static List<PersonaSummary> BuildPersonaSummaries(IReadOnlyList<SyntheticPersona> personas)
        => personas.Select(p => new PersonaSummary(
            p.Id, p.Name, p.Cluster.ToString(),
            p.Pillar.Select(v => Math.Round(v, 1)).ToList(),
            p.Fingerprint.Select(v => Math.Round(v, 3)).ToList())).ToList();

    // ---------------------------------------------------------------
    // Math helpers
    // ---------------------------------------------------------------

    private static float CosinePillar(SyntheticPersona a, SyntheticPersona b)
        => Cosine(a.Pillar.Select(v => v / 100f).ToArray(), b.Pillar.Select(v => v / 100f).ToArray());

    private static float CosineFingerprint(SyntheticPersona a, SyntheticPersona b)
        => Cosine(a.Fingerprint, b.Fingerprint);

    private static float Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            na  += a[i] * a[i];
            nb  += b[i] * b[i];
        }
        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom < 1e-10 ? 0f : (float)(dot / denom);
    }

    private static float Sigmoid(float z) => 1f / (1f + MathF.Exp(-z));
}

// ---------------------------------------------------------------
// Result DTOs (records — serialise to JSON cleanly)
// ---------------------------------------------------------------

public record ValidationReport(
    int PersonaCount,
    int Clusters,
    double NdcgAt5_Initial,
    double NdcgAt10_Initial,
    double NdcgAt5_Final,
    double NdcgAt10_Final,
    double LearnedPillarWeight,
    double LearnedFingerprintWeight,
    List<ConvergencePoint> ConvergenceCurve,
    List<ClusterCompatRow> ClusterCompatibility,
    List<PersonaSummary> Personas);

public record ConvergencePoint(
    int Epoch,
    double PillarWeight,
    double FingerprintWeight,
    double NdcgAt5,
    double NdcgAt10);

public record ClusterCompatRow(
    string FromCluster,
    string ToCluster,
    double OracleScore,
    double EngineScore);

public record PersonaSummary(
    int Id,
    string Name,
    string Cluster,
    List<double> PillarScores,
    List<double> FingerprintScores);
