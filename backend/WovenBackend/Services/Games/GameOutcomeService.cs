using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities.Games;

namespace WovenBackend.Services.Games;

public interface IGameOutcomeService
{
    Task RecordOutcomeAsync(Guid sessionId, GameOutcomeData outcome, CancellationToken ct = default);
    Task<GameAnalyticsDto> GetGameAnalyticsAsync(int userId, CancellationToken ct = default);
    Task<List<GameOutcome>> GetRecentOutcomesAsync(int userId, int limit, CancellationToken ct = default);
}

public class GameOutcomeService : IGameOutcomeService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<GameOutcomeService> _logger;

    public GameOutcomeService(WovenDbContext db, ILogger<GameOutcomeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordOutcomeAsync(Guid sessionId, GameOutcomeData outcome, CancellationToken ct = default)
    {
        _logger.LogInformation("[GameOutcome] Recording outcome for session {SessionId}, status={Status}",
            sessionId, outcome.CompletionStatus);

        // Check if outcome already exists
        var existing = await _db.GameOutcomes.AnyAsync(o => o.SessionId == sessionId, ct);
        if (existing)
        {
            _logger.LogWarning("[GameOutcome] Outcome already exists for session {SessionId}", sessionId);
            return;
        }

        // Load session for metadata
        var session = await _db.GameSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null)
        {
            _logger.LogWarning("[GameOutcome] Session {SessionId} not found", sessionId);
            return;
        }

        // Parse session metadata
        var metadata = ParseSessionMetadata(session.MetadataJson);

        // Load match to get partner ID
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == session.MatchId, ct);
        if (match == null)
        {
            _logger.LogWarning("[GameOutcome] Match {MatchId} not found for session", session.MatchId);
            return;
        }

        var partnerUserId = match.UserAId == session.InitiatorUserId ? match.UserBId : match.UserAId;

        var gameOutcome = new GameOutcome
        {
            SessionId = sessionId,
            GameType = session.GameType,
            InitiatorUserId = session.InitiatorUserId,
            PartnerUserId = partnerUserId,
            MatchId = session.MatchId,
            Difficulty = metadata.Difficulty ?? "MEDIUM",
            Tone = metadata.Tone ?? "BALANCED",
            Bucket = metadata.Bucket ?? "EXPLORER",
            IntentAlignment = metadata.IntentAlignment,
            TotalRounds = outcome.TotalRounds,
            CompletedRounds = outcome.CompletedRounds,
            InitiatorScore = outcome.InitiatorScore,
            PartnerScore = outcome.PartnerScore,
            AverageResponseTimeMs = outcome.AverageResponseTimeMs,
            CompletionStatus = outcome.CompletionStatus,
            UserFeedback = outcome.UserFeedback,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.GameOutcomes.Add(gameOutcome);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[GameOutcome] Recorded outcome {Id} for session {SessionId}",
            gameOutcome.Id, sessionId);
    }

    public async Task<GameAnalyticsDto> GetGameAnalyticsAsync(int userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[GameOutcome] Getting analytics for user {UserId}", userId);

        var outcomes = await _db.GameOutcomes
            .Where(o => o.InitiatorUserId == userId || o.PartnerUserId == userId)
            .ToListAsync(ct);

        if (outcomes.Count == 0)
        {
            return new GameAnalyticsDto
            {
                TotalGames = 0,
                CompletedGames = 0,
                AbandonedGames = 0,
                AverageScore = 0,
                WinRate = 0
            };
        }

        var completed = outcomes.Where(o => o.CompletionStatus == "COMPLETED").ToList();
        var abandoned = outcomes.Where(o => o.CompletionStatus == "ABANDONED").ToList();

        // Calculate win rate
        var wins = completed.Count(o =>
        {
            if (o.InitiatorUserId == userId)
                return o.InitiatorScore > o.PartnerScore;
            return o.PartnerScore > o.InitiatorScore;
        });

        // Stats by difficulty
        var byDifficulty = completed
            .GroupBy(o => o.Difficulty)
            .ToDictionary(g => g.Key, g => new DifficultyStats
            {
                GamesPlayed = g.Count(),
                CompletionRate = (double)g.Count(o => o.CompletionStatus == "COMPLETED") / g.Count(),
                AverageScore = g.Average(o => o.InitiatorUserId == userId ? o.InitiatorScore : o.PartnerScore)
            });

        // Stats by tone
        var byTone = completed
            .GroupBy(o => o.Tone)
            .ToDictionary(g => g.Key, g => new ToneStats
            {
                GamesPlayed = g.Count(),
                AverageScore = g.Average(o => o.InitiatorUserId == userId ? o.InitiatorScore : o.PartnerScore)
            });

        // Stats by game type
        var byGameType = outcomes
            .GroupBy(o => o.GameType)
            .ToDictionary(g => g.Key, g => new GameTypeStats
            {
                GamesPlayed = g.Count(),
                CompletedGames = g.Count(o => o.CompletionStatus == "COMPLETED"),
                AverageScore = g.Where(o => o.CompletionStatus == "COMPLETED")
                    .Select(o => o.InitiatorUserId == userId ? o.InitiatorScore : o.PartnerScore)
                    .DefaultIfEmpty(0)
                    .Average()
            });

        // Find best performing settings
        var bestDifficulty = byDifficulty.OrderByDescending(kv => kv.Value.AverageScore).FirstOrDefault().Key ?? "MEDIUM";
        var bestTone = byTone.OrderByDescending(kv => kv.Value.AverageScore).FirstOrDefault().Key ?? "BALANCED";

        return new GameAnalyticsDto
        {
            TotalGames = outcomes.Count,
            CompletedGames = completed.Count,
            AbandonedGames = abandoned.Count,
            AverageScore = completed.Count > 0
                ? completed.Average(o => o.InitiatorUserId == userId ? o.InitiatorScore : o.PartnerScore)
                : 0,
            WinRate = completed.Count > 0 ? (double)wins / completed.Count : 0,
            ByDifficulty = byDifficulty,
            ByTone = byTone,
            ByGameType = byGameType,
            BestPerformingDifficulty = bestDifficulty,
            BestPerformingTone = bestTone
        };
    }

    public async Task<List<GameOutcome>> GetRecentOutcomesAsync(int userId, int limit, CancellationToken ct = default)
    {
        return await _db.GameOutcomes
            .Where(o => o.InitiatorUserId == userId || o.PartnerUserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    private SessionMetadata ParseSessionMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SessionMetadata();

        try
        {
            return JsonSerializer.Deserialize<SessionMetadata>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SessionMetadata();
        }
        catch
        {
            return new SessionMetadata();
        }
    }

    private class SessionMetadata
    {
        public string? Difficulty { get; set; }
        public string? Tone { get; set; }
        public string? Bucket { get; set; }
        public double IntentAlignment { get; set; } = 0.5;
    }
}

