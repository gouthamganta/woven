using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WovenBackend.Data.Entities.Games;

[Table("game_sessions")]
public class GameSession
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("match_id")]
    public Guid MatchId { get; set; }

    [Column("game_type")]
    [MaxLength(50)]
    public string GameType { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "PENDING"; // PENDING, ACTIVE, COMPLETED, EXPIRED, REJECTED

    [Column("initiator_user_id")]
    public int InitiatorUserId { get; set; }

    [Column("round_data", TypeName = "jsonb")]
    public string RoundDataJson { get; set; } = "{}";

    [Column("current_round")]
    public int CurrentRound { get; set; } = 1;

    [Column("total_rounds")]
    public int TotalRounds { get; set; } = 2;

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Game session metadata including difficulty, tone, bucket, intent alignment.
    /// </summary>
    [Column("metadata_json", TypeName = "jsonb")]
    public string? MetadataJson { get; set; }
}

[Table("game_rounds")]
public class GameRound
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Column("round_number")]
    public int RoundNumber { get; set; }

    [Column("guesser_user_id")]
    public int GuesserUserId { get; set; }

    [Column("target_user_id")]
    public int TargetUserId { get; set; }

    [Column("questions_json", TypeName = "jsonb")]
    public string QuestionsJson { get; set; } = "[]";

    [Column("answers_json", TypeName = "jsonb")]
    public string? AnswersJson { get; set; }

    [Column("target_answers_json", TypeName = "jsonb")]
    public string? TargetAnswersJson { get; set; }

    [Column("score")]
    public int? Score { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("game_results")]
public class GameResult
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Column("match_id")]
    public Guid MatchId { get; set; }

    [Column("game_type")]
    [MaxLength(50)]
    public string GameType { get; set; } = string.Empty;

    [Column("user_a_score")]
    public int? UserAScore { get; set; }

    [Column("user_b_score")]
    public int? UserBScore { get; set; }

    [Column("winner_user_id")]
    public int? WinnerUserId { get; set; }

    [Column("result_data", TypeName = "jsonb")]
    public string ResultDataJson { get; set; } = "{}";

    [Column("ai_insight")]
    public string? AiInsight { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("game_analytics")]
public class GameAnalytic
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("session_id")]
    public Guid SessionId { get; set; }

    [Column("match_id")]
    public Guid MatchId { get; set; }

    [Column("game_type")]
    [MaxLength(50)]
    public string GameType { get; set; } = string.Empty;

    [Column("completed")]
    public bool Completed { get; set; } = false;

    [Column("messages_before_game")]
    public int MessagesBeforeGame { get; set; }

    [Column("messages_1h_after")]
    public int? Messages1hAfter { get; set; }

    [Column("messages_24h_after")]
    public int? Messages24hAfter { get; set; }

    [Column("match_still_active")]
    public bool? MatchStillActive { get; set; }

    [Column("recorded_at")]
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
