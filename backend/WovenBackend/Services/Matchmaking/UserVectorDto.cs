using System.Text.Json;

namespace WovenBackend.Services.Matchmaking;

public class UserVectorDto
{
    public int UserId { get; set; }
    public int Version { get; set; }

    // Intent section
    public IntentMetadata Intent { get; set; } = new();

    // Foundational section
    public PillarScores PillarScores { get; set; } = new();
    public Dictionary<string, List<string>> FoundationalTags { get; set; } = new();

    // Lifestyle section
    public Dictionary<string, string> Lifestyle { get; set; } = new();

    // Pulse section (from dynamic intake)
    public Dictionary<string, double> PulseFeatures { get; set; } = new();

    public string ToJson()
    {
        // ✅ IMPORTANT: intent is flat (matches OpenAI output + scoring parser)
        return JsonSerializer.Serialize(new
        {
            intent = new
            {
                seriousness = Intent.Seriousness,
                flexibility = Intent.Flexibility,
                commitmentReadiness = Intent.CommitmentReadiness,
                tags = Intent.Tags
            },
            foundational = new
            {
                pillars = PillarScores,
                tags = FoundationalTags
            },
            lifestyle = Lifestyle,
            pulse = PulseFeatures
        });
    }

    public string PillarScoresToJson()
    {
        return JsonSerializer.Serialize(new
        {
            Lifestyle = PillarScores.Lifestyle,
            Energy = PillarScores.Energy,
            Values = PillarScores.Values,
            Communication = PillarScores.Communication,
            Ambition = PillarScores.Ambition,
            Stability = PillarScores.Stability,
            Curiosity = PillarScores.Curiosity,
            Affection = PillarScores.Affection
        });
    }
}
