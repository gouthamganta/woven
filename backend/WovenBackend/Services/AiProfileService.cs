using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;

namespace WovenBackend.Services;

/// <summary>
/// Service for extracting AI-ready user profile data from UserVector and related entities.
/// Provides enriched context for all AI touchpoints (questions, games, match explanations).
/// </summary>
public interface IAiProfileService
{
    /// <summary>
    /// Gets a fully-hydrated AI profile for a single user.
    /// </summary>
    Task<AiProfile?> GetProfileAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Gets pair context for two users (for games, match explanations).
    /// </summary>
    Task<PairContext?> GetPairContextAsync(int userId, int candidateId, CancellationToken ct = default);
}

public class AiProfileService : IAiProfileService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<AiProfileService> _logger;

    // Banned patterns for prompt injection protection
    private static readonly Regex[] PromptInjectionPatterns = new[]
    {
        new Regex(@"ignore\s+(previous|all|above)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"system\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"<\|endoftext\|>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"<\|im_start\|>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"<\|im_end\|>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"assistant\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"human\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\[\[INST\]\]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // PII patterns to sanitize before sending to AI
    private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"\b(\+?1?[-.\s]?)?\(?[0-9]{3}\)?[-.\s]?[0-9]{3}[-.\s]?[0-9]{4}\b", RegexOptions.Compiled);

    public AiProfileService(WovenDbContext db, ILogger<AiProfileService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AiProfile?> GetProfileAsync(int userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[AiProfile] Loading profile for user {UserId}", userId);

        // Load user profile with User for name
        var profile = await _db.UserProfiles
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile == null)
        {
            _logger.LogWarning("[AiProfile] No profile found for user {UserId}", userId);
            return null;
        }

        // Extract first name from FullName
        var firstName = ExtractFirstName(profile.User?.FullName);

        // Load latest vector
        var vector = await _db.UserVectors
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        // Load intent
        var intent = await _db.UserIntents
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.UserId == userId, ct);

        // Load tags
        var tags = await _db.UserVectorTags
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToListAsync(ct);

        // Build the profile
        var aiProfile = new AiProfile
        {
            UserId = userId,
            FirstName = SanitizeForAi(firstName),
            Age = profile.Age,
            Gender = profile.Gender,
            Intent = intent?.PrimaryIntent,
        };

        // Parse vector data if available
        if (vector != null)
        {
            ParseVectorData(vector, aiProfile);
            ParsePillarScores(vector.PillarScoresJson, aiProfile);
        }

        // Extract tags by category (up to 3 per category)
        aiProfile.Tags = ExtractTagsByCategory(tags, maxPerCategory: 3);

        // Determine conversation tone from pulse
        aiProfile.ConversationTone = DetermineTone(aiProfile.Pulse);

        // Compute data completeness
        aiProfile.DataCompleteness = ComputeDataCompleteness(aiProfile);
        aiProfile.DataQuality = DetermineDataQuality(aiProfile.DataCompleteness);

        // Apply cohort fallback for new users with insufficient data
        if (aiProfile.DataCompleteness < 0.2)
        {
            _logger.LogInformation("[AiProfile] Low completeness ({Completeness:F2}) for user {UserId}, applying cohort fallback",
                aiProfile.DataCompleteness, userId);
            await ApplyCohortFallbackAsync(aiProfile, ct);
        }

        _logger.LogInformation("[AiProfile] Profile loaded for user {UserId}: {TopPillars} top pillars, {TagCount} tags, completeness {Completeness:F2}, quality {Quality}",
            userId, aiProfile.TopPillars.Count, aiProfile.Tags.Values.Sum(v => v.Count), aiProfile.DataCompleteness, aiProfile.DataQuality);

        return aiProfile;
    }

    public async Task<PairContext?> GetPairContextAsync(int userId, int candidateId, CancellationToken ct = default)
    {
        _logger.LogInformation("[AiProfile] Loading pair context for users {UserId} and {CandidateId}", userId, candidateId);

        var userProfile = await GetProfileAsync(userId, ct);
        var candidateProfile = await GetProfileAsync(candidateId, ct);

        if (userProfile == null || candidateProfile == null)
        {
            _logger.LogWarning("[AiProfile] Could not load profiles for pair context");
            return null;
        }

        var pairContext = new PairContext
        {
            UserProfile = userProfile,
            CandidateProfile = candidateProfile,
        };

        // Compute shared tags (intersection across categories, up to 6)
        pairContext.SharedTags = ComputeSharedTags(userProfile.Tags, candidateProfile.Tags, maxTotal: 6);

        // Compute aligned pillars (|diff| < 0.15, top 3)
        pairContext.AlignedPillars = ComputeAlignedPillars(userProfile.AllPillars, candidateProfile.AllPillars, threshold: 0.15, maxCount: 3);

        // Compute shared hobbies
        pairContext.SharedHobbies = ComputeSharedHobbies(userProfile.Hobbies, candidateProfile.Hobbies, maxCount: 3);

        // Compute tone alignment
        pairContext.ToneAlignment = ComputeToneAlignment(userProfile.ConversationTone, candidateProfile.ConversationTone);

        // Compute intent alignment
        pairContext.IntentAlignment = ComputeIntentAlignment(userProfile.Intent, candidateProfile.Intent);

        _logger.LogInformation("[AiProfile] Pair context computed: {SharedTags} shared tags, {AlignedPillars} aligned pillars",
            pairContext.SharedTags.Count, pairContext.AlignedPillars.Count);

        return pairContext;
    }

    /// <summary>
    /// Computes data completeness score (0..1) based on available profile data.
    /// </summary>
    private double ComputeDataCompleteness(AiProfile profile)
    {
        double score = 0.0;

        // Pillar variance (40% weight) - meaningful scores indicate real data
        if (profile.AllPillars.Count > 0)
        {
            var variance = profile.AllPillars.Values
                .Select(v => Math.Pow(v - 0.5, 2))
                .Average();

            // Variance > 0.04 indicates non-neutral pillars
            if (variance > 0.04)
                score += 0.4 * Math.Min(variance / 0.1, 1.0);
        }

        // Tags presence (25% weight)
        var totalTags = profile.Tags.Values.Sum(v => v.Count);
        if (totalTags > 0)
            score += 0.25 * Math.Min(totalTags / 6.0, 1.0);

        // Hobbies presence (15% weight)
        if (profile.Hobbies.Count > 0)
            score += 0.15 * Math.Min(profile.Hobbies.Count / 3.0, 1.0);

        // Pulse data presence (10% weight)
        if (profile.Pulse != null)
        {
            var pulseVariance = new[]
            {
                Math.Abs(profile.Pulse.SocialCapacity - 0.5),
                Math.Abs(profile.Pulse.Banter - 0.5),
                Math.Abs(profile.Pulse.Depth - 0.5),
                Math.Abs(profile.Pulse.Initiative - 0.5)
            }.Average();

            if (pulseVariance > 0.05)
                score += 0.1;
        }

        // Intent presence (10% weight)
        if (!string.IsNullOrWhiteSpace(profile.Intent))
            score += 0.1;

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Determines data quality category based on completeness score.
    /// </summary>
    private DataQuality DetermineDataQuality(double completeness)
    {
        return completeness switch
        {
            >= 0.6 => DataQuality.HIGH,
            >= 0.3 => DataQuality.MEDIUM,
            _ => DataQuality.LOW
        };
    }

    /// <summary>
    /// Applies cohort fallback for new users with insufficient data.
    /// Samples similar users (age ±5, same gender) and averages their data.
    /// </summary>
    private async Task ApplyCohortFallbackAsync(AiProfile profile, CancellationToken ct)
    {
        try
        {
            var minAge = profile.Age - 5;
            var maxAge = profile.Age + 5;

            // Find similar users with recent vectors (last 30 days)
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            var cohortVectors = await _db.UserVectors
                .AsNoTracking()
                .Where(v => v.UpdatedAt >= thirtyDaysAgo)
                .Join(_db.UserProfiles, v => v.UserId, p => p.UserId, (v, p) => new { Vector = v, Profile = p })
                .Where(x => x.Profile.Age >= minAge && x.Profile.Age <= maxAge)
                .Where(x => string.IsNullOrEmpty(profile.Gender) || x.Profile.Gender == profile.Gender)
                .Where(x => x.Profile.UserId != profile.UserId)
                .Select(x => new
                {
                    x.Vector.UserId,
                    x.Vector.PillarScoresJson,
                    x.Vector.VectorJson
                })
                .Take(50)
                .ToListAsync(ct);

            if (cohortVectors.Count == 0)
            {
                _logger.LogWarning("[AiProfile] No cohort data found for user {UserId}, using neutral defaults", profile.UserId);
                ApplyNeutralDefaults(profile);
                profile.UsedCohortDefaults = true;
                return;
            }

            _logger.LogInformation("[AiProfile] Found {CohortSize} cohort members for user {UserId}",
                cohortVectors.Count, profile.UserId);

            // Average pillar scores from cohort
            var cohortPillars = new Dictionary<string, List<double>>();
            var cohortTags = new Dictionary<string, Dictionary<string, int>>();
            var cohortHobbies = new Dictionary<string, int>();

            foreach (var cohortVector in cohortVectors)
            {
                // Parse pillar scores
                try
                {
                    var pillars = JsonSerializer.Deserialize<Dictionary<string, double>>(cohortVector.PillarScoresJson);
                    if (pillars != null)
                    {
                        foreach (var kvp in pillars)
                        {
                            if (!cohortPillars.ContainsKey(kvp.Key))
                                cohortPillars[kvp.Key] = new List<double>();
                            cohortPillars[kvp.Key].Add(kvp.Value);
                        }
                    }
                }
                catch { /* Skip malformed data */ }

                // Parse tags and hobbies from vector JSON
                try
                {
                    var vectorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(cohortVector.VectorJson);
                    if (vectorData == null) continue;

                    // Extract tags
                    if (vectorData.TryGetValue("foundational", out var foundationalElement))
                    {
                        var foundational = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(foundationalElement.GetRawText());
                        if (foundational != null && foundational.TryGetValue("tags", out var tagsElement))
                        {
                            var tags = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(tagsElement.GetRawText());
                            if (tags != null)
                            {
                                foreach (var category in tags)
                                {
                                    if (!cohortTags.ContainsKey(category.Key))
                                        cohortTags[category.Key] = new Dictionary<string, int>();

                                    foreach (var tag in category.Value)
                                    {
                                        if (!cohortTags[category.Key].ContainsKey(tag))
                                            cohortTags[category.Key][tag] = 0;
                                        cohortTags[category.Key][tag]++;
                                    }
                                }
                            }
                        }
                    }

                    // Extract hobbies
                    if (vectorData.TryGetValue("lifestyle", out var lifestyleElement))
                    {
                        var lifestyle = JsonSerializer.Deserialize<Dictionary<string, string>>(lifestyleElement.GetRawText());
                        if (lifestyle != null && lifestyle.TryGetValue("hobbies", out var hobbiesStr))
                        {
                            var hobbies = hobbiesStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            foreach (var hobby in hobbies)
                            {
                                if (!cohortHobbies.ContainsKey(hobby))
                                    cohortHobbies[hobby] = 0;
                                cohortHobbies[hobby]++;
                            }
                        }
                    }
                }
                catch { /* Skip malformed data */ }
            }

            // Apply averaged pillar scores (only if user has none or all neutral)
            if (profile.AllPillars.Count == 0 || profile.TopPillars.Count == 0)
            {
                profile.AllPillars = cohortPillars.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Average()
                );

                profile.TopPillars = profile.AllPillars
                    .Where(p => Math.Abs(p.Value - 0.5) > 0.05)
                    .OrderByDescending(p => Math.Abs(p.Value - 0.5))
                    .Take(3)
                    .ToDictionary(p => p.Key, p => p.Value);
            }

            // Apply common tags (if user has none)
            if (profile.Tags.Values.Sum(v => v.Count) == 0)
            {
                foreach (var category in cohortTags)
                {
                    profile.Tags[category.Key] = category.Value
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(3)
                        .Select(kvp => kvp.Key)
                        .ToList();
                }
            }

            // Apply common hobbies (if user has none)
            if (profile.Hobbies.Count == 0)
            {
                profile.Hobbies = cohortHobbies
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(3)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }

            profile.UsedCohortDefaults = true;

            _logger.LogInformation("[AiProfile] Applied cohort fallback for user {UserId}: {Pillars} pillars, {Tags} tags, {Hobbies} hobbies",
                profile.UserId, profile.AllPillars.Count, profile.Tags.Values.Sum(v => v.Count), profile.Hobbies.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiProfile] Failed to apply cohort fallback for user {UserId}, using neutral defaults", profile.UserId);
            ApplyNeutralDefaults(profile);
            profile.UsedCohortDefaults = true;
        }
    }

    /// <summary>
    /// Applies neutral defaults when cohort data is unavailable.
    /// </summary>
    private void ApplyNeutralDefaults(AiProfile profile)
    {
        // Neutral pillar scores (0.5 = neutral for all 8 pillars)
        if (profile.AllPillars.Count == 0)
        {
            profile.AllPillars = new Dictionary<string, double>
            {
                ["Lifestyle"] = 0.5,
                ["Energy"] = 0.5,
                ["Values"] = 0.5,
                ["Communication"] = 0.5,
                ["Ambition"] = 0.5,
                ["Stability"] = 0.5,
                ["Curiosity"] = 0.5,
                ["Affection"] = 0.5
            };
        }

        // No specific top pillars for neutral defaults
        profile.TopPillars = new Dictionary<string, double>();
    }

    private void ParseVectorData(Data.Entities.UserVector vector, AiProfile profile)
    {
        try
        {
            var vectorData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(vector.VectorJson);
            if (vectorData == null) return;

            // Extract pulse features
            if (vectorData.TryGetValue("pulse", out var pulseElement))
            {
                var pulse = JsonSerializer.Deserialize<Dictionary<string, double>>(pulseElement.GetRawText());
                if (pulse != null)
                {
                    profile.Pulse = new PulseContext
                    {
                        SocialCapacity = pulse.GetValueOrDefault("socialCapacity", 0.5),
                        Banter = pulse.GetValueOrDefault("banter", 0.5),
                        Depth = pulse.GetValueOrDefault("depth", 0.5),
                        Initiative = pulse.GetValueOrDefault("initiative", 0.5),
                        GhostRisk = pulse.GetValueOrDefault("ghostRisk", 0.3),
                    };
                }
            }

            // Extract hobbies from lifestyle section
            if (vectorData.TryGetValue("lifestyle", out var lifestyleElement))
            {
                var lifestyle = JsonSerializer.Deserialize<Dictionary<string, string>>(lifestyleElement.GetRawText());
                if (lifestyle != null && lifestyle.TryGetValue("hobbies", out var hobbiesStr))
                {
                    profile.Hobbies = SanitizeForAi(hobbiesStr)?
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Take(5)
                        .ToList() ?? new List<string>();
                }
            }

            // Extract tags from foundational section
            if (vectorData.TryGetValue("foundational", out var foundationalElement))
            {
                var foundational = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(foundationalElement.GetRawText());
                if (foundational != null && foundational.TryGetValue("tags", out var tagsElement))
                {
                    var embeddedTags = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(tagsElement.GetRawText());
                    if (embeddedTags != null)
                    {
                        foreach (var kvp in embeddedTags)
                        {
                            if (!profile.Tags.ContainsKey(kvp.Key))
                            {
                                profile.Tags[kvp.Key] = kvp.Value
                                    .Select(t => SanitizeForAi(t) ?? "")
                                    .Where(t => !string.IsNullOrWhiteSpace(t))
                                    .Take(3)
                                    .ToList();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AiProfile] Failed to parse vector data for user {UserId}", profile.UserId);
        }
    }

    private void ParsePillarScores(string pillarScoresJson, AiProfile profile)
    {
        try
        {
            var pillars = JsonSerializer.Deserialize<Dictionary<string, double>>(pillarScoresJson);
            if (pillars == null) return;

            profile.AllPillars = pillars;

            // Find top 3 pillars where |score - 0.5| > 0.05 (meaningful deviation from neutral)
            profile.TopPillars = pillars
                .Where(p => Math.Abs(p.Value - 0.5) > 0.05)
                .OrderByDescending(p => Math.Abs(p.Value - 0.5))
                .Take(3)
                .ToDictionary(p => p.Key, p => p.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AiProfile] Failed to parse pillar scores for user {UserId}", profile.UserId);
        }
    }

    private Dictionary<string, List<string>> ExtractTagsByCategory(List<Data.Entities.UserVectorTag> tags, int maxPerCategory)
    {
        var result = new Dictionary<string, List<string>>();

        var grouped = tags
            .GroupBy(t => t.TagType.ToString())
            .ToDictionary(g => g.Key.ToLowerInvariant(), g => g.ToList());

        foreach (var kvp in grouped)
        {
            result[kvp.Key] = kvp.Value
                .OrderByDescending(t => t.Weight)
                .Take(maxPerCategory)
                .Select(t => SanitizeForAi(t.Tag) ?? "")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
        }

        return result;
    }

    private string DetermineTone(PulseContext? pulse)
    {
        if (pulse == null) return "balanced";

        // High banter + high social = playful
        if (pulse.Banter > 0.65 && pulse.SocialCapacity > 0.6)
            return "playful";

        // High depth + low banter = serious/thoughtful
        if (pulse.Depth > 0.65 && pulse.Banter < 0.4)
            return "thoughtful";

        // Low social + high depth = calm/introspective
        if (pulse.SocialCapacity < 0.4 && pulse.Depth > 0.5)
            return "calm";

        return "balanced";
    }

    private List<string> ComputeSharedTags(
        Dictionary<string, List<string>> userTags,
        Dictionary<string, List<string>> candidateTags,
        int maxTotal)
    {
        var shared = new List<string>();

        foreach (var category in userTags.Keys)
        {
            if (!candidateTags.TryGetValue(category, out var candidateList))
                continue;

            var userSet = new HashSet<string>(userTags[category], StringComparer.OrdinalIgnoreCase);
            var intersection = candidateList.Where(t => userSet.Contains(t));
            shared.AddRange(intersection);

            if (shared.Count >= maxTotal)
                break;
        }

        return shared.Take(maxTotal).ToList();
    }

    private List<PillarAlignment> ComputeAlignedPillars(
        Dictionary<string, double> userPillars,
        Dictionary<string, double> candidatePillars,
        double threshold,
        int maxCount)
    {
        var aligned = new List<PillarAlignment>();

        foreach (var pillar in userPillars.Keys)
        {
            if (!candidatePillars.TryGetValue(pillar, out var candidateValue))
                continue;

            var userValue = userPillars[pillar];
            var diff = Math.Abs(userValue - candidateValue);

            if (diff < threshold)
            {
                aligned.Add(new PillarAlignment
                {
                    Pillar = pillar,
                    UserScore = userValue,
                    CandidateScore = candidateValue,
                    Difference = diff
                });
            }
        }

        return aligned
            .OrderBy(a => a.Difference)
            .ThenByDescending(a => (a.UserScore + a.CandidateScore) / 2)
            .Take(maxCount)
            .ToList();
    }

    private List<string> ComputeSharedHobbies(List<string> userHobbies, List<string> candidateHobbies, int maxCount)
    {
        var userSet = new HashSet<string>(userHobbies, StringComparer.OrdinalIgnoreCase);
        return candidateHobbies
            .Where(h => userSet.Contains(h))
            .Take(maxCount)
            .ToList();
    }

    private string ComputeToneAlignment(string? userTone, string? candidateTone)
    {
        if (string.IsNullOrEmpty(userTone) || string.IsNullOrEmpty(candidateTone))
            return "unknown";

        if (userTone == candidateTone)
            return "matched";

        // Complementary tones
        var complementary = new HashSet<(string, string)>
        {
            ("playful", "balanced"),
            ("thoughtful", "calm"),
            ("balanced", "calm"),
        };

        if (complementary.Contains((userTone, candidateTone)) ||
            complementary.Contains((candidateTone, userTone)))
            return "complementary";

        return "different";
    }

    private double ComputeIntentAlignment(string? userIntent, string? candidateIntent)
    {
        if (string.IsNullOrEmpty(userIntent) || string.IsNullOrEmpty(candidateIntent))
            return 0.5; // Unknown

        // Exact match
        if (string.Equals(userIntent, candidateIntent, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        // Similar intents
        var serious = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "relationship", "long-term", "serious", "marriage" };
        var casual = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "casual", "fun", "short-term", "hookup" };
        var exploring = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "exploring", "open", "figuring out", "unsure" };

        if ((serious.Contains(userIntent) && serious.Contains(candidateIntent)) ||
            (casual.Contains(userIntent) && casual.Contains(candidateIntent)))
            return 0.85;

        if (exploring.Contains(userIntent) || exploring.Contains(candidateIntent))
            return 0.6; // Exploring matches moderately with anything

        // Mismatch (serious vs casual)
        if ((serious.Contains(userIntent) && casual.Contains(candidateIntent)) ||
            (casual.Contains(userIntent) && serious.Contains(candidateIntent)))
            return 0.3;

        return 0.5;
    }

    /// <summary>
    /// Sanitizes user-provided content to prevent prompt injection and remove PII.
    /// </summary>
    private string? SanitizeForAi(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var sanitized = input.Trim();

        // Check for prompt injection patterns
        foreach (var pattern in PromptInjectionPatterns)
        {
            if (pattern.IsMatch(sanitized))
            {
                _logger.LogWarning("[AiProfile] Detected potential prompt injection pattern in user content");
                sanitized = pattern.Replace(sanitized, "[REDACTED]");
            }
        }

        // Remove PII
        sanitized = EmailPattern.Replace(sanitized, "[EMAIL]");
        sanitized = PhonePattern.Replace(sanitized, "[PHONE]");

        // Truncate very long strings
        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 200) + "...";

        return sanitized;
    }

    /// <summary>
    /// Extracts the first name from a full name string.
    /// </summary>
    private static string? ExtractFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }
}

