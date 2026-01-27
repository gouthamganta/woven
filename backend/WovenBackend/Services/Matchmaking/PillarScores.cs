namespace WovenBackend.Services.Matchmaking;

public class PillarScores
{
    public double Lifestyle { get; set; }
    public double Energy { get; set; }
    public double Values { get; set; }
    public double Communication { get; set; }
    public double Ambition { get; set; }
    public double Stability { get; set; }
    public double Curiosity { get; set; }
    public double Affection { get; set; }

    public PillarScores()
    {
        // Default to 0.5 (neutral)
        Lifestyle = 0.5;
        Energy = 0.5;
        Values = 0.5;
        Communication = 0.5;
        Ambition = 0.5;
        Stability = 0.5;
        Curiosity = 0.5;
        Affection = 0.5;
    }

    public double[] ToArray()
    {
        return new[]
        {
            Lifestyle,
            Energy,
            Values,
            Communication,
            Ambition,
            Stability,
            Curiosity,
            Affection
        };
    }

    public double CosineSimilarity(PillarScores other)
    {
        var a = ToArray();
        var b = other.ToArray();

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0;

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}