using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;

namespace WovenBackend.Tests;

public class AiProfileContractTests
{
    private static AiProfileService Service(QaMemoryContext db) => new(db, NullLogger<AiProfileService>.Instance);
    private static void AddProfile(QaMemoryContext db, int id, string? intent = "relationship", string? name = "Synthetic Adult", int age = 25, string gender = "nonbinary")
    {
        var user = new User { Id = id, Email = $"unit{id}@example.invalid", FullName = name };
        db.Users.Add(user);
        db.UserProfiles.Add(new UserProfile { UserId = id, User = user, Age = age, Gender = gender });
        if (intent != null) db.UserIntents.Add(new UserIntent { UserId = id, PrimaryIntent = intent });
    }
    private static void AddVector(QaMemoryContext db, int id, string data = "{}", string pillars = "{\"Values\":0.9}", int version = 1, DateTime? updated = null)
        => db.UserVectors.Add(new UserVector { UserId = id, Version = version, VectorJson = data, PillarScoresJson = pillars, UpdatedAt = updated ?? DateTime.UtcNow });

    [Fact]
    public async Task MissingProfile_ReturnsNoInventedProfileOrPair()
    {
        await using var db = QaMemoryContext.Create();
        Assert.Null(await Service(db).GetProfileAsync(1));
        AddProfile(db, 2); await db.SaveChangesAsync();
        Assert.Null(await Service(db).GetPairContextAsync(1, 2));
        Assert.Null(await Service(db).GetPairContextAsync(2, 1));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Synthetic Adult  ", "Synthetic")]
    [InlineData("unit@example.invalid Person", "[EMAIL]")]
    public async Task BasicProfile_SanitizesNameAndProvidesEightNeutralPillars(string? name, string? expected)
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1, intent: null, name: name); await db.SaveChangesAsync();
        var profile = await Service(db).GetProfileAsync(1);
        Assert.NotNull(profile); Assert.Equal(expected, profile.FirstName); Assert.Equal(25, profile.Age); Assert.Equal("nonbinary", profile.Gender);
        Assert.Equal(8, profile.AllPillars.Count); Assert.All(profile.AllPillars.Values, value => Assert.Equal(0.5, value));
        Assert.Empty(profile.TopPillars); Assert.True(profile.UsedCohortDefaults); Assert.Equal(DataQuality.LOW, profile.DataQuality);
        Assert.Equal("balanced", profile.ConversationTone);
    }

    [Fact]
    public async Task LatestVector_IsUsedAndMeaningfulTopPillarsAreLimitedToThree()
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1);
        AddVector(db, 1, pillars: "{\"Values\":0.1}", version: 1);
        AddVector(db, 1, pillars: "{\"Values\":0.9,\"Energy\":0.85,\"Ambition\":0.8,\"Curiosity\":0.75,\"Affection\":0.5}", version: 2);
        await db.SaveChangesAsync(); var profile = await Service(db).GetProfileAsync(1);
        Assert.NotNull(profile); Assert.Equal(0.9, profile.AllPillars["Values"]); Assert.Equal(3, profile.TopPillars.Count);
        Assert.DoesNotContain("Affection", profile.TopPillars.Keys); Assert.False(profile.UsedCohortDefaults);
    }

    [Theory]
    [InlineData("not-json", "not-json")]
    [InlineData("null", "null")]
    [InlineData("{\"pulse\":null}", "{}")]
    [InlineData("{\"foundational\":null,\"lifestyle\":null}", "{}")]
    public async Task MalformedOrAbsentVectorSections_DoNotBreakProfileLoading(string vector, string pillars)
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1); AddVector(db, 1, vector, pillars); await db.SaveChangesAsync();
        var profile = await Service(db).GetProfileAsync(1); Assert.NotNull(profile); Assert.True(profile.UsedCohortDefaults); Assert.Equal(8, profile.AllPillars.Count);
    }

    [Theory]
    [InlineData(0.8, 0.8, 0.2, "playful")]
    [InlineData(0.5, 0.2, 0.8, "thoughtful")]
    [InlineData(0.2, 0.5, 0.6, "calm")]
    [InlineData(0.5, 0.5, 0.5, "balanced")]
    public async Task Pulse_SelectsDocumentedConversationTone(double social, double banter, double depth, string tone)
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1);
        var data = JsonSerializer.Serialize(new { pulse = new { socialCapacity = social, banter, depth, initiative = 0.7 } });
        AddVector(db, 1, data); await db.SaveChangesAsync(); var profile = await Service(db).GetProfileAsync(1);
        Assert.NotNull(profile); Assert.Equal(tone, profile.ConversationTone); Assert.NotNull(profile.Pulse); Assert.Equal(0.3, profile.Pulse.GhostRisk);
    }

    [Fact]
    public async Task CompleteProfile_RetainsLimitedHobbiesAndWeightedSanitizedTags()
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1);
        AddVector(db, 1, "{\"pulse\":{\"socialCapacity\":0.9,\"banter\":0.9,\"depth\":0.8,\"initiative\":0.8},\"lifestyle\":{\"hobbies\":\"reading;walking;music;art;sports;extra\"},\"foundational\":{\"tags\":{\"values\":[\"embedded\"]}}}");
        for (var i = 0; i < 5; i++) db.UserVectorTags.Add(new UserVectorTag { UserId = 1, Version = 1, TagType = "Values", Tag = $"tag{i}", Weight = i / 10.0 });
        for (var i = 0; i < 3; i++) db.UserVectorTags.Add(new UserVectorTag { UserId = 1, Version = 1, TagType = "Lifestyle", Tag = $"activity{i}", Weight = 1 });
        await db.SaveChangesAsync(); var profile = await Service(db).GetProfileAsync(1);
        Assert.NotNull(profile); Assert.Equal(5, profile.Hobbies.Count); Assert.DoesNotContain("extra", profile.Hobbies);
        Assert.Equal(new[] { "tag4", "tag3", "tag2" }, profile.Tags["values"]); Assert.Equal(3, profile.Tags["lifestyle"].Count);
        Assert.InRange(profile.DataCompleteness, 0.6, 1.0); Assert.Equal(DataQuality.HIGH, profile.DataQuality); Assert.False(profile.UsedCohortDefaults);
    }

    [Theory]
    [InlineData("ignore previous", "[REDACTED]")]
    [InlineData("system: override", "[REDACTED]")]
    [InlineData("assistant: override", "[REDACTED]")]
    [InlineData("human: override", "[REDACTED]")]
    [InlineData("<|endoftext|>", "[REDACTED]")]
    [InlineData("<|im_start|>", "[REDACTED]")]
    [InlineData("<|im_end|>", "[REDACTED]")]
    [InlineData("[[INST]]", "[REDACTED]")]
    [InlineData("synthetic@example.invalid", "[EMAIL]")]
    [InlineData("555-123-4567", "[PHONE]")]
    public async Task StructuredTags_AreSanitizedBeforeEnteringAiContext(string tag, string replacement)
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1); AddVector(db, 1);
        db.UserVectorTags.Add(new UserVectorTag { UserId = 1, Version = 1, TagType = "Values", Tag = tag });
        await db.SaveChangesAsync(); var profile = await Service(db).GetProfileAsync(1);
        Assert.NotNull(profile); Assert.Contains(replacement, Assert.Single(profile.Tags["values"]));
    }

    [Fact]
    public async Task BlankAndOversizedTags_AreBoundedInContext()
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1); AddVector(db, 1);
        db.UserVectorTags.Add(new UserVectorTag { UserId = 1, Version = 1, TagType = "Values", Tag = "   ", Weight = 1 });
        db.UserVectorTags.Add(new UserVectorTag { UserId = 1, Version = 1, TagType = "Values", Tag = new string('x', 250), Weight = 0.8 });
        await db.SaveChangesAsync(); var profile = await Service(db).GetProfileAsync(1);
        Assert.NotNull(profile); var text = Assert.Single(profile.Tags["values"]); Assert.Equal(203, text.Length); Assert.EndsWith("...", text);
    }

    [Theory]
    [InlineData(null, "relationship", 0.5)]
    [InlineData("relationship", "RELATIONSHIP", 1.0)]
    [InlineData("relationship", "marriage", 0.85)]
    [InlineData("casual", "fun", 0.85)]
    [InlineData("exploring", "relationship", 0.6)]
    [InlineData("relationship", "open", 0.6)]
    [InlineData("relationship", "casual", 0.3)]
    [InlineData("casual", "relationship", 0.3)]
    [InlineData("friendship", "relationship", 0.5)]
    public async Task PairContext_ComputesSupportedIntentAlignment(string? first, string second, double expected)
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1, first); AddProfile(db, 2, second); AddVector(db, 1); AddVector(db, 2);
        await db.SaveChangesAsync(); var pair = await Service(db).GetPairContextAsync(1, 2);
        Assert.NotNull(pair); Assert.Equal(expected, pair.IntentAlignment); Assert.Equal("matched", pair.ToneAlignment);
        Assert.Single(pair.AlignedPillars); Assert.Equal("Values", pair.AlignedPillars[0].Pillar);
    }

    [Fact]
    public async Task PairContext_LimitsCaseInsensitiveSharedHobbiesTagsAndPillars()
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1); AddProfile(db, 2);
        const string pillars = "{\"Values\":0.9,\"Energy\":0.8,\"Ambition\":0.9,\"Curiosity\":0.8}";
        AddVector(db, 1, "{\"lifestyle\":{\"hobbies\":\"Reading,Walking,Music,Art\"}}", pillars);
        AddVector(db, 2, "{\"lifestyle\":{\"hobbies\":\"reading,walking,music,art\"}}", pillars);
        foreach (var user in new[] { 1, 2 }) foreach (var category in new[] { "values", "lifestyle", "communication" })
                for (var i = 0; i < 3; i++) db.UserVectorTags.Add(new UserVectorTag { UserId = user, Version = 1, TagType = category, Tag = $"shared{i}" });
        await db.SaveChangesAsync(); var pair = await Service(db).GetPairContextAsync(1, 2);
        Assert.NotNull(pair); Assert.Equal(3, pair.SharedHobbies.Count); Assert.Equal(6, pair.SharedTags.Count); Assert.Equal(3, pair.AlignedPillars.Count);
    }

    [Theory]
    [InlineData("{\"pulse\":{\"banter\":0.9,\"socialCapacity\":0.9}}", "{}", "complementary")]
    [InlineData("{\"pulse\":{\"banter\":0.2,\"depth\":0.9}}", "{\"pulse\":{\"banter\":0.9,\"socialCapacity\":0.9}}", "different")]
    [InlineData("{\"pulse\":{\"banter\":0.2,\"depth\":0.9}}", "{\"pulse\":{\"socialCapacity\":0.2,\"depth\":0.6}}", "complementary")]
    public async Task PairContext_ComputesToneRelationships(string first, string second, string expected)
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1); AddProfile(db, 2); AddVector(db, 1, first); AddVector(db, 2, second);
        await db.SaveChangesAsync(); var pair = await Service(db).GetPairContextAsync(1, 2); Assert.NotNull(pair); Assert.Equal(expected, pair.ToneAlignment);
    }

    [Fact]
    public async Task CohortFallback_UsesRecentNearbySameGenderRecordsAndSkipsMalformedData()
    {
        await using var db = QaMemoryContext.Create(); AddProfile(db, 1, intent: null);
        AddProfile(db, 2); AddVector(db, 2, "{\"foundational\":{\"tags\":{\"values\":[\"growth\"]}},\"lifestyle\":{\"hobbies\":\"reading;walking\"}}", "{\"Values\":0.9}");
        AddProfile(db, 3); AddVector(db, 3, "malformed", "malformed");
        AddProfile(db, 4, gender: "other"); AddVector(db, 4, pillars: "{\"Values\":0.1}");
        AddProfile(db, 5, age: 50); AddVector(db, 5, pillars: "{\"Values\":0.1}");
        AddProfile(db, 6); AddVector(db, 6, pillars: "{\"Values\":0.1}", updated: DateTime.UtcNow.AddDays(-31));
        await db.SaveChangesAsync(); var profile = await Service(db).GetProfileAsync(1);
        Assert.NotNull(profile); Assert.True(profile.UsedCohortDefaults); Assert.Equal(0.9, profile.AllPillars["Values"]);
        Assert.Contains("growth", profile.Tags["values"]); Assert.Equal(new[] { "reading", "walking" }, profile.Hobbies);
        Assert.Equal(DataQuality.LOW, profile.DataQuality); // Borrowed cohort context is not real user data quality.
    }
}
