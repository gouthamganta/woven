using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WovenBackend.Services;

public class OpenAiRewriteService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAiRewriteService> _logger;
    private readonly IAiProfileService _aiProfileService;

    public OpenAiRewriteService(
        HttpClient http,
        IConfiguration config,
        ILogger<OpenAiRewriteService> logger,
        IAiProfileService aiProfileService)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _aiProfileService = aiProfileService;
    }

    public record RewriteUserContext(string? FirstName, string? Gender, string? Intent, int? UserId = null);

    public async Task<BankQuestion[]> RewriteAsync(
        BankQuestion[] baseQuestions,
        RewriteUserContext ctx,
        string style,
        CancellationToken ct)
    {
        // ✅ Never block onboarding
        var apiKey = _config["OpenAI:ApiKey"];

        // ✅ DEBUG (safe): confirm key is loaded without printing full key
        _logger.LogInformation("[OpenAI] ApiKey present={Present} prefix={Prefix}",
            !string.IsNullOrWhiteSpace(apiKey),
            string.IsNullOrWhiteSpace(apiKey) ? "" : apiKey.Substring(0, Math.Min(6, apiKey.Length)));

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("[OpenAI] ApiKey missing -> using bank questions.");
            return baseQuestions;
        }

        // Load AiProfile for personalization
        AiProfile? aiProfile = null;
        if (ctx.UserId.HasValue && ctx.UserId.Value > 0)
        {
            aiProfile = await _aiProfileService.GetProfileAsync(ctx.UserId.Value, ct);
            _logger.LogInformation("[OpenAI] Loaded AiProfile for user {UserId}: {TopPillars} top pillars",
                ctx.UserId, aiProfile?.TopPillars.Count ?? 0);
        }

        var endpoint = _config["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/responses";
        var model = _config["OpenAI:Model"] ?? "gpt-4.1-mini";

        var systemPrompt = BuildSystemPrompt(style, aiProfile);

        var payload = new
        {
            model,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new
                        {
                            // ✅ Responses API expects input_text (NOT "text")
                            type = "input_text",
                            text = systemPrompt
                        }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        // ✅ Responses API expects input_text (NOT "text")
                        new { type = "input_text", text = BuildUserPrompt(baseQuestions, ctx, aiProfile) }
                    }
                }
            },

            // ✅ JSON mode
            text = new { format = new { type = "json_object" } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            _logger.LogInformation("[OpenAI] Calling model={Model} endpoint={Endpoint}", model, endpoint);

            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[OpenAI] FAIL status={Status} body={Body}", (int)resp.StatusCode, Trunc(raw, 1000));
                return baseQuestions;
            }

            var parsed = TryParseQuestionsFromResponsesApi(raw, expectedCount: baseQuestions.Length);
            if (parsed == null || parsed.Length != baseQuestions.Length)
            {
                _logger.LogWarning("[OpenAI] Parse/shape invalid -> using bank. raw={Body}", Trunc(raw, 1000));
                return baseQuestions;
            }

            // ✅ Validate IDs unchanged
            var baseIds = new HashSet<string>(baseQuestions.Select(q => q.Id));
            if (parsed.Any(q => string.IsNullOrWhiteSpace(q.Id) || !baseIds.Contains(q.Id)))
            {
                _logger.LogWarning("[OpenAI] ID mismatch -> using bank.");
                return baseQuestions;
            }

            // ✅ Preserve pillars from bank
            var pillarsById = baseQuestions.ToDictionary(q => q.Id, q => q.Pillars);
            var baseTextById = baseQuestions.ToDictionary(q => q.Id, q => q.Text);

            var normalized = parsed.Select(q =>
            {
                var text = (q.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                    text = baseTextById[q.Id];

                return new BankQuestion(
                    Id: q.Id,
                    Text: text,
                    Pillars: pillarsById[q.Id]
                );
            }).ToArray();

            _logger.LogInformation("[OpenAI] Rewrite OK.");
            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[OpenAI] Exception -> using bank.");
            return baseQuestions;
        }
    }

    private static string BuildSystemPrompt(string style, AiProfile? profile)
    {
        var personalizationRules = "";
        if (profile != null && profile.TopPillars.Count > 0)
        {
            var topTraits = profile.TopPillars.Take(2).Select(p => p.Key).ToList();
            var vibe = profile.ConversationTone;

            personalizationRules = $@"

PERSONALIZATION RULES:
- This user's top traits are: {string.Join(", ", topTraits)}
- Their current vibe is: {vibe}
- Each rewritten question should feel relevant to at least ONE of their traits or interests
- Match the tone to their vibe ({vibe}): {GetToneGuidance(vibe)}";
        }

        return $@"You rewrite dating-app onboarding questions.

Hard rules:
- Keep the same question IDs. Never change IDs.
- Do not add, remove, reorder, or merge questions.
- Preserve semantic intent. Only rewrite wording.
- Tone/style: {style}
- Keep each question 1–2 sentences. No emojis.
- Output JSON ONLY. No commentary.
{personalizationRules}

CRITICAL ANTI-GENERIC RULES:
- NEVER use these banned phrases: ""meaningful"", ""genuine"", ""good energy"", ""real conversations"", ""authentic"", ""connection"", ""vibe"", ""deep connection"", ""truly"", ""special""
- Each question must feel specific and personal, not like a template
- If user context is provided, reference at least 1 trait or interest in the wording

Return EXACTLY this JSON object shape:
{{
  ""questions"": [
    {{ ""id"": ""q1"", ""text"": ""..."", ""pillars"": [""Lifestyle"",""Energy""] }},
    ...
  ]
}}";
    }

    private static string GetToneGuidance(string tone)
    {
        return tone switch
        {
            "playful" => "use light, fun, slightly cheeky language",
            "thoughtful" => "use reflective, introspective phrasing",
            "calm" => "use gentle, unhurried, reassuring language",
            _ => "use warm, conversational language"
        };
    }

    private static string BuildUserPrompt(BankQuestion[] baseQuestions, RewriteUserContext ctx, AiProfile? profile)
    {
        var contextBits = new List<string>();
        if (!string.IsNullOrWhiteSpace(ctx.FirstName)) contextBits.Add($"first_name: {ctx.FirstName}");
        if (!string.IsNullOrWhiteSpace(ctx.Gender)) contextBits.Add($"gender: {ctx.Gender}");
        if (!string.IsNullOrWhiteSpace(ctx.Intent)) contextBits.Add($"intent: {ctx.Intent}");

        // Add rich context from AiProfile
        if (profile != null)
        {
            if (profile.Age > 0) contextBits.Add($"age: {profile.Age}");
            if (profile.TopPillars.Count > 0) contextBits.Add($"top_traits: {profile.GetTopTraitsFormatted()}");
            var keyTags = profile.GetKeyTagsFormatted();
            if (keyTags != "not specified") contextBits.Add($"key_tags: {keyTags}");
            var hobbies = profile.GetHobbiesFormatted();
            if (hobbies != "not specified") contextBits.Add($"hobbies: {hobbies}");
            contextBits.Add($"current_vibe: {profile.ConversationTone}");
        }

        var contextLine = contextBits.Count == 0 ? "user_context: none" : "user_context: " + string.Join(", ", contextBits);

        var questionsJson = JsonSerializer.Serialize(
            baseQuestions.Select(q => new { id = q.Id, text = q.Text, pillars = q.Pillars })
        );

        return
$@"{contextLine}

Rewrite these base questions (JSON):
{questionsJson}

Return ONLY the JSON object in the required shape.";
    }

    // We ignore model pillars; only parse id + text.
    private record Wrapper(ModelQuestion[] Questions);
    private record ModelQuestion(string Id, string Text);

    private static ModelQuestion[]? TryParseQuestionsFromResponsesApi(string raw, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);

            // 1) Responses API typical path:
            // output[].content[] may contain output_text blocks with a "text" string
            if (doc.RootElement.TryGetProperty("output", out var outputArr) &&
                outputArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var outItem in outputArr.EnumerateArray())
                {
                    if (!outItem.TryGetProperty("content", out var contentArr) || contentArr.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var c in contentArr.EnumerateArray())
                    {
                        // We look for any block that has a "text" string.
                        if (c.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        {
                            var inner = textEl.GetString();
                            var q = ParseWrapper(inner);
                            if (q != null && q.Length == expectedCount) return q;
                        }
                    }
                }
            }

            // 2) Sometimes the API/gateway returns the JSON object directly
            var direct = ParseWrapper(doc.RootElement.GetRawText());
            if (direct != null && direct.Length == expectedCount) return direct;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static ModelQuestion[]? ParseWrapper(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var wrapper = JsonSerializer.Deserialize<Wrapper>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            return wrapper?.Questions;
        }
        catch
        {
            return null;
        }
    }

    private static string Trunc(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "...");
}
