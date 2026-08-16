using System.Text.Json;

namespace WovenBackend.Services;

public class OpenAiRewriteService
{
    private readonly IOpenAiResilientClient _ai;
    private readonly ILogger<OpenAiRewriteService> _logger;
    private readonly IAiProfileService _aiProfileService;

    public OpenAiRewriteService(
        IOpenAiResilientClient ai,
        ILogger<OpenAiRewriteService> logger,
        IAiProfileService aiProfileService)
    {
        _ai = ai;
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
        // Load AiProfile for personalization
        AiProfile? aiProfile = null;
        if (ctx.UserId.HasValue && ctx.UserId.Value > 0)
        {
            aiProfile = await _aiProfileService.GetProfileAsync(ctx.UserId.Value, ct);
            _logger.LogInformation("[OpenAI] Loaded AiProfile for user {UserId}: {TopPillars} top pillars",
                ctx.UserId, aiProfile?.TopPillars.Count ?? 0);
        }

        var systemPrompt = BuildSystemPrompt(style, aiProfile);
        var userPrompt = BuildUserPrompt(baseQuestions, ctx, aiProfile);

        try
        {
            var content = await _ai.ExecuteWithSystemAsync("rewrite-foundational", systemPrompt, userPrompt, ct: ct);
            if (content == null)
            {
                _logger.LogWarning("[OpenAI] Resilient client returned null -> using bank.");
                return baseQuestions;
            }

            var parsed = ParseWrapper(content);
            if (parsed == null || parsed.Length != baseQuestions.Length)
            {
                _logger.LogWarning("[OpenAI] Parse/shape invalid -> using bank.");
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

    // Only parses id + text; pillars are preserved from the bank.
    private record Wrapper(ModelQuestion[] Questions);
    private record ModelQuestion(string Id, string Text);

    private static ModelQuestion[]? ParseWrapper(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var wrapper = JsonSerializer.Deserialize<Wrapper>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return wrapper?.Questions;
        }
        catch { return null; }
    }
}
