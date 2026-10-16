using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WovenBackend.Data;
using WovenBackend.Endpoints;
using WovenBackend.Migrations;
using WovenBackend.Services;
using WovenBackend.Services.Moments;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Tests;

// Registered handlers and real AfterthoughtService, in-memory EF storage.
// This does not certify PostgreSQL migrations, middleware, concurrency or browser UI.
public class AfterthoughtAcceptanceContractTests
{
    internal static AfterthoughtService Service(QaMemoryContext db) =>
        new(db, new ValidationOnlyIntegration(), NullLogger<AfterthoughtService>.Instance);

    // These cases isolate submission validation; EchoIntegrationContractTests
    // separately exercises the real integration and downstream services.
    private sealed class ValidationOnlyIntegration : IAfterthoughtIntegrationService
    {
        public Task ProcessAfterthoughtAsync(int id, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("trial_end", 1, false)]
    [InlineData("unmatch", 5, false)]
    [InlineData("expiry", null, false)]
    [InlineData("block", null, true)]
    public async Task ServicePersistsSupportedPathsWithPrivateActorAndTarget(string path, int? rating, bool safety)
    {
        await using var db = QaMemoryContext.Create();
        var id = Guid.NewGuid();
        await Service(db).RecordAsync(id, 1, 2, path, (short?)rating, " reason ", " synthetic note ", safety);
        var row = Assert.Single(await db.MatchAfterthoughts.ToListAsync());
        Assert.Equal(id, row.MatchId); Assert.Equal(1, row.FromUserId); Assert.Equal(2, row.AboutUserId);
        Assert.Equal(path, row.ClosePath); Assert.Equal((short?)rating, row.Rating); Assert.Equal(safety, row.IsSafety);
        Assert.Equal("reason", row.ReasonTag); Assert.Equal("synthetic note", row.NoteText);
    }