#region DTOs

public class AiProfile
{
    public int UserId { get; set; }
    public string? FirstName { get; set; }
    public int Age { get; set; }
    public string? Gender { get; set; }
    public string? Intent { get; set; }

    /// <summary>
    /// Top 3 pillars with meaningful deviation from neutral (0.5).
    /// </summary>
    public Dictionary<string, double> TopPillars { get; set; } = new();

    /// <summary>
    /// All 8 pillar scores.
    /// </summary>
    public Dictionary<string, double> AllPillars { get; set; } = new();

    /// <summary>
    /// Tags by category (up to 3 per category).
    /// </summary>
    public Dictionary<string, List<string>> Tags { get; set; } = new();

    /// <summary>
    /// User's hobbies (up to 5).
    /// </summary>
    public List<string> Hobbies { get; set; } = new();

    /// <summary>
    /// Pulse context for current mood/energy.
    /// </summary>
    public PulseContext? Pulse { get; set; }

    /// <summary>
    /// Derived conversation tone: playful, thoughtful, calm, balanced.
    /// </summary>
    public string ConversationTone { get; set; } = "balanced";

    /// <summary>
    /// Data completeness score (0..1). < 0.2 indicates new user needing cohort fallback.
    /// </summary>
    public double DataCompleteness { get; set; } = 0.0;

