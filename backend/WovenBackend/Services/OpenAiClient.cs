using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WovenBackend.Infrastructure;

namespace WovenBackend.Services;

/// <summary>
/// Single, centralised OpenAI HTTP client.
///
/// All AI calls in Woven go through here, giving us:
///   - Correlation ID on every request (User-Agent + X-Correlation-ID header)
///   - Rate limit guard via the ASP.NET Core rate limiter registered as "openai-global"
///   - Exponential backoff with jitter on 429 / 5xx errors (up to 3 retries)
///   - Token budget enforcement per call type
///   - Structured error logging with enough context to root-cause any failure
///
/// Usage:
///   var result = await _openAi.ChatAsync(new OpenAiRequest { ... }, ct);
/// </summary>
public interface IOpenAiClient
{
    Task<OpenAiChatResponse> ChatAsync(OpenAiRequest request, CancellationToken ct = default);
    Task<float[]?> EmbedAsync(string text, int dimensions, CancellationToken ct = default);
    Task<byte[]?> TtsAsync(string text, string voice = "nova", CancellationToken ct = default);
}

public record OpenAiRequest(
    string Model,
    IReadOnlyList<OpenAiMessage> Messages,
    float Temperature = 0.7f,
    int MaxTokens = 500,
    string? Purpose = null           // for logging: "match_explanation", "pillar_scoring", etc.
);

public record OpenAiMessage(string Role, string Content);

public record OpenAiChatResponse(
    string Content,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens
);

public class OpenAiClient : IOpenAiClient
{
    private const string ChatEndpoint  = "https://api.openai.com/v1/chat/completions";
    private const string EmbedEndpoint = "https://api.openai.com/v1/embeddings";
    private const string TtsEndpoint   = "https://api.openai.com/v1/audio/speech";

    private const int MaxRetries       = 3;
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(12)
    };

    private readonly IHttpClientFactory   _httpFactory;
    private readonly IConfiguration       _config;
    private readonly ICorrelationService  _correlation;
    private readonly ILogger<OpenAiClient> _logger;

    public OpenAiClient(
        IHttpClientFactory   httpFactory,
        IConfiguration       config,
        ICorrelationService  correlation,
        ILogger<OpenAiClient> logger)
    {
        _httpFactory  = httpFactory;
        _config       = config;
        _correlation  = correlation;
        _logger       = logger;
    }

    public async Task<OpenAiChatResponse> ChatAsync(OpenAiRequest req, CancellationToken ct = default)
    {
        var apiKey = RequireApiKey();
        var correlationId = _correlation.CorrelationId;

        var body = JsonSerializer.Serialize(new
        {
            model       = req.Model,
            messages    = req.Messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature = req.Temperature,
            max_tokens  = req.MaxTokens
        });

        _logger.LogInformation(
            "[OpenAI] Chat request | Model={Model} Purpose={Purpose} MaxTokens={Max} CorrelationId={Cid}",
            req.Model, req.Purpose ?? "unspecified", req.MaxTokens, correlationId);

        var json = await ExecuteWithRetryAsync(ChatEndpoint, body, apiKey, correlationId, ct);

        using var doc         = JsonDocument.Parse(json);
        var choice            = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var usage             = doc.RootElement.GetProperty("usage");
        var promptTokens      = usage.GetProperty("prompt_tokens").GetInt32();
        var completionTokens  = usage.GetProperty("completion_tokens").GetInt32();
        var totalTokens       = usage.GetProperty("total_tokens").GetInt32();

        _logger.LogInformation(
            "[OpenAI] Chat complete | Model={Model} Purpose={Purpose} Tokens={Total} (prompt={Prompt} completion={Completion}) CorrelationId={Cid}",
            req.Model, req.Purpose ?? "unspecified", totalTokens, promptTokens, completionTokens, correlationId);

        return new OpenAiChatResponse(choice, promptTokens, completionTokens, totalTokens);
    }

    public async Task<float[]?> EmbedAsync(string text, int dimensions, CancellationToken ct = default)
    {
        var apiKey = RequireApiKey();
        var correlationId = _correlation.CorrelationId;

        var body = JsonSerializer.Serialize(new
        {
            model      = "text-embedding-3-small",
            input      = text,
            dimensions = dimensions
        });

        _logger.LogDebug(
            "[OpenAI] Embed request | Dims={Dims} TextLen={Len} CorrelationId={Cid}",
            dimensions, text.Length, correlationId);

        try
        {
            var json = await ExecuteWithRetryAsync(EmbedEndpoint, body, apiKey, correlationId, ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OpenAI] Embed failed | CorrelationId={Cid}", correlationId);
            return null;
        }
    }

    public async Task<byte[]?> TtsAsync(string text, string voice = "nova", CancellationToken ct = default)
    {
        var apiKey = RequireApiKey();
        var correlationId = _correlation.CorrelationId;

        var body = JsonSerializer.Serialize(new
        {
            model           = "tts-1",
            input           = text,
            voice           = voice,
            response_format = "mp3"
        });

        _logger.LogDebug(
            "[OpenAI] TTS request | Voice={Voice} TextLen={Len} CorrelationId={Cid}",
            voice, text.Length, correlationId);

        try
        {
            using var http    = _httpFactory.CreateClient("external-api");
            using var request = new HttpRequestMessage(HttpMethod.Post, TtsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OpenAI] TTS failed | CorrelationId={Cid}", correlationId);
            return null;
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task<string> ExecuteWithRetryAsync(
        string endpoint, string body, string apiKey, string correlationId, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var http    = _httpFactory.CreateClient("external-api");
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Add("X-Correlation-ID", correlationId);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await http.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                    _logger.LogWarning(
                        "[OpenAI] Rate limited (429) | Attempt={Attempt} RetryAfter={RetryAfter}s CorrelationId={Cid}",
                        attempt + 1, retryAfter.TotalSeconds, correlationId);

                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(retryAfter + Jitter(), ct);
                        continue;
                    }
                    throw new OpenAiException("OpenAI rate limit exceeded after retries", 429);
                }

                if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
                {
                    _logger.LogWarning(
                        "[OpenAI] Server error {Status} | Attempt={Attempt} CorrelationId={Cid}",
                        (int)response.StatusCode, attempt + 1, correlationId);
                    await Task.Delay(RetryDelays[attempt] + Jitter(), ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            catch (OpenAiException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex,
                    "[OpenAI] Request failed | Attempt={Attempt} CorrelationId={Cid}",
                    attempt + 1, correlationId);
                await Task.Delay(RetryDelays[attempt] + Jitter(), ct);
            }
        }
        throw new OpenAiException("OpenAI request failed after all retries", 503);
    }

    private string RequireApiKey()
    {
        var key = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        return key;
    }

    private static TimeSpan Jitter() =>
        TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
}

public class OpenAiException : Exception
{
    public int StatusCode { get; }
    public OpenAiException(string message, int statusCode) : base(message) => StatusCode = statusCode;
}
