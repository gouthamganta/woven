using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services;
using WovenBackend.Services.Matchmaking;
using WovenBackend.Services.Moments;
using WovenBackend.Services.Security;
using WovenBackend.Services.Trust;

namespace WovenBackend.Tests;

public class MatchHistoryContractTests
{
    [Theory]
    [InlineData("chat")]
    [InlineData("unmatch")]
    [InlineData("expire")]
    public async Task Outcome_RepeatEventsUpdateOwnSameDayRecordAndPreserveHistory(string action)
    {
        await using var db = QaMemoryContext.Create();
        var matchId = Guid.NewGuid();
        var yesterday = new MatchOutcome { MatchId = matchId, UserId = 1, CandidateId = 2, DateUtc = MomentsRules.UtcToday().AddDays(-1), Messages24h = 12 };
        db.MatchOutcomes.Add(yesterday); await db.SaveChangesAsync();
        var service = new MatchOutcomeService(db, NullLogger<MatchOutcomeService>.Instance);
        async Task Record() { if (action == "chat") await service.RecordChatStartedAsync(matchId, 1, 2); else if (action == "unmatch") await service.RecordUnmatchAsync(matchId, 1, 2); else await service.RecordExpiredAsync(matchId, 1, 2); }
        await Record(); var row = await db.MatchOutcomes.SingleAsync(o => o.DateUtc == MomentsRules.UtcToday()); row.Messages24h = 7; await db.SaveChangesAsync(); await Record();
        Assert.Equal(2, await db.MatchOutcomes.CountAsync()); Assert.Equal(12, yesterday.Messages24h); Assert.Equal(7, row.Messages24h);
        Assert.Equal(action == "chat", row.ChatStarted); Assert.Equal(action == "unmatch", row.Unmatched); Assert.Equal(action == "expire", row.Expired); Assert.False(row.Blocked);
        await service.RecordChatStartedAsync(matchId, 2, 1); Assert.Equal(3, await db.MatchOutcomes.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outcome_BlockLinksLatestPairMatchOrRecordsWithoutMatch(bool hasMatch)
    {
        await using var db = QaMemoryContext.Create(); var latest = new Match { UserAId = 2, UserBId = 1, CreatedAt = DateTimeOffset.UtcNow };
        if (hasMatch) db.Matches.AddRange(new Match { UserAId = 1, UserBId = 2, CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) }, latest);
        db.Matches.Add(new Match { UserAId = 3, UserBId = 4, CreatedAt = DateTimeOffset.UtcNow.AddDays(1) }); await db.SaveChangesAsync();
        await new MatchOutcomeService(db, NullLogger<MatchOutcomeService>.Instance).RecordBlockAsync(1, 2);
        var outcome = await db.MatchOutcomes.SingleAsync(); Assert.True(outcome.Blocked); Assert.Equal(hasMatch ? latest.Id : (Guid?)null, outcome.MatchId);
        Assert.Equal(1, outcome.UserId); Assert.Equal(2, outcome.CandidateId); Assert.False(outcome.Unmatched); Assert.False(outcome.Expired);
    }

    [Fact]
    public async Task Signal_SelfEventsAreIgnoredAndForeignPairKeepsPayloadAndTimestamp()
    {
        await using var db = QaMemoryContext.Create(); var service = new MatchSignalService(db, NullLogger<MatchSignalService>.Instance);
        await service.RecordAsync(1, 1, "Unit", 1); Assert.Empty(await db.MatchSignalLogs.ToListAsync());
        var before = DateTimeOffset.UtcNow; await service.RecordAsync(1, 2, "Unit", 2.5f, "{\"source\":\"unit\"}");
        var signal = await db.MatchSignalLogs.SingleAsync(); Assert.Equal(1, signal.ViewerId); Assert.Equal(2, signal.CandidateId); Assert.Equal(2.5f, signal.EventValue);
        Assert.Equal("Unit", signal.EventType); Assert.Equal("{\"source\":\"unit\"}", signal.MetadataJson); Assert.InRange(signal.OccurredAt, before, DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, -5)]
    [InlineData(3, -5)]
    [InlineData(4, -12)]
    public async Task Delivery_FatigueStartsAtSecondExposureAndUsesSevenDayWindow(int exposures, double expected)
    {
        await using var db = QaMemoryContext.Create(); var today = MomentsRules.UtcToday();
        for (var i = 0; i < exposures; i++) db.CandidateExposures.Add(new CandidateExposure { ViewerUserId = 1, ShownUserId = 2, DateUtc = today.AddDays(-i) });
        db.CandidateExposures.Add(new CandidateExposure { ViewerUserId = 1, ShownUserId = 2, DateUtc = today.AddDays(-8) });
        await db.SaveChangesAsync(); Assert.Equal(expected, (await Boost(db, [2, 2]))[2]);
    }

    [Theory]
    [InlineData(false, 18)]
    [InlineData(true, 0)]
    public async Task Delivery_ReciprocalExposureBoostEndsAfterViewerSeesCandidate(bool seen, double expected)
    {
        await using var db = QaMemoryContext.Create();
        db.CandidateExposures.Add(new CandidateExposure { ViewerUserId = 2, ShownUserId = 1, DateUtc = MomentsRules.UtcToday() });
        if (seen) db.CandidateExposures.Add(new CandidateExposure { ViewerUserId = 1, ShownUserId = 2, DateUtc = MomentsRules.UtcToday() });
        await db.SaveChangesAsync(); Assert.Equal(expected, (await Boost(db, [2]))[2]);
    }

    [Theory]
    [InlineData(MomentChoice.MAGICAL, 12)]
    [InlineData(MomentChoice.LOGICAL, 12)]
    [InlineData(MomentChoice.YES, 12)]
    [InlineData(MomentChoice.NO, 0)]
    public async Task Delivery_PositiveChoiceHasOneBoostAndDoesNotCountOldOrWrongDirection(MomentChoice choice, double expected)
    {
        await using var db = QaMemoryContext.Create();
        db.MomentResponses.AddRange(new MomentResponse { FromUserId = 2, ToUserId = 1, Choice = choice, DateUtc = MomentsRules.UtcToday() },
            new MomentResponse { FromUserId = 3, ToUserId = 1, Choice = MomentChoice.MAGICAL, DateUtc = MomentsRules.UtcToday().AddDays(-8) },
            new MomentResponse { FromUserId = 1, ToUserId = 4, Choice = MomentChoice.MAGICAL, DateUtc = MomentsRules.UtcToday() });
        await db.SaveChangesAsync(); var boost = await Boost(db, [2, 3, 4]); Assert.Equal(expected, boost[2]); Assert.Equal(0, boost[3]); Assert.Equal(0, boost[4]);
    }

    [Theory]
    [InlineData(ClosedReason.POP, 29, -10)]
    [InlineData(ClosedReason.POP, 31, 0)]
    [InlineData(ClosedReason.UNMATCH, 89, -18)]
    [InlineData(ClosedReason.UNMATCH, 91, 0)]
    [InlineData(ClosedReason.EXPIRE, 1, 0)]
    [InlineData(ClosedReason.BLOCK, 1, 0)]
    public async Task Delivery_ClosedPairHistoryUsesReasonSpecificWindows(ClosedReason reason, int ageDays, double expected)
    {
        await using var db = QaMemoryContext.Create();
        db.Matches.Add(new Match { UserAId = 2, UserBId = 1, BalloonState = BalloonState.CLOSED, ClosedReason = reason, ClosedAt = DateTimeOffset.UtcNow.AddDays(-ageDays) }); await db.SaveChangesAsync();
        Assert.Equal(expected, (await Boost(db, [2]))[2]);
    }

    [Theory]
    [InlineData(0.5f, 1, false, 0)]
    [InlineData(1f, 1, false, 10)]
    [InlineData(0f, 1, false, -10)]
    [InlineData(1f, 0, false, 0)]
    [InlineData(1f, 0.5f, false, 5)]
    [InlineData(1f, 1, true, 10.5)]
    public async Task Delivery_TrustGhostAndVerificationAdjustCandidateOnly(float trust, float ghost, bool verified, double expected)
    {
        await using var db = QaMemoryContext.Create(); db.Users.Add(new User { Id = 2, Email = "unit@woven.invalid", FullName = "Unit", TrustScore = trust, GhostScore = ghost, IsVerified = verified }); await db.SaveChangesAsync();
        var boost = await Boost(db, [2, 3]); Assert.Equal(expected, boost[2], 8); Assert.Equal(0, boost[3]);
        Assert.Empty(await Boost(db, []));
    }

    private static Task<Dictionary<int, double>> Boost(QaMemoryContext db, List<int> ids)
        => new DeliveryBoostService(db, NullLogger<DeliveryBoostService>.Instance).GetBoostMapAsync(1, ids, MomentsRules.UtcToday());
}

public class TrustContractTests
{
    private static TrustService Service(QaMemoryContext db, QaCache cache, out RecordedSideEffects audit)
    {
        audit = RecordedSideEffects.Create<ISecurityAuditService>(new ServiceCollection());
        return new TrustService(db, cache, (ISecurityAuditService)(object)audit, NullLogger<TrustService>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Fingerprint_EmptyInputLeavesCacheAndTrustUntouched(string? fingerprint)
    {
        await using var db = QaMemoryContext.Create(); var cache = new QaCache(); var service = Service(db, cache, out var audit);
        await service.CheckDeviceFingerprintAsync(1, fingerprint); Assert.Empty(cache.Values); Assert.Empty(audit.Calls);
    }

    [Fact]
    public async Task Fingerprint_HashesDeviceAndOnlyPenalizesDifferentAccount()
    {
        await using var db = QaMemoryContext.Create(); var cache = new QaCache(); var service = Service(db, cache, out var audit);
        db.Users.AddRange(new User { Id = 1, Email = "one@woven.invalid", FullName = "One", TrustScore = .5f }, new User { Id = 2, Email = "two@woven.invalid", FullName = "Two", TrustScore = .1f }); await db.SaveChangesAsync();
        const string fingerprint = "unit-device-fingerprint";
        await service.CheckDeviceFingerprintAsync(1, fingerprint); await service.CheckDeviceFingerprintAsync(1, fingerprint);
        var key = Assert.Single(cache.Values).Key; Assert.DoesNotContain(fingerprint, key); Assert.Equal("fp:" + PiiSanitizer.HashForAudit(fingerprint, "fp-v1"), key);
        Assert.Empty(audit.Calls); await service.CheckDeviceFingerprintAsync(2, fingerprint);
        Assert.Equal(0, (await db.Users.SingleAsync(u => u.Id == 2)).TrustScore); Assert.Equal(.5f, (await db.Users.SingleAsync(u => u.Id == 1)).TrustScore);
        Assert.Equal("1", cache.Values[key]); Assert.Equal("suspicious_pattern", Assert.Single(audit.Calls).Args[0]);
    }

    [Theory]
    [InlineData(.249f, false)]
    [InlineData(.25f, true)]
    [InlineData(1f, true)]
    public async Task Trust_ThresholdIsInclusiveAndMissingAccountsAreNotTrusted(float score, bool expected)
    {
        await using var db = QaMemoryContext.Create(); db.Users.Add(new User { Id = 1, Email = "unit@woven.invalid", FullName = "Unit", TrustScore = score }); await db.SaveChangesAsync();
        var service = Service(db, new QaCache(), out _); Assert.Equal(expected, await service.IsTrustedEnoughAsync(1)); Assert.False(await service.IsTrustedEnoughAsync(99));
    }

    [Theory]
    [InlineData(10, .5f)]
    [InlineData(11, .4f)]
    public async Task Velocity_OnlyRecentOwnTilesBeyondThresholdReduceTrust(int count, float expected)
    {
        await using var db = QaMemoryContext.Create(); db.Users.Add(new User { Id = 1, Email = "unit@woven.invalid", FullName = "Unit", TrustScore = .5f });
        for (var i = 0; i < count; i++) db.Tiles.Add(new Tile { UserId = 1, ContentType = "TEXT" });
        db.Tiles.AddRange(new Tile { UserId = 1, ContentType = "TEXT", CreatedAt = DateTimeOffset.UtcNow.AddHours(-2) }, new Tile { UserId = 2, ContentType = "TEXT" }); await db.SaveChangesAsync();
        await Service(db, new QaCache(), out _).CheckVelocityAsync(1); Assert.Equal(expected, (await db.Users.SingleAsync()).TrustScore, 5);
    }

    [Theory]
    [InlineData(false, false, .5f)]
    [InlineData(true, false, .65f)]
    [InlineData(false, true, .55f)]
    [InlineData(true, true, .7f)]
    public async Task BotDetection_UsesOwnProfileAndPhotoAndIsRepeatable(bool profile, bool photo, float expected)
    {
        await using var db = QaMemoryContext.Create(); db.Users.Add(new User { Id = 1, Email = "unit@woven.invalid", FullName = "Unit", TrustScore = 0 });
        if (profile) db.UserProfiles.Add(new UserProfile { UserId = 1, Age = 30 }); if (photo) db.UserPhotos.Add(new UserPhoto { UserId = 1, Url = "https://unit.invalid/photo" });
        db.UserPhotos.Add(new UserPhoto { UserId = 2, Url = "https://unit.invalid/foreign" }); await db.SaveChangesAsync();
        var service = Service(db, new QaCache(), out _); await service.RunBotDetectionAsync(1); await service.RunBotDetectionAsync(1); await service.RunBotDetectionAsync(99);
        Assert.Equal(expected, (await db.Users.SingleAsync()).TrustScore, 5);
    }
}
