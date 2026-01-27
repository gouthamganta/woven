using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WovenBackend.Services;

/// <summary>
/// Resilient wrapper for OpenAI API calls with circuit breaker, retry logic, and cost tracking.
/// </summary>
public interface IOpenAiResilientClient
{
    /// <summary>
    /// Executes an OpenAI API call with resilience patterns.
    /// Returns null if circuit is open or budget exceeded (fallback mode).
    /// </summary>
    Task<string?> ExecuteAsync(
        string operationType,
        string prompt,
        bool useJsonMode = true,
        CancellationToken ct = default);
}

public class OpenAiResilientClient : IOpenAiResilientClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ICircuitBreakerService _circuitBreaker;
    private readonly IOpenAiCostTracker _costTracker;
    private readonly ILogger<OpenAiResilientClient> _logger;

    private const string CircuitName = "openai-api";
    private const int MaxRetries = 2;
    private static readonly TimeSpan[] RetryDelays = new[]
    {
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1500)
    };

    public OpenAiResilientClient(
        HttpClient http,
        IConfiguration config,
        ICircuitBreakerService circuitBreaker,
        IOpenAiCostTracker costTracker,
        ILogger<OpenAiResilientClient> logger)
    {
        _http = http;
        _config = config;
        _circuitBreaker = circuitBreaker;
        _costTracker = costTracker;
        _logger = logger;
    }

    public async Task<string?> ExecuteAsync(
        string operationType,
        string prompt,
        bool useJsonMode = true,
        CancellationToken ct = default)
    {
        // Check budget first (before circuit breaker to avoid wasting retries)
        if (_costTracker.IsBudgetExceeded())
        {
            _logger.LogWarning("[OpenAI Resilient] Daily budget exceeded, denying request for {OperationType}",
                operationType);
            return null; // Graceful degradation - return null for fallback handling
        }

        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[OpenAI Resilient] API key missing");
            return null;
        }

        // Execute through circuit breaker with retry logic
        try
        {
            var result = await _circuitBreaker.ExecuteAsync(
                CircuitName,
                async (circuitCt) => await ExecuteWithRetryAsync(operationType, prompt, apiKey, useJsonMode, circuitCt),
                ct);

            return result;
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning("[OpenAI Resilient] {Message}", ex.Message);
            return null; // Graceful degradation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenAI Resilient] Failed after all retries for {OperationType}", operationType);
            return null; // Graceful degradation
        }
    }

    private async Task<string> ExecuteWithRetryAsync(
        string operationType,
        string prompt,
        string apiKey,
        bool useJsonMode,
        CancellationToken ct)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = RetryDelays[attempt - 1];
                    _logger.LogInformation("[OpenAI Resilient] Retry attempt {Attempt}/{MaxRetries} after {Delay}ms for {OperationType}",
                        attempt, MaxRetries, delay.TotalMilliseconds, operationType);
                    await Task.Delay(delay, ct);
                }

                return await ExecuteOpenAiCallAsync(operationType, prompt, apiKey, useJsonMode, ct);
            }
            catch (HttpRequestException ex) when (IsTransientError(ex))
            {
                lastException = ex;
                _logger.LogWarning(ex, "[OpenAI Resilient] Transient error on attempt {Attempt}/{MaxRetries} for {OperationType}",
                    attempt + 1, MaxRetries + 1, operationType);

                if (attempt >= MaxRetries)
                {
                    throw;
                }
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout (not user cancellation)
                lastException = new TimeoutException($"OpenAI request timed out for {operationType}");
                _logger.LogWarning("[OpenAI Resilient] Timeout on attempt {Attempt}/{MaxRetries} for {OperationType}",
                    attempt + 1, MaxRetries + 1, operationType);

                if (attempt >= MaxRetries)
                {
                    throw lastException;
                }
            }
        }

        throw lastException ?? new Exception($"Unknown error executing {operationType}");
    }

    private async Task<string> ExecuteOpenAiCallAsync(
        string operationType,
        string prompt,
        string apiKey,
        bool useJsonMode,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var endpoint = _config["OpenAI:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
        var model = _config["OpenAI:Model"] ?? "gpt-4.1-mini";

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful assistant for a dating app." },
                new { role = "user", content = prompt }
            },
            response_format = useJsonMode ? new { type = "json_object" } : null,
            temperature = 0.7
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request, ct);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var truncatedError = errorBody.Length > 500 ? errorBody.Substring(0, 500) : errorBody;

            _logger.LogError("[OpenAI Resilient] HTTP {StatusCode} for {OperationType}: {Error}",
                (int)response.StatusCode, operationType, truncatedError);

            throw new HttpRequestException($"OpenAI API returned {response.StatusCode}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Extract token usage
            var usage = root.GetProperty("usage");
            var promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
            var completionTokens = usage.GetProperty("completion_tokens").GetInt32();

            // Extract response content
            var choices = root.GetProperty("choices");
            var firstChoice = choices[0];
            var message = firstChoice.GetProperty("message");
            var responseContent = message.GetProperty("content").GetString();

            // Record usage
            _costTracker.RecordUsage(operationType, promptTokens, completionTokens, stopwatch.Elapsed.TotalMilliseconds);

            return responseContent ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenAI Resilient] Failed to parse OpenAI response for {OperationType}", operationType);
            throw;
        }
    }

    private bool IsTransientError(HttpRequestException ex)
    {
        // Retry on network errors, 5xx errors, rate limits
        var statusCode = ex.StatusCode;
        return statusCode == null ||
               statusCode == System.Net.HttpStatusCode.TooManyRequests ||
               statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
               statusCode == System.Net.HttpStatusCode.GatewayTimeout ||
               ((int)statusCode >= 500 && (int)statusCode < 600);
    }
}
