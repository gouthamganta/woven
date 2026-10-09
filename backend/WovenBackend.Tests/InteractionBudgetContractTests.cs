using Microsoft.EntityFrameworkCore;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services;
using WovenBackend.Services.Moments;

namespace WovenBackend.Tests;

public class InteractionBudgetContractTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task CachedTotalCap_RejectsWithoutWritingOrIncrementing(int count)
    {
        await using var db = QaMemoryContext.Create();
        var cache = new QaCache();
        cache.Values[CacheKeys.SparkCounter(1, MomentsRules.UtcToday())] = (long)count;
        var result = await new InteractionBudgetService(db, cache).TrySpendAsync(1, InteractionBudgetService.SpendType.Moment);
        Assert.False(result.Allowed);
        Assert.Equal("DAILY_TOTAL_CAP_REACHED", result.DenyReason);
        Assert.Equal(count, result.TotalUsed);
        Assert.Empty(await db.DailyInteractions.ToListAsync());
        Assert.Empty(cache.Incremented);
    }

    [Fact]
    public async Task CachedLegacyPendingCap_RejectsWithoutWriting()
    {
        await using var db = QaMemoryContext.Create();
        var cache = new QaCache();
        var today = MomentsRules.UtcToday();
        cache.Values[CacheKeys.SparkCounter(1, today)] = 1L;
        cache.Values[CacheKeys.PendingCounter(1, today)] = 2L;
        var result = await new InteractionBudgetService(db, cache).TrySpendAsync(1, InteractionBudgetService.SpendType.Pending);
        Assert.False(result.Allowed);
        Assert.Equal("DAILY_PENDING_CAP_REACHED", result.DenyReason);
        Assert.Empty(await db.DailyInteractions.ToListAsync());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(5, 4)]
    public async Task Refund_OnlyDecrementsPositiveCacheAndPersistedUsage(long cachedBefore, int expectedCached)
    {
        await using var db = QaMemoryContext.Create();
        var today = MomentsRules.UtcToday();
        db.DailyInteractions.Add(new DailyInteraction { UserId = 1, DateUtc = today, TotalUsed = 2, PendingUsed = 1 });
        db.DailyInteractions.Add(new DailyInteraction { UserId = 2, DateUtc = today, TotalUsed = 3 });
        await db.SaveChangesAsync();
        var cache = new QaCache();
        var key = CacheKeys.SparkCounter(1, today);
        cache.Values[key] = cachedBefore;
        await new InteractionBudgetService(db, cache).RefundSparkAsync(1);
        db.ChangeTracker.Clear();
        var mine = await db.DailyInteractions.SingleAsync(r => r.UserId == 1);
        Assert.Equal(1, mine.TotalUsed);
        Assert.Equal(1, mine.PendingUsed);
        Assert.Equal(3, (await db.DailyInteractions.SingleAsync(r => r.UserId == 2)).TotalUsed);
        Assert.Equal(cachedBefore > 0 ? expectedCached : cachedBefore, await cache.GetCounterAsync(key));
    }

    [Fact]
    public async Task Refund_DoesNotCreateUsageOrMakeZeroNegative()
    {
        await using var db = QaMemoryContext.Create();
        var service = new InteractionBudgetService(db, new QaCache());
        await service.RefundSparkAsync(1);
        Assert.Empty(await db.DailyInteractions.ToListAsync());
        db.DailyInteractions.Add(new DailyInteraction { UserId = 1, DateUtc = MomentsRules.UtcToday(), TotalUsed = 0 });
        await db.SaveChangesAsync();
        await service.RefundSparkAsync(1);
        Assert.Equal(0, (await db.DailyInteractions.SingleAsync()).TotalUsed);
    }
}
