using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Tests;

public class MatchingSelectionContractTests
{
    [Fact]
    public async Task HardFilter_EmptyCandidatesOrMissingViewerDataReturnsEmpty()
    {
        await using var db = QaMemoryContext.Create(); var service = new HardFilterService(db, NullLogger<HardFilterService>.Instance);
        Assert.Empty(await service.ApplyAsync(1, [])); Assert.Empty(await service.ApplyAsync(1, [2]));
        db.UserProfiles.Add(new UserProfile { UserId = 1, Age = 30 }); await db.SaveChangesAsync();
        Assert.Empty(await service.ApplyAsync(1, [2]));
    }

    [Theory]
    [InlineData(24, 25, 35, false)]
    [InlineData(25, 25, 35, true)]
    [InlineData(35, 25, 35, true)]
    [InlineData(36, 25, 35, false)]
    public async Task HardFilter_ReciprocalAgeIncludesExactLimits(int age, int min, int max, bool expected)
    {
        await using var db = QaMemoryContext.Create();
        db.UserProfiles.AddRange(new UserProfile { UserId = 1, Age = age }, new UserProfile { UserId = 2, Age = 30 });
        db.UserPreferences.AddRange(new UserPreference { UserId = 1 }, new UserPreference { UserId = 2, AgeMin = min, AgeMax = max }); await db.SaveChangesAsync();
        var result = await new HardFilterService(db, NullLogger<HardFilterService>.Instance).ApplyAsync(1, [2]);
        Assert.Equal(expected ? new[] { 2 } : [], result);
    }

    [Theory]
    [InlineData(0d, 0d, 0d, 0d, 0, 0, true)]
    [InlineData(0d, 0d, 0d, 1d, 70, 70, true)]
    [InlineData(0d, 0d, 0d, 1d, 60, 70, false)]
    [InlineData(0d, 0d, 0d, 1d, 70, 60, false)]
    [InlineData(0d, null, 0d, 1d, 0, 0, true)]
    [InlineData(0d, 0d, null, 1d, 0, 0, true)]
    [InlineData(0d, 0d, 0d, null, 0, 0, true)]
    [InlineData(null, 0d, 0d, 1d, 0, 0, true)]
    [InlineData(0d, 179.9, 0d, -179.9, 15, 15, true)]
    public async Task HardFilter_RequiresBothDistanceWindowsOnlyWithCompleteCoordinates(double? lat1, double? lng1, double? lat2, double? lng2, int miles1, int miles2, bool expected)
    {
        await using var db = QaMemoryContext.Create();
        db.UserProfiles.AddRange(new UserProfile { UserId = 1, Age = 30, Lat = lat1, Lng = lng1 }, new UserProfile { UserId = 2, Age = 30, Lat = lat2, Lng = lng2 });
        db.UserPreferences.AddRange(new UserPreference { UserId = 1, DistanceMiles = miles1 }, new UserPreference { UserId = 2, DistanceMiles = miles2 }); await db.SaveChangesAsync();
        Assert.Equal(expected ? new[] { 2 } : [], await new HardFilterService(db, NullLogger<HardFilterService>.Instance).ApplyAsync(1, [2]));
    }

