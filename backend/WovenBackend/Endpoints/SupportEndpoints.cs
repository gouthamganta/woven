using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace WovenBackend.Endpoints;

public static class SupportEndpoints
{
    private record ChatMessage(string Role, string Content);
    private record ChatRequest(List<ChatMessage> Messages);
    private record ChatResponse(string Reply);

    private const string SystemPrompt = """
        You are Woven's in-app companion — a warm, perceptive presence that speaks on behalf of the app.
        You are not a generic chatbot. You are an extension of Woven itself.

        Woven is a dating app built around intention, compatibility, and meaningful connection. Here is how it works:

        - Moments: A daily deck of profile cards. Users can Like (◈), Save (◇), or Pass (⏳). If two people both like each other, a timed "balloon chat" opens automatically.
        - Chats: Ongoing conversations with matches. Balloon chats are timed to encourage intentional conversation.
        - Commons: A shared discovery space (still rolling out).
        - You: The user's profile, settings, and personal insights.
        - Onboarding: 9 steps — basics, intent, foundational questions, photos, about you, lifestyle, review, then the start screen.

        Your role:
        - Help users navigate ("where are my saved profiles?" → "tap the ◇ Saved button in the top-right of Moments")
        - Explain features simply and warmly
        - Listen to frustrations — acknowledge them genuinely before helping
        - Be honest: if a feature isn't live yet, say so simply
        - Keep responses short — 2 to 4 sentences unless the user clearly needs more
        - Never claim to take actions you cannot take (you cannot send messages, edit profiles, etc.)

        Voice: thoughtful, warm, direct. A little poetic but never overwrought. Never corporate. Never robotic.
        """;

    public static void MapSupportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/support").RequireAuthorization();

        group.MapPost("/chat", async (
            ChatRequest req,
            IConfiguration config,
            IHttpClientFactory httpFactory,
            ILogger<ChatRequest> logger,
            CancellationToken ct) =>
        {
            if (req.Messages is null || req.Messages.Count == 0)
                return Results.BadRequest(new { error = "No messages provided." });

            // Cap conversation history to last 20 messages to control token spend
            var recentMessages = req.Messages.TakeLast(20).ToList();

            var apiKey = config["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("[Support] OpenAI API key not configured");
                return Results.Json(new ChatResponse("I'm having a little trouble connecting right now. Try again in a moment."));
            }

            var endpoint = config["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
            var model    = config["OpenAI:Model"]    ?? "gpt-4o-mini";

            var messages = new List<object>
            {
                new { role = "system", content = SystemPrompt }
            };
            messages.AddRange(recentMessages.Select(m => new { role = m.Role, content = m.Content }));

            var body = JsonSerializer.Serialize(new
            {
                model,
                messages,
                temperature = 0.75,
                max_tokens  = 300,
            });

            using var http = httpFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[Support] OpenAI returned {Status}", response.StatusCode);
                    return Results.Json(new ChatResponse("Something went wrong on my end. Try again?"));
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var reply = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "I didn't quite catch that. Could you rephrase?";

                return Results.Json(new ChatResponse(reply.Trim()));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Support] Chat request failed");
                return Results.Json(new ChatResponse("I'm having trouble reaching my thoughts right now. Try again in a moment."));
            }
        });
    }
}
