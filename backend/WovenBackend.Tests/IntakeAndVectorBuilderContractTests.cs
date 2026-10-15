using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Tests;

public class IntakeAndVectorBuilderContractTests
{
    public static IEnumerable<object[]> PulseCases()
    {
        foreach (var b in new[] { "high", "medium", "low", "unknown" }) foreach (var t in new[] { "playful", "serious", "calm", "unknown" }) foreach (var r in new[] { "driver", "copilot", "passenger", "unknown" }) yield return [b, t, r];
    }
    [Theory]
    [MemberData(nameof(PulseCases))]
    public void PulseMappingHasStableFeatureVersionAndExpectedDirectionalSignals(string battery, string tone, string role)
    {
        var (json, version) = DynamicIntakeCycleService.ComputeFeaturesJson(battery, tone, role); using var d = JsonDocument.Parse(json); var v = d.RootElement;
        Assert.Equal(1, version); Assert.Equal(battery == "high" ? .9 : battery == "low" ? .3 : .6, v.GetProperty("socialCapacity").GetDouble());
        Assert.Equal(role == "driver" ? .9 : role == "passenger" ? .2 : .6, v.GetProperty("initiative").GetDouble()); Assert.Equal(role == "passenger", v.GetProperty("needsInitiationFromOther").GetBoolean());
        Assert.Equal(tone == "playful" ? .9 : tone == "serious" ? .3 : .4, v.GetProperty("banter").GetDouble()); Assert.Equal(tone == "serious" ? .9 : tone == "playful" ? .3 : .55, v.GetProperty("depth").GetDouble());
        Assert.All(v.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number), p => Assert.InRange(p.Value.GetDouble(), 0, 1));
    }
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    public void CorruptVariantsAreRejected(string? json) => Assert.False(DynamicIntakeCycleService.IsValidVariant(json!));
    [Fact]
    public void CanonicalDynamicVariantIsValidAndWrongIdsKeysAndBlankLabelsAreRejected()
    {
        var bank = DynamicQuestionBank.GetBaseThree(); var rows = bank.Select(q => new { id = q.Id, text = q.Text, options = q.Options.Select(o => new { key = o.Key, label = o.Label, subLabel = o.SubLabel }) }).ToArray();
        Assert.True(DynamicIntakeCycleService.IsValidVariant(JsonSerializer.Serialize(rows))); Assert.False(DynamicIntakeCycleService.IsValidVariant(JsonSerializer.Serialize(rows.Take(2))));
        Assert.False(DynamicIntakeCycleService.IsValidVariant(JsonSerializer.Serialize(rows).Replace(bank[0].Id, "foreign"))); Assert.False(DynamicIntakeCycleService.IsValidVariant(JsonSerializer.Serialize(rows).Replace(bank[0].Options[0].Key, "foreign-key")));
    }
    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 45)]
    [InlineData(3, 60)]
    [InlineData(8, 60)]
    public async Task FoundationalRecurrenceUsesVersionIntervalAndDoesNotAdvanceEarly(int version, int days)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var answered = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc); Assert.Equal(answered.AddDays(days), h.Foundational.ComputeNextEligibleAt(version, answered));
        h.Db.UserFoundationalQuestionSets.Add(new UserFoundationalQuestionSet { UserId = 1, Version = version, AnsweredAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) }); await h.Db.SaveChangesAsync();
        Assert.False((await h.Foundational.GetDueStateAsync(1, default)).due); Assert.Single(await h.Db.UserFoundationalQuestionSets.ToListAsync());
    }
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task FoundationalUnansweredQuestionsSelfHealAndDueStatePreservesHardBlock(int version, bool hard)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); h.Db.UserFoundationalQuestionSets.Add(new UserFoundationalQuestionSet { UserId = 1, Version = version, QuestionsJson = "[]" }); await h.Db.SaveChangesAsync();
        var state = await h.Foundational.GetDueStateAsync(1, default); Assert.True(state.due); Assert.Equal(version, state.version); Assert.Equal(hard, state.hardBlock);
        using var json = JsonDocument.Parse((await h.Db.UserFoundationalQuestionSets.SingleAsync()).QuestionsJson); Assert.Equal(FoundationalQuestionBank.GetQuestionsForVersion(version).Length, json.RootElement.GetArrayLength()); Assert.Empty(h.Ai.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VectorBuilderPersistsPillarsTagsAndOnlyAllowedLifestyleFieldsOnce(bool useProviderValues)
    {
        await using var db = QaVectorContext.Create(); var services = new ServiceCollection(); var tags = RecordedSideEffects.Create<IOpenAiTaggingService>(services); var cache = new QaCache();
        tags.Results[nameof(IOpenAiTaggingService.ExtractIntentMetadataAsync)] = Task.FromResult<IntentMetadata?>(useProviderValues ? new IntentMetadata { Seriousness = .8, Tags = ["growth"] } : null);
        tags.Results[nameof(IOpenAiTaggingService.ComputePillarScoresAsync)] = Task.FromResult<PillarScores?>(useProviderValues ? new PillarScores { Values = .9 } : null);
        tags.Results[nameof(IOpenAiTaggingService.ExtractTagsAsync)] = Task.FromResult<Dictionary<string, List<string>>?>(useProviderValues ? new() { { "values", ["curiosity"] } } : null);
        db.UserIntents.Add(new UserIntent { UserId = 1, PrimaryIntent = "relationship", ReflectionSentence = "Unit reflection" });
        db.UserFoundationalQuestionSets.Add(new UserFoundationalQuestionSet { UserId = 1, Version = 1, AnsweredAt = DateTime.UtcNow, QuestionsJson = "[]", AnswersJson = "[]" });
        db.UserOptionalFields.AddRange(new UserOptionalField { UserId = 1, Key = "hobbies", Value = "Unit hiking", Visibility = VisibilityLevel.Public }, new UserOptionalField { UserId = 1, Key = "pref_smoking", Value = "No", Visibility = VisibilityLevel.MatchingOnly }); await db.SaveChangesAsync();
        var builder = new UserVectorBuilder(db, (IOpenAiTaggingService)(object)tags, cache, NullLogger<UserVectorBuilder>.Instance); var id = await builder.BuildAndSaveV1Async(1); Assert.Equal(id, await builder.BuildAndSaveV1Async(1));
        db.ChangeTracker.Clear(); var vector = await db.UserVectors.SingleAsync(); Assert.Equal(id, vector.Id); Assert.Equal(1, vector.Version); Assert.NotNull(vector.PillarEmbedding); Assert.Equal(8, vector.PillarEmbedding!.ToArray().Length);
        using var json = JsonDocument.Parse(vector.VectorJson); Assert.Equal("Unit hiking", json.RootElement.GetProperty("lifestyle").GetProperty("hobbies").GetString()); Assert.Equal("No", json.RootElement.GetProperty("lifestyle").GetProperty("pref_smoking").GetString());
        Assert.Equal(useProviderValues ? 2 : 0, await db.UserVectorTags.CountAsync()); Assert.Single(cache.Deleted);
        await builder.UpdatePulseAsync(1, new() { { "d1_battery", "low" }, { "d2_tone", "serious" }, { "d3_role", "passenger" } }); var updated = await db.UserVectors.SingleAsync(); using var pulse = JsonDocument.Parse(updated.VectorJson); Assert.Equal(.3, pulse.RootElement.GetProperty("pulse").GetProperty("socialCapacity").GetDouble());
        await builder.UpdatePulseAsync(99, new()); Assert.Single(await db.UserVectors.ToListAsync());
    }
}
