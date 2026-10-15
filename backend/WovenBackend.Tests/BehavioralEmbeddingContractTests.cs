using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Embeddings;

namespace WovenBackend.Tests;

public class BehavioralEmbeddingContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(16)]
    public async Task RhythmRequiresTenActivitiesAndPersistsBoundedFortyEightDimensions(int activity)
    {
        await using var db = QaVectorContext.Create(); db.UserVectors.Add(new UserVector { UserId = 1, Version = 1, VectorJson = "{}", PillarScoresJson = "{}" });
        for (var i = 0; i < activity; i++) { db.ChatMessages.Add(new ChatMessage { SenderUserId = 1, Body = "Unit message", CreatedAt = DateTimeOffset.UtcNow.AddHours(-i) }); db.Tiles.Add(new Tile { UserId = 2, ContentType = "text" }); }
        await db.SaveChangesAsync();
        await new EmotionalRhythmService(db, NullLogger<EmotionalRhythmService>.Instance).ComputeEmotionalRhythmAsync(1); db.ChangeTracker.Clear(); var row = await db.UserVectors.SingleAsync();
        if (activity < 10) { Assert.Null(row.EmotionalRhythmEmbedding); return; }
        var values = row.EmotionalRhythmEmbedding!.ToArray(); Assert.Equal(48, values.Length); Assert.All(values, v => Assert.InRange(v, 0, 1)); Assert.True(values[12] > 0); Assert.Equal(0, values[0]);
    }
    [Theory]
    [InlineData(0, false)]
    [InlineData(13, false)]
    [InlineData(15, true)]
    public async Task LifestyleRequiresHistoryAndPersistsNeutralOrEncodedFields(int days, bool expected)
    {
        await using var db = QaVectorContext.Create(); db.UserVectors.Add(new UserVector { UserId = 1, Version = 1, VectorJson = "{}", PillarScoresJson = "{}" });
        if (days > 0) db.Tiles.Add(new Tile { UserId = 1, ContentType = "text", CreatedAt = DateTimeOffset.UtcNow.AddDays(-days) });
        db.UserOptionalFields.Add(new UserOptionalField { UserId = 1, Key = "smoking", Value = " Never " }); await db.SaveChangesAsync();
        await new LifestyleEmbeddingService(db, NullLogger<LifestyleEmbeddingService>.Instance).ComputeLifestyleEmbeddingAsync(1); db.ChangeTracker.Clear(); var row = await db.UserVectors.SingleAsync();
        if (!expected) { Assert.Null(row.LifestyleEmbedding); return; }
        var values = row.LifestyleEmbedding!.ToArray(); Assert.Equal(128, values.Length); Assert.Equal(0, values[2]); Assert.Equal(.5f, values[0]); Assert.All(values, v => Assert.InRange(v, 0, 1)); Assert.True(values[33] > 0);
    }
    [Fact]
    public async Task FingerprintUsesOwnRecentSignalsUpsertsAndRejectsCorruptStoredJson()
    {
        await using var db = QaVectorContext.Create(); var service = new BehavioralFingerprintService(db, NullLogger<BehavioralFingerprintService>.Instance); Assert.Null(await service.GetAsync(1));
        db.MatchSignalLogs.AddRange(new MatchSignalLog { ViewerId = 1, CandidateId = 2, EventType = MatchSignalEventTypes.MessageResponseLatencyMs, EventValue = 1800000 },
            new MatchSignalLog { ViewerId = 2, CandidateId = 1, EventType = MatchSignalEventTypes.MessageResponseLatencyMs, EventValue = 0 },
            new MatchSignalLog { ViewerId = 1, CandidateId = 3, EventType = MatchSignalEventTypes.MessageResponseLatencyMs, EventValue = 0, OccurredAt = DateTimeOffset.UtcNow.AddDays(-181) }); await db.SaveChangesAsync();
        await service.ComputeAsync(1); await service.ComputeAsync(1); var fp = await service.GetAsync(1); Assert.NotNull(fp); Assert.Equal(16, fp!.Length); Assert.Equal(.5f, fp[0]); Assert.Single(await db.UserBehavioralFingerprints.ToListAsync());
        var row = await db.UserBehavioralFingerprints.SingleAsync(); row.VectorJson = "invalid"; await db.SaveChangesAsync(); Assert.Null(await service.GetAsync(1));
    }
    [Theory]
    [InlineData("MessageSent", 1, 1, .1)]
    [InlineData("TrialAccepted", 1, 4, 1)]
    [InlineData("TrialRejected", 1, 4, 0)]
    [InlineData("SelfDisclosureRatio", .5f, 3, 1)]
    [InlineData("ExplicitFeedback", 2, 6, 1)]
    [InlineData("TileDwell", 8000, 7, 1)]
    [InlineData("VoiceDwell", 4000, 8, .5)]
    [InlineData("ProfileVisitDepth", 5, 11, 1)]
    [InlineData("GameCompleted", 1, 12, .1)]
    [InlineData("BalloonPop", 1, 13, .05)]
    [InlineData("DateIdeaAccepted", 1, 10, .5)]
    public void FingerprintDimensionsFollowEventMeaningAndCaps(string type, float value, int index, float expected)
    {
        var result = (float[])typeof(BehavioralFingerprintService).GetMethod("Compute", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [new List<(string, float, int)> { (type, value, 2) }])!;
        Assert.Equal(expected, result[index], 5); Assert.All(result, v => Assert.InRange(v, 0, 1)); Assert.Equal(.02f, result[14]); Assert.Equal(.002f, result[15]);
    }
    [Theory]
    [InlineData(0f, 0f, .25f, 1f, .25f)]
    [InlineData(1f, 1f, .75f, 0f, 0f)]
    [InlineData(.5f, .5f, .5f, .5f, .5f)]
    public void AttachmentUsesIndependentBoundedDimensions(float input, float secure, float anxious, float avoidant, float disorganised)
    {
        var fp = Enumerable.Repeat(input, 16).ToArray(); var result = (float[])typeof(AttachmentProxyService).GetMethod("Derive", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [fp])!;
        Assert.Equal(new[] { secure, anxious, avoidant, disorganised }, result); Assert.All(result, v => Assert.InRange(v, 0, 1));
    }
}
