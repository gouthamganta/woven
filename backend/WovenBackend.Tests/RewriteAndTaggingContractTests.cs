using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Services;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Tests;

public class RewriteAndTaggingContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"questions\":[]}")]
    public async Task FoundationalRewriteInvalidResponsesKeepStableBank(string? response)
    {
        await using var h = ChatHarness.Create(); var bank = FoundationalQuestionBank.GetQuestionsForVersion(1); h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult(response);
        var service = new OpenAiRewriteService((IOpenAiResilientClient)(object)h.Ai, NullLogger<OpenAiRewriteService>.Instance, (IAiProfileService)(object)h.Profiles);
        var result = await service.RewriteAsync(bank, new(null, null, null), "unit", default); Assert.Same(bank, result); Assert.Single(h.Ai.Calls);
    }
    [Theory]
    [InlineData("playful")]
    [InlineData("thoughtful")]
    [InlineData("calm")]
    [InlineData("balanced")]
    public async Task ValidRewordingPreservesIdsAndBankPillarsWithPersonalization(string tone)
    {
        await using var h = ChatHarness.Create(); var bank = FoundationalQuestionBank.GetQuestionsForVersion(1);
        h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult<string?>(JsonSerializer.Serialize(new { questions = bank.Select((q, i) => new { id = q.Id, text = i == 0 ? " " : " Unit wording ", pillars = new[] { "invalid" } }) }));
        h.Profiles.Results[nameof(IAiProfileService.GetProfileAsync)] = Task.FromResult<AiProfile?>(new AiProfile { UserId = 1, Age = 30, ConversationTone = tone, TopPillars = new() { { "Values", .9 } }, Tags = new() { { "values", ["growth"] } }, Hobbies = ["walking"] });
        var service = new OpenAiRewriteService((IOpenAiResilientClient)(object)h.Ai, NullLogger<OpenAiRewriteService>.Instance, (IAiProfileService)(object)h.Profiles);
        var result = await service.RewriteAsync(bank, new("Unit", "female", "relationship", 1), "unit", default); Assert.Equal(bank.Select(q => q.Id), result.Select(q => q.Id)); Assert.Equal(bank[0].Text, result[0].Text); Assert.Equal("Unit wording", result[1].Text);
        for (var i = 0; i < bank.Length; i++) Assert.Equal(bank[i].Pillars, result[i].Pillars); var call = Assert.Single(h.Ai.Calls); Assert.Contains(tone, (string)call.Args[1]!); Assert.Contains("walking", (string)call.Args[2]!); Assert.Contains("Unit", (string)call.Args[2]!);
    }
    [Fact]
    public async Task UnknownRewrittenIdsAndProviderFailureKeepCanonicalBank()
    {
        await using var h = ChatHarness.Create(); var bank = FoundationalQuestionBank.GetQuestionsForVersion(1); var service = new OpenAiRewriteService((IOpenAiResilientClient)(object)h.Ai, NullLogger<OpenAiRewriteService>.Instance, (IAiProfileService)(object)h.Profiles);
        h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult<string?>(JsonSerializer.Serialize(new { questions = bank.Select(q => new { id = "invalid", text = "Unit" }) })); Assert.Same(bank, await service.RewriteAsync(bank, new(null, null, null), "unit", default));
        h.Ai.Fail = true; Assert.Same(bank, await service.RewriteAsync(bank, new(null, null, null), "unit", default));
    }
    [Theory]
    [InlineData("relationship", .8, .7)]
    [InlineData("long-term", .8, .7)]
    [InlineData("casual", .3, .2)]
    [InlineData("fun", .3, .2)]
    [InlineData("exploring", .5, .4)]
    public async Task TaggingUnavailableUsesIntentSpecificDefaultsAndNeutralPillars(string intent, double serious, double commitment)
    {
        await using var h = ChatHarness.Create(); var service = new OpenAiTaggingService((IOpenAiResilientClient)(object)h.Ai, NullLogger<OpenAiTaggingService>.Instance);
        var metadata = await service.ExtractIntentMetadataAsync(intent, "Unit"); Assert.NotNull(metadata); Assert.Equal(serious, metadata!.Seriousness); Assert.Equal(commitment, metadata.CommitmentReadiness); Assert.Equal(2, metadata.Tags.Count);
        var pillars = await service.ComputePillarScoresAsync("[]", "[]"); Assert.All(pillars!.ToArray(), v => Assert.Equal(.5, v)); Assert.Empty((await service.ExtractTagsAsync("[]", "[]"))!);
    }
    [Theory]
    [InlineData("invalid")]
    [InlineData("null")]
    public async Task CorruptTaggingOutputDoesNotPersistInventedMetadata(string response)
    {
        await using var h = ChatHarness.Create(); h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult<string?>(response); var service = new OpenAiTaggingService((IOpenAiResilientClient)(object)h.Ai, NullLogger<OpenAiTaggingService>.Instance);
        Assert.Equal(.8, (await service.ExtractIntentMetadataAsync("relationship", "Unit"))!.Seriousness); Assert.All((await service.ComputePillarScoresAsync("[]", "[]"))!.ToArray(), v => Assert.Equal(.5, v)); Assert.Empty((await service.ExtractTagsAsync("[]", "[]"))!);
    }
    [Fact]
    public async Task TaggingValidResponsesPreserveStructuredValues()
    {
        await using var h = ChatHarness.Create(); var service = new OpenAiTaggingService((IOpenAiResilientClient)(object)h.Ai, NullLogger<OpenAiTaggingService>.Instance);
        h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult<string?>("{\"seriousness\":0.9,\"commitmentReadiness\":0.8,\"tags\":[\"unit-growth\"]}"); Assert.Equal("unit-growth", Assert.Single((await service.ExtractIntentMetadataAsync("relationship", "Unit"))!.Tags));
        h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult<string?>("{\"Values\":0.9}"); Assert.Equal(.9, (await service.ComputePillarScoresAsync("[]", "[]"))!.Values);
        h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult<string?>("{\"values\":[\"unit-growth\"]}"); Assert.Equal("unit-growth", Assert.Single((await service.ExtractTagsAsync("[]", "[]"))!["values"]));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"questions\":[]}")]
    public async Task DynamicRewriteInvalidOutputUsesBaseAndCycleRemainsStable(string? response)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); h.Ai.Results[nameof(IOpenAiResilientClient.ExecuteWithSystemAsync)] = Task.FromResult(response);
        var rewrite = new OpenAiDynamicIntakeRewriteService((IOpenAiResilientClient)(object)h.Ai, NullLogger<OpenAiDynamicIntakeRewriteService>.Instance, (IAiProfileService)(object)h.Profiles); var cycle = new DynamicIntakeCycleService(h.Db, rewrite);
        var first = await cycle.GetOrCreateCurrentAsync(1, default); var second = await cycle.GetOrCreateCurrentAsync(1, default); Assert.Equal(first.Id, second.Id); Assert.Equal("base", first.VariantSource); Assert.True(DynamicIntakeCycleService.IsValidVariant(first.VariantJson)); Assert.Equal(TimeSpan.FromHours(48), first.CycleEndUtc - first.CycleStartUtc); Assert.Single(await h.Db.UserDynamicIntakeSets.ToListAsync());
    }
}
