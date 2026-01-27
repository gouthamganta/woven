using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public class MatchExplanationService : IMatchExplanationService
{
    private readonly WovenDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MatchExplanationService> _logger;
    private readonly IAiProfileService _aiProfileService;

    public MatchExplanationService(
        WovenDbContext db,
        HttpClient http,
        IConfiguration config,
        ILogger<MatchExplanationService> logger,
        IAiProfileService aiProfileService)
    {
        _db = db;
        _http = http;
        _config = config;
        _logger = logger;
        _aiProfileService = aiProfileService;
    }

    public async Task<int> GenerateAndSaveExplanationAsync(
        int userId,
        int candidateId,
        MatchScore score,
        MatchBucket bucket,
        DateOnly dateUtc,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[Explanation] Generating for user {UserId} + candidate {CandidateId}",
            userId, candidateId);

        // Load pair context for rich personalization
        var pairContext = await _aiProfileService.GetPairContextAsync(userId, candidateId, ct);

        // Load user and candidate vectors
        var userVector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        var candidateVector = await _db.UserVectors
            .Where(v => v.UserId == candidateId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (userVector == null || candidateVector == null)
        {
            _logger.LogWarning("[Explanation] Missing vectors, using fallback");
            return await SaveFallbackExplanationAsync(userId, candidateId, bucket, dateUtc, ct);
        }

        // Extract safe features for explanation with pair context
        var reasons = ExtractMatchReasons(userVector, candidateVector, score, bucket, pairContext);

        // Determine tone from pulse (prefer user's tone if available)
        var tone = pairContext?.UserProfile.ConversationTone ?? DetermineTone(userVector.VectorJson);

        // Generate explanation via OpenAI with pair context
        var (headline, bullets, dateIdea) = await GenerateExplanationAsync(reasons, bucket, tone, pairContext, ct);

        // Save to database
        var explanation = new MatchExplanation
        {
            UserId = userId,
            CandidateId = candidateId,
            DateUtc = dateUtc,
            Headline = headline,
            BulletsJson = JsonSerializer.Serialize(bullets),
            Tone = tone,
            DateIdea = dateIdea,
            CreatedAt = DateTime.UtcNow
        };

        _db.MatchExplanations.Add(explanation);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[Explanation] Saved explanation {Id} for user {UserId}. SharedTags: {SharedTags}, AlignedPillars: {AlignedPillars}",
            explanation.Id, userId, pairContext?.SharedTags.Count ?? 0, pairContext?.AlignedPillars.Count ?? 0);

        return explanation.Id;
    }

    private Dictionary<string, object> ExtractMatchReasons(
        UserVector userVector,
        UserVector candidateVector,
        MatchScore score,
        MatchBucket bucket,
        PairContext? pairContext = null)
    {
        var reasons = new Dictionary<string, object>
        {
            ["bucket"] = bucket.ToString(),
            ["totalScore"] = score.TotalScore,
            ["topComponent"] = GetTopScoringComponent(score)
        };

        // Add rich context from PairContext
        if (pairContext != null)
        {
            if (pairContext.SharedTags.Count > 0)
                reasons["sharedTags"] = pairContext.SharedTags.Take(4).ToList();

            if (pairContext.AlignedPillars.Count > 0)
                reasons["alignedPillars"] = pairContext.AlignedPillars.Take(2)
                    .Select(p => new { p.Pillar, avgScore = (p.UserScore + p.CandidateScore) / 2 })
                    .ToList();

            if (pairContext.SharedHobbies.Count > 0)
                reasons["sharedHobbies"] = pairContext.SharedHobbies.Take(3).ToList();

            reasons["toneAlignment"] = pairContext.ToneAlignment;
            reasons["intentAlignment"] = pairContext.GetIntentAlignmentDescription();
        }
        else
        {
            // Fallback to old logic if no pair context
            try
            {
                var userVectorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(userVector.VectorJson);
                var candidateVectorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(candidateVector.VectorJson);

                if (userVectorData != null && candidateVectorData != null)
                {
                    if (score.IntentScore >= 70)
                    {
                        reasons["intentAlignment"] = "high";
                    }

                    var userPillars = JsonSerializer.Deserialize<PillarScores>(userVector.PillarScoresJson);
                    var candidatePillars = JsonSerializer.Deserialize<PillarScores>(candidateVector.PillarScoresJson);

                    if (userPillars != null && candidatePillars != null)
                    {
                        var topPillars = FindTopAlignedPillars(userPillars, candidatePillars);
                        if (topPillars.Count > 0)
                            reasons["topPillars"] = topPillars;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Explanation] Failed to extract detailed reasons");
            }
        }

        return reasons;
    }

    private List<string> FindTopAlignedPillars(PillarScores user, PillarScores candidate)
    {
        var pillars = new[]
        {
            ("Lifestyle", user.Lifestyle, candidate.Lifestyle),
            ("Energy", user.Energy, candidate.Energy),
            ("Values", user.Values, candidate.Values),
            ("Communication", user.Communication, candidate.Communication),
            ("Ambition", user.Ambition, candidate.Ambition),
            ("Stability", user.Stability, candidate.Stability),
            ("Curiosity", user.Curiosity, candidate.Curiosity),
            ("Affection", user.Affection, candidate.Affection)
        };

        return pillars
            .Where(p => Math.Abs(p.Item2 - p.Item3) < 0.2) // Similar scores
            .OrderByDescending(p => (p.Item2 + p.Item3) / 2) // High values
            .Take(2)
            .Select(p => p.Item1)
            .ToList();
    }

    private string GetTopScoringComponent(MatchScore score)
    {
        var components = new[]
        {
            ("intent", score.IntentScore),
            ("foundational", score.FoundationalScore),
            ("lifestyle", score.LifestyleScore),
            ("pulse", score.PulseScore)
        };

        return components.OrderByDescending(c => c.Item2).First().Item1;
    }

    private string DetermineTone(string vectorJson)
    {
        try
        {
            var vector = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(vectorJson);
            if (vector != null && vector.TryGetValue("pulse", out var pulseElement))
            {
                var pulse = JsonSerializer.Deserialize<Dictionary<string, double>>(pulseElement.GetRawText());
                if (pulse != null)
                {
                    // Use socialCapacity as proxy for energy/playfulness
                    if (pulse.TryGetValue("socialCapacity", out var capacity))
                    {
                        if (capacity > 0.7) return "playful";
                        if (capacity < 0.4) return "serious";
                    }
                }
            }
        }
        catch { }

        return "calm";
    }

    private async Task<(string Headline, List<string> Bullets, string? DateIdea)> GenerateExplanationAsync(
        Dictionary<string, object> reasons,
        MatchBucket bucket,
        string tone,
        PairContext? pairContext,
        CancellationToken ct)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[Explanation] OpenAI API key missing, using fallback");
            return GenerateFallbackExplanation(bucket);
        }

        // Build rich match data section if we have pair context
        var matchDataSection = "";
        if (pairContext != null)
        {
            var sharedInterests = pairContext.SharedTags.Count > 0
                ? string.Join(", ", pairContext.SharedTags)
                : "exploring similar things";

            var alignedTraits = pairContext.AlignedPillars.Count > 0
                ? string.Join(", ", pairContext.AlignedPillars.Select(p => p.Pillar))
                : "compatible energy";

            var sharedHobbies = pairContext.SharedHobbies.Count > 0
                ? string.Join(", ", pairContext.SharedHobbies)
                : null;

            matchDataSection = $@"

MATCH DATA (USE THIS - IT'S SPECIFIC TO THESE TWO PEOPLE):
- Shared interests/tags: {sharedInterests}
- Aligned traits: {alignedTraits}
{(sharedHobbies != null ? $"- Shared hobbies: {sharedHobbies}" : "")}
- Tone match: {pairContext.ToneAlignment}
- Intent alignment: {pairContext.GetIntentAlignmentDescription()}";
        }

        var systemPrompt = $@"You write 'why you're a match' explanations for a dating app.

Tone: {tone}
{matchDataSection}

STRICT REQUIREMENTS:
- Write 1 headline (max 15 words) - MUST reference at least 1 aligned trait OR 2 shared interests
- Write 1-2 bullet points (max 20 words each) - MUST mention specific shared interests/traits
- Write 1 date idea (max 15 words) - If they share hobbies, the date idea MUST incorporate one
- Focus on what they SPECIFICALLY have in common
- Never overpromise or guarantee outcomes
- Sound natural, not robotic
- No emojis

BANNED PHRASES (NEVER USE):
- 'good energy'
- 'meaningful connection'
- 'real conversations'
- 'authentic'
- 'genuine'
- 'vibe'
- 'worth exploring'
- 'something special'

USE THEIR ACTUAL DATA - be specific, not generic!

Output ONLY valid JSON:
{{
  ""headline"": ""You both [specific shared trait/interest]..."",
  ""bullets"": [
    ""[Specific shared interest]: [what you have in common]"",
    ""[Aligned trait]: [how you're similar]""
  ],
  ""dateIdea"": ""[Activity that uses a shared hobby/interest]""
}}";

        var userPrompt = $@"Match reasons: {JsonSerializer.Serialize(reasons)}
Bucket: {bucket}

Generate explanation JSON. Be SPECIFIC using the match data provided.";

        var response = await CallOpenAiAsync(systemPrompt, userPrompt, ct);
        if (response == null)
        {
            return GenerateFallbackExplanation(bucket);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ExplanationResponse>(response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Headline))
            {
                return (parsed.Headline, parsed.Bullets ?? new List<string>(), parsed.DateIdea);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Explanation] Failed to parse OpenAI response");
        }

        return GenerateFallbackExplanation(bucket);
    }

    private async Task<string?> CallOpenAiAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var endpoint = _config["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/responses";
        var model = _config["OpenAI:Model"] ?? "gpt-4.1-mini";
        var apiKey = _config["OpenAI:ApiKey"];

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
                format = new { type = "json_object" }
            },
            temperature = 0.7
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
                _logger.LogWarning("[Explanation] OpenAI failed: {Status}", (int)resp.StatusCode);
                return null;
            }

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

            _logger.LogWarning("[Explanation] Unexpected response structure");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Explanation] OpenAI exception");
            return null;
        }
    }

    private (string, List<string>, string?) GenerateFallbackExplanation(MatchBucket bucket)
    {
        return bucket switch
        {
            MatchBucket.CORE_FIT => (
                "You both want something meaningful.",
                new List<string> { "Shared: similar values and relationship goals" },
                "Have a deep conversation over dinner"
            ),
            MatchBucket.LIFESTYLE_FIT => (
                "Your lifestyles align in key ways.",
                new List<string> { "Lifestyle: compatible daily routines and priorities" },
                "Try a new activity you both enjoy"
            ),
            MatchBucket.CONVERSATION_FIT => (
                "You're on the same wavelength right now.",
                new List<string> { "Energy: good match for today's vibe" },
                "Grab coffee and see where the conversation goes"
            ),
            MatchBucket.EXPLORER => (
                "There's potential here worth exploring.",
                new List<string> { "Shared: some interesting common ground" },
                "Meet for a casual walk and chat"
            ),
            _ => (
                "This could be an interesting match.",
                new List<string> { "Worth a conversation" },
                "Start with a coffee and see how it feels"
            )
        };
    }

    private async Task<int> SaveFallbackExplanationAsync(
        int userId,
        int candidateId,
        MatchBucket bucket,
        DateOnly dateUtc,
        CancellationToken ct)
    {
        var (headline, bullets, dateIdea) = GenerateFallbackExplanation(bucket);

        var explanation = new MatchExplanation
        {
            UserId = userId,
            CandidateId = candidateId,
            DateUtc = dateUtc,
            Headline = headline,
            BulletsJson = JsonSerializer.Serialize(bullets),
            Tone = "calm",
            DateIdea = dateIdea,
            CreatedAt = DateTime.UtcNow
        };

        _db.MatchExplanations.Add(explanation);
        await _db.SaveChangesAsync(ct);

        return explanation.Id;
    }

    private class ExplanationResponse
    {
        public string Headline { get; set; } = "";
        public List<string>? Bullets { get; set; }
        public string? DateIdea { get; set; } // ✅ NEW
    }
}