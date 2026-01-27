using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WovenBackend.Data;
using WovenBackend.Data.Entities.Games;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Games;

public interface IGameService
{
    Task<GameSessionDto> CreateSessionAsync(Guid matchId, int initiatorUserId, GameSessionType gameType, CancellationToken ct = default);
    Task<bool> AcceptSessionAsync(Guid sessionId, int userId, CancellationToken ct = default);
    Task<bool> RejectSessionAsync(Guid sessionId, int userId, CancellationToken ct = default);
    Task<GameRoundDto?> GetCurrentRoundAsync(Guid sessionId, int userId, CancellationToken ct = default);
    Task<RoundResultDto> SubmitAnswersAsync(Guid sessionId, int userId, Dictionary<string, string> answers, CancellationToken ct = default);
    Task<RoundResultDto> SubmitTargetAnswersAsync(Guid sessionId, int userId, Dictionary<string, string> answers, CancellationToken ct = default);
    Task<GameResultDto?> GetFinalResultAsync(Guid sessionId, CancellationToken ct = default);
    Task<GameAvailabilityDto> CheckAvailabilityAsync(Guid matchId, int userId, CancellationToken ct = default);
}

public class GameService : IGameService
{
    private readonly WovenDbContext _db;
    private readonly IGameAgentFactory _agentFactory;
    private readonly ILogger<GameService> _logger;
    private readonly IAiProfileService _aiProfileService;
    private readonly IGameOutcomeService _gameOutcomeService;

    public GameService(
        WovenDbContext db,
        IGameAgentFactory agentFactory,
        ILogger<GameService> logger,
        IAiProfileService aiProfileService,
        IGameOutcomeService gameOutcomeService)
    {
        _db = db;
        _agentFactory = agentFactory;
        _logger = logger;
        _aiProfileService = aiProfileService;
        _gameOutcomeService = gameOutcomeService;
    }

    public async Task<GameAvailabilityDto> CheckAvailabilityAsync(
        Guid matchId,
        int userId,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var dailyInteraction = await _db.DailyInteractions
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DateUtc == today, ct);

        var gamesInitiated = dailyInteraction?.GamesInitiated ?? 0;
        var canInitiate = gamesInitiated < 2; // Daily limit

        // Check for pending game in this match
        var hasPending = await _db.GameSessions
            .AnyAsync(g => g.MatchId == matchId && g.Status == GameSessionStatus.PENDING.ToString(), ct);

