using System.Text.Json;

namespace WovenBackend.Data.Entities;

/// <summary>
/// Validated question from the question bank (IPIP-NEO, Schwartz Values, etc.)
/// Users see a random subset of these questions to prevent gaming.
/// </summary>
public class FoundationalQuestion
{
    public Guid Id { get; set; }

    /// <summary>
    /// Which of the 8 pillars this question measures
    /// (Lifestyle, Energy, Values, Communication, Ambition, Stability, Curiosity, Affection)
    /// </summary>
    public string Pillar { get; set; } = string.Empty;

    /// <summary>
    /// Game type: slider, ranking, swipe, selection
    /// </summary>
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// Question data (JSON) - structure varies by game type:
    /// - Slider: { question, leftLabel, rightLabel }
    /// - Ranking: { question, items: [] }
    /// - Swipe: { question, options: [{text}] }
    /// - Selection: { question, subtitle, maxSelect, options: [{label, value}] }
    /// </summary>
    public JsonDocument QuestionData { get; set; } = null!;

    /// <summary>
    /// Source instrument (e.g., "IPIP-NEO", "Schwartz Values", "Love Languages")
    /// </summary>
    public string SourceInstrument { get; set; } = string.Empty;

    /// <summary>
    /// Full academic citation
    /// </summary>
    public string Citation { get; set; } = string.Empty;

    /// <summary>
    /// Original validated item text (before adaptation to game format)
    /// </summary>
    public string? OriginalItem { get; set; }

    /// <summary>
    /// Whether this question is currently active in the pool
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Selection weight (higher = more likely to be shown)
    /// </summary>
    public int Weight { get; set; } = 100;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Tracks which questions a user has seen (to avoid repetition)
/// </summary>
public class UserQuestionHistory
{
    public int UserId { get; set; }
    public Guid QuestionId { get; set; }
    public DateTime ShownAt { get; set; }
}

/// <summary>
/// User's raw answer to a game question + LLM interpretation
/// </summary>
public class UserGameResponse
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public Guid QuestionId { get; set; }
    public string Pillar { get; set; } = string.Empty;
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// User's answer (JSON) - varies by game type
    /// </summary>
    public JsonDocument AnswerJson { get; set; } = null!;

    /// <summary>
    /// LLM-interpreted score (0.0-1.0)
    /// </summary>
    public decimal? LlmInterpretedScore { get; set; }

    /// <summary>
    /// LLM's reasoning (optional, for debugging)
    /// </summary>
    public string? LlmInterpretation { get; set; }

    public DateTime RespondedAt { get; set; }
}