#region DTOs

public class GameOutcomeData
{
    public int TotalRounds { get; set; }
    public int CompletedRounds { get; set; }
    public int InitiatorScore { get; set; }
    public int PartnerScore { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public string CompletionStatus { get; set; } = "COMPLETED";
    public string? UserFeedback { get; set; }
}

public class GameAnalyticsDto
{
    public int TotalGames { get; set; }
    public int CompletedGames { get; set; }
    public int AbandonedGames { get; set; }
    public double AverageScore { get; set; }
    public double WinRate { get; set; }
    public Dictionary<string, DifficultyStats> ByDifficulty { get; set; } = new();
    public Dictionary<string, ToneStats> ByTone { get; set; } = new();
    public Dictionary<string, GameTypeStats> ByGameType { get; set; } = new();
    public string BestPerformingDifficulty { get; set; } = "MEDIUM";
    public string BestPerformingTone { get; set; } = "BALANCED";
}

public class DifficultyStats
{
    public int GamesPlayed { get; set; }
    public double CompletionRate { get; set; }
    public double AverageScore { get; set; }
}

public class ToneStats
{
    public int GamesPlayed { get; set; }
    public double AverageScore { get; set; }
}

public class GameTypeStats
{
    public int GamesPlayed { get; set; }
    public int CompletedGames { get; set; }
    public double AverageScore { get; set; }
}

#endregion