    [Theory]
    [InlineData("trial_end", 0, false)]
    [InlineData("unmatch", 6, false)]
    [InlineData("expiry", 3, false)]
    [InlineData("block", 3, true)]
    [InlineData("block", null, false)]
    [InlineData("unknown", null, false)]
    public async Task InvalidRatingPathOrBlockFlagDoesNotPersist(string path, int? rating, bool safety)
    {
        await using var db = QaMemoryContext.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).RecordAsync(Guid.NewGuid(), 1, 2, path, (short?)rating, "reason", null, safety));
        Assert.Empty(await db.MatchAfterthoughts.ToListAsync());
    }

    [Fact]
    public void MigrationUsesGuidMatchIdAndTheSameColumnForItsForeignKey()
    {
        var table = Assert.Single(new AddMatchAfterthoughts().UpOperations.OfType<CreateTableOperation>());
        var column = Assert.Single(table.Columns, c => c.Name == "match_id");
        Assert.Equal(typeof(Guid), column.ClrType);
        Assert.Contains(table.ForeignKeys, fk => fk.Columns.SequenceEqual(new[] { "match_id" }));
    }

    [Fact]
    public void SchemaPreventsConcurrentDuplicateSubmissionsForMatchAndActor()
    {
        using var db = QaMemoryContext.Create();
        var entity = db.Model.FindEntityType(typeof(MatchAfterthought))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name).ToHashSet().SetEquals(new[] { "MatchId", "FromUserId" }));
    }

    [Theory]
    [InlineData(51, 0)]
    [InlineData(0, 301)]
    public async Task ServiceRejectsOversizedTextBeforeStorage(int tagLength, int noteLength)
    {
        await using var db = QaMemoryContext.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).RecordAsync(Guid.NewGuid(), 1, 2, "unmatch", 3, new string('x', tagLength), new string('x', noteLength), false));
        Assert.Empty(await db.MatchAfterthoughts.ToListAsync());
    }

    [Theory]
    [InlineData(1, true, 200)]
    [InlineData(2, true, 200)]
    [InlineData(3, true, 403)]
    [InlineData(1, false, 400)]
    public async Task DeferredSubmissionRequiresParticipantAndClosedMatch(int actor, bool closed, int expected)
    {
        await using var h = AfterthoughtHarness.Create(); var match = await h.Seed(closed: closed);
        var result = await h.Call("/matches/{matchId:guid}/afterthought", match.Id, actor, new { rating = (int?)null, reasonTag = "busy", noteText = (string?)null });
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == 200 ? 1 : 0, await h.Db.MatchAfterthoughts.CountAsync());
    }

    [Fact]
    public async Task DeferredSequentialRetryDoesNotCreateDuplicate()
    {
        await using var h = AfterthoughtHarness.Create(); var match = await h.Seed();
        var payload = new { rating = (int?)null, reasonTag = "busy", noteText = (string?)null };
        Assert.Equal(200, (await h.Call("/matches/{matchId:guid}/afterthought", match.Id, 1, payload)).Status);
        Assert.Equal(400, (await h.Call("/matches/{matchId:guid}/afterthought", match.Id, 1, payload)).Status);
        Assert.Single(await h.Db.MatchAfterthoughts.ToListAsync());
    }

    [Fact]
    public async Task BlockedParticipantCannotSubmitSafetyAfterthought()
    {
        await using var h = AfterthoughtHarness.Create(); var match = await h.Seed(reason: ClosedReason.BLOCK);
        h.Db.Blocks.Add(new Block { BlockerId = 1, BlockedId = 2 }); await h.Db.SaveChangesAsync();
        var result = await h.Call("/matches/{matchId:guid}/afterthought", match.Id, 2, new { rating = (int?)null, reasonTag = "inappropriate", noteText = (string?)null });
        Assert.Equal(403, result.Status); Assert.Empty(await h.Db.MatchAfterthoughts.ToListAsync());
    }

    [Fact]
    public async Task RepeatedBlockDoesNotStoreTheSameActorsAfterthoughtTwice()
    {
        await using var h = AfterthoughtHarness.Create(); var match = await h.Seed(closed: false);
        var payload = new { afterthoughtReasonTag = "inappropriate", afterthoughtNote = (string?)null };
        Assert.Equal(200, (await h.Call("/matches/{matchId:guid}/block", match.Id, 1, payload)).Status);
        Assert.Equal(200, (await h.Call("/matches/{matchId:guid}/block", match.Id, 1, payload)).Status);
        Assert.Single(await h.Db.MatchAfterthoughts.ToListAsync());
    }

    [Theory]
    [InlineData("END")]
    [InlineData("CONTINUE")]
    public async Task TrialEndPreservesTheEndingPersonsAfterthoughtRegardlessOfResponseOrder(string secondDecision)
    {
        await using var h = AfterthoughtHarness.Create(); var match = await h.Seed(closed: false, trial: true);
        var first = await h.Call("/chats/{threadId:guid}/trial-decision", h.ThreadId, 1,
            new { decision = "END", endReason = "no_spark", afterthoughtRating = 2, afterthoughtReasonTag = "no_spark", afterthoughtNote = "Unit first response" });
        Assert.Equal(200, first.Status);
        var second = await h.Call("/chats/{threadId:guid}/trial-decision", h.ThreadId, 2,
            new { decision = secondDecision, endReason = "no_spark", afterthoughtRating = (int?)null, afterthoughtReasonTag = (string?)null, afterthoughtNote = (string?)null });
        Assert.Equal(200, second.Status);
        var own = await h.Db.MatchAfterthoughts.SingleOrDefaultAsync(a => a.FromUserId == 1);
        Assert.NotNull(own); Assert.Equal((short)2, own.Rating); Assert.Equal("trial_end", own.ClosePath);
    }

    [Fact]
    public async Task MissingOptionalUnmatchInputDoesNotConsumeDeferredFeedbackOpportunity()
    {
        await using var h = AfterthoughtHarness.Create(); var match = await h.Seed(closed: false);
        Assert.Equal(200, (await h.Call("/matches/{matchId:guid}/unmatch", match.Id, 1, new { })).Status);
        var result = await h.Call("/matches/{matchId:guid}/afterthought", match.Id, 1, new { rating = 4, reasonTag = "timing", noteText = (string?)null });
        Assert.Equal(200, result.Status);
        Assert.Equal((short)4, Assert.Single(await h.Db.MatchAfterthoughts.ToListAsync()).Rating);
    }

    [Fact]
    public async Task DeferredEndpointAllowsRecoveryOfPendingIntegration()
    {
        await using var h = AfterthoughtHarness.Create(realIntegration: true);
        var match = await h.Seed(reason: ClosedReason.UNMATCH);
        h.Db.MatchAfterthoughts.Add(new MatchAfterthought { MatchId = match.Id, FromUserId = 1, AboutUserId = 2, ClosePath = "unmatch", Rating = 3, ReasonTag = "timing", ProcessedAt = null });
        await h.Db.SaveChangesAsync();
        var result = await h.Call("/matches/{matchId:guid}/afterthought", match.Id, 1, new { rating = 3, reasonTag = "timing", noteText = (string?)null });
        Assert.Equal(200, result.Status);
        Assert.NotNull(Assert.Single(await h.Db.MatchAfterthoughts.ToListAsync()).ProcessedAt);
        Assert.Single(await h.Db.MatchSignalLogs.ToListAsync());
    }
}

