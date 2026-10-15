using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Tests;

// Numerical unit tests only. EF InMemory cannot translate the latest-voice
// grouped projection; successful SQL scoring workflows remain a separate gate.
internal sealed class QaVectorContext(DbContextOptions<WovenDbContext> options) : WovenDbContext(options)
{
    public static QaVectorContext Create() => new(new DbContextOptionsBuilder<WovenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); builder.Ignore<Vector>();
        var converter = new ValueConverter<Vector, string>(v => Encode(v), s => Decode(s));
        foreach (var entity in builder.Model.GetEntityTypes().ToList())
            foreach (var property in entity.ClrType.GetProperties().Where(p => p.PropertyType == typeof(Vector)))
                builder.Entity(entity.ClrType).Property(property.Name).HasConversion(converter);
    }
    private static string Encode(Vector vector) => JsonSerializer.Serialize(vector.ToArray());
    private static Vector Decode(string value) => new(JsonSerializer.Deserialize<float[]>(value)!);
}

public class VectorScoringContractTests
{
    private static object? Calculate(string name, params object?[] args)
    {
        using var db = QaMemoryContext.Create(); var service = new MatchScoringService(db, NullLogger<MatchScoringService>.Instance);
        return typeof(MatchScoringService).GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, args);
    }
    private static Dictionary<string, JsonElement> Data(object value) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;
    [Fact]
    public async Task EmptyAndMissingViewerVectorCannotProduceCandidateScores()
    {
        await using var db = QaMemoryContext.Create(); var service = new MatchScoringService(db, NullLogger<MatchScoringService>.Instance);
        Assert.Empty(await service.ScoreCandidatesAsync(1, [])); Assert.Empty(await service.ScoreCandidatesAsync(1, [2]));
    }
    [Theory]
    [InlineData(1f, 0f, 100)]
    [InlineData(-1f, 0f, 0)]
    [InlineData(0f, 1f, 50)]
    [InlineData(0f, 0f, 50)]
    public void EmbeddingsDistinguishAlignedOpposedOrthogonalAndZeroVectors(float x, float y, double expected)
    {
        Assert.Equal(expected, (double)Calculate("CosineSimilarityToScore", new Vector(new[] { 1f, 0 }), new Vector(new[] { x, y }))!, 8);
        Assert.Equal(expected, (double)Calculate("VectorCosineSimilarityToScore", new[] { 1f, 0 }, new[] { x, y })!, 8);
        Assert.Equal((double)x, (double)Calculate("DotProduct", new Vector(new[] { 1f, 0 }), new Vector(new[] { x, y }))!, 8);
    }
    [Theory]
    [InlineData(0, false, 80)]
    [InlineData(1, false, 90)]
    [InlineData(2, false, 100)]
    [InlineData(3, false, 100)]
    [InlineData(1, true, 90)]
    [InlineData(2, true, 100)]
    [InlineData(3, true, 100)]
    public void IntentOverlapIsCaseInsensitiveAndBonusIsCapped(int shared, bool mixedCase, double expected)
    {
        var a = Data(new { intent = new { seriousness = .8, commitmentReadiness = .6, tags = Enumerable.Range(0, shared).Select(i => (mixedCase ? "TAG" : "tag") + i) } });
        var b = Data(new { intent = new { seriousness = .8, commitmentReadiness = .6, tags = Enumerable.Range(0, shared).Select(i => "tag" + i) } });
        Assert.Equal(expected, (double)Calculate("ComputeIntentScore", a, b)!, 8);
    }
    [Theory]
    [InlineData(0, 1.05)]
    [InlineData(.2, 1)]
    [InlineData(.5, .92)]
    [InlineData(.8, .82)]
    [InlineData(1, .7)]
    public void IntentDistanceDampensAlignment(double difference, double expected)
    {
        var a = Data(new { intent = new { seriousness = 0, commitmentReadiness = .5 } }); var b = Data(new { intent = new { seriousness = difference, commitmentReadiness = .5 } });
        Assert.Equal(80 - 50 * difference, (double)Calculate("ComputeIntentScore", a, b)!, 8); Assert.Equal(expected, (double)Calculate("ComputeIntentMultiplier", a, b)!, 8);
    }
    [Theory]
    [InlineData(.1, .1, .5, 75)]
    [InlineData(.1, .5, .5, 70)]
    [InlineData(.1, .9, .5, 85)]
    [InlineData(.1, .9, .7, 75)]
    public void PulseSupportsSimilarityComplementarityAndGhostRisk(double a, double b, double risk, double expected)
        => Assert.Equal(expected, (double)Calculate("ComputePulseScore", Data(new { pulse = new { socialCapacity = .5, initiative = a } }), Data(new { pulse = new { socialCapacity = .5, initiative = b, ghostRisk = risk } }))!, 8);
    [Theory]
    [InlineData("children", "Never", "Never", 70)]
    [InlineData("children", "Never", "Have", 20)]
    [InlineData("pref_children", "Have", "Want someday", 20)]
    [InlineData("smoking", "No", " no ", 65)]
    [InlineData("pref_smoking", "No", "Yes", 35)]
    [InlineData("diet", "Vegan", "Vegan", 60)]
    [InlineData("diet", "Vegan", "Omnivore", 40)]
    [InlineData("religion", "Same", "same", 60)]
    [InlineData("pref_drinking", "Rare", "Rare", 60)]
    [InlineData("pref_drinking", "Rare", "Often", 40)]
    [InlineData("pref_workout", "Daily", "Daily", 58)]
    [InlineData("pref_workout", "Daily", "Never", 42)]
    [InlineData("diet", " ", "Vegan", 50)]
    public void LifestyleFieldsUseMatchMismatchAndAliasRules(string key, string a, string b, double expected)
        => Assert.Equal(expected, (double)Calculate("ComputeLifestyleScore", new Dictionary<string, string> { { key, a } }, new Dictionary<string, string> { { key, b } })!, 8);
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("malformed")]
    [InlineData("{\"intent\":null,\"pulse\":null}")]
    [InlineData("{\"intent\":\"bad\",\"pulse\":\"bad\"}")]
    public void MissingCorruptOrWrongShapeDataDoesNotInventIntentOrPulse(string json)
    {
        var data = (Dictionary<string, JsonElement>)Calculate("ParseVector", json)!;
        Assert.Null(Calculate("ComputeIntentScore", data, data)); Assert.Null(Calculate("ComputePulseScore", data, data)); Assert.Equal(.7, (double)Calculate("ComputeIntentMultiplier", data, data)!);
        var pillars = (PillarScores)Calculate("ParsePillarScores", json)!; Assert.All(pillars.ToArray(), v => Assert.Equal(.5, v));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("malformed")]
    [InlineData("null")]
    public void MissingOrCorruptBehavioralArrayIsUnavailable(string? json) => Assert.Null(Calculate("ParseFloatArray", json));
    [Fact]
    public void BehavioralArraysPreserveNumbersAndHandleEmptyVectorsWithoutNaN()
    {
        Assert.Equal(new float[] { 1, 2 }, (float[])Calculate("ParseFloatArray", "[1,2]")!);
        Assert.Equal(50, (double)Calculate("VectorCosineSimilarityToScore", Array.Empty<float>(), new[] { 1f })!);
        Assert.Equal(50, (double)Calculate("VectorCosineSimilarityToScore", new[] { 1f }, new[] { 0f })!);
        var zero = new PillarScores { Lifestyle = 0, Energy = 0, Values = 0, Communication = 0, Ambition = 0, Stability = 0, Curiosity = 0, Affection = 0 }; Assert.Equal(0, zero.CosineSimilarity(new PillarScores()));
    }
}
