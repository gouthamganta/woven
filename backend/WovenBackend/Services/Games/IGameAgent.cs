using System.Text.Json;
using WovenBackend.Services;

namespace WovenBackend.Services.Games;

public interface IGameAgent
{
    Task<GameRoundData> GenerateRoundAsync(GameContext context, CancellationToken ct = default);
    Task<string> GenerateInsightAsync(GameContext context, List<RoundResult> rounds, CancellationToken ct = default);
}

/// <summary>
/// Difficulty level for game questions.
/// </summary>
public enum GameDifficulty
{
    EASY,    // Light, surface-level questions (high pillar alignment)
    MEDIUM,  // Lifestyle + values questions
    HARD     // Deep, insightful questions (low pillar alignment)
}

/// <summary>
/// Tone for game content.
/// </summary>
public enum GameTone
{
    PLAYFUL,    // Fun, cheeky, light
    BALANCED,   // Mix of fun and depth
    THOUGHTFUL  // Introspective, deeper
}

/// <summary>
/// Match bucket classification for game calibration.
/// </summary>
public enum MatchBucketType
{
    CORE_FIT,        // Strong overall match
    LIFESTYLE_FIT,   // Lifestyle-focused match
    CONVERSATION_FIT,// Communication-focused match
    EXPLORER         // Still getting to know each other
}

public class GameContext
{
    public Guid SessionId { get; set; }
    public Guid MatchId { get; set; }
    public int UserAId { get; set; }
    public int UserBId { get; set; }
    public int CurrentRound { get; set; }
    public int GuesserUserId { get; set; }
    public int TargetUserId { get; set; }
    public UserVectorData? UserAVector { get; set; }
    public UserVectorData? UserBVector { get; set; }
    public List<ChatMessageData>? RecentMessages { get; set; }

    // New personalization fields
    public PairContext? PairContext { get; set; }
    public MatchBucketType Bucket { get; set; } = MatchBucketType.EXPLORER;
    public double IntentAlignment { get; set; } = 0.5;
    public GameDifficulty Difficulty { get; set; } = GameDifficulty.MEDIUM;
    public GameTone Tone { get; set; } = GameTone.BALANCED;
}

public class GameRoundData
{
    public List<QuestionData> Questions { get; set; } = new();
    public int TimeLimit { get; set; } = 90; // seconds
}

public class QuestionData
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<OptionData> Options { get; set; } = new();
    public string Difficulty { get; set; } = "MEDIUM";
    public string Category { get; set; } = string.Empty;
}

public class OptionData
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

public class RoundResult
{
    public int RoundNumber { get; set; }
    public int GuesserUserId { get; set; }
    public int? Score { get; set; }
    public int TotalQuestions { get; set; }
}

public class UserVectorData
{
    public int UserId { get; set; }
    public int Age { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, double> Pillars { get; set; } = new();
    public Dictionary<string, double> Pulse { get; set; } = new();
    public Dictionary<string, string> Lifestyle { get; set; } = new();
}

public class ChatMessageData
{
    public int SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}