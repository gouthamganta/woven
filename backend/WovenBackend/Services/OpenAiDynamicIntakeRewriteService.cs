using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WovenBackend.Services;

public class OpenAiDynamicIntakeRewriteService
{
    private readonly IOpenAiResilientClient _ai;
    private readonly ILogger<OpenAiDynamicIntakeRewriteService> _logger;
    private readonly IAiProfileService _aiProfileService;

    public OpenAiDynamicIntakeRewriteService(
        IOpenAiResilientClient ai,
        ILogger<OpenAiDynamicIntakeRewriteService> logger,
        IAiProfileService aiProfileService)
    {
        _ai = ai;
        _logger = logger;
        _aiProfileService = aiProfileService;
    }

    public record RewriteContext(string? FirstName, string? Gender, string? Intent, int? UserId = null);

    // Returned objects are still canonical IDs/keys, only display text changes
    public async Task<DynamicBankQuestion[]> RewriteAsync(
        DynamicBankQuestion[] baseQs,
        RewriteContext ctx,
        string style,
        CancellationToken ct)
    {
        // Load AiProfile for personalization
        AiProfile? aiProfile = null;
        if (ctx.UserId.HasValue && ctx.UserId.Value > 0)
        {
            aiProfile = await _aiProfileService.GetProfileAsync(ctx.UserId.Value, ct);
            _logger.LogInformation("[OpenAI-DYN] Loaded AiProfile for user {UserId}: vibe={Vibe}",
                ctx.UserId, aiProfile?.ConversationTone ?? "unknown");
        }

        var systemPrompt = BuildSystemPrompt(style, aiProfile);
        var userPrompt = BuildUserPrompt(baseQs, ctx, aiProfile);

        try
        {
            var content = await _ai.ExecuteWithSystemAsync("rewrite-dynamic-intake", systemPrompt, userPrompt, ct: ct);
            if (content == null)
            {
                _logger.LogWarning("[OpenAI-DYN] Resilient client returned null -> using base.");
                return baseQs;
            }

            var parsed = ParseWrapper(content);
            if (parsed == null)
            {
                _logger.LogWarning("[OpenAI-DYN] Parse invalid -> using base.");
                return baseQs;
            }

            // Validate IDs and keys. If any mismatch, fallback to base.
            var baseById = baseQs.ToDictionary(q => q.Id, q => q);
            if (parsed.Any(q => !baseById.ContainsKey(q.Id)))
                return baseQs;

            foreach (var q in parsed)
            {
                var baseKeys = baseById[q.Id].Options.Select(o => o.Key).ToHashSet();
                var parsedKeys = q.Options.Select(o => o.Key).ToHashSet();
                if (!baseKeys.SetEquals(parsedKeys)) return baseQs;
            }

            // Normalize: preserve canonical ids/keys, but accept rewritten text fields.
            var normalized = parsed.Select(q =>
            {
                var baseQ = baseById[q.Id];

                var optByKey = q.Options.ToDictionary(o => o.Key, o => o);
                var normalizedOptions = baseQ.Options.Select(o =>
                {
                    var p = optByKey[o.Key];
                    var label = string.IsNullOrWhiteSpace(p.Label) ? o.Label : p.Label.Trim();
                    var sub = string.IsNullOrWhiteSpace(p.SubLabel) ? o.SubLabel : p.SubLabel.Trim();
                    return new DynamicBankOption(o.Key, label, sub);
                }).ToArray();

                var text = string.IsNullOrWhiteSpace(q.Text) ? baseQ.Text : q.Text.Trim();
                return new DynamicBankQuestion(baseQ.Id, text, normalizedOptions);
            }).ToArray();

            _logger.LogInformation("[OpenAI-DYN] Rewrite OK.");
            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[OpenAI-DYN] Exception -> using base.");
            return baseQs;
        }
    }