        return new GameAvailabilityDto
        {
            Available = canInitiate && !hasPending,
            GamesRemaining = Math.Max(0, 2 - gamesInitiated),
            Reason = !canInitiate ? "DAILY_LIMIT"
                : hasPending ? "PENDING_GAME"
                : null
        };
    }

    public async Task<GameSessionDto> CreateSessionAsync(
        Guid matchId,
        int initiatorUserId,
        GameSessionType gameType,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[Game] Creating session for match {MatchId}, initiator {UserId}, type {GameType}",
            matchId, initiatorUserId, gameType);

        // Check daily limit
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var dailyInteraction = await _db.DailyInteractions
            .FirstOrDefaultAsync(d => d.UserId == initiatorUserId && d.DateUtc == today, ct);

        if (dailyInteraction == null)
        {
            dailyInteraction = new DailyInteraction
            {
                UserId = initiatorUserId,
                DateUtc = today
            };
            _db.DailyInteractions.Add(dailyInteraction);
        }

        if (dailyInteraction.GamesInitiated >= 2)
            throw new InvalidOperationException("Daily game limit reached");

        // Get match
        var match = await _db.Matches
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match == null)
            throw new InvalidOperationException("Match not found");

        if (match.BalloonState != BalloonState.ACTIVE)
            throw new InvalidOperationException("Balloon not active");

        // Prevent creating multiple pending sessions in same match
        var hasPending = await _db.GameSessions
            .AnyAsync(g => g.MatchId == matchId && g.Status == GameSessionStatus.PENDING.ToString(), ct);

        if (hasPending)
            throw new InvalidOperationException("A game is already pending for this match");

        // ✅ FIX #1: Extended expiry from 3 to 10 minutes
        var session = new GameSession
        {
            MatchId = matchId,
            GameType = gameType.ToString(),
            InitiatorUserId = initiatorUserId,
            Status = GameSessionStatus.PENDING.ToString(),
            TotalRounds = 2,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) // ✅ Changed from 3 to 10
        };

        _db.GameSessions.Add(session);

        // Increment counter
        dailyInteraction.GamesInitiated++;

        await _db.SaveChangesAsync(ct);

        await AddGameChatMessageAsync(
            matchId: session.MatchId,
            senderUserId: initiatorUserId,
            body: $"🎮 Game request: {session.GameType.Replace("_", " ")}",
            meta: new
            {
                sessionId = session.Id,
                matchId = session.MatchId,
                gameType = session.GameType,
                status = session.Status,
                expiresAt = session.ExpiresAt
            },
            ct: ct
        );

        await AddSystemMessageAsync(
            matchId,
            initiatorUserId,
            $"🎮 {gameType} game invite sent",
            new
            {
                type = "GAME_INVITE",
                sessionId = session.Id,
                gameType = session.GameType,
                status = session.Status,
                expiresAt = session.ExpiresAt
            },
            ct
        );

        _logger.LogInformation("[Game] Created session {SessionId}, expires at {ExpiresAt}", 
            session.Id, session.ExpiresAt);

        return new GameSessionDto
        {
            SessionId = session.Id,
            MatchId = session.MatchId,
            GameType = session.GameType,
            Status = session.Status,
            ExpiresAt = session.ExpiresAt
        };
    }

    public async Task<bool> AcceptSessionAsync(Guid sessionId, int userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[Game] User {UserId} accepting session {SessionId}", userId, sessionId);

        var session = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null || session.Status != GameSessionStatus.PENDING.ToString())
        {
            _logger.LogWarning("[Game] Session {SessionId} not found or not pending. Status: {Status}", 
                sessionId, session?.Status ?? "NULL");
            return false;
        }

        // ✅ FIX #2: Add 30-second grace period for accepting
        if (DateTimeOffset.UtcNow > session.ExpiresAt.AddSeconds(30))
        {
            _logger.LogWarning("[Game] Session {SessionId} expired. ExpiresAt: {ExpiresAt}, Now: {Now}", 
                sessionId, session.ExpiresAt, DateTimeOffset.UtcNow);
            session.Status = GameSessionStatus.EXPIRED.ToString();
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return false;
        }

        var match = await _db.Matches
            .FirstOrDefaultAsync(m => m.Id == session.MatchId, ct);

        if (match == null)
        {
            _logger.LogWarning("[Game] Match {MatchId} not found for session {SessionId}", 
                session.MatchId, sessionId);
            return false;
        }

        // Verify user is part of this match
        if (match.UserAId != userId && match.UserBId != userId)
        {
            _logger.LogWarning("[Game] User {UserId} is not part of match {MatchId}", userId, match.Id);
            return false;
        }

        // Start the game
        session.Status = GameSessionStatus.ACTIVE.ToString();
        session.UpdatedAt = DateTimeOffset.UtcNow;

        // ✅ FIX #3: Extend expiry to 30 minutes when game is accepted
        session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        // Generate first round
        var context = await BuildGameContextAsync(session, match, 1, ct);
        var agent = _agentFactory.GetAgent(session.GameType);
        var roundData = await agent.GenerateRoundAsync(context, ct);

        // Store game metadata for outcome tracking
        session.MetadataJson = JsonSerializer.Serialize(new
        {
            difficulty = context.Difficulty.ToString(),
            tone = context.Tone.ToString(),
            bucket = context.Bucket.ToString(),
            intentAlignment = context.IntentAlignment,
            hasAlignedPillars = context.PairContext?.AlignedPillars.Count ?? 0,
            hasSharedTags = context.PairContext?.SharedTags.Count ?? 0,
            hasSharedHobbies = context.PairContext?.SharedHobbies.Count ?? 0,
            toneAlignment = context.PairContext?.ToneAlignment ?? "unknown"
        });

        var gameRound = new GameRound
        {
            SessionId = session.Id,
            RoundNumber = 1,
            GuesserUserId = session.InitiatorUserId,
            TargetUserId = match.UserAId == session.InitiatorUserId ? match.UserBId : match.UserAId,
            QuestionsJson = JsonSerializer.Serialize(roundData.Questions),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.GameRounds.Add(gameRound);

        // Track analytics
        var threadId = await _db.ChatThreads
            .Where(t => t.MatchId == session.MatchId)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        var messagesBefore = 0;
        if (threadId != Guid.Empty)
        {
            messagesBefore = await _db.ChatMessages
                .Where(m => m.ThreadId == threadId && m.CreatedAt < session.CreatedAt)
                .CountAsync(ct);
        }

        var analytic = new GameAnalytic
        {
            SessionId = session.Id,
            MatchId = session.MatchId,
            GameType = session.GameType,
            MessagesBeforeGame = messagesBefore
        };

        _db.GameAnalytics.Add(analytic);

        await _db.SaveChangesAsync(ct);

        await AddSystemMessageAsync(
            session.MatchId,
            userId,
            "✅ Game started",
            new
            {
                type = "GAME_ACCEPTED",
                sessionId = session.Id,
                gameType = session.GameType,
                status = session.Status
            },
            ct
        );

        _logger.LogInformation("[Game] Session {SessionId} started, round 1 generated, expires at {ExpiresAt}", 
            session.Id, session.ExpiresAt);

        return true;
    }

    public async Task<bool> RejectSessionAsync(Guid sessionId, int userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[Game] User {UserId} rejecting session {SessionId}", userId, sessionId);

        var session = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null || session.Status != GameSessionStatus.PENDING.ToString())
            return false;

        session.Status = GameSessionStatus.REJECTED.ToString();
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<GameRoundDto?> GetCurrentRoundAsync(Guid sessionId, int userId, CancellationToken ct = default)
    {
        var session = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null || session.Status != GameSessionStatus.ACTIVE.ToString())
            return null;

        var currentRound = await _db.GameRounds
            .FirstOrDefaultAsync(r => r.SessionId == sessionId && r.RoundNumber == session.CurrentRound, ct);

        if (currentRound == null)
            return null;

        var questions = JsonSerializer.Deserialize<List<QuestionData>>(currentRound.QuestionsJson) ?? new();

        var isGuesser = currentRound.GuesserUserId == userId;
        var hasAnswered = isGuesser
            ? !string.IsNullOrWhiteSpace(currentRound.AnswersJson)
            : !string.IsNullOrWhiteSpace(currentRound.TargetAnswersJson);

        return new GameRoundDto
        {
            RoundNumber = session.CurrentRound,
            TotalRounds = session.TotalRounds,
            Questions = questions,
            TimeLimit = 90,
            IsGuesser = isGuesser,
            HasAnswered = hasAnswered,
            WaitingForOther = hasAnswered
        };
    }

    public async Task<RoundResultDto> SubmitAnswersAsync(
        Guid sessionId,
        int userId,
        Dictionary<string, string> answers,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[Game] User {UserId} submitting answers for session {SessionId}", userId, sessionId);

        var session = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null || session.Status != GameSessionStatus.ACTIVE.ToString())
            throw new InvalidOperationException("Session not active");

        var currentRound = await _db.GameRounds
            .FirstOrDefaultAsync(r => r.SessionId == sessionId && r.RoundNumber == session.CurrentRound, ct);

        if (currentRound == null)
            throw new InvalidOperationException("Current round not found");

        // Store guesser's answers
        currentRound.AnswersJson = JsonSerializer.Serialize(answers);

        await _db.SaveChangesAsync(ct);

        return new RoundResultDto
        {
            RoundNumber = session.CurrentRound,
            Status = "WAITING_FOR_TARGET",
            Message = "Waiting for them to reveal their answers..."
        };
    }

    public async Task<RoundResultDto> SubmitTargetAnswersAsync(
        Guid sessionId,
        int userId,
        Dictionary<string, string> answers,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[Game] User {UserId} submitting target answers for session {SessionId}", userId, sessionId);

        var session = await _db.GameSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null || session.Status != GameSessionStatus.ACTIVE.ToString())
            throw new InvalidOperationException("Session not active");

        var currentRound = await _db.GameRounds
            .FirstOrDefaultAsync(r => r.SessionId == sessionId && r.RoundNumber == session.CurrentRound, ct);

        if (currentRound == null)
            throw new InvalidOperationException("Current round not found");

        // Store target's actual answers
        currentRound.TargetAnswersJson = JsonSerializer.Serialize(answers);

        // Calculate score
        var questions = JsonSerializer.Deserialize<List<QuestionData>>(currentRound.QuestionsJson) ?? new();
        var guesserAnswers = JsonSerializer.Deserialize<Dictionary<string, string>>(currentRound.AnswersJson ?? "{}") ?? new();

        var score = questions.Count(q =>
            guesserAnswers.TryGetValue(q.Id, out var guessed) &&
            answers.TryGetValue(q.Id, out var actual) &&
            guessed == actual
        );

        currentRound.Score = score;
        currentRound.CompletedAt = DateTimeOffset.UtcNow;

        // Move to next round or complete game
        if (session.CurrentRound < session.TotalRounds)
        {
            session.CurrentRound++;

            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == session.MatchId, ct);
            if (match != null)
            {
                var context = await BuildGameContextAsync(session, match, session.CurrentRound, ct);
                var agent = _agentFactory.GetAgent(session.GameType);
                var roundData = await agent.GenerateRoundAsync(context, ct);

                var nextRound = new GameRound
                {
                    SessionId = session.Id,
                    RoundNumber = session.CurrentRound,
                    GuesserUserId = currentRound.TargetUserId, // Flip roles
                    TargetUserId = currentRound.GuesserUserId,
                    QuestionsJson = JsonSerializer.Serialize(roundData.Questions),
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _db.GameRounds.Add(nextRound);
            }

            await _db.SaveChangesAsync(ct);

            return new RoundResultDto
            {
                RoundNumber = session.CurrentRound - 1,
                Score = score,
                TotalQuestions = questions.Count,
                Status = "NEXT_ROUND",
                Message = $"Round {session.CurrentRound - 1} complete! Score: {score}/{questions.Count}"
            };
        }

        await CompleteGameAsync(session, ct);

        return new RoundResultDto
        {
            RoundNumber = session.CurrentRound,
            Score = score,
            TotalQuestions = questions.Count,
            Status = "GAME_COMPLETE",
            Message = "Game complete! See final results."
        };
    }

    public async Task<GameResultDto?> GetFinalResultAsync(Guid sessionId, CancellationToken ct = default)
    {
        var result = await _db.GameResults
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, ct);

        if (result == null)
            return null;

        var match = await _db.Matches
            .FirstOrDefaultAsync(m => m.Id == result.MatchId, ct);

        if (match == null)
            return null;

        return new GameResultDto
        {
            SessionId = result.SessionId,
            GameType = result.GameType,
            UserAScore = result.UserAScore ?? 0,
            UserBScore = result.UserBScore ?? 0,
            WinnerUserId = result.WinnerUserId,
            AiInsight = result.AiInsight ?? "",
            UserAId = match.UserAId,
            UserBId = match.UserBId
        };
    }

    private async Task CompleteGameAsync(GameSession session, CancellationToken ct)
    {
        _logger.LogInformation("[Game] Completing session {SessionId}", session.Id);

        session.Status = GameSessionStatus.COMPLETED.ToString();
        session.UpdatedAt = DateTimeOffset.UtcNow;

        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == session.MatchId, ct);
        if (match == null) return;

        var rounds = await _db.GameRounds
            .Where(r => r.SessionId == session.Id)
            .ToListAsync(ct);

        var userAScore = rounds
            .Where(r => r.GuesserUserId == match.UserAId)
            .Sum(r => r.Score ?? 0);

        var userBScore = rounds
            .Where(r => r.GuesserUserId == match.UserBId)
            .Sum(r => r.Score ?? 0);

        // Generate AI insight
        var context = await BuildGameContextAsync(session, match, session.CurrentRound, ct);
        var agent = _agentFactory.GetAgent(session.GameType);

        var roundResults = rounds.Select(r => new RoundResult
        {
            RoundNumber = r.RoundNumber,
            GuesserUserId = r.GuesserUserId,
            Score = r.Score,
            TotalQuestions = 3
        }).ToList();

        var insight = await agent.GenerateInsightAsync(context, roundResults, ct);

        var result = new GameResult
        {
            SessionId = session.Id,
            MatchId = session.MatchId,
            GameType = session.GameType,
            UserAScore = userAScore,
            UserBScore = userBScore,
            WinnerUserId = userAScore > userBScore
                ? match.UserAId
                : (userBScore > userAScore ? match.UserBId : null),
            AiInsight = insight
        };

        _db.GameResults.Add(result);

        var analytic = await _db.GameAnalytics
            .FirstOrDefaultAsync(a => a.SessionId == session.Id, ct);

        if (analytic != null)
            analytic.Completed = true;

        await _db.SaveChangesAsync(ct);

        // Record game outcome for analytics
        await _gameOutcomeService.RecordOutcomeAsync(session.Id, new GameOutcomeData
        {
            TotalRounds = session.TotalRounds,
            CompletedRounds = rounds.Count(r => r.CompletedAt != null),
            InitiatorScore = rounds.Where(r => r.GuesserUserId == session.InitiatorUserId).Sum(r => r.Score ?? 0),
            PartnerScore = rounds.Where(r => r.GuesserUserId != session.InitiatorUserId).Sum(r => r.Score ?? 0),
            AverageResponseTimeMs = 0, // Could be computed from round timestamps if needed
            CompletionStatus = "COMPLETED"
        }, ct);

        await AddSystemMessageAsync(
            session.MatchId,
            session.InitiatorUserId,
            "🏁 Game completed — tap to see results",
            new
            {
                type = "GAME_COMPLETED",
                sessionId = session.Id,
                gameType = session.GameType,
                status = session.Status,
                winnerUserId = result.WinnerUserId
            },
            ct
        );

        _logger.LogInformation("[Game] Session {SessionId} completed, winner: {Winner}",
            session.Id, result.WinnerUserId?.ToString() ?? "tie");
    }

    private async Task<GameContext> BuildGameContextAsync(
        GameSession session,
        Match match,
        int roundNumber,
        CancellationToken ct)
    {
        var guesserUserId = roundNumber == 1
            ? session.InitiatorUserId
            : (match.UserAId == session.InitiatorUserId ? match.UserBId : match.UserAId);

        var targetUserId = guesserUserId == match.UserAId ? match.UserBId : match.UserAId;

        var userAVector = await LoadUserVectorAsync(match.UserAId, ct);
        var userBVector = await LoadUserVectorAsync(match.UserBId, ct);

        // Load pair context for personalization
        var pairContext = await _aiProfileService.GetPairContextAsync(match.UserAId, match.UserBId, ct);

        // Determine difficulty based on pillar alignment
        var difficulty = DetermineDifficulty(pairContext);

        // Determine tone based on both users' preferences
        var tone = DetermineTone(pairContext);

        // Calculate intent alignment
        var intentAlignment = pairContext?.IntentAlignment ?? 0.5;

        // Determine match bucket
        var bucket = DetermineMatchBucket(pairContext);

        _logger.LogInformation("[Game] Context built for session {SessionId}: Difficulty={Difficulty}, Tone={Tone}, Bucket={Bucket}",
            session.Id, difficulty, tone, bucket);

        return new GameContext
        {
            SessionId = session.Id,
            MatchId = session.MatchId,
            UserAId = match.UserAId,
            UserBId = match.UserBId,
            CurrentRound = roundNumber,
            GuesserUserId = guesserUserId,
            TargetUserId = targetUserId,
            UserAVector = userAVector,
            UserBVector = userBVector,
            PairContext = pairContext,
            Difficulty = difficulty,
            Tone = tone,
            IntentAlignment = intentAlignment,
            Bucket = bucket
        };
    }

    private GameDifficulty DetermineDifficulty(PairContext? pairContext)
    {
        if (pairContext == null)
            return GameDifficulty.MEDIUM;

        var alignedCount = pairContext.AlignedPillars.Count;

        // High pillar alignment = harder questions (they already know each other)
        if (alignedCount >= 3)
            return GameDifficulty.HARD;

        // Medium alignment
        if (alignedCount >= 1)
            return GameDifficulty.MEDIUM;

        // Low alignment = easier questions to help them learn
        return GameDifficulty.EASY;
    }

    private GameTone DetermineTone(PairContext? pairContext)
    {
        if (pairContext == null)
            return GameTone.BALANCED;

        var userTone = pairContext.UserProfile.ConversationTone;
        var candidateTone = pairContext.CandidateProfile.ConversationTone;

        // Both playful = playful
        if (userTone == "playful" && candidateTone == "playful")
            return GameTone.PLAYFUL;

        // Either thoughtful/calm = thoughtful
        if (userTone == "thoughtful" || candidateTone == "thoughtful" ||
            userTone == "calm" || candidateTone == "calm")
            return GameTone.THOUGHTFUL;

        return GameTone.BALANCED;
    }

    private MatchBucketType DetermineMatchBucket(PairContext? pairContext)
    {
        if (pairContext == null)
            return MatchBucketType.EXPLORER;

        var alignedPillars = pairContext.AlignedPillars.Select(p => p.Pillar).ToHashSet();
        var sharedTagCount = pairContext.SharedTags.Count;

        // Core fit: strong values/communication alignment
        if ((alignedPillars.Contains("Values") || alignedPillars.Contains("Communication")) &&
            pairContext.IntentAlignment >= 0.8)
            return MatchBucketType.CORE_FIT;

        // Lifestyle fit: lifestyle/stability alignment
        if (alignedPillars.Contains("Lifestyle") || alignedPillars.Contains("Stability"))
            return MatchBucketType.LIFESTYLE_FIT;

        // Conversation fit: good shared tags, tone match
        if (sharedTagCount >= 3 || pairContext.ToneAlignment == "matched")
            return MatchBucketType.CONVERSATION_FIT;

        return MatchBucketType.EXPLORER;
    }

    private async Task<UserVectorData?> LoadUserVectorAsync(int userId, CancellationToken ct)
    {
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        var tags = await _db.UserVectorTags
            .Where(t => t.UserId == userId)
            .Select(t => t.Tag)
            .Take(10)
            .ToListAsync(ct);

        var lifestyle = await _db.UserOptionalFields
            .Where(f => f.UserId == userId)
            .ToDictionaryAsync(f => f.Key, f => f.Value ?? "", ct);

        if (vector == null || profile == null)
            return null;

        var pillars = JsonSerializer.Deserialize<Dictionary<string, double>>(vector.PillarScoresJson) ?? new();
        var vectorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(vector.VectorJson) ?? new();

        var pulse = new Dictionary<string, double>();
        if (vectorData.TryGetValue("pulse", out var pulseElement))
        {
            pulse = JsonSerializer.Deserialize<Dictionary<string, double>>(pulseElement.GetRawText()) ?? new();
        }

        return new UserVectorData
        {
            UserId = userId,
            Age = profile.Age,
            Tags = tags,
            Pillars = pillars,
            Pulse = pulse,
            Lifestyle = lifestyle
        };
    }

    private async Task<Guid?> GetThreadIdForMatchAsync(Guid matchId, CancellationToken ct)
    {
        return await _db.ChatThreads
            .Where(t => t.MatchId == matchId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task AddSystemMessageAsync(
        Guid matchId,
        int actorUserId,
        string body,
        object meta,
        CancellationToken ct)
    {
        var threadId = await GetThreadIdForMatchAsync(matchId, ct);

        if (threadId == null)
        {
            var newThread = new WovenBackend.data.Entities.Moments.ChatThread
            {
                MatchId = matchId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _db.ChatThreads.Add(newThread);
            await _db.SaveChangesAsync(ct);
            threadId = newThread.Id;
        }

        var msg = new WovenBackend.data.Entities.Moments.ChatMessage
        {
            ThreadId = threadId.Value,
            SenderUserId = actorUserId,
            Body = body,
            MessageType = "SYSTEM",
            MetaJson = JsonSerializer.Serialize(meta),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.ChatMessages.Add(msg);

        var thread = await _db.ChatThreads.FirstAsync(t => t.Id == threadId.Value, ct);
        thread.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    private async Task AddGameChatMessageAsync(
        Guid matchId,
        int senderUserId,
        string body,
        object meta,
        CancellationToken ct)
    {
        var threadId = await _db.ChatThreads
            .Where(t => t.MatchId == matchId)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (threadId == Guid.Empty)
        {
            var thread = new WovenBackend.data.Entities.Moments.ChatThread
            {
                MatchId = matchId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.ChatThreads.Add(thread);
            await _db.SaveChangesAsync(ct);
            threadId = thread.Id;
        }

        var msg = new WovenBackend.data.Entities.Moments.ChatMessage
        {
            ThreadId = threadId,
            SenderUserId = senderUserId,
            Body = body,
            MessageType = "GAME",
            MetaJson = JsonSerializer.Serialize(meta),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.ChatMessages.Add(msg);

        var threadToUpdate = await _db.ChatThreads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
        if (threadToUpdate != null)
            threadToUpdate.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }
}

// DTOs
public class GameSessionDto
{
    public Guid SessionId { get; set; }
    public Guid MatchId { get; set; }
    public string GameType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public class GameRoundDto
{
    public int RoundNumber { get; set; }
    public int TotalRounds { get; set; }
    public List<QuestionData> Questions { get; set; } = new();
    public int TimeLimit { get; set; }
    public bool IsGuesser { get; set; }
    public bool HasAnswered { get; set; }
    public bool WaitingForOther { get; set; }
}

public class RoundResultDto
{
    public int RoundNumber { get; set; }
    public int? Score { get; set; }
    public int TotalQuestions { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class GameResultDto
{
    public Guid SessionId { get; set; }
    public string GameType { get; set; } = string.Empty;
    public int UserAId { get; set; }
    public int UserBId { get; set; }
    public int UserAScore { get; set; }
    public int UserBScore { get; set; }
    public int? WinnerUserId { get; set; }
    public string AiInsight { get; set; } = string.Empty;
}

public class GameAvailabilityDto
{
    public bool Available { get; set; }
    public int GamesRemaining { get; set; }
    public string? Reason { get; set; }
}