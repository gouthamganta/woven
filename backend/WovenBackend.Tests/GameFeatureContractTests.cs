using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Data.Entities.Games;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Games;
using MatchType = WovenBackend.data.Entities.Moments.MatchType;

namespace WovenBackend.Tests;

// All persistence here is in-memory and all AI/notification/outcome boundaries
// are controlled doubles. These are code contracts, not a sandbox/persona run.
public class GameFeatureContractTests
{
    private static Match AddMatch(QaMemoryContext db)
    {
        foreach (var id in new[] { 1, 2 })
        {
            var user = new User { Id = id, Email = $"unit{id}@example.invalid", FullName = $"Unit Player {id}", ProfileStatus = ProfileStatus.COMPLETE };
            db.Users.Add(user);
            db.UserProfiles.Add(new UserProfile { UserId = id, User = user, Age = 25, Gender = "nonbinary" });
            db.UserVectors.Add(new UserVector { UserId = id, Version = 1, PillarScoresJson = "{\"Values\":0.9}", VectorJson = "{\"pulse\":{\"banter\":0.7}}" });
        }
        var match = new Match { UserAId = 1, UserBId = 2, MatchType = MatchType.PURE, BalloonState = BalloonState.ACTIVE };
        db.Matches.Add(match);
        return match;
    }
    private static (GameService Service, UnitGameAgent Agent) Setup(QaMemoryContext db, PairContext? pair = null)
    {
        var agent = new UnitGameAgent();
        return (new GameService(db, new UnitAgentFactory(agent), NullLogger<GameService>.Instance,
            new UnitAiContext(pair), UnitTaskBoundary.Create<IGameOutcomeService>(), UnitTaskBoundary.Create<INotificationService>(), UnitTaskBoundary.Create<IAnalyticsService>()), agent);
    }

    [Theory]
    [InlineData(0, false, true, 2, null)]
    [InlineData(1, false, true, 1, null)]
    [InlineData(2, false, false, 0, "DAILY_LIMIT")]
    [InlineData(3, true, false, 0, "DAILY_LIMIT")]
    [InlineData(0, true, false, 2, "PENDING_GAME")]
    public async Task Availability_CombinesDailyLimitAndPendingGameState(int used, bool pending, bool allowed, int remaining, string? reason)
    {
        await using var db = QaMemoryContext.Create(); var match = AddMatch(db);
        if (used > 0) db.DailyInteractions.Add(new DailyInteraction { UserId = 1, DateUtc = DateOnly.FromDateTime(DateTime.UtcNow), GamesInitiated = (short)used });
        if (pending) db.GameSessions.Add(new GameSession { MatchId = match.Id, Status = "PENDING", InitiatorUserId = 1 });
        await db.SaveChangesAsync(); var result = await Setup(db).Service.CheckAvailabilityAsync(match.Id, 1);
        Assert.Equal(allowed, result.Available); Assert.Equal(remaining, result.GamesRemaining); Assert.Equal(reason, result.Reason);
    }

