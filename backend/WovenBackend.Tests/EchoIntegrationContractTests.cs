using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Moments;
using WovenBackend.Services.Security;
using WovenBackend.Services.Trust;

namespace WovenBackend.Tests;

// Real integration, real MatchSignalService and real TrustService; isolated EF
// memory store. No nightly PostgreSQL worker, network provider or real user.
public class EchoIntegrationContractTests
{
    [Theory]
    [InlineData(1, 0f)]
    [InlineData(2, .25f)]
    [InlineData(3, .5f)]
    [InlineData(4, .75f)]
    [InlineData(5, 1f)]
    public async Task NonSafetyStarsPersistNormalizedExplicitFeedbackWithoutChangingTrust(int stars, float expected)
    {
        await using var db = QaMemoryContext.Create();
        var row = await Seed(db, rating: (short)stars);
        await Integration(db).ProcessAfterthoughtAsync(row.Id);
        var signal = Assert.Single(await db.MatchSignalLogs.ToListAsync());
        Assert.Equal(MatchSignalEventTypes.ExplicitFeedback, signal.EventType);
        Assert.Equal(expected, signal.EventValue); Assert.Equal(1, signal.ViewerId); Assert.Equal(2, signal.CandidateId);
        Assert.Equal(.8f, (await db.Users.FindAsync(2))!.TrustScore);
    }

    [Theory]
    [InlineData("block", .8f, .15f)]
    [InlineData("unmatch", .8f, .30f)]
    [InlineData("block", .1f, .1f)]
    [InlineData("unmatch", .1f, .1f)]
    public async Task SafetyFeedbackCapsOnlyTargetTrustAndNeverCreatesMatchingSignal(string path, float initial, float expected)
    {
        await using var db = QaMemoryContext.Create();
        var row = await Seed(db, path: path, safety: true, rating: null);
        (await db.Users.FindAsync(2))!.TrustScore = initial; await db.SaveChangesAsync();
        await Integration(db).ProcessAfterthoughtAsync(row.Id);
        Assert.Empty(await db.MatchSignalLogs.ToListAsync());
        Assert.Equal(expected, (await db.Users.FindAsync(2))!.TrustScore);
        Assert.Equal(.8f, (await db.Users.FindAsync(1))!.TrustScore);
    }

    [Fact]
    public async Task SafetyRowWithHistoricalStarsStillDoesNotEmitMatchingSignal()
    {
        await using var db = QaMemoryContext.Create(); var row = await Seed(db, path: "block", safety: true, rating: 5);
        await Integration(db).ProcessAfterthoughtAsync(row.Id);
        Assert.Empty(await db.MatchSignalLogs.ToListAsync());
        Assert.Equal(.15f, (await db.Users.FindAsync(2))!.TrustScore);
    }

    [Fact]
    public async Task ExpiryReasonWithoutStarsDoesNotInventMatchingValueOrTrustPenalty()
    {
        await using var db = QaMemoryContext.Create(); var row = await Seed(db, path: "expiry", rating: null);
        await Integration(db).ProcessAfterthoughtAsync(row.Id);
        Assert.Empty(await db.MatchSignalLogs.ToListAsync());
        Assert.Equal(.8f, (await db.Users.FindAsync(2))!.TrustScore);
    }

    [Fact]
    public async Task MissingRecordDoesNotEmitSignalsOrChangeTrust()
    {
        await using var db = QaMemoryContext.Create(); await Seed(db);
        await Integration(db).ProcessAfterthoughtAsync(int.MaxValue);
        Assert.Empty(await db.MatchSignalLogs.ToListAsync());
        Assert.Equal(.8f, (await db.Users.FindAsync(2))!.TrustScore);
    }

    [Fact]
    public async Task SubmissionRunsRealIntegrationAfterPersistingFeedback()
    {
        await using var db = QaMemoryContext.Create(); var row = await Seed(db);
        db.MatchAfterthoughts.Remove(row); await db.SaveChangesAsync();
        await new AfterthoughtService(db, Integration(db), NullLogger<AfterthoughtService>.Instance)
            .RecordAsync(row.MatchId, 1, 2, "unmatch", 4, "timing", "Synthetic input", false);
        Assert.Single(await db.MatchAfterthoughts.ToListAsync());
        Assert.Equal(.75f, Assert.Single(await db.MatchSignalLogs.ToListAsync()).EventValue);
    }