    [Fact]
    public async Task HardFilter_ExcludesUnrequestedAndIncompleteCandidates()
    {
        await using var db = QaMemoryContext.Create();
        foreach (var id in new[] { 1, 2, 3, 4 }) db.UserProfiles.Add(new UserProfile { UserId = id, Age = 30 });
        foreach (var id in new[] { 1, 2, 3, 5 }) db.UserPreferences.Add(new UserPreference { UserId = id });
        await db.SaveChangesAsync();
        Assert.Equal(new[] { 2 }, await new HardFilterService(db, NullLogger<HardFilterService>.Instance).ApplyAsync(1, [2, 4, 5]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(25)]
    public void Deck_ReturnsAtMostFiveUniqueEligibleCandidatesWithoutMutatingInput(int count)
    {
        var input = Enumerable.Range(1, count).Select(i => new MatchScore(i) { TotalScore = i }).ToList();
        var selected = new DeckSelectionService(NullLogger<DeckSelectionService>.Instance).SelectTop5(input);
        Assert.Equal(Math.Min(count, 5), selected.Count); Assert.Equal(selected.Count, selected.Select(s => s.CandidateId).Distinct().Count());
        Assert.All(selected, s => Assert.Contains(input, i => i.CandidateId == s.CandidateId));
        Assert.Equal(Enumerable.Range(1, count), input.Select(i => i.CandidateId));
    }

    [Fact]
    public void Deck_SelectsDiverseBucketsAndExplorerFromHighQualityPool()
    {
        var input = new List<MatchScore>
        {
            new(1) { IntentScore = 99, PillarScore = 99, TotalScore = 99 }, new(2) { IntentScore = 98, PillarScore = 98, TotalScore = 98 },
            new(3) { LifestyleScore = 99, TotalScore = 97 }, new(4) { PulseScore = 99, TotalScore = 96 },
            new(5) { PillarScore = 10, TotalScore = 95 }
        };
        input.AddRange(Enumerable.Range(6, 21).Select(i => new MatchScore(i) { PillarScore = i == 26 ? 0 : 40, TotalScore = 90 - i }));
        Assert.Equal(new[] { (1, MatchBucket.CORE_FIT), (2, MatchBucket.CORE_FIT), (3, MatchBucket.LIFESTYLE_FIT), (4, MatchBucket.CONVERSATION_FIT), (5, MatchBucket.EXPLORER) },
            new DeckSelectionService(NullLogger<DeckSelectionService>.Instance).SelectTop5(input));
    }

    [Fact]
    public void Deck_DeliveryBoostCanChangeCoreSelectionWithoutDuplicatingCandidate()
    {
        var scores = Enumerable.Range(1, 6).Select(i => new MatchScore(i) { IntentScore = 100 - i, PillarScore = 50, TotalScore = 80 - i }).ToList();
        var service = new DeckSelectionService(NullLogger<DeckSelectionService>.Instance);
        Assert.Equal(new[] { 1, 2 }, service.SelectTop5(scores).Where(s => s.Bucket == MatchBucket.CORE_FIT).Select(s => s.CandidateId));
        var boosted = service.SelectTop5(scores, new() { [6] = 100 });
        Assert.Equal(new[] { 6, 1 }, boosted.Where(s => s.Bucket == MatchBucket.CORE_FIT).Select(s => s.CandidateId));
        Assert.Equal(5, boosted.Select(s => s.CandidateId).Distinct().Count());
    }

    [Theory]
    [InlineData(39.999, "EXPLORER")]
    [InlineData(40, "OK")]
    [InlineData(59.999, "OK")]
    [InlineData(60, "GOOD")]
    [InlineData(79.999, "GOOD")]
    [InlineData(80, "STRONG")]
    [InlineData(100, "STRONG")]
    public void Score_RespectsExactBucketThresholds(double pillar, string bucket)
    {
        var score = new MatchScore(2) { PillarScore = pillar };
        score.ComputeTotal([true, false], [1, 99]);
        Assert.Equal(pillar, score.TotalScore); Assert.Equal(bucket, score.Bucket); Assert.Equal(0, score.DepthSignals);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 35)]
    [InlineData(1, 70)]
    [InlineData(2, 70)]
    public void Score_ClampsTrustAndRedistributesOnlyAvailableWeights(double trust, double expected)
    {
        var score = new MatchScore(2) { PillarScore = 80, IntentScore = 40, ExpressionScore = 0 };
        score.ComputeTotal([true, true, false], [3, 1, 100], trustScore: trust);
        Assert.Equal(expected, score.TotalScore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Score_NoPositiveAvailableWeightUsesNeutralFallback(bool available)
    {
        var score = new MatchScore(2) { PillarScore = 100 };
        score.ComputeTotal([available, false], [0, 1]); Assert.Equal(50, score.TotalScore); Assert.Equal("EXPLORER", score.Bucket);
    }

    [Theory]
    [InlineData(0, false, 50)]
    [InlineData(1, false, 52.5)]
    [InlineData(6, false, 65)]
    [InlineData(14, false, 65)]
    [InlineData(14, true, 70)]
    public void Score_DepthAndSeasonBonusesAreBounded(int extra, bool season, double expected)
    {
        var score = new MatchScore(2);
        var available = Enumerable.Range(0, 16).Select(i => i < 2 + extra).ToArray();
        score.ComputeTotal(available, Enumerable.Repeat(1d, 16).ToArray(), hasSeasonResponse: season);
        Assert.Equal(expected, score.TotalScore, 8); Assert.Equal(extra, score.DepthSignals);
    }

    [Fact]
    public void Score_LearnedWeightsOverrideBaseAndFinalMultiplierCannotExceedOneHundred()
    {
        var score = new MatchScore(2) { PillarScore = 100, IntentScore = 0 };
        score.ComputeTotal([true, true], [0, 1], learnedWeights: [1, 0]); Assert.Equal(100, score.TotalScore);
        score.ComputeTotal([true, true], [1, 0], intentMultiplier: 2); Assert.Equal(100, score.TotalScore);
        score.ComputeTotal([true, true], [1, 0], intentMultiplier: -1); Assert.Equal(0, score.TotalScore);
    }
}