internal sealed class AfterthoughtHarness(WebApplication app, QaMemoryContext db) : IAsyncDisposable
{
    public QaMemoryContext Db => db;
    public Guid ThreadId { get; private set; }
    public static AfterthoughtHarness Create(bool realIntegration = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        foreach (var type in typeof(WovenDbContext).Assembly.GetTypes().Where(t => t.IsInterface && t.Namespace?.StartsWith("WovenBackend.Services") == true))
            builder.Services.AddSingleton(type, _ => DispatchProxy.Create(type, typeof(AfterthoughtSideEffectStub)));
        var db = QaMemoryContext.Create();
        builder.Services.AddSingleton<WovenDbContext>(db);
        builder.Services.AddSingleton<IAfterthoughtService>(realIntegration
            ? new AfterthoughtService(db, EchoIntegrationContractTests.Integration(db), NullLogger<AfterthoughtService>.Instance)
            : AfterthoughtAcceptanceContractTests.Service(db));
        builder.Services.AddSingleton(new SparkWalletService(db));
        builder.Services.AddSingleton<IIdempotencyService>(new IdempotencyService(db, NullLogger<IdempotencyService>.Instance));
        builder.Services.AddAuthentication("QA").AddScheme<AuthenticationSchemeOptions, AfterthoughtUnitAuth>("QA", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build(); app.MapMatchesEndpoints(); app.MapChatEndpoints();
        return new(app, db);
    }
    public async Task<Match> Seed(bool closed = true, ClosedReason reason = ClosedReason.EXPIRE, bool trial = false)
    {
        var match = new Match { UserAId = 1, UserBId = 2, BalloonState = closed ? BalloonState.CLOSED : BalloonState.ACTIVE,
            ClosedReason = closed ? reason : null, BothMessagedAt = DateTimeOffset.UtcNow.AddMinutes(-10), IsTrial = trial,
            TrialStartedAt = trial ? DateTimeOffset.UtcNow.AddMinutes(-4) : null, TrialEndsAt = trial ? DateTimeOffset.UtcNow.AddMinutes(-1) : null };
        db.Users.AddRange(new User { Id = 1, Email = "one@unit.invalid", EmailHash = "unit1" }, new User { Id = 2, Email = "two@unit.invalid", EmailHash = "unit2" });
        db.Matches.Add(match); var thread = new ChatThread { MatchId = match.Id }; ThreadId = thread.Id; db.ChatThreads.Add(thread);
        await db.SaveChangesAsync(); return match;
    }
    public async Task<(int Status, JsonElement? Json)> Call(string route, Guid id, int actor, object payload)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText?.TrimEnd('/') == route);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var http = new DefaultHttpContext { RequestServices = app.Services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("uid", actor.ToString())], "Unit")) };
        http.Request.Method = "POST"; http.Request.Path = route;
        http.Request.RouteValues[route.StartsWith("/chats") ? "threadId" : "matchId"] = id.ToString();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload); http.Request.Body = new MemoryStream(bytes); http.Request.ContentType = "application/json"; http.Request.ContentLength = bytes.Length;
        http.Features.Set<IHttpRequestBodyDetectionFeature>(new AfterthoughtBodyDetection());
        await using var stream = new MemoryStream(); http.Response.Body = stream; await endpoint.RequestDelegate!(http); stream.Position = 0;
        if (stream.Length == 0) return (http.Response.StatusCode, null);
        using var doc = await JsonDocument.ParseAsync(stream); return (http.Response.StatusCode, doc.RootElement.Clone());
    }
    public async ValueTask DisposeAsync() { await app.DisposeAsync(); await db.DisposeAsync(); }
    private sealed class AfterthoughtBodyDetection : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
}

public class AfterthoughtSideEffectStub : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.ReturnType == typeof(Task)) return Task.CompletedTask;
        throw new NotSupportedException("No fake result configured for " + method.Name);
    }
}

public sealed class AfterthoughtUnitAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
