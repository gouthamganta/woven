using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public class MatchExplanationService : IMatchExplanationService
{
    private readonly WovenDbContext _db;
    private readonly IOpenAiResilientClient _ai;
    private readonly ILogger<MatchExplanationService> _logger;
    private readonly IAiProfileService _aiProfileService;

    public MatchExplanationService(
        WovenDbContext db,
        IOpenAiResilientClient ai,
        ILogger<MatchExplanationService> logger,
        IAiProfileService aiProfileService)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
        _aiProfileService = aiProfileService;
    }

    // Returns the dominant tone from past fast-contact explanations (≥50% threshold, min 3 contacts).
    private async Task<string?> GetToneBiasAsync(int userId, CancellationToken ct)
    {
        const long fortyEightHoursMs = 172_800_000L;

        var fastContactIds = await _db.MatchSignalLogs
            .AsNoTracking()
            .Where(s => s.ViewerId == userId
                     && s.EventType == MatchSignalEventTypes.TimeToFirstMessageMs
                     && s.EventValue < fortyEightHoursMs)
            .Select(s => s.CandidateId)
            .Distinct()
            .ToListAsync(ct);

        if (fastContactIds.Count < 3) return null;

        var tones = await _db.MatchExplanations
            .AsNoTracking()
            .Where(e => e.UserId == userId && fastContactIds.Contains(e.CandidateId))
            .Select(e => e.Tone)
            .ToListAsync(ct);

        if (tones.Count == 0) return null;

        var dominant = tones
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .First();

        return dominant.Count() * 2 >= tones.Count ? dominant.Key : null;
    }

    // Returns semicolon-joined idea texts from last 5 DateIdeaAccepted signals.
    private async Task<string?> GetDateStyleHintAsync(int userId, CancellationToken ct)
    {
        var signals = await _db.MatchSignalLogs
            .AsNoTracking()
            .Where(s => s.ViewerId == userId && s.EventType == MatchSignalEventTypes.DateIdeaAccepted)
            .OrderByDescending(s => s.OccurredAt)
            .Take(5)
            .Select(s => s.MetadataJson)
            .ToListAsync(ct);

        if (signals.Count == 0) return null;

        var ideas = signals
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m =>
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(m!);
                    if (doc.RootElement.TryGetProperty("chosenIdea", out var el))
                        return el.GetString();
                }
                catch { }
                return null;
            })
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        return ideas.Count > 0 ? string.Join("; ", ideas) : null;
    }

    public async Task<int> GenerateAndSaveExplanationAsync(
        int userId,
        int candidateId,
        MatchScore score,
        MatchBucket bucket,
        DateOnly dateUtc,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[Explanation] Generating for user {UserId} + candidate {CandidateId}",
            userId, candidateId);

        // Load pair context for rich personalization
        var pairContext = await _aiProfileService.GetPairContextAsync(userId, candidateId, ct);

        // Load user and candidate vectors
        var userVector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        var candidateVector = await _db.UserVectors
            .Where(v => v.UserId == candidateId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (userVector == null || candidateVector == null)
        {
            _logger.LogWarning("[Explanation] Missing vectors, using fallback");
            return await SaveFallbackExplanationAsync(userId, candidateId, bucket, dateUtc, ct);
        }

        // Extract safe features for explanation with pair context
        var reasons = ExtractMatchReasons(userVector, candidateVector, score, bucket, pairContext);

        // Determine tone from pulse (prefer user's tone if available)
        var tone = pairContext?.UserProfile.ConversationTone ?? DetermineTone(userVector.VectorJson);

        // Read behavioral feedback to personalize this viewer's experience
        var toneBias = await GetToneBiasAsync(userId, ct);
        var dateStyleHint = await GetDateStyleHintAsync(userId, ct);

        // Generate explanation via OpenAI with pair context + feedback loop
        var (headline, bullets, dateIdeas, bridgeQuestion) = await GenerateExplanationAsync(reasons, bucket, tone, pairContext, toneBias, dateStyleHint, ct);

        // Save to database
        var explanation = new MatchExplanation
        {
            UserId = userId,
            CandidateId = candidateId,
            DateUtc = dateUtc,
            Headline = headline,
            BulletsJson = JsonSerializer.Serialize(bullets),
            Tone = tone,
            DateIdea = dateIdeas.Count > 0 ? dateIdeas[0] : null,
            DateIdeasJson = JsonSerializer.Serialize(dateIdeas),
            BridgeQuestion = bridgeQuestion,
            CreatedAt = DateTime.UtcNow
        };

        _db.MatchExplanations.Add(explanation);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[Explanation] Saved explanation {Id} for user {UserId}. SharedTags: {SharedTags}, AlignedPillars: {AlignedPillars}",
            explanation.Id, userId, pairContext?.SharedTags.Count ?? 0, pairContext?.AlignedPillars.Count ?? 0);

        return explanation.Id;
    }

    private Dictionary<string, object> ExtractMatchReasons(
        UserVector userVector,
        UserVector candidateVector,
        MatchScore score,
        MatchBucket bucket,
        PairContext? pairContext = null)
    {
        var reasons = new Dictionary<string, object>
        {
            ["bucket"] = bucket.ToString(),
            ["totalScore"] = score.TotalScore,
            ["topComponent"] = GetTopScoringComponent(score)
        };

        // Add rich context from PairContext
        if (pairContext != null)
        {
            if (pairContext.SharedTags.Count > 0)
                reasons["sharedTags"] = pairContext.SharedTags.Take(4).ToList();

            if (pairContext.AlignedPillars.Count > 0)
                reasons["alignedPillars"] = pairContext.AlignedPillars.Take(2)
                    .Select(p => new { p.Pillar, avgScore = (p.UserScore + p.CandidateScore) / 2 })
                    .ToList();

            if (pairContext.SharedHobbies.Count > 0)
                reasons["sharedHobbies"] = pairContext.SharedHobbies.Take(3).ToList();

            reasons["toneAlignment"] = pairContext.ToneAlignment;
            reasons["intentAlignment"] = pairContext.GetIntentAlignmentDescription();
        }
        else
        {
            // Fallback to old logic if no pair context
            try
            {
                var userVectorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(userVector.VectorJson);
                var candidateVectorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(candidateVector.VectorJson);

                if (userVectorData != null && candidateVectorData != null)
                {
                    if (score.IntentScore >= 70)
                    {
                        reasons["intentAlignment"] = "high";
                    }

                    var userPillars = JsonSerializer.Deserialize<PillarScores>(userVector.PillarScoresJson);
                    var candidatePillars = JsonSerializer.Deserialize<PillarScores>(candidateVector.PillarScoresJson);

                    if (userPillars != null && candidatePillars != null)
                    {
                        var topPillars = FindTopAlignedPillars(userPillars, candidatePillars);
                        if (topPillars.Count > 0)
                            reasons["topPillars"] = topPillars;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Explanation] Failed to extract detailed reasons");
            }
        }

        return reasons;
    }

    private List<string> FindTopAlignedPillars(PillarScores user, PillarScores candidate)
    {
        var pillars = new[]
        {
            ("Lifestyle", user.Lifestyle, candidate.Lifestyle),
            ("Energy", user.Energy, candidate.Energy),
            ("Values", user.Values, candidate.Values),
            ("Communication", user.Communication, candidate.Communication),
            ("Ambition", user.Ambition, candidate.Ambition),
            ("Stability", user.Stability, candidate.Stability),
            ("Curiosity", user.Curiosity, candidate.Curiosity),
            ("Affection", user.Affection, candidate.Affection)
        };

        return pillars
            .Where(p => Math.Abs(p.Item2 - p.Item3) < 0.2) // Similar scores
            .OrderByDescending(p => (p.Item2 + p.Item3) / 2) // High values
            .Take(2)
            .Select(p => p.Item1)
            .ToList();
    }

    private string GetTopScoringComponent(MatchScore score)
    {
        var components = new[]
        {
            ("intent", score.IntentScore),
            ("foundational", score.FoundationalScore),
            ("lifestyle", score.LifestyleScore),
            ("pulse", score.PulseScore)
        };

        return components.OrderByDescending(c => c.Item2).First().Item1;
    }

    private string DetermineTone(string vectorJson)
    {
        try
        {
            var vector = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(vectorJson);
            if (vector != null && vector.TryGetValue("pulse", out var pulseElement))
            {
                var pulse = JsonSerializer.Deserialize<Dictionary<string, double>>(pulseElement.GetRawText());
                if (pulse != null)
                {
                    // Use socialCapacity as proxy for energy/playfulness
                    if (pulse.TryGetValue("socialCapacity", out var capacity))
                    {
                        if (capacity > 0.7) return "playful";
                        if (capacity < 0.4) return "serious";
                    }
                }
            }
        }
        catch { }

        return "calm";
    }

    private async Task<(string Headline, List<string> Bullets, List<string> DateIdeas, string? BridgeQuestion)> GenerateExplanationAsync(
        Dictionary<string, object> reasons,
        MatchBucket bucket,
        string tone,
        PairContext? pairContext,
        string? toneBias,
        string? dateStyleHint,
        CancellationToken ct)
    {
        // Build rich match data section if we have pair context
        var matchDataSection = "";
        if (pairContext != null)
        {
            var sharedInterests = pairContext.SharedTags.Count > 0
                ? string.Join(", ", pairContext.SharedTags)
                : "exploring similar things";

            var alignedTraits = pairContext.AlignedPillars.Count > 0
                ? string.Join(", ", pairContext.AlignedPillars.Select(p => p.Pillar))
                : "compatible energy";

            var sharedHobbies = pairContext.SharedHobbies.Count > 0
                ? string.Join(", ", pairContext.SharedHobbies)
                : null;

            matchDataSection = $@"

MATCH DATA (USE THIS - IT'S SPECIFIC TO THESE TWO PEOPLE):
- Shared interests/tags: {sharedInterests}
- Aligned traits: {alignedTraits}
{(sharedHobbies != null ? $"- Shared hobbies: {sharedHobbies}" : "")}
- Tone match: {pairContext.ToneAlignment}
- Intent alignment: {pairContext.GetIntentAlignmentDescription()}";
        }

        // Build viewer history section from behavioral feedback
        var viewerHistorySection = "";
        if (toneBias != null || dateStyleHint != null)
        {
            viewerHistorySection = "\n\nVIEWER HISTORY (personalize for this specific viewer):";
            if (toneBias != null)
                viewerHistorySection += $"\n- This viewer connects faster when tone is {toneBias} — lean into that";
            if (dateStyleHint != null)
                viewerHistorySection += $"\n- This viewer has previously chosen: {dateStyleHint} — offer variety but lean toward that style";
        }

        var systemPrompt = $@"You write 'why you're a match' explanations for a dating app.

Tone: {tone}
{matchDataSection}
{viewerHistorySection}

STRICT REQUIREMENTS:
- Write 1 headline (max 15 words) - MUST reference at least 1 aligned trait OR 2 shared interests
- Write 1-2 bullet points (max 20 words each) - MUST mention specific shared interests/traits
- Write 3 date ideas (max 15 words each):
  * One active/outdoor activity
  * One social/cultural activity
  * One casual/low-key activity
  * If they share hobbies, at least 1 idea MUST incorporate one
  * No overlap in activity type across the 3
- Write 1 bridge question: an opening question the viewer could ask THIS specific person.
  * It must be rooted in something real from the match data (shared tag, aligned pillar, or hobby)
  * It should invite a story or memory, not a yes/no answer
  * Max 20 words
  * Sound like something a curious, warm person would genuinely ask
  * NOT generic (""what do you like to do?"") — must be specific to this pair
- Focus on what they SPECIFICALLY have in common
- Never overpromise or guarantee outcomes
- Sound natural, not robotic
- No emojis

BANNED PHRASES (NEVER USE):
- 'good energy'
- 'meaningful connection'
- 'real conversations'
- 'authentic'
- 'genuine'
- 'vibe'
- 'worth exploring'
- 'something special'

USE THEIR ACTUAL DATA - be specific, not generic!

Output ONLY valid JSON:
{{
  ""headline"": ""You both [specific shared trait/interest]..."",
  ""bullets"": [
    ""[Specific shared interest]: [what you have in common]"",
    ""[Aligned trait]: [how you're similar]""
  ],
  ""dateIdeas"": [
    ""[Active/outdoor idea using their shared interest]"",
    ""[Social/cultural idea]"",
    ""[Casual/low-key idea]""
  ],
  ""bridgeQuestion"": ""[Specific opening question for this pair]""
}}";

        var userPrompt = $@"Match reasons: {JsonSerializer.Serialize(reasons)}
Bucket: {bucket}

Generate explanation JSON. Be SPECIFIC using the match data provided.";

        var response = await _ai.ExecuteWithSystemAsync("match-explanation", systemPrompt, userPrompt, ct: ct);
        if (response == null)
            return GenerateFallbackExplanation(bucket);

        try
        {
            var parsed = JsonSerializer.Deserialize<ExplanationResponse>(response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Headline))
            {
                var ideas = parsed.DateIdeas?.Where(s => !string.IsNullOrWhiteSpace(s)).Take(3).ToList()
                            ?? new List<string>();
                var bridge = string.IsNullOrWhiteSpace(parsed.BridgeQuestion) ? null : parsed.BridgeQuestion.Trim();
                return (parsed.Headline, parsed.Bullets ?? new List<string>(), ideas, bridge);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Explanation] Failed to parse OpenAI response");
        }

        return GenerateFallbackExplanation(bucket);
    }

    private (string, List<string>, List<string>, string?) GenerateFallbackExplanation(MatchBucket bucket)
    {
        return bucket switch
        {
            MatchBucket.CORE_FIT => (
                "You both know what you want and it lines up.",
                new List<string> { "Shared: similar values and relationship goals" },
                new List<string>
                {
                    "Have dinner somewhere with a real menu and talk",
                    "Find a bookshop or record store and wander together",
                    "Cook something together — doesn't need to be perfect"
                },
                "What's something you believed strongly that you've since changed your mind about?"
            ),
            MatchBucket.LIFESTYLE_FIT => (
                "Your daily rhythms point in the same direction.",
                new List<string> { "Lifestyle: compatible routines and priorities" },
                new List<string>
                {
                    "Go for a morning run or walk together",
                    "Hit a farmers market and grab breakfast after",
                    "Try a new neighborhood restaurant neither of you has been to"
                },
                "What does a genuinely good Saturday morning look like for you?"
            ),
            MatchBucket.CONVERSATION_FIT => (
                "The way you each communicate fits well.",
                new List<string> { "Communication: you talk and listen in similar ways" },
                new List<string>
                {
                    "Grab coffee and keep the conversation going",
                    "Check out a local exhibit or gallery together",
                    "Find a rooftop or park with a good view and just talk"
                },
                "What's the last thing someone said that genuinely made you think differently?"
            ),
            MatchBucket.EXPLORER => (
                "Some interesting things overlap here.",
                new List<string> { "Shared: common ground to discover together" },
                new List<string>
                {
                    "Meet for a walk and see where the conversation goes",
                    "Try a trivia night or board game café",
                    "Find a low-key spot and figure each other out"
                },
                "What's something you've gotten really into recently that surprised you?"
            ),
            _ => (
                "You share enough to make it interesting.",
                new List<string> { "Start a conversation and find out" },
                new List<string>
                {
                    "Coffee — low pressure, easy to extend if it clicks",
                    "Walk somewhere neither of you has been",
                    "Grab food from a place you both want to try"
                },
                null
            )
        };
    }

    private async Task<int> SaveFallbackExplanationAsync(
        int userId,
        int candidateId,
        MatchBucket bucket,
        DateOnly dateUtc,
        CancellationToken ct)
    {
        var (headline, bullets, dateIdeas, bridgeQuestion) = GenerateFallbackExplanation(bucket);

        var explanation = new MatchExplanation
        {
            UserId = userId,
            CandidateId = candidateId,
            DateUtc = dateUtc,
            Headline = headline,
            BulletsJson = JsonSerializer.Serialize(bullets),
            Tone = "calm",
            DateIdea = dateIdeas.Count > 0 ? dateIdeas[0] : null,
            DateIdeasJson = JsonSerializer.Serialize(dateIdeas),
            BridgeQuestion = bridgeQuestion,
            CreatedAt = DateTime.UtcNow
        };

        _db.MatchExplanations.Add(explanation);
        await _db.SaveChangesAsync(ct);

        return explanation.Id;
    }

    private class ExplanationResponse
    {
        public string Headline { get; set; } = "";
        public List<string>? Bullets { get; set; }
        public List<string>? DateIdeas { get; set; }
        public string? BridgeQuestion { get; set; }
    }
}