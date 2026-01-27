using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services;

public class DynamicIntakeCycleService
{
    private readonly WovenDbContext _db;
    private readonly OpenAiDynamicIntakeRewriteService _rewrite;

    private const int CYCLE_HOURS = 48;
    private static readonly DateTime EpochUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public const int CURRENT_MAPPING_VERSION = 1;

    public DynamicIntakeCycleService(WovenDbContext db, OpenAiDynamicIntakeRewriteService rewrite)
    {
        _db = db;
        _rewrite = rewrite;
    }

    public (string cycleId, DateTime startUtc, DateTime endUtc) GetCurrentCycle()
    {
        var now = DateTime.UtcNow;
        var hours = (now - EpochUtc).TotalHours;
        var bucket = (long)Math.Floor(hours / CYCLE_HOURS);
        var start = EpochUtc.AddHours(bucket * CYCLE_HOURS);
        var end = start.AddHours(CYCLE_HOURS);
        var cycleId = "dyn48_" + start.ToString("yyyyMMddHH");
        return (cycleId, start, end);
    }

    public async Task<UserDynamicIntakeSet> GetOrCreateCurrentAsync(int userId, CancellationToken ct)
    {
        var (cycleId, start, end) = GetCurrentCycle();

        var set = await _db.Set<UserDynamicIntakeSet>()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.CycleId == cycleId, ct);

        if (set == null)
        {
            set = new UserDynamicIntakeSet
            {
                UserId = userId,
                CycleId = cycleId,
                CycleStartUtc = start,
                CycleEndUtc = end,
                VariantJson = "[]",
                AnswersJson = "{}",
                FeaturesJson = "{}",
                MappingVersion = CURRENT_MAPPING_VERSION,
                VariantSource = "base",
                GenerationMetaJson = "{}",
                AnsweredAtUtc = null,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _db.Add(set);
            await _db.SaveChangesAsync(ct);
        }

        // Ensure variant exists + is valid (self-heal)
        if (!IsValidVariant(set.VariantJson))
        {
            await EnsureVariantAsync(set, userId, ct);
        }

        return set;
    }

    public async Task EnsureVariantAsync(UserDynamicIntakeSet set, int userId, CancellationToken ct)
    {
        var baseQs = DynamicQuestionBank.GetBaseThree();

        // Load user context for personalization
        var userProfile = await _db.UserProfiles
            .Include(p => p.User)
            .Where(p => p.UserId == userId)
            .Select(p => new { FullName = p.User.FullName, p.Gender })
            .FirstOrDefaultAsync(ct);

        var firstName = ExtractFirstName(userProfile?.FullName);

        var intent = await _db.UserIntents
            .Where(i => i.UserId == userId)
            .Select(i => i.PrimaryIntent)
            .FirstOrDefaultAsync(ct);

        var style = "minimalist, calm, playful";

        var rewritten = await _rewrite.RewriteAsync(
            baseQs,
            new OpenAiDynamicIntakeRewriteService.RewriteContext(firstName, userProfile?.Gender, intent, userId),
            style,
            ct
        );

        // If rewrite returns base, still OK.
        var variantObj = rewritten.Select(q => new
        {
            id = q.Id,
            text = q.Text,
            options = q.Options.Select(o => new { key = o.Key, label = o.Label, subLabel = o.SubLabel })
        });

        set.VariantJson = JsonSerializer.Serialize(variantObj);

        // Fix: Use proper comparison instead of reference equality
        var wasRewritten = !AreQuestionsIdentical(baseQs, rewritten);
        set.VariantSource = wasRewritten ? "ai" : "base";

        set.GenerationMetaJson = JsonSerializer.Serialize(new
        {
            promptVersion = 1,
            generatedAtUtc = DateTime.UtcNow,
            wasPersonalized = wasRewritten
        });
        set.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Compares two arrays of DynamicBankQuestion for content equality.
    /// </summary>
    private static bool AreQuestionsIdentical(DynamicBankQuestion[] a, DynamicBankQuestion[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Text != b[i].Text) return false;
            if (a[i].Options.Length != b[i].Options.Length) return false;
            for (int j = 0; j < a[i].Options.Length; j++)
            {
                if (a[i].Options[j].Label != b[i].Options[j].Label) return false;
            }
        }
        return true;
    }

    public static bool IsValidVariant(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            var qs = JsonSerializer.Deserialize<VariantQuestion[]>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (qs == null || qs.Length != 3) return false;

            var expectedIds = DynamicQuestionBank.QuestionIds.ToHashSet();
            if (!qs.Select(q => q.Id).ToHashSet().SetEquals(expectedIds)) return false;

            foreach (var q in qs)
            {
                var expectedKeys = DynamicQuestionBank.KeysFor(q.Id);
                if (q.Options == null || q.Options.Length != 3) return false;
                var gotKeys = q.Options.Select(o => o.Key).ToHashSet();
                if (!gotKeys.SetEquals(expectedKeys)) return false;

                if (string.IsNullOrWhiteSpace(q.Text)) return false;
                if (q.Options.Any(o => string.IsNullOrWhiteSpace(o.Label) || string.IsNullOrWhiteSpace(o.SubLabel)))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static (string featuresJson, int mappingVersion) ComputeFeaturesJson(string battery, string tone, string role)
    {
        // Primary numeric features
        double socialCapacity = battery switch
        {
            "high" => 0.90,
            "medium" => 0.60,
            "low" => 0.30,
            _ => 0.60
        };

        double interactionIntensity = battery switch
        {
            "high" => 0.70,
            "medium" => 0.45,
            "low" => 0.20,
            _ => 0.45
        };

        // Tone profile
        (double banter, double depth, double softness, double lowPressure) = tone switch
        {
            "playful" => (0.90, 0.30, 0.40, 0.40),
            "serious" => (0.30, 0.90, 0.50, 0.35),
            "calm" => (0.40, 0.55, 0.90, 0.90),
            _ => (0.40, 0.55, 0.60, 0.60)
        };

        // Role / initiative
        double initiative = role switch
        {
            "driver" => 0.90,
            "copilot" => 0.60,
            "passenger" => 0.20,
            _ => 0.60
        };

        bool needsInitiationFromOther = role == "passenger";

        // Useful derived flags for matchmaking penalties
        double ghostRisk = (role == "passenger") ? 0.70 : (role == "copilot" ? 0.35 : 0.20);

        var obj = new
        {
            socialCapacity,
            interactionIntensity,
            banter,
            depth,
            softness,
            lowPressure,
            initiative,
            needsInitiationFromOther,
            ghostRisk
        };

        return (JsonSerializer.Serialize(obj), CURRENT_MAPPING_VERSION);
    }

    private static string? ExtractFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private record VariantQuestion(string Id, string Text, VariantOption[] Options);
    private record VariantOption(string Key, string Label, string SubLabel);
}