    [Fact]
    public async Task ReprocessingAnAfterthoughtDoesNotDuplicateItsLearningSignal()
    {
        await using var db = QaMemoryContext.Create(); var row = await Seed(db);
        var integration = Integration(db);
        await integration.ProcessAfterthoughtAsync(row.Id);
        await integration.ProcessAfterthoughtAsync(row.Id);
        Assert.Single(await db.MatchSignalLogs.ToListAsync());
    }

    [Fact]
    public async Task RetryingSavedSubmissionAfterTransientIntegrationFailureRecoversItsLearningSignal()
    {
        await using var db = QaMemoryContext.Create(); var row = await Seed(db);
        db.MatchAfterthoughts.Remove(row); await db.SaveChangesAsync();
        var transient = new TransientSignals(new MatchSignalService(db, NullLogger<MatchSignalService>.Instance));
        var integration = Integration(db, transient);
        var service = new AfterthoughtService(db, integration, NullLogger<AfterthoughtService>.Instance);
        await service.RecordAsync(row.MatchId, 1, 2, "unmatch", 3, "timing", null, false);
        Assert.Single(await db.MatchAfterthoughts.ToListAsync()); Assert.Empty(await db.MatchSignalLogs.ToListAsync());
        transient.Fail = false;
        try { await service.RecordAsync(row.MatchId, 1, 2, "unmatch", 3, "timing", null, false); }
        catch (InvalidOperationException) { /* Inspect actual retry result, not just its exception. */ }
        Assert.Single(await db.MatchAfterthoughts.ToListAsync());
        Assert.Single(await db.MatchSignalLogs.ToListAsync());
    }

    private static AfterthoughtIntegrationService Integration(QaMemoryContext db, IMatchSignalService? signals = null)
    {
        var audit = DispatchProxy.Create<ISecurityAuditService, EchoAuditStub>();
        var trust = new TrustService(db, new QaCache(), audit, NullLogger<TrustService>.Instance);
        return new(db, signals ?? new MatchSignalService(db, NullLogger<MatchSignalService>.Instance), trust, NullLogger<AfterthoughtIntegrationService>.Instance);
    }

    private static async Task<MatchAfterthought> Seed(QaMemoryContext db, string path = "unmatch", bool safety = false, short? rating = 3)
    {
        db.Users.AddRange(new User { Id = 1, Email = "one@unit.invalid", EmailHash = "unit1", TrustScore = .8f },
            new User { Id = 2, Email = "two@unit.invalid", EmailHash = "unit2", TrustScore = .8f });
        var match = new Match { UserAId = 1, UserBId = 2, BalloonState = BalloonState.CLOSED, ClosedReason = ClosedReason.UNMATCH };
        db.Matches.Add(match);
        var row = new MatchAfterthought { Match = match, MatchId = match.Id, FromUserId = 1, AboutUserId = 2, ClosePath = path, IsSafety = safety, Rating = rating, ReasonTag = safety ? "inappropriate" : "timing" };
        db.MatchAfterthoughts.Add(row); await db.SaveChangesAsync(); return row;
    }

    private sealed class TransientSignals(IMatchSignalService inner) : IMatchSignalService
    {
        public bool Fail { get; set; } = true;
        public Task RecordAsync(int viewerId, int candidateId, string eventType, float eventValue, string? metadataJson = null, CancellationToken ct = default)
            => Fail ? throw new InvalidOperationException("Injected local failure") : inner.RecordAsync(viewerId, candidateId, eventType, eventValue, metadataJson, ct);
    }
}

public class EchoAuditStub : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.ReturnType == typeof(void)) return null;
        if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
        throw new NotSupportedException("No audit fake result for " + method.Name);
    }
}
