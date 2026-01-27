using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WovenBackend.Services.Games;

public class RedGreenFlagAgent : IGameAgent
{
    private readonly IOpenAiResilientClient _openAi;
    private readonly ILogger<RedGreenFlagAgent> _logger;

    public RedGreenFlagAgent(
        IOpenAiResilientClient openAi,
        ILogger<RedGreenFlagAgent> logger)
    {
        _openAi = openAi;
        _logger = logger;
    }

    public async Task<GameRoundData> GenerateRoundAsync(GameContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("[RedGreenFlag] Generating round {Round} for session {SessionId}. Difficulty={Difficulty}, Tone={Tone}",
            context.CurrentRound, context.SessionId, context.Difficulty, context.Tone);

        // Target is the person being judged (the other person)
        var targetVector = context.GuesserUserId == context.UserAId
            ? context.UserBVector
            : context.UserAVector;

        if (targetVector == null)
        {
            _logger.LogWarning("[RedGreenFlag] Missing target vector, using fallback");
            return GenerateFallbackRound();
        }

        // Use enhanced prompt with full context if available
        var prompt = context.PairContext != null
            ? BuildEnhancedPrompt(context)
            : BuildPrompt(targetVector);

        var response = await CallOpenAiAsync(prompt, ct, useJson: true);

        if (!string.IsNullOrWhiteSpace(response))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<OpenAiStatementsResponse>(
                    response,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed?.Statements != null && parsed.Statements.Count >= 3)
                {
                    var statements = parsed.Statements.Take(3).ToList();

                    var questions = new List<QuestionData>();
                    for (var i = 0; i < statements.Count; i++)
                    {
                        var s = statements[i];

                        questions.Add(new QuestionData
                        {
                            Id = $"s{i + 1}",
                            Text = s.Text?.Trim() ?? "Statement",
                            Difficulty = s.Difficulty?.Trim().ToUpperInvariant() ?? "MEDIUM",
                            Category = "red_green_flag",
                            Options = new List<OptionData>
                            {
                                new() { Id = "GREEN", Text = "Green flag ✅", IsCorrect = false },
                                new() { Id = "YELLOW", Text = "Yellow flag ⚠️", IsCorrect = false },
                                new() { Id = "RED", Text = "Red flag 🚩", IsCorrect = false },
                                new() { Id = "DEPENDS", Text = "Depends 🤷", IsCorrect = false },
                            }
                        });
                    }

                    return new GameRoundData
                    {
                        Questions = questions,
                        TimeLimit = 90
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RedGreenFlag] Failed to parse OpenAI response");
            }
        }

