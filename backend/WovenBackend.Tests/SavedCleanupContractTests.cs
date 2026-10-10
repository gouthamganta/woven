using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Tests;

public class SavedCleanupContractTests
{
    private static WovenDbContext Database() => new VectorlessTestContext(new DbContextOptionsBuilder<WovenDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task HistoricalSavedRecord_DoesNotBoostCandidate(int ageDays)
    {
        await using var db = Database();
        db.PendingMatches.Add(new PendingMatch { UserId = 2, TargetUserId = 1, CreatedAt = DateTimeOffset.UtcNow.AddDays(-ageDays) });
        await db.SaveChangesAsync();
        var service = new DeliveryBoostService(db, NullLogger<DeliveryBoostService>.Instance);
        var boosts = await service.GetBoostMapAsync(1, [2], DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(0.0, boosts[2]);
        Assert.Equal(1, await db.PendingMatches.CountAsync()); // Historical data must remain intact.
    }

    [Theory]
    [InlineData(MomentChoice.MAGICAL)]
    [InlineData(MomentChoice.LOGICAL)]
    [InlineData(MomentChoice.YES)]
    public async Task PositiveChoice_StillBoostsCandidate(MomentChoice choice)
    {
        await using var db = Database();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.MomentResponses.Add(new MomentResponse { FromUserId = 2, ToUserId = 1, DateUtc = today, Choice = choice, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var service = new DeliveryBoostService(db, NullLogger<DeliveryBoostService>.Instance);
        var boosts = await service.GetBoostMapAsync(1, [2], today);
        Assert.Equal(12.0, boosts[2]);
    }

    [Fact]
    public async Task EmptyCandidates_ReturnsEmptyBoostMap()
    {
        await using var db = Database();
        var service = new DeliveryBoostService(db, NullLogger<DeliveryBoostService>.Instance);
        Assert.Empty(await service.GetBoostMapAsync(1, [], DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private sealed class VectorlessTestContext(DbContextOptions<WovenDbContext> options) : WovenDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder); builder.Ignore<Pgvector.Vector>();
            foreach (var entity in builder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.ClrType.GetProperties().Where(p => p.PropertyType == typeof(Pgvector.Vector)))
                    builder.Entity(entity.ClrType).Ignore(property.Name);
        }
    }
}
