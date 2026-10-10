using Microsoft.EntityFrameworkCore;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Nudges;
using WovenBackend.Services.Venues;

namespace WovenBackend.Tests;

public partial class ChatLifecycleContractTests
{
    [Theory]
    [InlineData("", "GET")]
    [InlineData("/messages", "POST")]
    [InlineData("/voice-message", "POST")]
    [InlineData("/trial-decision", "POST")]
    [InlineData("/close-gracefully", "POST")]
    [InlineData("/date-interest", "POST")]
    [InlineData("/availability", "POST")]
    [InlineData("/venue-suggestions", "GET")]
    public async Task ThreadActions_MissingThreadAndMissingMatchReturnSpecific404(string suffix, string method)
    {
        await using var h = ChatHarness.Create();
        object payload = new { body = "Hello", audioUrl = "https://unit.invalid/audio", durationSecs = 5, decision = "CONTINUE", signalText = "Tomorrow" };
        var missing = await h.ThreadCall(suffix, payload, method: method); Assert.Equal(404, missing.Status); Assert.Equal("THREAD_NOT_FOUND", missing.Error);
        await h.Seed(); var thread = await h.Db.ChatThreads.SingleAsync(); thread.MatchId = Guid.NewGuid(); await h.Db.SaveChangesAsync();
        var orphan = await h.ThreadCall(suffix, payload, method: method); Assert.Equal(404, orphan.Status); Assert.Equal("MATCH_NOT_FOUND", orphan.Error);
        Assert.Empty(h.Notify.Calls); Assert.Empty(h.Signals.Calls);
    }