        return GenerateFallbackRound();
    }

    public async Task<string> GenerateInsightAsync(
        GameContext context,
        List<RoundResult> rounds,
        CancellationToken ct = default)
    {
        // We only know scores (alignment count). Keep it short and fun.
        var userAResult = rounds.FirstOrDefault(r => r.GuesserUserId == context.UserAId);
        var userBResult = rounds.FirstOrDefault(r => r.GuesserUserId == context.UserBId);

        var userAScore = userAResult?.Score ?? 0;
        var userBScore = userBResult?.Score ?? 0;

        var prompt = $@"Two people played a dating game: Red Flag / Green Flag.
They judged statements about each other as GREEN/YELLOW/RED/DEPENDS.
Scoring = alignment (how often their guess matched the person's self-rating).

User A alignment: {userAScore}/3
User B alignment: {userBScore}/3

Write a 1–2 sentence insight that feels playful, dating-app friendly, and encourages conversation.
No JSON. No emojis overload. Keep it punchy.";

        var response = await CallOpenAiAsync(prompt, ct, useJson: false);

        if (!string.IsNullOrWhiteSpace(response))
            return response.Trim();

        // Fallback
        if (userAScore == userBScore)
        {
            return userAScore >= 2
                ? "You’re reading each other pretty well — keep going."
                : "Okay… some surprises here. Ask why they picked what they picked.";
        }

        return userAScore > userBScore
            ? "One of you is clocking the vibe better — ask what gave it away."
            : "Someone’s harder to read — that’s either mystery or chaos. Explore it.";
    }

    private string BuildPrompt(UserVectorData target)
    {
        var tagsStr = target.Tags.Count > 0 ? string.Join(", ", target.Tags.Take(12)) : "not specified";

        var energy = target.Pillars.TryGetValue("Energy", out var e) ? e : 0.5;
        var social = target.Pulse.TryGetValue("socialCapacity", out var s) ? s : 0.5;

        var diet = target.Lifestyle.TryGetValue("diet", out var d) ? d : "not specified";
        var workout = target.Lifestyle.TryGetValue("workout", out var w) ? w : "not specified";

        return BuildEnhancedPromptInternal(target.Age, tagsStr, energy, social, diet, workout, target.Pillars, null, GameTone.BALANCED);
    }

    private string BuildEnhancedPrompt(GameContext context)
    {
        var target = context.GuesserUserId == context.UserAId
            ? context.UserBVector
            : context.UserAVector;

        if (target == null)
            return BuildPrompt(new UserVectorData());

        var tagsStr = target.Tags.Count > 0 ? string.Join(", ", target.Tags.Take(12)) : "not specified";
        var energy = target.Pillars.TryGetValue("Energy", out var e) ? e : 0.5;
        var social = target.Pulse.TryGetValue("socialCapacity", out var s) ? s : 0.5;
        var diet = target.Lifestyle.TryGetValue("diet", out var d) ? d : "not specified";
        var workout = target.Lifestyle.TryGetValue("workout", out var w) ? w : "not specified";

        return BuildEnhancedPromptInternal(target.Age, tagsStr, energy, social, diet, workout, target.Pillars, context.PairContext, context.Tone);
    }

    private string BuildEnhancedPromptInternal(
        int age,
        string tagsStr,
        double energy,
        double social,
        string diet,
        string workout,
        Dictionary<string, double> pillars,
        PairContext? pairContext,
        GameTone tone)
    {
        // Check if either profile has low data quality or uses cohort defaults
        var anyLowQuality = pairContext?.UserProfile?.DataQuality == DataQuality.LOW ||
                            pairContext?.CandidateProfile?.DataQuality == DataQuality.LOW;
        var anyUsedCohortDefaults = pairContext?.UserProfile?.UsedCohortDefaults == true ||
                                    pairContext?.CandidateProfile?.UsedCohortDefaults == true;
        var needsExploratoryApproach = anyLowQuality || anyUsedCohortDefaults;

        // Build pillar summary
        var topPillars = pillars.Count > 0
            ? string.Join(", ", pillars.OrderByDescending(p => Math.Abs(p.Value - 0.5)).Take(3).Select(p => $"{p.Key} ({p.Value:F2})"))
            : "balanced";

        // Tone guidance
        var toneGuidance = tone switch
        {
            GameTone.PLAYFUL => "Make statements fun, cheeky, and light-hearted. Use playful language.",
            GameTone.THOUGHTFUL => "Make statements more reflective and insightful. Still fun but with depth.",
            _ => "Keep it conversational and warm. Mix of fun and meaningful."
        };

        // Data quality guidance
        var dataQualityNote = needsExploratoryApproach
            ? @"

⚠️ LIMITED DATA AVAILABLE - This person is new or has minimal profile data.
ADAPT YOUR APPROACH:
- Use broader statements that apply to general personality types
- Don't make assumptions about specific niche behaviors
- Frame as 'likely to' or 'tends to' rather than definitive statements
- Focus on universal dating scenarios vs hyper-specific interests"
            : "";

        // Match context section
        var matchContextSection = "";
        if (pairContext != null)
        {
            var sharedHobbies = pairContext.SharedHobbies.Count > 0
                ? string.Join(", ", pairContext.SharedHobbies)
                : null;

            var alignedPillars = pairContext.AlignedPillars.Count > 0
                ? string.Join(", ", pairContext.AlignedPillars.Select(p => p.Pillar))
                : null;

            matchContextSection = $@"

MATCH CONTEXT (what they share with the guesser):
{(sharedHobbies != null ? $"- Shared hobbies: {sharedHobbies}" : "")}
{(alignedPillars != null ? $"- Similar traits: {alignedPillars}" : "")}
- Tone alignment: {pairContext.ToneAlignment}";
        }

        return $@"You are creating a dating game called: Red Flag / Green Flag.

The goal:
- Create 3 short statements about THIS target person.
- Another person will label each statement as GREEN / YELLOW / RED / DEPENDS.
- Then the target person will label themselves.
- Score is how often they match.

TARGET PERSON (use their actual data):
- Age: {age}
- Top traits: {topPillars}
- Interests/Tags: {tagsStr}
- Energy (0-1): {energy:F2}
- Social capacity (0-1): {social:F2}
- Diet: {diet}
- Workout: {workout}
{matchContextSection}

TONE GUIDANCE: {toneGuidance}
{dataQualityNote}

STATEMENT RULES:
1) Each statement MUST reference THIS person's actual traits/tags/hobbies
2) No heavy topics (trauma, exes, politics, religion, medical/mental health, explicit sex)
3) Mix:
   - one light lifestyle habit (based on their actual interests)
   - one social/communication habit (based on their social capacity/energy)
   - one dating preference vibe habit
