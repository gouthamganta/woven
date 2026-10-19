using System.Text.Json;
using WovenBackend.Services;

namespace WovenBackend.Services.Onboarding;

public interface IGameScoringService
{
    Task<Dictionary<string, PillarScoreResult>> ScoreAnswersAsync(
        List<GameAnswerForScoring> answers,
        CancellationToken ct = default);
}

public class GameScoringService : IGameScoringService
{
    private readonly IOpenAiClient _openAi;
    private readonly ILogger<GameScoringService> _logger;

    public GameScoringService(IOpenAiClient openAi, ILogger<GameScoringService> logger)
    {
        _openAi = openAi;
        _logger = logger;
    }

    public async Task<Dictionary<string, PillarScoreResult>> ScoreAnswersAsync(
        List<GameAnswerForScoring> answers,
        CancellationToken ct = default)
    {
        var prompt = BuildScoringPrompt(answers);

        _logger.LogInformation("[GameScoring] Sending {Count} answers to LLM for interpretation", answers.Count);

        var messages = new[]
        {
            new OpenAiMessage("system", GetSystemPrompt()),
            new OpenAiMessage("user", prompt)
        };

        var response = await _openAi.ChatAsync(new OpenAiRequest(
            Model: "gpt-4o-mini",
            Messages: messages,
            Temperature: 0.3f, // Lower temperature for consistent scoring
            MaxTokens: 1000,
            Purpose: "game_scoring"
        ), ct);

        var scores = ParseScoresFromResponse(response.Content);

        // Validate scores are in valid range
        ValidateScores(scores);

        _logger.LogInformation("[GameScoring] Received scores for {Count} pillars", scores.Count);

        return scores;
    }

    private string GetSystemPrompt()
    {
        return @"You are a psychometric scoring assistant for a dating app.

Your job: Analyze user answers to personality/preference questions and output pillar scores (0.0-1.0) with user-friendly interpretations.

The 8 pillars are:
1. **Energy** (Introvert 0.0 ↔ 1.0 Extrovert) - Social energy preference
2. **Stability** (Spontaneous 0.0 ↔ 1.0 Structured) - Need for routine/planning
3. **Ambition** (Relaxed 0.0 ↔ 1.0 Driven) - Career/achievement orientation
4. **Curiosity** (Practical 0.0 ↔ 1.0 Exploratory) - Openness to new experiences
5. **Communication** (Reserved 0.0 ↔ 1.0 Expressive) - Comfort with emotional sharing
6. **Lifestyle** (Homebody 0.0 ↔ 1.0 Active) - Activity preference spectrum
7. **Values** (Individualist 0.0 ↔ 1.0 Collectivist) - Relationship priority vs independence
8. **Affection** (Independent 0.0 ↔ 1.0 Affectionate) - Physical/verbal affection comfort

Output ONLY valid JSON:
{
  ""Energy"": {
    ""score"": 0.75,
    ""interpretation"": ""You get energized by being around people""
  },
  ""Stability"": {
    ""score"": 0.40,
    ""interpretation"": ""You prefer spontaneity over rigid plans""
  },
  ...
}

**Interpretation guidelines:**
- Write in second person (""You..."")
- Keep it warm, positive, non-judgmental
- 8-12 words max
- Reference their actual choices when possible
- No jargon or clinical terms

Scores must be 0.0-1.0. No explanations outside JSON.";
    }

    private string BuildScoringPrompt(List<GameAnswerForScoring> answers)
    {
        var lines = new List<string>
        {
            "User completed onboarding games. Analyze their answers and score each pillar.\n"
        };

        foreach (var answer in answers)
        {
            lines.Add($"## {answer.Pillar} Pillar - {answer.GameType} game");
            lines.Add($"Question: {answer.QuestionText}");
            lines.Add($"Answer: {FormatAnswer(answer)}");
            lines.Add("");
        }

        lines.Add("Return JSON with scores for all 8 pillars.");

        return string.Join("\n", lines);
    }