    [Theory]
    [InlineData(GameSessionType.KNOW_ME)]
    [InlineData(GameSessionType.RED_GREEN_FLAG)]
    public async Task CreateSession_PersistsInviteMessagesAndOneDailyGameUse(GameSessionType type)
    {
        await using var db = QaMemoryContext.Create(); var match = AddMatch(db); await db.SaveChangesAsync();
        var before = DateTimeOffset.UtcNow; var result = await Setup(db).Service.CreateSessionAsync(match.Id, 1, type);
        Assert.Equal("PENDING", result.Status); Assert.Equal(type.ToString(), result.GameType);
        Assert.InRange(result.ExpiresAt, before.AddMinutes(10), DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(1, (await db.DailyInteractions.SingleAsync()).GamesInitiated);
        Assert.Single(await db.ChatThreads.ToListAsync());
        Assert.Equal(new[] { "GAME", "SYSTEM" }, (await db.ChatMessages.ToListAsync()).Select(m => m.MessageType).Order());
    }

    [Fact]
    public async Task Creation_RejectsMissingClosedPendingOrDailyCappedMatches()
    {
        await using var db = QaMemoryContext.Create(); var setup = Setup(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateSessionAsync(Guid.NewGuid(), 1, GameSessionType.KNOW_ME));
        db.ChangeTracker.Clear(); var match = AddMatch(db); match.BalloonState = BalloonState.CLOSED; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateSessionAsync(match.Id, 1, GameSessionType.KNOW_ME));
        match.BalloonState = BalloonState.ACTIVE; await db.SaveChangesAsync(); await setup.Service.CreateSessionAsync(match.Id, 1, GameSessionType.KNOW_ME);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateSessionAsync(match.Id, 1, GameSessionType.KNOW_ME));
        var row = await db.DailyInteractions.SingleAsync(); row.GamesInitiated = 2; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.CreateSessionAsync(match.Id, 1, GameSessionType.KNOW_ME));
        Assert.Single(await db.GameSessions.ToListAsync());
    }

    [Fact]
    public async Task Accept_RejectsMissingInactiveExpiredForeignAndOrphanedSessions()
    {
        await using var db = QaMemoryContext.Create(); var match = AddMatch(db); await db.SaveChangesAsync(); var service = Setup(db).Service;
        Assert.False(await service.AcceptSessionAsync(Guid.NewGuid(), 2));
        var session = new GameSession { MatchId = match.Id, InitiatorUserId = 1, GameType = "KNOW_ME", Status = "COMPLETED", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) };
        db.GameSessions.Add(session); await db.SaveChangesAsync(); Assert.False(await service.AcceptSessionAsync(session.Id, 2));
        session.Status = "PENDING"; session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); Assert.False(await service.AcceptSessionAsync(session.Id, 2)); Assert.Equal("EXPIRED", session.Status);
        session.Status = "PENDING"; session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10); await db.SaveChangesAsync(); Assert.False(await service.AcceptSessionAsync(session.Id, 3));
        session.MatchId = Guid.NewGuid(); await db.SaveChangesAsync(); Assert.False(await service.AcceptSessionAsync(session.Id, 2));
        Assert.Empty(await db.GameRounds.ToListAsync());
    }

    [Theory]
    [InlineData(0, "playful", "playful", "Other", 0.5, "EASY", "PLAYFUL", "EXPLORER")]
    [InlineData(1, "calm", "balanced", "Lifestyle", 0.5, "MEDIUM", "THOUGHTFUL", "LIFESTYLE_FIT")]
    [InlineData(3, "thoughtful", "playful", "Values", 0.9, "HARD", "THOUGHTFUL", "CORE_FIT")]
    [InlineData(1, "balanced", "balanced", "Other", 0.5, "MEDIUM", "BALANCED", "CONVERSATION_FIT")]
    public async Task AcceptedRound_UsesPairContextAndGuesserTargetRoles(int aligned, string firstTone, string secondTone, string pillar, double intent, string difficulty, string tone, string bucket)
    {
        await using var db = QaMemoryContext.Create(); var match = AddMatch(db);
        var pair = new PairContext { UserProfile = new AiProfile { ConversationTone = firstTone }, CandidateProfile = new AiProfile { ConversationTone = secondTone }, IntentAlignment = intent, ToneAlignment = aligned == 1 && pillar == "Other" ? "matched" : "different" };
        for (var i = 0; i < aligned; i++) pair.AlignedPillars.Add(new PillarAlignment { Pillar = pillar });
        var session = new GameSession { MatchId = match.Id, InitiatorUserId = 1, GameType = "KNOW_ME", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) };
        db.GameSessions.Add(session); await db.SaveChangesAsync(); var setup = Setup(db, pair);
        Assert.True(await setup.Service.AcceptSessionAsync(session.Id, 2));
        var context = Assert.Single(setup.Agent.Contexts); Assert.Equal(1, context.GuesserUserId); Assert.Equal(2, context.TargetUserId);
        Assert.Equal(difficulty, context.Difficulty.ToString()); Assert.Equal(tone, context.Tone.ToString()); Assert.Equal(bucket, context.Bucket.ToString());
        Assert.NotNull(context.UserAVector); Assert.Equal(25, context.UserAVector.Age); Assert.Equal(0.9, context.UserAVector.Pillars["Values"]);
        Assert.Equal("ACTIVE", session.Status); Assert.NotNull(session.MetadataJson); Assert.Single(await db.GameRounds.ToListAsync());
        Assert.InRange(session.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Reject_ChangesOnlyPendingSessions()
    {
        await using var db = QaMemoryContext.Create(); var match = AddMatch(db);
        var session = new GameSession { MatchId = match.Id, InitiatorUserId = 1, GameType = "KNOW_ME", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) };
        db.GameSessions.Add(session); await db.SaveChangesAsync(); var service = Setup(db).Service;
        Assert.False(await service.RejectSessionAsync(Guid.NewGuid(), 2)); Assert.True(await service.RejectSessionAsync(session.Id, 2)); Assert.Equal("REJECTED", session.Status); Assert.False(await service.RejectSessionAsync(session.Id, 2));
    }

    [Fact]
    public async Task CurrentRound_ReturnsNothingForAbsentInactiveOrMissingRoundData()
    {
        await using var db = QaMemoryContext.Create(); var service = Setup(db).Service;
        Assert.Null(await service.GetCurrentRoundAsync(Guid.NewGuid(), 1));
        var session = new GameSession { MatchId = Guid.NewGuid(), Status = "PENDING" }; db.GameSessions.Add(session); await db.SaveChangesAsync();
        Assert.Null(await service.GetCurrentRoundAsync(session.Id, 1)); session.Status = "ACTIVE"; await db.SaveChangesAsync(); Assert.Null(await service.GetCurrentRoundAsync(session.Id, 1));
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(0, 2, 2)]
    [InlineData(1, 1, 0)]
    public async Task TwoRoundGame_FlipsRolesScoresAndCompletesWithTheCorrectWinner(int firstScore, int secondScore, int winner)
    {
        await using var db = QaMemoryContext.Create(); var match = AddMatch(db); await db.SaveChangesAsync(); var service = Setup(db).Service;
        var invite = await service.CreateSessionAsync(match.Id, 1, GameSessionType.KNOW_ME); Assert.True(await service.AcceptSessionAsync(invite.SessionId, 2));
        var guesses = new Dictionary<string, string> { ["q1"] = "A", ["q2"] = "A" };
        var first = await service.GetCurrentRoundAsync(invite.SessionId, 1); Assert.NotNull(first); Assert.True(first.IsGuesser); Assert.False(first.HasAnswered);
        Assert.Equal("WAITING_FOR_TARGET", (await service.SubmitAnswersAsync(invite.SessionId, 1, guesses)).Status);
        Assert.True((await service.GetCurrentRoundAsync(invite.SessionId, 1))!.HasAnswered);
        var one = await service.SubmitTargetAnswersAsync(invite.SessionId, 2, TargetAnswers(firstScore)); Assert.Equal(firstScore, one.Score); Assert.Equal("NEXT_ROUND", one.Status);
        var round = await db.GameRounds.SingleAsync(r => r.RoundNumber == 2); Assert.Equal(2, round.GuesserUserId); Assert.Equal(1, round.TargetUserId);
        await service.SubmitAnswersAsync(invite.SessionId, 2, guesses); var two = await service.SubmitTargetAnswersAsync(invite.SessionId, 1, TargetAnswers(secondScore)); Assert.Equal("GAME_COMPLETE", two.Status);
        var result = await service.GetFinalResultAsync(invite.SessionId); Assert.NotNull(result); Assert.Equal(firstScore, result.UserAScore); Assert.Equal(secondScore, result.UserBScore); Assert.Equal(winner == 0 ? (int?)null : winner, result.WinnerUserId);
        Assert.Equal("Controlled unit insight", result.AiInsight); Assert.True((await db.GameAnalytics.SingleAsync()).Completed); Assert.Equal("COMPLETED", (await db.GameSessions.SingleAsync()).Status);
    }

    private static Dictionary<string, string> TargetAnswers(int score) => new() { ["q1"] = score >= 1 ? "A" : "B", ["q2"] = score >= 2 ? "A" : "B" };

    [Fact]
    public async Task AnswerSubmission_RejectsInactiveAndMissingRoundStates()
    {
        await using var db = QaMemoryContext.Create(); var service = Setup(db).Service;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAnswersAsync(Guid.NewGuid(), 1, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitTargetAnswersAsync(Guid.NewGuid(), 1, []));
        var session = new GameSession { Status = "ACTIVE" }; db.GameSessions.Add(session); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAnswersAsync(session.Id, 1, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitTargetAnswersAsync(session.Id, 1, []));
    }

    [Fact]
    public async Task FinalResult_HandlesMissingResultMissingMatchAndNullableFields()
    {
        await using var db = QaMemoryContext.Create(); var service = Setup(db).Service; Assert.Null(await service.GetFinalResultAsync(Guid.NewGuid()));
        var result = new GameResult { SessionId = Guid.NewGuid(), MatchId = Guid.NewGuid() }; db.GameResults.Add(result); await db.SaveChangesAsync(); Assert.Null(await service.GetFinalResultAsync(result.SessionId));
        var match = AddMatch(db); result.MatchId = match.Id; await db.SaveChangesAsync(); var output = await service.GetFinalResultAsync(result.SessionId);
        Assert.NotNull(output); Assert.Equal(0, output.UserAScore); Assert.Equal(0, output.UserBScore); Assert.Equal("", output.AiInsight);
    }

    private sealed class UnitAiContext(PairContext? pair) : IAiProfileService
    {
        public Task<AiProfile?> GetProfileAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException("Direct AI profile calls are outside this test boundary.");
        public Task<PairContext?> GetPairContextAsync(int userId, int candidateId, CancellationToken ct = default) => Task.FromResult(pair);
    }
    private sealed class UnitAgentFactory(UnitGameAgent agent) : IGameAgentFactory
    {
        public IGameAgent GetAgent(string gameType) => agent;
    }
    private sealed class UnitGameAgent : IGameAgent
    {
        public List<GameContext> Contexts { get; } = [];
        public Task<GameRoundData> GenerateRoundAsync(GameContext context, CancellationToken ct = default)
        {
            Contexts.Add(context);
            return Task.FromResult(new GameRoundData { Questions = [new QuestionData { Id = "q1", Text = "Unit question one" }, new QuestionData { Id = "q2", Text = "Unit question two" }] });
        }
        public Task<string> GenerateInsightAsync(GameContext context, List<RoundResult> rounds, CancellationToken ct = default) => Task.FromResult("Controlled unit insight");
    }
}

public class UnitTaskBoundary : DispatchProxy
{
    public static T Create<T>() where T : class => DispatchProxy.Create<T, UnitTaskBoundary>();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.ReturnType == typeof(Task)) return Task.CompletedTask;
        throw new NotSupportedException("The unit boundary permits only non-network Task callbacks.");
    }
}