4) Each statement should be short (1 sentence) and a little provocative/fun

CRITICAL ANTI-GENERIC RULES:
NEVER use these banned topics/patterns:
- 'Texting speed' or 'reply time' (overused)
- 'Ghosting' or 'leaving on read' (negative)
- 'Replying habits' in general (cliche)
- 'Coffee preferences' (boring)
- 'Exes' or past relationships (too heavy)
- 'Weekend plans' (generic)

INSTEAD use THEIR actual pillars/tags/hobbies. For example:
- If they're high energy: ""They need plans every weekend or they feel restless""
- If they like cooking: ""They judge dates by whether they'll try their cooking""
- If they're introverted: ""They recharge alone, even when really into someone""

Return ONLY valid JSON exactly in this format:
{{
  ""statements"": [
    {{ ""text"": ""[Statement based on their actual data]"", ""difficulty"": ""EASY"" }},
    {{ ""text"": ""[Statement based on their actual data]"", ""difficulty"": ""MEDIUM"" }},
    {{ ""text"": ""[Statement based on their actual data]"", ""difficulty"": ""HARD"" }}
  ]
}}";
    }

    private async Task<string?> CallOpenAiAsync(string prompt, CancellationToken ct, bool useJson)
    {
        // Use resilient client with circuit breaker, retry logic, and cost tracking
        return await _openAi.ExecuteAsync("game-redflag", prompt, useJson, ct);
    }

    private GameRoundData GenerateFallbackRound()
    {
        return new GameRoundData
        {
            Questions = new List<QuestionData>
            {
                new()
                {
                    Id = "s1",
                    Text = "They reply fast… but disappear for hours randomly.",
                    Difficulty = "EASY",
                    Category = "red_green_flag",
                    Options = BaseOptions()
                },
                new()
                {
                    Id = "s2",
                    Text = "They’re super social — always down to be outside and doing something.",
                    Difficulty = "MEDIUM",
                    Category = "red_green_flag",
                    Options = BaseOptions()
                },
                new()
                {
                    Id = "s3",
                    Text = "They like planning the date… but want you to pick the vibe.",
                    Difficulty = "HARD",
                    Category = "red_green_flag",
                    Options = BaseOptions()
                }
            },
            TimeLimit = 90
        };
    }

    private static List<OptionData> BaseOptions() => new()
    {
        new() { Id = "GREEN", Text = "Green flag ✅", IsCorrect = false },
        new() { Id = "YELLOW", Text = "Yellow flag ⚠️", IsCorrect = false },
        new() { Id = "RED", Text = "Red flag 🚩", IsCorrect = false },
        new() { Id = "DEPENDS", Text = "Depends 🤷", IsCorrect = false },
    };

    private class OpenAiStatementsResponse
    {
        public List<OpenAiStatement>? Statements { get; set; }
    }

    private class OpenAiStatement
    {
        public string? Text { get; set; }
        public string? Difficulty { get; set; }
    }
}
