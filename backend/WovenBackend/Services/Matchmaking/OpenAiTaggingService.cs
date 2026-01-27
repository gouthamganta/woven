using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WovenBackend.Services.Matchmaking;

public class OpenAiTaggingService : IOpenAiTaggingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAiTaggingService> _logger;

    public OpenAiTaggingService(
        HttpClient http,
        IConfiguration config,
        ILogger<OpenAiTaggingService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<IntentMetadata?> ExtractIntentMetadataAsync(
        string primaryIntent,
        string reflectionSentence,
        CancellationToken ct = default)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[TaggingService] OpenAI API key missing, using defaults for intent");
            return GetDefaultIntentMetadata(primaryIntent);
        }

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

        var userPrompt = $@"Primary intent: {primaryIntent}
Reflection: {reflectionSentence}

Analyze and return JSON.";

        var schema = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                seriousness = new { type = "number" },
                flexibility = new { type = "number" },
                commitmentReadiness = new { type = "number" },
                tags = new { type = "array", items = new { type = "string" } }
            },
            required = new[] { "seriousness", "flexibility", "commitmentReadiness", "tags" }
        };

        var response = await CallOpenAiAsync(systemPrompt, userPrompt, schema, ct);
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
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[TaggingService] OpenAI API key missing, using default pillar scores");
            return new PillarScores();
        }

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

Each score: 0.0 to 1.0
Higher = stronger trait

No commentary, just JSON.";

        var userPrompt = $@"Questions: {questionsJson}
Answers: {answersJson}

Compute pillar scores and return JSON.";

        var schema = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                Lifestyle = new { type = "number" },
                Energy = new { type = "number" },
                Values = new { type = "number" },
                Communication = new { type = "number" },
                Ambition = new { type = "number" },
                Stability = new { type = "number" },
                Curiosity = new { type = "number" },
                Affection = new { type = "number" }
            },
            required = new[] { "Lifestyle", "Energy", "Values", "Communication", "Ambition", "Stability", "Curiosity", "Affection" }
        };

        var response = await CallOpenAiAsync(systemPrompt, userPrompt, schema, ct);
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
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[TaggingService] OpenAI API key missing, returning empty tags");
            return new Dictionary<string, List<string>>();
        }

        var systemPrompt = @"You extract personality tags from foundational answers.

Output ONLY valid JSON in this exact format:
{
  ""values"": [""growth"", ""family-oriented"", ""kindness""],
  ""lifestyle"": [""active"", ""exploring"", ""balanced""],
  ""communication"": [""direct"", ""thoughtful""],
  ""hobbies"": [""outdoors"", ""creative""]
}

Rules:
- Each category: 2-5 tags
- Tags: lowercase, hyphenated, descriptive
- Only include categories with strong signals

No commentary, just JSON.";

        var userPrompt = $@"Questions: {questionsJson}
Answers: {answersJson}

Extract tags and return JSON.";

        var schema = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                values = new { type = "array", items = new { type = "string" } },
                lifestyle = new { type = "array", items = new { type = "string" } },
                communication = new { type = "array", items = new { type = "string" } },
                hobbies = new { type = "array", items = new { type = "string" } }
            },
            required = new[] { "values", "lifestyle", "communication", "hobbies" }
        };

        var response = await CallOpenAiAsync(systemPrompt, userPrompt, schema, ct);
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

    private async Task<string?> CallOpenAiAsync(string systemPrompt, string userPrompt, object schema, CancellationToken ct)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        var endpoint = _config["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/responses";
        var model = _config["OpenAI:Model"] ?? "gpt-4.1-mini";

        _logger.LogInformation("[TaggingService] Using endpoint: {Endpoint}, Model: {Model}, HasApiKey: {HasKey}", 
            endpoint, model, !string.IsNullOrEmpty(apiKey));

        var payload = new
        {
            model,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[] { new { type = "input_text", text = systemPrompt } }
                },
                new
                {
                    role = "user",
                    content = new object[] { new { type = "input_text", text = userPrompt } }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "woven_tagging",
                    strict = true,
                    schema
                }
            },
            temperature = 0.3
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[TaggingService] OpenAI API failed: {Status} {Body}",
                    (int)resp.StatusCode, Truncate(raw, 500));
                return null;
            }

            _logger.LogInformation("[TaggingService] OpenAI response: {Response}", Truncate(raw, 1000));

            using var doc = JsonDocument.Parse(raw);
            
            // Responses API returns: output[0].content[0].text
            if (doc.RootElement.TryGetProperty("output", out var outputArr) &&
                outputArr.ValueKind == JsonValueKind.Array &&
                outputArr.GetArrayLength() > 0)
            {
                var firstOutput = outputArr[0];
                if (firstOutput.TryGetProperty("content", out var contentArr) &&
                    contentArr.ValueKind == JsonValueKind.Array &&
                    contentArr.GetArrayLength() > 0)
                {
                    var firstContent = contentArr[0];
                    if (firstContent.TryGetProperty("text", out var textProp))
                    {
                        return textProp.GetString();
                    }
                }
            }

            _logger.LogWarning("[TaggingService] Unexpected response structure from OpenAI");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TaggingService] OpenAI API exception");
            return null;
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

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "...");
}