using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Tests;

public class ExplanationContractTests
{
    private static MatchExplanationService Service(ChatHarness h) => new(h.Db, (IOpenAiResilientClient)(object)h.Ai, NullLogger<MatchExplanationService>.Instance, (IAiProfileService)(object)h.Profiles);
    private static async Task Vectors(ChatHarness h, string vector = "{}", string pillars = "{}")
    {
        h.Db.UserVectors.AddRange(new UserVector { UserId = 1, VectorJson = vector, PillarScoresJson = pillars }, new UserVector { UserId = 2, VectorJson = "{}", PillarScoresJson = pillars }); await h.Db.SaveChangesAsync();
    }
    [Theory]
    [InlineData(MatchBucket.CORE_FIT)]
    [InlineData(MatchBucket.LIFESTYLE_FIT)]
    [InlineData(MatchBucket.CONVERSATION_FIT)]
    [InlineData(MatchBucket.EXPLORER)]
    [InlineData(MatchBucket.WILDCARD)]
    public async Task MissingVectorsSaveSpecificFallbackWithoutCallingProvider(MatchBucket bucket)
    {
        await using var h = ChatHarness.Create(); var date = new DateOnly(2026, 10, 9);
        var id = await Service(h).GenerateAndSaveExplanationAsync(1, 2, new MatchScore(2), bucket, date);
        var row = await h.Db.MatchExplanations.SingleAsync(); Assert.Equal(id, row.Id); Assert.Equal(1, row.UserId); Assert.Equal(2, row.CandidateId); Assert.Equal(date, row.DateUtc);
        Assert.NotEmpty(row.Headline); Assert.Equal("calm", row.Tone); Assert.Equal(3, row.GetDateIdeas().Count); Assert.NotEmpty(row.BulletsJson); Assert.Empty(h.Ai.Calls);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"headline\":\" \"}")]
    public async Task UnavailableOrMalformedProviderOutputSavesUsableFallback(string? response)
    {
        await using var h = ChatHarness.Create(); await Vectors(h);
        h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult(response);
        var id = await Service(h).GenerateAndSaveExplanationAsync(1, 2, new MatchScore(2), MatchBucket.CORE_FIT, new DateOnly(2026, 10, 9));
        var row = await h.Db.MatchExplanations.SingleAsync(); Assert.Equal(id, row.Id); Assert.Equal(3, row.GetDateIdeas().Count); Assert.False(string.IsNullOrWhiteSpace(row.Headline)); Assert.Single(h.Ai.Calls);
    }
    [Theory]
    [InlineData("{\"headline\":\"Unit shared values\",\"bullets\":[\"Unit detail\"],\"dateIdeas\":[\"Walk\",\" \",\"Museum\",\"Cafe\",\"Extra\"],\"bridgeQuestion\":\"  Unit question?  \"}", 3, "Unit question?")]
    [InlineData("{\"Headline\":\"Unit shared values\",\"Bullets\":null,\"DateIdeas\":null,\"BridgeQuestion\":\" \"}", 0, null)]
    public async Task ValidProviderOutputBoundsIdeasTrimsBridgeAndHandlesNullLists(string response, int ideas, string? bridge)
    {
        await using var h = ChatHarness.Create(); await Vectors(h); h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult<string?>(response);
        await Service(h).GenerateAndSaveExplanationAsync(1, 2, new MatchScore(2), MatchBucket.EXPLORER, new DateOnly(2026, 10, 9)); var row = await h.Db.MatchExplanations.SingleAsync();
        Assert.Equal("Unit shared values", row.Headline); Assert.Equal(ideas, row.GetDateIdeas().Count); Assert.Equal(bridge, row.BridgeQuestion);
        Assert.Equal(ideas == 0 ? null : "Walk", row.DateIdea); Assert.DoesNotContain("Extra", row.DateIdeasJson);
    }
    [Theory]
    [InlineData("{\"pulse\":{\"socialCapacity\":0.8}}", "playful")]
    [InlineData("{\"pulse\":{\"socialCapacity\":0.3}}", "serious")]
    [InlineData("{\"pulse\":{\"socialCapacity\":0.5}}", "calm")]
    [InlineData("{\"pulse\":null}", "calm")]
    [InlineData("invalid", "calm")]
    public async Task ToneUsesViewerPulseAndFallsBackForCorruptData(string vector, string expected)
    {
        await using var h = ChatHarness.Create(); await Vectors(h, vector, "malformed"); await Service(h).GenerateAndSaveExplanationAsync(1, 2, new MatchScore(2), MatchBucket.EXPLORER, new DateOnly(2026, 10, 9));
        Assert.Equal(expected, (await h.Db.MatchExplanations.SingleAsync()).Tone);
    }
    [Fact]
    public async Task RichContextAndRecentHistoryReachProviderWithoutRawIdentity()
    {
        await using var h = ChatHarness.Create(); await Vectors(h);
        var pair = new PairContext { UserProfile = new AiProfile { UserId = 1, FirstName = "Unit", ConversationTone = "playful" }, CandidateProfile = new AiProfile { UserId = 2 }, SharedTags = ["hiking", "books"], SharedHobbies = ["walking"], AlignedPillars = [new PillarAlignment { Pillar = "Values", UserScore = .8, CandidateScore = .8 }], IntentAlignment = .9, ToneAlignment = "matched" };
        h.Profiles.Results[nameof(IAiProfileService.GetPairContextAsync)] = Task.FromResult<PairContext?>(pair);
        for (var i = 2; i <= 4; i++) { h.Db.MatchSignalLogs.Add(new MatchSignalLog { ViewerId = 1, CandidateId = i, EventType = MatchSignalEventTypes.TimeToFirstMessageMs, EventValue = 10 }); h.Db.MatchExplanations.Add(new MatchExplanation { UserId = 1, CandidateId = i, Tone = "playful" }); }
        h.Db.MatchSignalLogs.AddRange(new MatchSignalLog { ViewerId = 1, CandidateId = 2, EventType = MatchSignalEventTypes.DateIdeaAccepted, MetadataJson = "{\"chosenIdea\":\"Unit trail\"}" }, new MatchSignalLog { ViewerId = 1, CandidateId = 2, EventType = MatchSignalEventTypes.DateIdeaAccepted, MetadataJson = "corrupt" }); await h.Db.SaveChangesAsync();
        var id = await Service(h).GenerateAndSaveExplanationAsync(1, 2, new MatchScore(2), MatchBucket.CORE_FIT, new DateOnly(2026, 10, 9)); var call = Assert.Single(h.Ai.Calls); var system = (string)call.Args[1]!;
        Assert.Contains("hiking", system); Assert.Contains("Values", system); Assert.Contains("walking", system); Assert.Contains("Unit trail", system); Assert.Contains("connects faster when tone is playful", system);
        Assert.DoesNotContain("Email", system); Assert.Equal("playful", (await h.Db.MatchExplanations.SingleAsync(e => e.Id == id)).Tone);
    }
}
