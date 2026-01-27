using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public class UserVectorBuilder : IUserVectorBuilder
{
    private readonly WovenDbContext _db;
    private readonly IOpenAiTaggingService _tagging;
    private readonly ILogger<UserVectorBuilder> _logger;

    public UserVectorBuilder(
        WovenDbContext db,
        IOpenAiTaggingService tagging,
        ILogger<UserVectorBuilder> logger)
    {
        _db = db;
        _tagging = tagging;
        _logger = logger;
    }

    public async Task<int> BuildAndSaveV1Async(int userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[VectorBuilder] Building v1 for user {UserId}", userId);

        try
        {
            var vectorDto = await BuildVectorAsync(userId, ct);

            // Check if v1 already exists
            var existing = await _db.UserVectors
                .FirstOrDefaultAsync(v => v.UserId == userId && v.Version == 1, ct);

            if (existing != null)
            {
                _logger.LogInformation("[VectorBuilder] v1 already exists for user {UserId}, skipping", userId);
                return existing.Id;
            }

            // Save vector
            var vector = new UserVector
            {
                UserId = userId,
                Version = 1,
                VectorJson = vectorDto.ToJson(),
                PillarScoresJson = vectorDto.PillarScoresToJson(),
                CreatedAt = DateTime.UtcNow
            };

            _db.UserVectors.Add(vector);
            await _db.SaveChangesAsync(ct);

            // Save tags (optional, for fast queries)
            await SaveTagsAsync(userId, 1, vectorDto, ct);

            _logger.LogInformation("[VectorBuilder] Saved v1 (id={VectorId}) for user {UserId}", vector.Id, userId);
            return vector.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VectorBuilder] Failed to build v1 for user {UserId}", userId);
            throw;
        }
    }

    public async Task UpdatePulseAsync(int userId, Dictionary<string, string> pulseAnswers, CancellationToken ct = default)
    {
        _logger.LogInformation("[VectorBuilder] Updating pulse for user {UserId}", userId);

        try
        {
            // Get latest vector
            var vector = await _db.UserVectors
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync(ct);

            if (vector == null)
            {
                _logger.LogWarning("[VectorBuilder] No vector found for user {UserId}, cannot update pulse", userId);
                return;
            }

            // Parse existing vector
            var vectorData = JsonSerializer.Deserialize<Dictionary<string, object>>(vector.VectorJson);
            if (vectorData == null) return;

            // Compute pulse features
            var pulseFeatures = ComputePulseFeatures(pulseAnswers);

            // Update pulse section
            vectorData["pulse"] = pulseFeatures;

            // Save updated vector
            vector.VectorJson = JsonSerializer.Serialize(vectorData);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("[VectorBuilder] Updated pulse for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VectorBuilder] Failed to update pulse for user {UserId}", userId);
        }
    }

    private async Task<UserVectorDto> BuildVectorAsync(int userId, CancellationToken ct)
    {
        var dto = new UserVectorDto
        {
            UserId = userId,
            Version = 1
        };

        // Load user data
        var profile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var intent = await _db.UserIntents.AsNoTracking()
            .FirstOrDefaultAsync(i => i.UserId == userId, ct);

        var optionalFields = await _db.UserOptionalFields.AsNoTracking()
            .Where(f => f.UserId == userId)
            .ToListAsync(ct);

        var foundational = await _db.UserFoundationalQuestionSets.AsNoTracking()
            .Where(f => f.UserId == userId && f.AnsweredAt != null)
            .OrderByDescending(f => f.Version)
            .FirstOrDefaultAsync(ct);

        // 1) Intent section
        if (intent != null)
        {
            dto.Intent = await BuildIntentMetadataAsync(intent, ct);
        }

        // 2) Foundational section (pillar scores + tags)
        if (foundational != null)
        {
            dto.PillarScores = await BuildPillarScoresAsync(foundational, optionalFields, ct);
            dto.FoundationalTags = await ExtractFoundationalTagsAsync(foundational, ct);
        }

        // 3) Lifestyle section
        dto.Lifestyle = BuildLifestyleSection(optionalFields);

        // 4) Pulse section (will be updated separately)
        dto.PulseFeatures = new Dictionary<string, double>();

        return dto;
    }

    private async Task<IntentMetadata> BuildIntentMetadataAsync(UserIntent intent, CancellationToken ct)
    {
        // Call OpenAI to extract intent metadata
        var metadata = await _tagging.ExtractIntentMetadataAsync(
            intent.PrimaryIntent,
            intent.ReflectionSentence,
            ct
        );

        return metadata ?? new IntentMetadata();
    }

    private async Task<PillarScores> BuildPillarScoresAsync(
        UserFoundationalQuestionSet foundational,
        List<UserOptionalField> optionalFields,
        CancellationToken ct)
    {
        // Call OpenAI to compute pillar scores from foundational answers
        var scores = await _tagging.ComputePillarScoresAsync(
            foundational.AnswersJson,
            foundational.QuestionsJson,
            ct
        );

        return scores ?? new PillarScores();
    }

    private async Task<Dictionary<string, List<string>>> ExtractFoundationalTagsAsync(
        UserFoundationalQuestionSet foundational,
        CancellationToken ct)
    {
        // Call OpenAI to extract tags
        var tags = await _tagging.ExtractTagsAsync(
            foundational.AnswersJson,
            foundational.QuestionsJson,
            ct
        );

        return tags ?? new Dictionary<string, List<string>>();
    }

    private Dictionary<string, string> BuildLifestyleSection(List<UserOptionalField> optionalFields)
    {
        var lifestyle = new Dictionary<string, string>();

        foreach (var field in optionalFields)
        {
            // Include matching-only and public fields
            if (field.Visibility == VisibilityLevel.Public || field.Visibility == VisibilityLevel.MatchingOnly)
            {
                lifestyle[field.Key] = field.Value;
            }
        }

        return lifestyle;
    }

    private Dictionary<string, double> ComputePulseFeatures(Dictionary<string, string> pulseAnswers)
    {
        // Use the same logic as DynamicIntakeCycleService
        pulseAnswers.TryGetValue("d1_battery", out var battery);
        pulseAnswers.TryGetValue("d2_tone", out var tone);
        pulseAnswers.TryGetValue("d3_role", out var role);

        var (featuresJson, _) = DynamicIntakeCycleService.ComputeFeaturesJson(
            battery ?? "medium",
            tone ?? "calm",
            role ?? "copilot"
        );

        var features = JsonSerializer.Deserialize<Dictionary<string, object>>(featuresJson);
        var result = new Dictionary<string, double>();

        if (features != null)
        {
            foreach (var kvp in features)
            {
                if (kvp.Value is JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.Number)
                    {
                        result[kvp.Key] = element.GetDouble();
                    }
                    else if (element.ValueKind == JsonValueKind.True)
                    {
                        result[kvp.Key] = 1.0;
                    }
                    else if (element.ValueKind == JsonValueKind.False)
                    {
                        result[kvp.Key] = 0.0;
                    }
                }
            }
        }

        return result;
    }

    private async Task SaveTagsAsync(int userId, int version, UserVectorDto dto, CancellationToken ct)
    {
        var tags = new List<UserVectorTag>();

        // Intent tags
        foreach (var tag in dto.Intent.Tags)
        {
            tags.Add(new UserVectorTag
            {
                UserId = userId,
                Version = version,
                TagType = TagType.Intent.ToString(),
                Tag = tag,
                Weight = 1.0
            });
        }

        // Foundational tags
        foreach (var kvp in dto.FoundationalTags)
        {
            var tagType = kvp.Key; // "values", "lifestyle", etc.
            foreach (var tag in kvp.Value)
            {
                tags.Add(new UserVectorTag
                {
                    UserId = userId,
                    Version = version,
                    TagType = tagType,
                    Tag = tag,
                    Weight = 1.0
                });
            }
        }

        if (tags.Count > 0)
        {
            _db.UserVectorTags.AddRange(tags);
            await _db.SaveChangesAsync(ct);
        }
    }
}