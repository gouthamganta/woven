using Microsoft.EntityFrameworkCore;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Moments;

namespace WovenBackend.Tests;

public class SparkWalletContractTests
{
    [Fact]
    public async Task NewWallet_ReceivesFiveSparksOnceAndPersistsDailyStamp()
    {
        await using var db = QaMemoryContext.Create();
        var service = new SparkWalletService(db);
        Assert.Equal(5m, await service.GetBalanceAsync(1));
        Assert.Equal(5m, await service.GetBalanceAsync(1));
        db.ChangeTracker.Clear();
        var wallet = await db.SparkWallets.SingleAsync();
        Assert.Equal(50, wallet.BalanceTenths);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), wallet.LastEarnedDate);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(20, 70)]
    [InlineData(75, 100)]
    [InlineData(100, 100)]
    public async Task DailyRefill_AddsFiveSparksAndCapsAtTen(int before, int after)
    {
        await using var db = QaMemoryContext.Create();
        db.SparkWallets.Add(new SparkWallet { UserId = 1, BalanceTenths = before, LastEarnedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3) });
        await db.SaveChangesAsync();
        var service = new SparkWalletService(db);
        Assert.Equal(after / 10m, await service.GetBalanceAsync(1));
        Assert.Equal(after / 10m, await service.GetBalanceAsync(1));
        db.ChangeTracker.Clear();
        Assert.Equal(after, (await db.SparkWallets.SingleAsync()).BalanceTenths);
    }

    [Fact]
    public async Task LegacyUnstampedWallet_EarnsExactlyOnce()
    {
        await using var db = QaMemoryContext.Create();
        db.SparkWallets.Add(new SparkWallet { UserId = 1, BalanceTenths = 10, LastEarnedDate = null });
        await db.SaveChangesAsync();
        var service = new SparkWalletService(db);
        Assert.Equal(6m, await service.GetBalanceAsync(1));
        Assert.Equal(6m, await service.GetBalanceAsync(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public async Task SameDayOrFutureStamp_DoesNotEarnAgain(int daysAhead)
    {
        await using var db = QaMemoryContext.Create();
        db.SparkWallets.Add(new SparkWallet { UserId = 1, BalanceTenths = 15, LastEarnedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead) });
        await db.SaveChangesAsync();
        Assert.Equal(1.5m, await new SparkWalletService(db).GetBalanceAsync(1));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(95, 100)]
    [InlineData(98, 100)]
    [InlineData(100, 100)]
    public async Task GhostRefund_AddsHalfSparkWithoutExceedingCap(int before, int after)
    {
        await using var db = QaMemoryContext.Create();
        db.SparkWallets.Add(new SparkWallet { UserId = 1, BalanceTenths = before, LastEarnedDate = DateOnly.FromDateTime(DateTime.UtcNow) });
        await db.SaveChangesAsync();
        await new SparkWalletService(db).GhostRefundAsync(1);
        db.ChangeTracker.Clear();
        Assert.Equal(after, (await db.SparkWallets.SingleAsync()).BalanceTenths);
    }

    [Fact]
    public async Task GhostRefund_CreatesOnlyTheRequestedUsersWallet()
    {
        await using var db = QaMemoryContext.Create();
        await new SparkWalletService(db).GhostRefundAsync(2);
        var wallet = await db.SparkWallets.SingleAsync();
        Assert.Equal(2, wallet.UserId);
        Assert.Equal(55, wallet.BalanceTenths);
    }
}
