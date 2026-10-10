using WovenBackend.Services.Moments;

namespace WovenBackend.Tests;

public class MatchRuleContractTests
{
    [Theory]
    [InlineData("2026-12-31T23:59:59Z")]
    [InlineData("2028-02-28T23:59:59Z")]
    [InlineData("2026-03-08T01:30:00-05:00")]
    [InlineData("2026-10-08T23:59:59+05:30")]
    [Trait("Contract", "Approved72Hours")]
    public void NewMatch_ExpiresExactly72ElapsedHoursAcrossCalendarBoundaries(string timestamp)
    {
        var created = DateTimeOffset.Parse(timestamp);
        var expiry = MomentsRules.ComputeExpiresAt(created);
        Assert.Equal(TimeSpan.FromHours(72), expiry - created);
        Assert.Equal(created.Offset, expiry.Offset);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(123, 7)]
    [InlineData(int.MaxValue, 1)]
    public void PairIdentity_IsOrderIndependentAndPreservesBothUsers(int first, int second)
    {
        var a = MomentsRules.NormalizePair(first, second);
        var b = MomentsRules.NormalizePair(second, first);
        Assert.Equal(a, b); Assert.True(a.A < a.B);
        Assert.Equal(new[] { first, second }.Order(), new[] { a.A, a.B });
    }
}
