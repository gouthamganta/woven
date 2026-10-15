using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Infrastructure;
using WovenBackend.Services;

namespace WovenBackend.Tests;

// Every HTTP client is attached to this local handler. It never opens a socket.
internal sealed class QaHttpTransport : HttpMessageHandler, IHttpClientFactory
{
    public List<(string Url, string Body, string? Authorization, string? Correlation)> Requests { get; } = [];
    public Func<int, HttpResponseMessage> Response { get; set; } = _ => Json("{}");
    public Exception? Failure { get; set; }
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Requests.Add((request.RequestUri!.ToString(), request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct), request.Headers.Authorization?.ToString(), request.Headers.TryGetValues("X-Correlation-ID", out var ids) ? ids.First() : null));
        if (Failure != null) throw Failure; return Response(Requests.Count);
    }
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json) };
}
public class AiTransportContractTests
{
    private const string ChatJson = "{\"choices\":[{\"message\":{\"content\":\"Unit output\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";
    private static IConfiguration Config(string? key = "unit-only-never-network", string? budget = null) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "OpenAI:ApiKey", key }, { "OpenAI:DailyBudgetUsd", budget } }).Build();
    private sealed class Correlation : ICorrelationService { public string CorrelationId => "unit-trace"; }
    private static OpenAiClient Central(QaHttpTransport transport, string? key = "unit-only-never-network") => new(transport, Config(key), new Correlation(), NullLogger<OpenAiClient>.Instance);
    private static OpenAiRequest Request() => new("unit-model", [new("system", "Unit system"), new("user", "Unit user")], .2f, 100, "unit-purpose");
    [Fact]
    public async Task ChatPreservesPayloadCorrelationAndTokenUsage()
    {
        using var transport = new QaHttpTransport { Response = _ => QaHttpTransport.Json(ChatJson) }; var result = await Central(transport).ChatAsync(Request());
        Assert.Equal("Unit output", result.Content); Assert.Equal(10, result.PromptTokens); Assert.Equal(5, result.CompletionTokens); Assert.Equal(15, result.TotalTokens);
        var request = Assert.Single(transport.Requests); Assert.EndsWith("/chat/completions", request.Url); Assert.Equal("unit-trace", request.Correlation); Assert.Equal("Bearer unit-only-never-network", request.Authorization);
        using var payload = JsonDocument.Parse(request.Body); Assert.Equal("unit-model", payload.RootElement.GetProperty("model").GetString()); Assert.Equal(100, payload.RootElement.GetProperty("max_tokens").GetInt32()); Assert.Equal(2, payload.RootElement.GetProperty("messages").GetArrayLength());
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingConfigurationCannotReachTransport(string? key)
    {
        using var transport = new QaHttpTransport(); var client = Central(transport, key);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ChatAsync(Request())); await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync("Unit", 3)); await Assert.ThrowsAsync<InvalidOperationException>(() => client.TtsAsync("Unit")); Assert.Empty(transport.Requests);
    }
    [Theory]
    [InlineData("{\"data\":[{\"embedding\":[1,0,-1]}]}", true)]
    [InlineData("{}", false)]
    [InlineData("malformed", false)]
    public async Task EmbeddingParsesNumericArrayOrReturnsUnavailable(string json, bool valid)
    {
        using var transport = new QaHttpTransport { Response = _ => QaHttpTransport.Json(json) }; var result = await Central(transport).EmbedAsync("Unit input", 3);
        if (valid) Assert.Equal(new[] { 1f, 0, -1 }, result); else Assert.Null(result); using var payload = JsonDocument.Parse(Assert.Single(transport.Requests).Body); Assert.Equal(3, payload.RootElement.GetProperty("dimensions").GetInt32()); Assert.Equal("Unit input", payload.RootElement.GetProperty("input").GetString());
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SpeechReturnsBytesOrUnavailableAndUsesRequestedVoice(bool valid)
    {
        using var transport = new QaHttpTransport { Response = _ => new HttpResponseMessage(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest) { Content = new ByteArrayContent([1, 2, 3]) } }; var result = await Central(transport).TtsAsync("Unit text", "unit-voice");
        if (valid) Assert.Equal(new byte[] { 1, 2, 3 }, result); else Assert.Null(result); using var payload = JsonDocument.Parse(Assert.Single(transport.Requests).Body); Assert.Equal("unit-voice", payload.RootElement.GetProperty("voice").GetString()); Assert.Equal("mp3", payload.RootElement.GetProperty("response_format").GetString());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateLimitRetriesBoundedlyAndEventuallySucceedsOrReports429(bool alwaysLimited)
    {
        using var transport = new QaHttpTransport { Response = i => { if (!alwaysLimited && i > 1) return QaHttpTransport.Json(ChatJson); var response = QaHttpTransport.Json("{}", HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return response; } };
        if (alwaysLimited) { var error = await Assert.ThrowsAsync<OpenAiException>(() => Central(transport).ChatAsync(Request())); Assert.Equal(429, error.StatusCode); Assert.Equal(4, transport.Requests.Count); }
        else { Assert.Equal("Unit output", (await Central(transport).ChatAsync(Request())).Content); Assert.Equal(2, transport.Requests.Count); }
    }
    [Fact]
    public async Task CancelledCentralCallDoesNotRetryOrPerformNetworkWork()
    {
        using var transport = new QaHttpTransport(); using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Central(transport).ChatAsync(Request(), cts.Token)); Assert.Empty(transport.Requests);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResilientPayloadJsonModeIsExplicitAndUsageIsRecordedOnlyAfterSuccess(bool jsonMode)
    {
        using var transport = new QaHttpTransport { Response = _ => QaHttpTransport.Json(ChatJson) }; using var http = transport.CreateClient("unit"); var tracker = new OpenAiCostTracker(NullLogger<OpenAiCostTracker>.Instance, Config());
        var client = new OpenAiResilientClient(http, Config(), new CircuitBreakerService(NullLogger<CircuitBreakerService>.Instance), tracker, NullLogger<OpenAiResilientClient>.Instance);
        Assert.Equal("Unit output", await client.ExecuteWithSystemAsync("unit", "System", "User", jsonMode, .1f)); var stats = tracker.GetTodayStats(); Assert.Equal(1, stats.TotalRequests); Assert.Equal(10, stats.TotalPromptTokens); Assert.Equal(5, stats.TotalCompletionTokens);
        using var payload = JsonDocument.Parse(Assert.Single(transport.Requests).Body); Assert.Equal(jsonMode ? JsonValueKind.Object : JsonValueKind.Null, payload.RootElement.GetProperty("response_format").ValueKind); Assert.Equal("System", payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }
    [Theory]
    [InlineData(null, null)]
    [InlineData("unit", "0")]
    public async Task ResilientMissingKeyOrExhaustedBudgetNeverCallsProvider(string? key, string? budget)
    {
        using var transport = new QaHttpTransport(); using var http = transport.CreateClient("unit"); var config = Config(key, budget); var tracker = new OpenAiCostTracker(NullLogger<OpenAiCostTracker>.Instance, config);
        var client = new OpenAiResilientClient(http, config, new CircuitBreakerService(NullLogger<CircuitBreakerService>.Instance), tracker, NullLogger<OpenAiResilientClient>.Instance); Assert.Null(await client.ExecuteAsync("unit", "Unit")); Assert.Empty(transport.Requests); Assert.Equal(0, tracker.GetTodayStats().TotalRequests);
    }
    [Theory]
    [InlineData("malformed")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}")]
    public async Task ResilientMalformedResponseIsUnavailableWithoutInventingUsage(string json)
    {
        using var transport = new QaHttpTransport { Response = _ => QaHttpTransport.Json(json) }; using var http = transport.CreateClient("unit"); var tracker = new OpenAiCostTracker(NullLogger<OpenAiCostTracker>.Instance, Config());
        var client = new OpenAiResilientClient(http, Config(), new CircuitBreakerService(NullLogger<CircuitBreakerService>.Instance), tracker, NullLogger<OpenAiResilientClient>.Instance); Assert.Null(await client.ExecuteAsync("unit", "Unit")); Assert.Equal(0, tracker.GetTodayStats().TotalRequests); Assert.Single(transport.Requests);
    }
    [Fact]
    public void CostAccountingSeparatesOperationsUsesExactBudgetBoundaryAndResets()
    {
        var tracker = new OpenAiCostTracker(NullLogger<OpenAiCostTracker>.Instance, Config(budget: "0.007")); Assert.False(tracker.IsBudgetExceeded()); Assert.Equal(0, tracker.GetTodayStats().AverageLatencyMs);
        tracker.RecordUsage("unit", 100, 200, 50); Assert.True(tracker.IsBudgetExceeded()); tracker.RecordUsage("other", 50, 100, 150); var stats = tracker.GetTodayStats(); Assert.Equal(2, stats.TotalRequests); Assert.Equal(150, stats.TotalPromptTokens); Assert.Equal(300, stats.TotalCompletionTokens); Assert.Equal(100, stats.AverageLatencyMs);
        Assert.Equal(2, stats.OperationBreakdown.Count); Assert.Equal(50, stats.OperationBreakdown["unit"].AverageLatencyMs); Assert.Equal(.0105, stats.TotalCostUsd, 8); tracker.ResetToday(); Assert.Equal(0, tracker.GetTodayStats().TotalRequests); Assert.False(tracker.IsBudgetExceeded());
    }
}
