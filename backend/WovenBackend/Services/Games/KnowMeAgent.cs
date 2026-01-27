using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WovenBackend.Services.Games;

public class KnowMeAgent : IGameAgent
{
    private readonly IOpenAiResilientClient _openAi;
    private readonly ILogger<KnowMeAgent> _logger;

    public KnowMeAgent(
        IOpenAiResilientClient openAi,
        ILogger<KnowMeAgent> logger)
    {
        _openAi = openAi;
        _logger = logger;
    }

    public async Task<GameRoundData> GenerateRoundAsync(GameContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("[KnowMe] Generating round {Round} for session {SessionId}. Difficulty={Difficulty}, Tone={Tone}",
            context.CurrentRound, context.SessionId, context.Difficulty, context.Tone);

        var targetVector = context.GuesserUserId == context.UserAId
            ? context.UserBVector
            : context.UserAVector;

        if (targetVector == null)
        {
            _logger.LogWarning("[KnowMe] Missing target vector, using fallback");
            return GenerateFallbackRound();
        }

        // Use enhanced prompt with full context if available
        var prompt = context.PairContext != null
            ? BuildPrompt(context)
            : BuildPrompt(targetVector);

        var response = await CallOpenAiAsync(prompt, ct);

        if (response != null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<OpenAiQuestionsResponse>(response,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed?.Questions != null && parsed.Questions.Count >= 3)
                {
                    return new GameRoundData
                    {
                        Questions = parsed.Questions.Take(3).Select(q => new QuestionData
                        {
                            Id = q.Id ?? Guid.NewGuid().ToString(),
                            Text = q.Text ?? "",
                            Options = q.Options?.Select(o => new OptionData
                            {
                                Id = o.Id ?? "",
                                Text = o.Text ?? "",
                                IsCorrect = o.IsCorrect
                            }).ToList() ?? new(),
                            Difficulty = q.Difficulty ?? "MEDIUM",
                            Category = q.Category ?? "lifestyle"
                        }).ToList(),
                        TimeLimit = 90
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[KnowMe] Failed to parse OpenAI response");
            }
        }

        return GenerateFallbackRound();
    }

    public async Task<string> GenerateInsightAsync(
        GameContext context,
        List<RoundResult> rounds,
        CancellationToken ct = default)
    {
        var userAResult = rounds.FirstOrDefault(r => r.GuesserUserId == context.UserAId);
        var userBResult = rounds.FirstOrDefault(r => r.GuesserUserId == context.UserBId);

        var userAScore = userAResult?.Score ?? 0;
        var userBScore = userBResult?.Score ?? 0;

        var prompt = $@"Two people played a guessing game about each other.

User A score: {userAScore}/3
User B score: {userBScore}/3

Generate a 1-2 sentence insight that's playful and insightful.

Examples:
- ""You both got 2/3 - reading each other pretty well already.""
- ""One of you is better at this than the other. Interesting.""
- ""Perfect scores! Either you're psychic or you actually listen.""

Return just the text, no JSON.";

        var response = await CallOpenAiAsync(prompt, ct, useJson: false);

        if (!string.IsNullOrWhiteSpace(response))
        {
            return response.Trim();
        }

        // Fallback
        if (userAScore == userBScore)
        {
            return userAScore >= 2
                ? "You both got it - seems like you're paying attention."
                : "Both missed some - might want to ask more questions.";
        }

        return userAScore > userBScore
            ? "One of you is reading the other better. Interesting dynamic."
            : "Looks like one person is easier to read than the other.";
    }

    private string BuildPrompt(UserVectorData target)
    {
        // Build basic target data
        var tagsStr = target.Tags.Count > 0
            ? string.Join(", ", target.Tags.Take(10))
            : "not specified";

        var energyLevel = target.Pillars.TryGetValue("Energy", out var energy) ? energy : 0.5;
        var socialLevel = target.Pulse.TryGetValue("socialCapacity", out var social) ? social : 0.5;

        var dietValue = target.Lifestyle.TryGetValue("diet", out var diet) ? diet : "not specified";
        var workoutValue = target.Lifestyle.TryGetValue("workout", out var workout) ? workout : "not specified";

        return BuildEnhancedPrompt(target, tagsStr, energyLevel, socialLevel, dietValue, workoutValue, null, GameDifficulty.MEDIUM, GameTone.BALANCED);
    }

    private string BuildPrompt(GameContext context)
    {
        var target = context.GuesserUserId == context.UserAId
            ? context.UserBVector
            : context.UserAVector;

        if (target == null)
            return BuildPrompt(new UserVectorData()); // Fallback

        var tagsStr = target.Tags.Count > 0
            ? string.Join(", ", target.Tags.Take(10))
            : "not specified";

        var energyLevel = target.Pillars.TryGetValue("Energy", out var energy) ? energy : 0.5;
        var socialLevel = target.Pulse.TryGetValue("socialCapacity", out var social) ? social : 0.5;

        var dietValue = target.Lifestyle.TryGetValue("diet", out var diet) ? diet : "not specified";
        var workoutValue = target.Lifestyle.TryGetValue("workout", out var workout) ? workout : "not specified";

        return BuildEnhancedPrompt(target, tagsStr, energyLevel, socialLevel, dietValue, workoutValue, context.PairContext, context.Difficulty, context.Tone);
    }

    private string BuildEnhancedPrompt(
        UserVectorData target,
        string tagsStr,
        double energyLevel,
        double socialLevel,
        string dietValue,
        string workoutValue,
        PairContext? pairContext,
        GameDifficulty difficulty,
        GameTone tone)
    {
        // Check if either profile has low data quality or uses cohort defaults
        // If either person has insufficient data, use exploratory approach for better experience
        var anyLowQuality = pairContext?.UserProfile?.DataQuality == DataQuality.LOW ||
                            pairContext?.CandidateProfile?.DataQuality == DataQuality.LOW;
        var anyUsedCohortDefaults = pairContext?.UserProfile?.UsedCohortDefaults == true ||
                                    pairContext?.CandidateProfile?.UsedCohortDefaults == true;
        var needsExploratoryApproach = anyLowQuality || anyUsedCohortDefaults;

        // Build target pillars section
        var targetPillars = target.Pillars.Count > 0
            ? string.Join(", ", target.Pillars.OrderByDescending(p => Math.Abs(p.Value - 0.5)).Take(3).Select(p => $"{p.Key} ({p.Value:F2})"))
            : "balanced across traits";

        // Build match context section if available
        var matchContextSection = "";
        if (pairContext != null)
        {
            var sharedTags = pairContext.SharedTags.Count > 0
                ? string.Join(", ", pairContext.SharedTags)
                : "exploring common ground";

            var alignedPillars = pairContext.AlignedPillars.Count > 0
                ? string.Join(", ", pairContext.AlignedPillars.Select(p => p.Pillar))
                : "still discovering similarities";

            var sharedHobbies = pairContext.SharedHobbies.Count > 0
                ? string.Join(", ", pairContext.SharedHobbies)
                : null;

            matchContextSection = $@"

MATCH CONTEXT (what these two people share):
- Shared interests: {sharedTags}
- Aligned values: {alignedPillars}
{(sharedHobbies != null ? $"- Shared hobbies: {sharedHobbies}" : "")}
- Intent alignment: {pairContext.GetIntentAlignmentDescription()}";
        }

        // Build difficulty guidance
        var difficultyGuidance = difficulty switch
        {
            GameDifficulty.EASY => "Light, surface-level questions that are fun and easy to guess. Focus on obvious interests and preferences.",
            GameDifficulty.MEDIUM => "Mix of lifestyle and values questions. Some require reading between the lines.",
            GameDifficulty.HARD => "Deep, insightful questions that reveal surprising things about the person. Not obvious from profile.",
            _ => "Balanced mix of difficulty levels."
        };

        // Build tone guidance
        var toneGuidance = tone switch
        {
            GameTone.PLAYFUL => "Fun, cheeky, light. Use playful language and keep it breezy.",
            GameTone.THOUGHTFUL => "Introspective, a bit deeper. Questions that make you think.",
            GameTone.BALANCED => "Conversational and warm. Mix of fun and meaningful.",
            _ => "Natural and engaging."
        };

        // Build data quality guidance
        var dataQualityNote = needsExploratoryApproach
            ? @"

⚠️ LIMITED DATA AVAILABLE - This person is new or has minimal profile data.
ADAPT YOUR APPROACH:
- Use broader, exploratory questions that work for most people
- Don't make strong assumptions about specific interests
- Phrase questions as 'tendencies' not facts (e.g., 'What might they enjoy?' vs 'What do they do?')
- Focus on general lifestyle patterns rather than niche interests
- Make questions that help them discover each other organically"
            : "";

        return $@"You are generating questions for a dating app guessing game called 'Know Me'.

TARGET PERSON (the one being guessed about):
- Age: {target.Age}
- Top traits: {targetPillars}
- Interests/Tags: {tagsStr}
- Energy level: {energyLevel:F2} (0=chill, 1=very active)
- Social capacity: {socialLevel:F2} (0=introverted, 1=very social)
- Diet: {dietValue}
- Workout: {workoutValue}
{matchContextSection}

GAME PARAMETERS:
- Difficulty: {difficulty} - {difficultyGuidance}
- Tone: {tone} - {toneGuidance}
{dataQualityNote}

REQUIREMENTS:
Generate 3 questions that MUST reference THIS person's actual interests/values:
1. Question 1 (EASY): Should be ~80% guessable from their visible data
2. Question 2 (MEDIUM): Requires reading between the lines (~50% guessable)
3. Question 3 (HARD): A surprising reveal based on their deeper traits (~30% guessable)

For each question:
- Create 4 options (A, B, C, D)
- One option MUST be correct based on their actual data
- Other 3 should be plausible alternatives
- Keep it fun and age-appropriate
- Avoid heavy topics (exes, trauma, etc.)

CRITICAL ANTI-GENERIC RULES:
NEVER use these banned question patterns:
- 'What's their weekend vibe?' (too generic)
- 'What's their coffee order?' (overused)
- 'How do they handle stress?' (too serious)
- 'Going out vs staying in?' (cliche)
- 'What's their ideal Saturday?' (boring)

INSTEAD: Reference THEIR actual interests/tags/hobbies. For example:
- If they like hiking: ""What's their go-to trail snack?""
- If they're creative: ""What would they spend 3 hours making?""
- If they're high energy: ""What sport would they try on a dare?""

Return ONLY valid JSON:
{{
  ""questions"": [
    {{
      ""id"": ""q1"",
      ""text"": ""[Question referencing their actual interests]"",
      ""options"": [
        {{""id"": ""a"", ""text"": ""..."", ""isCorrect"": false}},
        {{""id"": ""b"", ""text"": ""..."", ""isCorrect"": true}},
        {{""id"": ""c"", ""text"": ""..."", ""isCorrect"": false}},
        {{""id"": ""d"", ""text"": ""..."", ""isCorrect"": false}}
      ],
      ""difficulty"": ""EASY"",
      ""category"": ""lifestyle""
    }}
    // ... 2 more questions
  ]
}}";
    }

    private async Task<string?> CallOpenAiAsync(string prompt, CancellationToken ct, bool useJson = true)
    {
        // Use resilient client with circuit breaker, retry logic, and cost tracking
        return await _openAi.ExecuteAsync("game-knowme", prompt, useJson, ct);
    }

    private GameRoundData GenerateFallbackRound()
    {
        return new GameRoundData
        {
            Questions = new List<QuestionData>
            {
                new()
                {
                    Id = "q1",
                    Text = "Their ideal weekend morning?",
                    Options = new List<OptionData>
                    {
                        new() { Id = "a", Text = "Sleep in until noon", IsCorrect = false },
                        new() { Id = "b", Text = "Early workout", IsCorrect = true },
                        new() { Id = "c", Text = "Brunch with friends", IsCorrect = false },
                        new() { Id = "d", Text = "Farmers market", IsCorrect = false }
                    },
                    Difficulty = "EASY",
                    Category = "lifestyle"
                },
                new()
                {
                    Id = "q2",
                    Text = "Their coffee order?",
                    Options = new List<OptionData>
                    {
                        new() { Id = "a", Text = "Black coffee", IsCorrect = false },
                        new() { Id = "b", Text = "Latte with oat milk", IsCorrect = true },
                        new() { Id = "c", Text = "Doesn't drink coffee", IsCorrect = false },
                        new() { Id = "d", Text = "Iced americano", IsCorrect = false }
                    },
                    Difficulty = "MEDIUM",
                    Category = "preferences"
                },
                new()
                {
                    Id = "q3",
                    Text = "How do they handle stress?",
                    Options = new List<OptionData>
                    {
                        new() { Id = "a", Text = "Talk it out with friends", IsCorrect = false },
                        new() { Id = "b", Text = "Need alone time", IsCorrect = true },
                        new() { Id = "c", Text = "Stay busy", IsCorrect = false },
                        new() { Id = "d", Text = "Exercise", IsCorrect = false }
                    },
                    Difficulty = "HARD",
                    Category = "personality"
                }
            },
            TimeLimit = 90
        };
    }

    private class OpenAiQuestionsResponse
    {
        public List<OpenAiQuestion>? Questions { get; set; }
    }

    private class OpenAiQuestion
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        public List<OpenAiOption>? Options { get; set; }
        public string? Difficulty { get; set; }
        public string? Category { get; set; }
    }

    private class OpenAiOption
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        public bool IsCorrect { get; set; }
    }
}