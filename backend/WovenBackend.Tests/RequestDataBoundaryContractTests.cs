using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Infrastructure;
using WovenBackend.Services.Security;

namespace WovenBackend.Tests;

public class RequestDataBoundaryContractTests
{
    [Theory]
    [InlineData("Unauthorized", true)]
    [InlineData("Unexpected", false)]
    public async Task AuthHandler_HandlesOnlyAuthorizationErrorsAndNeverReturnsInternalDetails(string kind, bool handled)
    {
        var http = Context(); Exception error = kind == "Unauthorized" ? new UnauthorizedAccessException("Private internal identity") : new InvalidOperationException("Private internal failure");
        Assert.Equal(handled, await new AuthExceptionHandler().TryHandleAsync(http, error, default));
        if (!handled) { Assert.Equal(0, http.Response.Body.Length); return; }
        Assert.Equal(401, http.Response.StatusCode); var json = await Read(http);
        Assert.Equal("Unauthorized", json.GetProperty("error").GetString()); Assert.Equal("unit-correlation", json.GetProperty("correlationId").GetString()); Assert.DoesNotContain("Private", json.GetRawText());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GlobalHandler_RedactsExceptionAndReturnsTimestampAndCorrelation(bool hasCorrelation)
    {
        var http = Context(hasCorrelation); var before = DateTimeOffset.UtcNow;
        Assert.True(await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(http, new Exception("Synthetic private database detail"), default));
        Assert.Equal(500, http.Response.StatusCode); var json = await Read(http);
        Assert.Equal("An unexpected error occurred", json.GetProperty("error").GetString()); Assert.Equal(hasCorrelation ? "unit-correlation" : "unknown", json.GetProperty("correlationId").GetString());
        Assert.InRange(json.GetProperty("timestamp").GetDateTimeOffset(), before, DateTimeOffset.UtcNow); Assert.DoesNotContain("database", json.GetRawText());
    }

    [Fact]
    public async Task DomainHandler_Uses422ForKnownBusinessErrorsAndLeavesOtherErrorsUnhandled()
    {
        var http = Context(); var handler = new DomainExceptionHandler(); Assert.False(await handler.TryHandleAsync(http, new Exception("internal"), default)); Assert.Equal(0, http.Response.Body.Length);
        Assert.True(await handler.TryHandleAsync(http, new DomainException("QUOTA", "Daily quota reached"), default)); Assert.Equal(422, http.Response.StatusCode);
        var json = await Read(http); Assert.Equal("QUOTA", json.GetProperty("code").GetString()); Assert.Equal("Daily quota reached", json.GetProperty("error").GetString()); Assert.Equal("unit-correlation", json.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("client-id", "alternate-id", "client-id")]
    [InlineData(null, "alternate-id", "alternate-id")]
    [InlineData(null, null, null)]
    public async Task Correlation_RequestResponseAndDownstreamShareOneIdentity(string? correlation, string? request, string? expected)
    {
        var http = Context(false); if (correlation != null) http.Request.Headers[CorrelationIdMiddleware.HeaderName] = correlation; if (request != null) http.Request.Headers["X-Request-ID"] = request;
        string? downstream = null;
        await new CorrelationIdMiddleware(context => { downstream = (string)context.Items[CorrelationIdMiddleware.ItemsKey]!; return Task.CompletedTask; }).InvokeAsync(http);
        Assert.Equal(downstream, http.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
        if (expected != null) Assert.Equal(expected, downstream); else Assert.Matches("^[0-9a-f]{16}$", downstream!);
        var accessor = new HttpContextAccessor { HttpContext = http }; Assert.Equal(downstream, new CorrelationService(accessor).CorrelationId);
        accessor.HttpContext = null; Assert.Equal("no-context", new CorrelationService(accessor).CorrelationId);
    }

    [Theory]
    [InlineData("X-User-Id")]
    [InlineData("x-userid")]
    [InlineData("User-Id")]
    public async Task Outbound_RemovesRawIdentityKeepsRequestAndAuditsWithoutQueryData(string identityHeader)
    {
        var audit = RecordedSideEffects.Create<ISecurityAuditService>(new ServiceCollection());
        var transport = new CapturingTransport();
        using var client = new HttpClient(new OutboundPiiHandler((ISecurityAuditService)(object)audit, NullLogger<OutboundPiiHandler>.Instance) { InnerHandler = transport });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://unit.invalid/embedding?private=synthetic-marker") { Content = new StringContent("Unit payload") };
        request.Headers.Add(identityHeader, "42"); request.Headers.Add("X-Correlation-ID", "unit-trace");
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Same(request, transport.Request); Assert.False(request.Headers.Contains(identityHeader)); Assert.Equal("unit-trace", Assert.Single(request.Headers.GetValues("X-Correlation-ID")));
        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(request.Headers.GetValues("X-Anonymous-Token"))); Assert.Equal("Unit payload", await request.Content.ReadAsStringAsync());
        var call = Assert.Single(audit.Calls); Assert.Equal("external_api_call", call.Args[0]); Assert.Equal("POST https://unit.invalid/embedding", call.Args[4]); Assert.DoesNotContain("synthetic-marker", (string)call.Args[4]!);
    }

    private static DefaultHttpContext Context(bool correlation = true)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        http.Response.Body = new MemoryStream(); if (correlation) http.Items[CorrelationIdMiddleware.ItemsKey] = "unit-correlation"; return http;
    }
    private static async Task<JsonElement> Read(HttpContext http) { http.Response.Body.Position = 0; using var json = await JsonDocument.ParseAsync(http.Response.Body); return json.RootElement.Clone(); }
    private sealed class CapturingTransport : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Request = request; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)); }
    }
}
