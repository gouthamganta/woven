using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using MatchType = WovenBackend.data.Entities.Moments.MatchType;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Endpoints;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Moments;
using WovenBackend.Services.Nudges;
using WovenBackend.Services.Venues;
using WovenBackend.Services.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace WovenBackend.Tests;

// Registered handlers, real application logic, isolated EF memory store.
// Not JWT middleware, relational transactions, or production-host acceptance.
public partial class ChatLifecycleContractTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Start_IsParticipantOnlyAndReusesExistingThread(int actor)
    {
        await using var h = ChatHarness.Create();
        var match = await h.Seed(thread: false);
        var first = await h.Call("/chats/start", payload: new { matchId = match.Id }, actor: actor);
        Assert.Equal(200, first.Status);
        var second = await h.Call("/chats/start", payload: new { matchId = match.Id }, actor: actor);
        Assert.Equal(first.Json!.Value.GetProperty("threadId").GetGuid(), second.Json!.Value.GetProperty("threadId").GetGuid());
        var thread = Assert.Single(await h.Db.ChatThreads.ToListAsync());
        Assert.Equal(match.Id, thread.MatchId);
        Assert.Single(h.Analytics.Calls);
    }

    [Theory]
    [InlineData(3, false, 403, null)]
    [InlineData(1, true, 400, "BALLOON_NOT_ACTIVE")]
    public async Task Start_DeniesForeignOrClosedMatchWithoutCreatingThread(int actor, bool closed, int status, string? error)
    {
        await using var h = ChatHarness.Create();
        var match = await h.Seed(thread: false);
        if (closed) { match.BalloonState = BalloonState.CLOSED; await h.Db.SaveChangesAsync(); }
        var response = await h.Call("/chats/start", payload: new { matchId = match.Id }, actor: actor);
        Assert.Equal(status, response.Status);
        if (error != null) Assert.Equal(error, response.Error);
        Assert.Empty(await h.Db.ChatThreads.ToListAsync()); Assert.Empty(h.Analytics.Calls);
    }

    [Fact]
    public async Task Start_MissingMatchReturns404()
    {
        await using var h = ChatHarness.Create();
        Assert.Equal("MATCH_NOT_FOUND", (await h.Call("/chats/start", payload: new { matchId = Guid.NewGuid() })).Error);
        Assert.Empty(await h.Db.ChatThreads.ToListAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Read_ShowsBothOpeningNotesToEitherParticipantAndExcludesUnrelatedNotes(int actor)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed();
        h.Db.ChatNotes.AddRange(new ChatNote { MatchId = match.Id, FromUserId = 1, ToUserId = 2, NoteText = "First opening", Choice = MomentChoice.MAGICAL },
            new ChatNote { MatchId = match.Id, FromUserId = 2, ToUserId = 1, NoteText = "Second opening", Choice = MomentChoice.LOGICAL },
            new ChatNote { MatchId = Guid.NewGuid(), FromUserId = 3, ToUserId = 4, NoteText = "Unrelated private note" });
        await h.Db.SaveChangesAsync();
        var response = await h.ThreadCall("", actor: actor, method: "GET");
        Assert.Equal(200, response.Status);
        var notes = response.Json!.Value.GetProperty("chatNotes").EnumerateArray().ToArray();
        Assert.Equal(2, notes.Length);
        Assert.Equal(new[] { 1, 2 }, notes.Select(n => n.GetProperty("fromUserId").GetInt32()).Order());
        Assert.DoesNotContain(notes, n => n.GetProperty("noteText").GetString() == "Unrelated private note");
    }

    [Fact]
    public async Task Read_ForeignUserCannotSeeNotesOrStartTrialTimer()
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: true);
        var response = await h.ThreadCall("", actor: 3, method: "GET");
        Assert.Equal(403, response.Status); Assert.Null(response.Json);
        Assert.Null(match.TrialUserAOpenedAt); Assert.Null(match.TrialUserBOpenedAt); Assert.Null(match.TrialEndsAt);
    }

    [Fact]
    public async Task Trial_TimerStartsOnlyAfterSecondParticipantOpensAndDoesNotRestart()
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: true);
        Assert.Equal(200, (await h.ThreadCall("", actor: 1, method: "GET")).Status);
        Assert.NotNull(match.TrialUserAOpenedAt); Assert.Null(match.TrialEndsAt);
        var before = DateTimeOffset.UtcNow;
        await h.ThreadCall("", actor: 2, method: "GET");
        Assert.NotNull(match.TrialUserBOpenedAt);
        Assert.InRange(match.TrialEndsAt!.Value, before.AddMinutes(3), DateTimeOffset.UtcNow.AddMinutes(3));
        var ends = match.TrialEndsAt;
        await h.ThreadCall("", actor: 1, method: "GET"); await h.ThreadCall("", actor: 2, method: "GET");
        Assert.Equal(ends, match.TrialEndsAt);
    }

    [Theory]
    [InlineData("", "EMPTY_MESSAGE")]
    [InlineData("   ", "EMPTY_MESSAGE")]
    [InlineData(null, "EMPTY_MESSAGE")]
    public async Task Send_RejectsEmptyBodyWithoutPersisting(string? body, string error)
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        Assert.Equal(error, (await h.ThreadCall("/messages", new { body })).Error);
        Assert.Empty(await h.Db.ChatMessages.ToListAsync()); Assert.Empty(h.Notify.Calls);
    }

    [Theory]
    [InlineData(1000, 200)]
    [InlineData(1001, 400)]
    public async Task Send_EnforcesTrimmedLengthBoundary(int length, int status)
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        var response = await h.ThreadCall("/messages", new { body = "  " + new string('x', length) + "  " });
        Assert.Equal(status, response.Status);
        if (status == 200) Assert.Equal(new string('x', length), (await h.Db.ChatMessages.SingleAsync()).Body);
        else { Assert.Equal("MESSAGE_TOO_LONG", response.Error); Assert.Empty(await h.Db.ChatMessages.ToListAsync()); }
    }

    [Theory]
    [InlineData("/messages")]
    [InlineData("/voice-message")]
    public async Task Send_RateLimitReturnsRetryAndDoesNotWrite(string route)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); h.Cache.RateLimitAllowed = false;
        var response = await h.ThreadCall(route, new { body = "Hello", audioUrl = "https://unit.invalid/audio", durationSecs = 10 });
        Assert.Equal(429, response.Status); Assert.Equal("RATE_LIMIT_EXCEEDED", response.Error);
        Assert.Equal(60, response.Json!.Value.GetProperty("retryAfter").GetInt32());
        Assert.Empty(await h.Db.ChatMessages.ToListAsync()); Assert.Empty(h.Notify.Calls);
    }

    [Theory]
    [InlineData(3, false, 403)]
    [InlineData(1, true, 400)]
    public async Task Send_DeniesForeignOrClosedThreadWithoutSideEffects(int actor, bool closed, int status)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed();
        if (closed) { match.BalloonState = BalloonState.CLOSED; await h.Db.SaveChangesAsync(); }
        Assert.Equal(status, (await h.ThreadCall("/messages", new { body = "Hello" }, actor)).Status);
        Assert.Empty(await h.Db.ChatMessages.ToListAsync()); Assert.Empty(h.Signals.Calls); Assert.Empty(h.Notify.Calls);
    }

    [Fact]
    public async Task Send_MutualMessagesUnlockReflectionOnceAndNotifyOnlyOtherParticipant()
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed();
        await h.ThreadCall("/messages", new { body = " First " }, actor: 1);
        Assert.Null(match.BothMessagedAt); Assert.Null(match.FindLoveAt);
        await h.ThreadCall("/messages", new { body = "Reply" }, actor: 2);
        Assert.NotNull(match.BothMessagedAt);
        Assert.Equal(TimeSpan.FromMinutes(5), match.FindLoveAt - match.BothMessagedAt);
        var unlock = match.FindLoveAt;
        await h.ThreadCall("/messages", new { body = "Another" }, actor: 1);
        Assert.Equal(unlock, match.FindLoveAt);
        var thread = await h.Db.ChatThreads.SingleAsync(); Assert.Equal(3, thread.MessageCount); Assert.NotNull(thread.AvgResponseTimeMs);
        Assert.Equal(new[] { 2, 1, 2 }, h.Notify.Calls.Select(c => (int)c.Args[0]!));
        Assert.Equal(2, h.Signals.Calls.Count(c => (string)c.Args[2]! == MatchSignalEventTypes.TimeToFirstMessageMs));
        Assert.Equal(3, await h.Db.ChatMessages.CountAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Send_SignalFailureDoesNotLoseSavedMessage(int actor)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); h.Signals.Fail = true;
        Assert.Equal(200, (await h.ThreadCall("/messages", new { body = "Persist me" }, actor)).Status);
        Assert.Equal(actor, (await h.Db.ChatMessages.SingleAsync()).SenderUserId); Assert.Single(h.Notify.Calls);
    }

    [Theory]
    [InlineData(false, null, "CONTINUE", "NOT_IN_TRIAL")]
    [InlineData(true, null, "CONTINUE", "TRIAL_NOT_ENDED")]
    [InlineData(true, 5, "CONTINUE", "TRIAL_NOT_ENDED")]
    [InlineData(true, -5, "unknown", "INVALID_DECISION")]
    public async Task Decision_RejectsInvalidStateOrInputWithoutRecording(bool trial, int? minutes, string decision, string error)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: trial);
        match.TrialEndsAt = minutes.HasValue ? DateTimeOffset.UtcNow.AddMinutes(minutes.Value) : null; await h.Db.SaveChangesAsync();
        Assert.Equal(error, (await h.ThreadCall("/trial-decision", new { decision })).Error);
        Assert.Null(match.UserADecision); Assert.Empty(h.Signals.Calls); Assert.Empty(await h.Db.Blocks.ToListAsync());
    }

    [Fact]
    public async Task Decision_BothContinueUnlocksImmediatelyAfterWaitingForOther()
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: true); match.TrialEndsAt = DateTimeOffset.UtcNow.AddSeconds(-1); await h.Db.SaveChangesAsync();
        var waiting = await h.ThreadCall("/trial-decision", new { decision = " continue " });
        Assert.Equal("DECISION_RECORDED", waiting.StatusText); Assert.True(match.IsTrial); Assert.Null(match.FindLoveAt);
        var result = await h.ThreadCall("/trial-decision", new { decision = "CONTINUE" }, actor: 2);
        Assert.Equal("MATCH_CONTINUES", result.StatusText); Assert.False(match.IsTrial); Assert.NotNull(match.FindLoveAt);
        Assert.Equal(BalloonState.ACTIVE, match.BalloonState); Assert.Empty(await h.Db.Blocks.ToListAsync());
    }

    [Theory]
    [InlineData(1, -100)]
    [InlineData(2, 100)]
    [InlineData(1, 101)]
    [InlineData(2, -101)]
    [InlineData(1, null)]
    public async Task Decision_BlockClosesImmediatelyAndOnlyStoresValidPrivateRating(int actor, int? rating)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: true); match.TrialEndsAt = DateTimeOffset.UtcNow.AddSeconds(-1); await h.Db.SaveChangesAsync();
        var response = await h.ThreadCall("/trial-decision", new { decision = "BLOCK", endReason = " No_Spark ", rating }, actor);
        Assert.Equal("MATCH_BLOCKED", response.StatusText); Assert.False(match.IsTrial);
        Assert.Equal(BalloonState.CLOSED, match.BalloonState); Assert.Equal(ClosedReason.BLOCK, match.ClosedReason); Assert.NotNull(match.ClosedAt);
        var block = await h.Db.Blocks.SingleAsync(); Assert.Equal(actor, block.BlockerId); Assert.Equal(actor == 1 ? 2 : 1, block.BlockedId);
        Assert.Equal("no_spark", match.TrialEndReason);
        var ratings = await h.Db.UserRatings.ToListAsync();
        if (rating is >= -100 and <= 100) { var row = Assert.Single(ratings); Assert.Equal(rating, row.RatingValue); Assert.Equal(actor, row.RaterUserId); }
        else Assert.Empty(ratings);
        Assert.False(response.Json!.Value.TryGetProperty("rating", out _));
        Assert.Equal("NOT_IN_TRIAL", (await h.ThreadCall("/trial-decision", new { decision = "BLOCK" }, actor)).Error);
        Assert.Single(await h.Db.Blocks.ToListAsync());
    }

    [Theory]
    [InlineData("no_spark", "TrialEndedNoSpark")]
    [InlineData("wrong_timing", "TrialEndedWrongTiming")]
    [InlineData("not_my_type", "TrialEndedNotMyType")]
    public async Task Decision_EndAfterOtherContinueClosesWithoutCreatingBlock(string reason, string signal)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: true);
        match.TrialEndsAt = DateTimeOffset.UtcNow.AddSeconds(-1); match.UserBDecision = "CONTINUE"; match.BothMessagedAt = DateTimeOffset.UtcNow; await h.Db.SaveChangesAsync();
        Assert.Equal("MATCH_ENDED", (await h.ThreadCall("/trial-decision", new { decision = "END", endReason = reason, rating = 0 })).StatusText);
        Assert.Equal(ClosedReason.UNMATCH, match.ClosedReason); Assert.False(match.IsTrial); Assert.Empty(await h.Db.Blocks.ToListAsync());
        Assert.Single(await h.Db.UserRatings.ToListAsync()); Assert.Contains(h.Signals.Calls, c => (string)c.Args[2]! == signal);
    }

    [Fact]
    public async Task Decision_IdempotencyReplayDoesNotRepeatSignalsOrRating()
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: true); match.TrialEndsAt = DateTimeOffset.UtcNow.AddSeconds(-1); await h.Db.SaveChangesAsync();
        var first = await h.ThreadCall("/trial-decision", new { decision = "BLOCK", rating = 5 }, key: "unit-block");
        var count = h.Signals.Calls.Count;
        var repeat = await h.ThreadCall("/trial-decision", new { decision = "BLOCK", rating = 5 }, key: "unit-block");
        Assert.Equal(first.Json!.Value.GetRawText(), repeat.Json!.Value.GetRawText()); Assert.Equal(count, h.Signals.Calls.Count);
        Assert.Single(await h.Db.Blocks.ToListAsync()); Assert.Single(await h.Db.UserRatings.ToListAsync()); Assert.Single(await h.Db.IdempotencyRecords.ToListAsync());
    }

    [Theory]
    [InlineData(0, "https://unit.invalid/audio", 400, "INVALID_DURATION")]
    [InlineData(181, "https://unit.invalid/audio", 400, "INVALID_DURATION")]
    [InlineData(1, "", 400, "AUDIO_URL_REQUIRED")]
    [InlineData(1, "https://unit.invalid/audio", 200, null)]
    [InlineData(180, "https://unit.invalid/audio", 200, null)]
    public async Task Voice_ValidatesDurationAndPersistsTypedMetadata(int durationSecs, string audioUrl, int status, string? error)
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        var response = await h.ThreadCall("/voice-message", new { durationSecs, audioUrl }); Assert.Equal(status, response.Status);
        if (status != 200) { Assert.Equal(error, response.Error); Assert.Empty(await h.Db.ChatMessages.ToListAsync()); return; }
        var message = await h.Db.ChatMessages.SingleAsync(); Assert.Equal("VOICE", message.MessageType); Assert.Equal(1, message.SenderUserId);
        using var meta = JsonDocument.Parse(message.MetaJson); Assert.Equal(audioUrl, meta.RootElement.GetProperty("audioUrl").GetString()); Assert.Equal(durationSecs, meta.RootElement.GetProperty("durationSecs").GetInt32());
        Assert.Equal(2, Assert.Single(h.Notify.Calls).Args[0]);
    }

    [Theory]
    [InlineData(1, true, true, 400, "CANNOT_LOVE_OWN_NOTE")]
    [InlineData(2, false, true, 400, "NOTE_NOT_LINKED_TO_MATCH")]
    [InlineData(2, true, false, 400, "MATCH_NOT_ACTIVE")]
    [InlineData(3, true, true, 403, null)]
    [InlineData(2, true, true, 200, null)]
    public async Task NoteLove_EnforcesOwnershipLinkAndActiveMatchThenDeduplicates(int actor, bool linked, bool active, int status, string? error)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed();
        match.BalloonState = active ? BalloonState.ACTIVE : BalloonState.CLOSED;
        var note = new ChatNote { MatchId = linked ? match.Id : null, FromUserId = 1, ToUserId = 2, NoteText = "Opening" }; h.Db.ChatNotes.Add(note); await h.Db.SaveChangesAsync();
        var values = new Dictionary<string, string> { ["noteId"] = note.Id.ToString() };
        var result = await h.Call("/chats/chatnotes/{noteId:guid}/love", actor: actor, values: values); Assert.Equal(status, result.Status);
        if (status != 200) { Assert.Equal(error, result.Error); Assert.Empty(await h.Db.ChatNoteLoveReactions.ToListAsync()); Assert.Empty(h.Signals.Calls); return; }
        Assert.Equal("LOVED", result.StatusText); var reaction = await h.Db.ChatNoteLoveReactions.SingleAsync(); Assert.Equal(actor, reaction.FromUserId); Assert.Equal(1, reaction.NoteAuthorUserId);
        Assert.Equal("ALREADY_LOVED", (await h.Call("/chats/chatnotes/{noteId:guid}/love", actor: actor, values: values)).StatusText);
        Assert.Single(await h.Db.ChatNoteLoveReactions.ToListAsync()); Assert.Single(h.Signals.Calls);
    }

    [Fact]
    public async Task List_ContainsOnlyOwnActiveThreadsAndLatestMessage()
    {
        await using var h = ChatHarness.Create(); var own = await h.Seed();
        h.Db.Matches.AddRange(new Match { UserAId = 3, UserBId = 4, BalloonState = BalloonState.ACTIVE }, new Match { UserAId = 1, UserBId = 3, BalloonState = BalloonState.CLOSED });
        await h.Db.SaveChangesAsync();
        foreach (var m in await h.Db.Matches.Where(m => m.Id != own.Id).ToListAsync()) h.Db.ChatThreads.Add(new ChatThread { MatchId = m.Id });
        h.Db.ChatMessages.AddRange(new ChatMessage { ThreadId = h.ThreadId, SenderUserId = 1, Body = "Old", CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) }, new ChatMessage { ThreadId = h.ThreadId, SenderUserId = 2, Body = "Latest" }); await h.Db.SaveChangesAsync();
        var response = await h.Call("/chats", method: "GET"); Assert.Equal(200, response.Status);
        Assert.Equal(1, response.Json!.Value.GetProperty("count").GetInt32()); var item = Assert.Single(response.Json.Value.GetProperty("chats").EnumerateArray());
        Assert.Equal(own.Id, item.GetProperty("matchId").GetGuid()); Assert.Equal("Latest", item.GetProperty("lastMessage").GetProperty("body").GetString());
    }
}