    /// <summary>
    /// Data quality assessment based on completeness and variance.
    /// </summary>
    public DataQuality DataQuality { get; set; } = DataQuality.LOW;

    /// <summary>
    /// Indicates if this profile used cohort defaults due to low completeness.
    /// </summary>
    public bool UsedCohortDefaults { get; set; } = false;

    /// <summary>
    /// Returns a formatted string of top traits for AI prompts.
    /// </summary>
    public string GetTopTraitsFormatted()
    {
        if (TopPillars.Count == 0)
            return "not enough data";

        return string.Join(", ", TopPillars.Select(p => $"{p.Key} ({p.Value:F2})"));
    }

    /// <summary>
    /// Returns a formatted string of key tags for AI prompts.
    /// </summary>
    public string GetKeyTagsFormatted()
    {
        var allTags = Tags.Values.SelectMany(v => v).Take(6).ToList();
        return allTags.Count > 0 ? string.Join(", ", allTags) : "not specified";
    }

    /// <summary>
    /// Returns a formatted string of hobbies for AI prompts.
    /// </summary>
    public string GetHobbiesFormatted()
    {
        return Hobbies.Count > 0 ? string.Join(", ", Hobbies) : "not specified";
    }
}

public class PulseContext
{
    public double SocialCapacity { get; set; } = 0.5;
    public double Banter { get; set; } = 0.5;
    public double Depth { get; set; } = 0.5;
    public double Initiative { get; set; } = 0.5;
    public double GhostRisk { get; set; } = 0.3;
}

