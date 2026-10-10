using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Endpoints;
using WovenBackend.Services.Feedback;

namespace WovenBackend.Tests;

// Reuses the registered-handler technique from phone PR137. No listener,
// database container, authentication middleware or paid provider is started.
public class EndpointContractTests
{
    [Theory]
    [InlineData("uid", "1")]
    [InlineData("sub", "2")]
    [InlineData(ClaimTypes.NameIdentifier, "3")]
    public void SharedClaimHelper_UsesSupportedPositiveIdentifiers(string type, string value)
        => Assert.Equal(int.Parse(value), EndpointHelper.GetUserId(new ClaimsPrincipal(new ClaimsIdentity([new Claim(type, value)], "Unit"))));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("bad")]
    [InlineData("2147483648")]
    public void SharedClaimHelper_RejectsInvalidPrimaryClaimsEvenWithAValidFallback(string value)
        => Assert.Throws<UnauthorizedAccessException>(() => EndpointHelper.GetUserId(new ClaimsPrincipal(new ClaimsIdentity([new Claim("uid", value), new Claim("sub", "2")], "Unit"))));

    [Fact]
    public void SharedClaimHelper_RejectsMissingIdentity() => Assert.Throws<UnauthorizedAccessException>(() => EndpointHelper.GetUserId(new ClaimsPrincipal()));

    [Fact]
    public async Task CoachingSummary_ReturnsNoContentWhenNoUnreadOwnSummaryExists()
    {
        await using var harness = Harness.Create();
        var result = await harness.Call("/coaching/current-summary", "GET"); Assert.Equal(204, result.Status); Assert.Null(result.Json);
        harness.Db.CoachingSummaries.Add(Summary(1, 2)); await harness.Db.SaveChangesAsync();
        Assert.Equal(204, (await harness.Call("/coaching/current-summary", "GET")).Status);
    }

    [Fact]
    public async Task CoachingSummary_ReturnsLatestOwnUnreadRecordWithoutForeignOrDismissedContent()
    {
        await using var harness = Harness.Create();
        var old = Summary(1, 1); old.DeliveredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var latest = Summary(2, 1); latest.SummaryText = "Latest unit reflection";
        var dismissed = Summary(3, 1); dismissed.DismissedAt = DateTimeOffset.UtcNow;
        var optedOut = Summary(4, 1); optedOut.OptedOutAt = DateTimeOffset.UtcNow;
        harness.Db.CoachingSummaries.AddRange(old, latest, dismissed, optedOut, Summary(5, 2)); await harness.Db.SaveChangesAsync();
        var result = await harness.Call("/coaching/current-summary", "GET"); Assert.Equal(200, result.Status); Assert.NotNull(result.Json);
        Assert.Equal(2, result.Json.Value.GetProperty("id").GetInt64()); Assert.Equal("Latest unit reflection", result.Json.Value.GetProperty("summaryText").GetString()); Assert.Equal("2026-10-05", result.Json.Value.GetProperty("weekStart").GetString());
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(2, 404)]
    [InlineData(9, 404)]
    public async Task CoachingDismiss_ChangesOnlyTheRequestedOwnRecord(long id, int status)
    {
        await using var harness = Harness.Create(); harness.Db.CoachingSummaries.AddRange(Summary(1, 1), Summary(2, 2)); await harness.Db.SaveChangesAsync();
        var result = await harness.Call("/coaching/{id:long}/dismiss", "POST", values: new() { ["id"] = id.ToString() }); Assert.Equal(status, result.Status);
        Assert.Equal(id == 1, (await harness.Db.CoachingSummaries.SingleAsync(s => s.Id == 1)).DismissedAt != null);
        Assert.Null((await harness.Db.CoachingSummaries.SingleAsync(s => s.Id == 2)).DismissedAt);
    }

    [Theory]
    [InlineData(0, null, 0, "INVALID_STARS")]
    [InlineData(6, null, 0, "INVALID_STARS")]
    [InlineData(3, "invalid", 0, "INVALID_MEET_AGAIN")]
    [InlineData(3, "yes", 301, "TEXT_TOO_LONG")]
    public async Task Feedback_RejectsInvalidValuesBeforeCallingTheService(int stars, string? meetAgain, int length, string error)
    {
        await using var harness = Harness.Create();
        var result = await harness.Call("/matches/{matchId:guid}/feedback", "POST", new { metInPerson = true, stars, meetAgain, feltRightText = new string('x', length), feltOffText = "" }, new() { ["matchId"] = Guid.NewGuid().ToString() });
        Assert.Equal(400, result.Status); Assert.Equal(error, result.Json!.Value.GetProperty("error").GetString()); Assert.Empty(harness.Feedback.Submissions);
    }

    [Theory]
    [InlineData(1, "yes")]
    [InlineData(5, "NO")]
    [InlineData(null, "maybe")]
    [InlineData(null, null)]
    public async Task Feedback_ForwardsValidBoundaryValuesAndReportsSubmission(int? stars, string? meetAgain)
    {
        await using var harness = Harness.Create(); var id = Guid.NewGuid();
        var result = await harness.Call("/matches/{matchId:guid}/feedback", "POST", new { metInPerson = false, stars, meetAgain, feltRightText = new string('x', 300), feltOffText = (string?)null }, new() { ["matchId"] = id.ToString() });
        Assert.Equal(200, result.Status); Assert.True(result.Json!.Value.GetProperty("submitted").GetBoolean());
        var submitted = Assert.Single(harness.Feedback.Submissions); Assert.Equal(1, submitted.User); Assert.Equal(id, submitted.Match); Assert.Equal(stars, submitted.Dto.Stars); Assert.Equal(meetAgain, submitted.Dto.MeetAgain); Assert.Equal(300, submitted.Dto.FeltRightText!.Length);
    }

    [Fact]
    public async Task Feedback_ReportsMissingPromptAndUsesSubjectFallback()
    {
        await using var harness = Harness.Create(); harness.Feedback.Missing = true;
        var result = await harness.Call("/matches/{matchId:guid}/feedback", "POST", new { metInPerson = false }, new() { ["matchId"] = Guid.NewGuid().ToString() }, "sub");
        Assert.Equal(404, result.Status); Assert.Equal("NO_PROMPT_FOUND", result.Json!.Value.GetProperty("error").GetString()); Assert.Equal(1, Assert.Single(harness.Feedback.Submissions).User);
        var prompt = await harness.Call("/me/feedback-prompt", "GET"); Assert.False(prompt.Json!.Value.GetProperty("hasPendingPrompt").GetBoolean());
    }

    private static CoachingSummary Summary(long id, int user) => new() { Id = id, UserId = user, SummaryText = $"Unit reflection {id}", DeliveredAt = DateTimeOffset.UtcNow, WeekStartDate = new DateOnly(2026, 10, 5) };

    private sealed class Harness(WebApplication app, QaMemoryContext db, UnitFeedback feedback) : IAsyncDisposable
    {
        public QaMemoryContext Db => db;
        public UnitFeedback Feedback => feedback;
        public static Harness Create()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            var db = QaMemoryContext.Create(); var feedback = new UnitFeedback(); builder.Services.AddSingleton<WovenDbContext>(db); builder.Services.AddSingleton<IDateFeedbackService>(feedback);
            var app = builder.Build(); app.MapCoachingEndpoints(); app.MapFeedbackEndpoints(); return new Harness(app, db, feedback);
        }
        public async Task<(int Status, JsonElement? Json)> Call(string route, string method, object? payload = null, Dictionary<string, string>? values = null, string claim = "uid")
        {
            var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single(endpoint => endpoint.RoutePattern.RawText?.TrimEnd('/') == route);
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()); // Declaration only; middleware is not exercised here.
            var http = new DefaultHttpContext { RequestServices = app.Services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(claim, "1")], "Unit")) };
            http.Request.Method = method; http.Request.Path = route;
            if (values != null) foreach (var pair in values) http.Request.RouteValues[pair.Key] = pair.Value;
            if (payload != null)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload); http.Request.Body = new MemoryStream(bytes); http.Request.ContentType = "application/json"; http.Request.ContentLength = bytes.Length;
                http.Features.Set<IHttpRequestBodyDetectionFeature>(new UnitRequestBody());
            }
            await using var body = new MemoryStream(); http.Response.Body = body; await endpoint.RequestDelegate!(http); body.Position = 0;
            if (body.Length == 0) return (http.Response.StatusCode, null);
            using var json = await JsonDocument.ParseAsync(body); return (http.Response.StatusCode, json.RootElement.Clone());
        }
        public async ValueTask DisposeAsync() { await app.DisposeAsync(); await db.DisposeAsync(); }
    }
    private sealed class UnitRequestBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
    private sealed class UnitFeedback : IDateFeedbackService
    {
        public bool Missing { get; set; }
        public List<(int User, Guid Match, DateFeedbackDto Dto)> Submissions { get; } = [];
        public Task SubmitFeedbackAsync(int userId, Guid matchId, DateFeedbackDto dto, CancellationToken ct = default)
        {
            Submissions.Add((userId, matchId, dto)); if (Missing) throw new InvalidOperationException("Unit missing prompt"); return Task.CompletedTask;
        }
        public Task QueueFeedbackPromptsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendDuePromptsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReschedulePendingAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
