using System.Text.Json;

namespace WovenBackend.Services.Matchmaking;

public class OpenAiTaggingService : IOpenAiTaggingService
{
    private readonly IOpenAiResilientClient _ai;
    private readonly ILogger<OpenAiTaggingService> _logger;

    public OpenAiTaggingService(IOpenAiResilientClient ai, ILogger<OpenAiTaggingService> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<IntentMetadata?> ExtractIntentMetadataAsync(
        string primaryIntent,
        string reflectionSentence,
        CancellationToken ct = default)
    {
        var systemPrompt = @"You analyze dating app user intent.

Output ONLY valid JSON in this exact format:
{
  ""seriousness"": 0.7,
  ""flexibility"": 0.8,
  ""commitmentReadiness"": 0.6,
  ""tags"": [""relationship-forward"", ""open-minded""]
}

Rules:
- seriousness: 0.0 (very casual) to 1.0 (very serious)
- flexibility: 0.0 (rigid expectations) to 1.0 (very flexible)
- commitmentReadiness: 0.0 (exploring) to 1.0 (ready now)
- tags: 2-4 descriptive tags (lowercase, hyphenated)

No commentary, just JSON.";

        var userPrompt = $"Primary intent: {primaryIntent}\nReflection: {reflectionSentence}\n\nAnalyze and return JSON.";

        var response = await _ai.ExecuteWithSystemAsync("tagging-intent", systemPrompt, userPrompt,
            useJsonMode: true, temperature: 0.3f, ct: ct);
        if (response == null) return GetDefaultIntentMetadata(primaryIntent);

        try
        {
            var metadata = JsonSerializer.Deserialize<IntentMetadata>(response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return metadata ?? GetDefaultIntentMetadata(primaryIntent);
        }
        catch
        {
            _logger.LogWarning("[TaggingService] Failed to parse intent metadata, using defaults");
            return GetDefaultIntentMetadata(primaryIntent);
        }
    }

    public async Task<PillarScores?> ComputePillarScoresAsync(
        string answersJson,
        string questionsJson,
        CancellationToken ct = default)
    {
        var systemPrompt = @"You compute 8 pillar scores from foundational answers.

Output ONLY valid JSON in this exact format:
{
  ""Lifestyle"": 0.78,
  ""Energy"": 0.62,
  ""Values"": 0.85,
  ""Communication"": 0.55,
  ""Ambition"": 0.43,
  ""Stability"": 0.70,
  ""Curiosity"": 0.66,
  ""Affection"": 0.58
}

Each score: 0.0 to 1.0. Higher = stronger trait. No commentary, just JSON.";

        var userPrompt = $"Questions: {questionsJson}\nAnswers: {answersJson}\n\nCompute pillar scores and return JSON.";

        var response = await _ai.ExecuteWithSystemAsync("tagging-pillars", systemPrompt, userPrompt,
            useJsonMode: true, temperature: 0.3f, ct: ct);
        if (response == null) return new PillarScores();

        try
        {
            var scores = JsonSerializer.Deserialize<PillarScores>(response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return scores ?? new PillarScores();
        }
        catch
        {
            _logger.LogWarning("[TaggingService] Failed to parse pillar scores, using defaults");
            return new PillarScores();
        }
    }

    public async Task<Dictionary<string, List<string>>?> ExtractTagsAsync(
        string answersJson,
        string questionsJson,
        CancellationToken ct = default)
    {
        var systemPrompt = @"You extract personality tags from foundational answers.

Output ONLY valid JSON in this exact format:
{
  ""values"": [""growth"", ""family-oriented"", ""kindness""],
  ""lifestyle"": [""active"", ""exploring"", ""balanced""],
  ""communication"": [""direct"", ""thoughtful""],
  ""hobbies"": [""outdoors"", ""creative""]
}

Rules: Each category 2-5 tags, lowercase, hyphenated. No commentary, just JSON.";

        var userPrompt = $"Questions: {questionsJson}\nAnswers: {answersJson}\n\nExtract tags and return JSON.";

        var response = await _ai.ExecuteWithSystemAsync("tagging-tags", systemPrompt, userPrompt,
            useJsonMode: true, temperature: 0.3f, ct: ct);
        if (response == null) return new Dictionary<string, List<string>>();

        try
        {
            var tags = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return tags ?? new Dictionary<string, List<string>>();
        }
        catch
        {
            _logger.LogWarning("[TaggingService] Failed to parse tags, returning empty");
            return new Dictionary<string, List<string>>();
        }
    }

    private IntentMetadata GetDefaultIntentMetadata(string primaryIntent)
    {
        var lower = primaryIntent?.ToLowerInvariant() ?? "";

        if (lower.Contains("relationship") || lower.Contains("long-term"))
        {
            return new IntentMetadata
            {
                Seriousness = 0.8,
                Flexibility = 0.5,
                CommitmentReadiness = 0.7,
                Tags = new List<string> { "relationship-forward", "intentional" }
            };
        }

        if (lower.Contains("casual") || lower.Contains("fun"))
        {
            return new IntentMetadata
            {
                Seriousness = 0.3,
                Flexibility = 0.8,
                CommitmentReadiness = 0.2,
                Tags = new List<string> { "casual", "low-pressure" }
            };
        }

        // Default: exploring
        return new IntentMetadata
        {
            Seriousness = 0.5,
            Flexibility = 0.7,
            CommitmentReadiness = 0.4,
            Tags = new List<string> { "exploring", "open-minded" }
        };
    }

}