public class PairContext
{
    public AiProfile UserProfile { get; set; } = null!;
    public AiProfile CandidateProfile { get; set; } = null!;

    /// <summary>
    /// Tags shared between both users (up to 6).
    /// </summary>
    public List<string> SharedTags { get; set; } = new();

    /// <summary>
    /// Pillars where both users are aligned (|diff| less than 0.15).
    /// </summary>
    public List<PillarAlignment> AlignedPillars { get; set; } = new();

    /// <summary>
    /// Hobbies shared by both users.
    /// </summary>
    public List<string> SharedHobbies { get; set; } = new();

    /// <summary>
    /// How well their conversation tones align: matched, complementary, different.
    /// </summary>
    public string ToneAlignment { get; set; } = "unknown";

    /// <summary>
    /// How well their intents align (0-1 scale).
    /// </summary>
    public double IntentAlignment { get; set; } = 0.5;

    /// <summary>
    /// Returns intent alignment as a descriptive string.
    /// </summary>
    public string GetIntentAlignmentDescription()
    {
        return IntentAlignment switch
        {
            >= 0.85 => "high",
            >= 0.6 => "moderate",
            _ => "exploring"
        };
    }
}

public class PillarAlignment
{
    public string Pillar { get; set; } = "";
    public double UserScore { get; set; }
    public double CandidateScore { get; set; }
    public double Difference { get; set; }
}

public enum DataQuality
{
    LOW,
    MEDIUM,
    HIGH
}

#endregion