    [Theory]
    [InlineData("/trial-decision", "POST")]
    [InlineData("/close-gracefully", "POST")]
    [InlineData("/date-interest", "POST")]
    [InlineData("/availability", "POST")]
    [InlineData("/venue-suggestions", "GET")]
    [InlineData("/voice-message", "POST")]
    public async Task ThreadActions_ForeignUserCannotMutateOrQueryPair(string suffix, string method)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(trial: true); match.TrialEndsAt = DateTimeOffset.UtcNow.AddSeconds(-1); await h.Db.SaveChangesAsync();
        var result = await h.ThreadCall(suffix, new { decision = "BLOCK", signalText = "Tomorrow", audioUrl = "https://unit.invalid/audio", durationSecs = 5 }, actor: 3, method: method);
        Assert.Equal(403, result.Status); Assert.Null(result.Json); Assert.Equal(BalloonState.ACTIVE, match.BalloonState);
        Assert.Null(match.UserADecision); Assert.Null(match.UserBDecision); Assert.False(match.DateIdeaInterestedA); Assert.False(match.DateIdeaInterestedB);
        Assert.Empty(await h.Db.ChatMessages.ToListAsync()); Assert.Empty(await h.Db.ChatAvailabilitySignals.ToListAsync()); Assert.Empty(h.Notify.Calls); Assert.Empty(h.Signals.Calls);
    }

    [Fact]
    public async Task GracefulClose_PersistsTerminalStateNotifiesPairAndCannotCloseAgain()
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); match.BothMessagedAt = DateTimeOffset.UtcNow; await h.Db.SaveChangesAsync();
        var result = await h.ThreadCall("/close-gracefully"); Assert.Equal("CLOSED", result.StatusText);
        Assert.Equal(BalloonState.CLOSED, match.BalloonState); Assert.Equal(ClosedReason.UNMATCH, match.ClosedReason); var closed = match.ClosedAt; Assert.NotNull(closed);
        var notification = Assert.Single(h.Notify.Calls); Assert.Equal("MomentExpiredAsync", notification.Method); Assert.Equal(1, notification.Args[0]); Assert.Equal(2, notification.Args[1]); Assert.Equal(match.Id, notification.Args[2]);
        Assert.Equal("BALLOON_NOT_ACTIVE", (await h.ThreadCall("/close-gracefully", actor: 2)).Error); Assert.Equal(closed, match.ClosedAt); Assert.Single(h.Notify.Calls);
    }

    [Theory]
    [InlineData(1, 400, "CANNOT_LOVE_OWN_MESSAGE")]
    [InlineData(3, 403, null)]
    [InlineData(2, 200, null)]
    public async Task MessageLove_ParticipantCanReactToOtherSenderOnce(int actor, int status, string? error)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var message = new ChatMessage { ThreadId = h.ThreadId, SenderUserId = 1, Body = "Hello" }; h.Db.ChatMessages.Add(message); await h.Db.SaveChangesAsync();
        var values = new Dictionary<string, string> { ["threadId"] = h.ThreadId.ToString(), ["messageId"] = message.Id.ToString() };
        const string route = "/chats/{threadId:guid}/messages/{messageId:guid}/love";
        var result = await h.Call(route, actor: actor, values: values); Assert.Equal(status, result.Status);
        if (status != 200) { Assert.Equal(error, result.Error); Assert.Empty(await h.Db.MessageLoveReactions.ToListAsync()); return; }
        Assert.Equal("LOVED", result.StatusText); Assert.Equal("ALREADY_LOVED", (await h.Call(route, actor: actor, values: values)).StatusText);
        var reaction = await h.Db.MessageLoveReactions.SingleAsync(); Assert.Equal(actor, reaction.FromUserId); Assert.Equal(message.Id, reaction.MessageId); Assert.Equal(h.ThreadId, reaction.ThreadId); Assert.Single(h.Signals.Calls);
    }

    [Theory]
    [InlineData(1, "VOICE", false, 400, "CANNOT_LISTEN_OWN_NOTE")]
    [InlineData(3, "VOICE", false, 403, null)]
    [InlineData(2, "TEXT", false, 404, "MESSAGE_NOT_FOUND")]
    [InlineData(2, "VOICE", false, 200, null)]
    [InlineData(2, "VOICE", true, 200, null)]
    public async Task VoiceListen_ValidatesRecipientAndRecordsMutualVoiceOnlyWhenBothSent(int actor, string type, bool mutual, int status, string? error)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var message = new ChatMessage { ThreadId = h.ThreadId, SenderUserId = 1, MessageType = type, Body = "Unit" }; h.Db.ChatMessages.Add(message);
        if (mutual) h.Db.ChatMessages.Add(new ChatMessage { ThreadId = h.ThreadId, SenderUserId = 2, MessageType = "VOICE", Body = "Reply" }); await h.Db.SaveChangesAsync();
        var result = await h.Call("/chats/{threadId:guid}/messages/{messageId:guid}/voice-listened", actor: actor,
            values: new() { ["threadId"] = h.ThreadId.ToString(), ["messageId"] = message.Id.ToString() }); Assert.Equal(status, result.Status);
        if (status != 200) { Assert.Equal(error, result.Error); Assert.Empty(h.Signals.Calls); return; }
        Assert.Equal(mutual ? 2 : 1, h.Signals.Calls.Count); Assert.Equal(MatchSignalEventTypes.VoiceNoteListenComplete, h.Signals.Calls[0].Args[2]);
        if (mutual) Assert.Equal(MatchSignalEventTypes.MutualVoiceExchange, h.Signals.Calls[1].Args[2]);
        Assert.All(h.Signals.Calls, c => { Assert.Equal(2, c.Args[0]); Assert.Equal(1, c.Args[1]); });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DateInterest_NotifiesOtherOnceThenMutualNotifiesBoth(int firstActor)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed();
        var first = await h.ThreadCall("/date-interest", new { ideaText = "Unit picnic", ideaIndex = 2 }, actor: firstActor);
        Assert.False(first.Json!.Value.GetProperty("mutualInterest").GetBoolean()); Assert.Null(match.DateIdeaInterestedAt);
        Assert.Equal(firstActor == 1, match.DateIdeaInterestedA); Assert.Equal(firstActor == 2, match.DateIdeaInterestedB);
        await h.ThreadCall("/date-interest", new { ideaText = "Unit picnic", ideaIndex = 2 }, actor: firstActor); Assert.Single(h.Notify.Calls);
        var other = firstActor == 1 ? 2 : 1;
        Assert.Equal(other, h.Notify.Calls[0].Args[0]);
        var final = await h.ThreadCall("/date-interest", new { ideaText = "Unit picnic", ideaIndex = 2 }, actor: other);
        Assert.True(final.Json!.Value.GetProperty("mutualInterest").GetBoolean()); Assert.NotNull(match.DateIdeaInterestedAt); Assert.Equal(3, h.Notify.Calls.Count);
        using var meta = System.Text.Json.JsonDocument.Parse((string)h.Signals.Calls[0].Args[4]!); Assert.Equal("Unit picnic", meta.RootElement.GetProperty("chosenIdea").GetString()); Assert.Equal(2, meta.RootElement.GetProperty("ideaIndex").GetInt32());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Venues_RequiresBothParticipantsInterestBeforeCallingProvider(bool a, bool b)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); match.DateIdeaInterestedA = a; match.DateIdeaInterestedB = b; await h.Db.SaveChangesAsync();
        h.Venues.Results[nameof(IVenueService.GetVenueSuggestionsAsync)] = Task.FromResult(new List<VenueSuggestion> { new("Unit Cafe", "Unit Street", 4, 1, "https://unit.invalid/map") });
        var result = await h.ThreadCall("/venue-suggestions", method: "GET", actor: 2);
        Assert.Equal(a && b ? 200 : 403, result.Status);
        if (a && b) { var call = Assert.Single(h.Venues.Calls); Assert.Equal(2, call.Args[0]); Assert.Equal(1, call.Args[1]); Assert.Single(result.Json!.Value.GetProperty("venues").EnumerateArray()); }
        else { Assert.Equal("MUTUAL_INTEREST_REQUIRED", result.Error); Assert.Empty(h.Venues.Calls); Assert.Empty(h.Analytics.Calls); }
    }

    [Theory]
    [InlineData(0, 400, "SIGNAL_TEXT_REQUIRED")]
    [InlineData(200, 204, null)]
    [InlineData(201, 400, "SIGNAL_TEXT_TOO_LONG")]
    public async Task Availability_EnforcesTextBoundaryPersistsActorAndNotifiesOnlyPartner(int length, int status, string? error)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var text = new string('x', length);
        var response = await h.ThreadCall("/availability", new { signalText = text }, actor: 2); Assert.Equal(status, response.Status);
        if (status == 204) { var saved = await h.Db.ChatAvailabilitySignals.SingleAsync(); Assert.Equal(2, saved.UserId); Assert.Equal(h.ThreadId, saved.ThreadId); Assert.Equal(text, saved.SignalText); Assert.Equal(1, Assert.Single(h.Notify.Calls).Args[0]); }
        else { Assert.Equal(error, response.Error); Assert.Empty(await h.Db.ChatAvailabilitySignals.ToListAsync()); Assert.Empty(h.Notify.Calls); }
    }

    [Fact]
    public async Task Nudge_UsesActorAndThreadAndDismissalCacheIsActorSpecific()
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        h.Nudges.Results[nameof(INudgeService.GetConversationNudgeAsync)] = Task.FromResult<NudgeDto?>(new("unit", "Try saying hello", "say_hello"));
        var result = await h.ThreadCall("/nudge", actor: 2, method: "GET"); Assert.Equal(200, result.Status); Assert.Equal("Try saying hello", result.Json!.Value.GetProperty("nudge").GetProperty("text").GetString());
        var call = Assert.Single(h.Nudges.Calls); Assert.Equal(2, call.Args[0]); Assert.Equal(h.ThreadId, call.Args[1]);
        Assert.Equal(204, (await h.ThreadCall("/nudge/dismiss", actor: 2)).Status);
        Assert.Equal("1", h.Cache.Values[$"nudge:dismissed:{h.ThreadId}:2"]); Assert.False(h.Cache.Values.ContainsKey($"nudge:dismissed:{h.ThreadId}:1"));
    }
}