    private static string BuildSystemPrompt(string style, AiProfile? profile)
    {
        var personalizationRules = "";
        if (profile != null)
        {
            var vibe = profile.ConversationTone;
            var toneGuidance = vibe switch
            {
                "playful" => "Use light, fun language that matches their playful energy",
                "thoughtful" => "Use reflective, slightly deeper phrasing",
                "calm" => "Use gentle, unhurried language",
                _ => "Use warm, conversational language"
            };

            personalizationRules = $@"

PERSONALIZATION RULES:
- This user's current vibe is: {vibe}
- Tone guidance: {toneGuidance}
- Make the options feel like they're speaking to THIS person, not a generic user";
        }

        return $@"You rewrite a 3-question dynamic intake for a dating app.

Hard rules:
- Keep the same question IDs: d1_battery, d2_tone, d3_role (never change).
- Keep the same option keys for each question (never change).
- Do NOT add/remove/reorder questions or options.
- Only rewrite display text: question text, option label, option subLabel.
- Keep it natural, human, minimal, {style}.
- No emojis.
- Output JSON ONLY. No commentary.
{personalizationRules}

CRITICAL ANTI-GENERIC RULES:
- NEVER use these banned phrases: ""meaningful"", ""genuine"", ""good energy"", ""real conversations"", ""authentic"", ""connection"", ""vibe check""
- Each option should feel specific and relatable, not corporate or clinical
- Use conversational, human language

Return EXACTLY this JSON shape:
{{
  ""questions"": [
    {{
      ""id"": ""d1_battery"",
      ""text"": ""..."",
      ""options"": [
        {{ ""key"": ""high"", ""label"": ""..."", ""subLabel"": ""..."" }},
        {{ ""key"": ""medium"", ""label"": ""..."", ""subLabel"": ""..."" }},
        {{ ""key"": ""low"", ""label"": ""..."", ""subLabel"": ""..."" }}
      ]
    }},
    {{
      ""id"": ""d2_tone"",
      ""text"": ""..."",
      ""options"": [
        {{ ""key"": ""playful"", ""label"": ""..."", ""subLabel"": ""..."" }},
        {{ ""key"": ""serious"", ""label"": ""..."", ""subLabel"": ""..."" }},
        {{ ""key"": ""calm"", ""label"": ""..."", ""subLabel"": ""..."" }}
      ]
    }},
    {{
      ""id"": ""d3_role"",
      ""text"": ""..."",
      ""options"": [
        {{ ""key"": ""driver"", ""label"": ""..."", ""subLabel"": ""..."" }},
        {{ ""key"": ""copilot"", ""label"": ""..."", ""subLabel"": ""..."" }},
        {{ ""key"": ""passenger"", ""label"": ""..."", ""subLabel"": ""..."" }}
      ]
    }}
  ]
}}";
    }

    private static string BuildUserPrompt(DynamicBankQuestion[] baseQs, RewriteContext ctx, AiProfile? profile)
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
            contextBits.Add($"current_vibe: {profile.ConversationTone}");
        }

        var contextLine = contextBits.Count == 0 ? "user_context: none" : "user_context: " + string.Join(", ", contextBits);

        var baseJson = JsonSerializer.Serialize(baseQs.Select(q => new
        {
            id = q.Id,
            text = q.Text,
            options = q.Options.Select(o => new { key = o.Key, label = o.Label, subLabel = o.SubLabel })
        }));

        return
$@"{contextLine}

Rewrite this intake instrument (JSON):
{baseJson}

Return ONLY the JSON object in the required shape.";
    }

    private record Wrapper(ModelQuestion[] Questions);
    private record ModelQuestion(string Id, string Text, ModelOption[] Options);
    private record ModelOption(string Key, string Label, string SubLabel);

    private static DynamicBankQuestion[]? ParseWrapper(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var wrapper = JsonSerializer.Deserialize<Wrapper>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (wrapper?.Questions == null || wrapper.Questions.Length != 3) return null;

            return wrapper.Questions.Select(q => new DynamicBankQuestion(
                q.Id,
                q.Text ?? "",
                (q.Options ?? Array.Empty<ModelOption>()).Select(o =>
                    new DynamicBankOption(o.Key, o.Label ?? "", o.SubLabel ?? "")
                ).ToArray()
            )).ToArray();
        }
        catch
        {
            return null;
        }
    }

}