    private string FormatAnswer(GameAnswerForScoring answer)
    {
        return answer.GameType.ToLower() switch
        {
            "slider" => $"Value: {answer.AnswerJson.RootElement.GetProperty("value").GetInt32()}/100",
            "ranking" => FormatRankingAnswer(answer.AnswerJson),
            "swipe" => FormatSwipeAnswer(answer.AnswerJson),
            "selection" => FormatSelectionAnswer(answer.AnswerJson),
            _ => answer.AnswerJson.RootElement.ToString()
        };
    }

    private string FormatRankingAnswer(JsonDocument answer)
    {
        if (answer.RootElement.TryGetProperty("ranked", out var ranked))
        {
            var items = JsonSerializer.Deserialize<string[]>(ranked.GetRawText()) ?? Array.Empty<string>();
            return $"Ranked: {string.Join(" > ", items)}";
        }
        return answer.RootElement.ToString();
    }

    private string FormatSwipeAnswer(JsonDocument answer)
    {
        if (answer.RootElement.TryGetProperty("responses", out var responses))
        {
            var ratings = JsonSerializer.Deserialize<List<SwipeResponse>>(responses.GetRawText()) ?? new();
            var lines = ratings.Select(r => $"  • {r.Option}: {r.Rating}/5");
            return $"Ratings:\n{string.Join("\n", lines)}";
        }
        return answer.RootElement.ToString();
    }

    private string FormatSelectionAnswer(JsonDocument answer)
    {
        if (answer.RootElement.TryGetProperty("selected", out var selected))
        {
            var items = JsonSerializer.Deserialize<string[]>(selected.GetRawText()) ?? Array.Empty<string>();
            return $"Selected: {string.Join(", ", items)}";
        }
        return answer.RootElement.ToString();
    }

    private Dictionary<string, PillarScoreResult> ParseScoresFromResponse(string responseContent)
    {
        try
        {
            // Extract JSON from response (in case LLM wraps it in markdown)
            var jsonStart = responseContent.IndexOf('{');
            var jsonEnd = responseContent.LastIndexOf('}') + 1;

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = responseContent.Substring(jsonStart, jsonEnd - jsonStart);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, PillarScoreResult>>(jsonContent);

                if (parsed != null)
                {
                    return parsed;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GameScoring] Failed to parse LLM response as JSON");
        }

        // Fallback: neutral scores
        _logger.LogWarning("[GameScoring] Using neutral fallback scores");
        return new Dictionary<string, PillarScoreResult>
        {
            ["Energy"] = new(0.5, "Neutral (parsing failed)"),
            ["Stability"] = new(0.5, "Neutral (parsing failed)"),
            ["Ambition"] = new(0.5, "Neutral (parsing failed)"),
            ["Curiosity"] = new(0.5, "Neutral (parsing failed)"),
            ["Communication"] = new(0.5, "Neutral (parsing failed)"),
            ["Lifestyle"] = new(0.5, "Neutral (parsing failed)"),
            ["Values"] = new(0.5, "Neutral (parsing failed)"),
            ["Affection"] = new(0.5, "Neutral (parsing failed)")
        };
    }

    private void ValidateScores(Dictionary<string, PillarScoreResult> scores)
    {
        var requiredPillars = new[]
        {
            "Energy", "Stability", "Ambition", "Curiosity",
            "Communication", "Lifestyle", "Values", "Affection"
        };

        foreach (var pillar in requiredPillars)
        {
            if (!scores.ContainsKey(pillar))
            {
                _logger.LogWarning("[GameScoring] Missing pillar {Pillar}, adding neutral score", pillar);
                scores[pillar] = new PillarScoreResult(0.5, "Missing from LLM response");
            }
            else if (scores[pillar].Score < 0.0 || scores[pillar].Score > 1.0)
            {
                _logger.LogWarning(
                    "[GameScoring] Invalid score {Score} for {Pillar}, clamping to 0-1",
                    scores[pillar].Score, pillar);

                var clamped = Math.Clamp(scores[pillar].Score, 0.0, 1.0);
                scores[pillar] = new PillarScoreResult(clamped, scores[pillar].Interpretation);
            }
        }
    }

    private record SwipeResponse(string Option, int Rating);
}

public record GameAnswerForScoring(
    Guid QuestionId,
    string Pillar,
    string GameType,
    string QuestionText,
    JsonDocument AnswerJson
);

public record PillarScoreResult(
    double Score,
    string Interpretation
);