internal sealed record HandlerResponse(int Status, JsonElement? Json)
{
    public string? Error => Json is { } j && j.TryGetProperty("error", out var value) ? value.GetString() : null;
    public string? StatusText => Json is { } j && j.TryGetProperty("status", out var value) ? value.GetString() : null;
}

internal sealed class ChatHarness(WebApplication app, QaMemoryContext db, QaCache cache,
    RecordedSideEffects notify, RecordedSideEffects signals, RecordedSideEffects analytics) : IAsyncDisposable
{
    public QaMemoryContext Db => db;
    public QaCache Cache => cache;
    public RecordedSideEffects Notify => notify;
    public RecordedSideEffects Signals => signals;
    public RecordedSideEffects Analytics => analytics;
    public RecordedSideEffects Audit { get; private set; } = null!;
    public RecordedSideEffects Venues { get; private set; } = null!;
    public RecordedSideEffects Nudges { get; private set; } = null!;
    public Guid ThreadId { get; private set; }
    public const string SigningKey = "LocalSyntheticChatAuthKey012345678901234567890123456789";
    public static ChatHarness Create(bool jwt = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        var db = QaMemoryContext.Create(); var cache = new QaCache { RateLimitAllowed = true };
        builder.Services.AddSingleton<WovenDbContext>(db); builder.Services.AddSingleton<ICacheService>(cache);
        builder.Services.AddSingleton(new SparkWalletService(db));
        builder.Services.AddSingleton<IIdempotencyService>(new IdempotencyService(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<IdempotencyService>.Instance));
        var notify = RecordedSideEffects.Create<INotificationService>(builder.Services);
        var signals = RecordedSideEffects.Create<IMatchSignalService>(builder.Services);
        var analytics = RecordedSideEffects.Create<IAnalyticsService>(builder.Services);
        var nudges = RecordedSideEffects.Create<INudgeService>(builder.Services); var venues = RecordedSideEffects.Create<IVenueService>(builder.Services);
        RecordedSideEffects.Create<IMediaService>(builder.Services);
        var audit = RecordedSideEffects.Create<ISecurityAuditService>(builder.Services);
        if (jwt)
        {
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "WovenBackend",
                    ValidAudience = "WovenFrontend",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });
        }
        else builder.Services.AddAuthentication("Unit").AddScheme<AuthenticationSchemeOptions, UnitForbiddenHandler>("Unit", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build(); app.MapChatEndpoints(); app.MapUserDataEndpoints();
        return new ChatHarness(app, db, cache, notify, signals, analytics) { Audit = audit, Venues = venues, Nudges = nudges };
    }
    public async Task<Match> Seed(bool trial = false, bool thread = true)
    {
        var now = DateTimeOffset.UtcNow;
        var match = new Match
        {
            UserAId = 1,
            UserBId = 2,
            MatchType = MatchType.PURE,
            BalloonState = BalloonState.ACTIVE,
            CreatedAt = now,
            ExpiresAt = MomentsRules.ComputeExpiresAt(now),
            IsTrial = trial,
            TrialStartedAt = trial ? now.AddMinutes(-4) : null
        };
        db.Matches.Add(match);
        db.Users.AddRange(new User { Id = 1, Email = "one@unit.invalid", FullName = "Unit One" }, new User { Id = 2, Email = "two@unit.invalid", FullName = "Unit Two" });
        if (thread) { var chat = new ChatThread { MatchId = match.Id }; ThreadId = chat.Id; db.ChatThreads.Add(chat); }
        await db.SaveChangesAsync(); return match;
    }
    public Task<HandlerResponse> ThreadCall(string suffix, object? payload = null, int actor = 1, string method = "POST", string? key = null)
        => Call("/chats/{threadId:guid}" + suffix, payload, actor, method, new() { ["threadId"] = ThreadId.ToString() }, key);
    public async Task<HttpClient> StartHttp()
    {
        app.UseAuthentication(); app.UseAuthorization();
        app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
        return new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
    }
    public async Task<HandlerResponse> Call(string route, object? payload = null, int actor = 1, string method = "POST", Dictionary<string, string>? values = null, string? key = null)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText?.TrimEnd('/') == route && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var http = new DefaultHttpContext { RequestServices = app.Services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("uid", actor.ToString())], "Unit")) };
        http.Request.Method = method; http.Request.Path = route;
        if (values != null) foreach (var pair in values) http.Request.RouteValues[pair.Key] = pair.Value;
        if (key != null) http.Request.Headers["X-Idempotency-Key"] = key;
        if (payload != null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload); http.Request.Body = new MemoryStream(bytes); http.Request.ContentType = "application/json"; http.Request.ContentLength = bytes.Length;
            http.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());
        }
        await using var body = new MemoryStream(); http.Response.Body = body; await endpoint.RequestDelegate!(http); body.Position = 0;
        if (body.Length == 0) return new(http.Response.StatusCode, null);
        using var document = await JsonDocument.ParseAsync(body); return new(http.Response.StatusCode, document.RootElement.Clone());
    }
    public async ValueTask DisposeAsync() { await app.DisposeAsync(); await db.DisposeAsync(); }
    private sealed class BodyDetection : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
}

// Only Task-returning side effects are accepted; any unexpected dependency
// result request fails so a missing fake cannot silently make a test pass.
public class RecordedSideEffects : DispatchProxy
{
    public List<(string Method, object?[] Args)> Calls { get; } = [];
    public bool Fail { get; set; }
    public Dictionary<string, object> Results { get; } = [];
    public static RecordedSideEffects Create<T>(IServiceCollection services) where T : class
    {
        var proxy = Create<T, RecordedSideEffects>(); services.AddSingleton(proxy); return (RecordedSideEffects)(object)proxy;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Calls.Add((method!.Name, args!));
        if (Fail) throw new InvalidOperationException("Injected unit dependency failure");
        if (Results.TryGetValue(method.Name, out var result)) return result;
        if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
        if (method.ReturnType == typeof(void)) return null;
        throw new NotSupportedException("Configure an explicit result for " + method.Name);
    }
}

public sealed class UnitForbiddenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
