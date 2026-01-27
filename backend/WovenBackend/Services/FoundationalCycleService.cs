using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services;

public class FoundationalCycleService
{
    private readonly WovenDbContext _db;
    private readonly OpenAiRewriteService _openAi;

    // ✅ Intervals between answered sets (eligibility for next version)
    private const int V1_INTERVAL_DAYS = 15;     // v2 eligible after v1 answered + 15 days
    private const int V2_INTERVAL_DAYS = 45;     // v3 eligible after v2 answered + 45 days
    private const int V3PLUS_INTERVAL_DAYS = 60; // v4+ eligible after v3 answered + 60 days

    public FoundationalCycleService(WovenDbContext db, OpenAiRewriteService openAi)
    {
        _db = db;
        _openAi = openAi;
    }

    // Returns: due?, version, hardBlock
    public async Task<(bool due, int? version, bool hardBlock)> GetDueStateAsync(int userId, CancellationToken ct)
    {
        // ✅ Active unanswered set?
        var active = await _db.UserFoundationalQuestionSets
            .Where(x => x.UserId == userId && x.AnsweredAt == null)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        if (active != null)
        {
            // If deferred, don't redirect until deferral ends
            if (active.DeferredUntil.HasValue && DateTime.UtcNow < active.DeferredUntil.Value)
                return (false, null, false);

            // v1 = hard-block, v2+ = soft-block
            var hard = active.Version == 1;

            // ✅ Ensure questions exist (safety) — IMPORTANT: NO OpenAI call here
            if (string.IsNullOrWhiteSpace(active.QuestionsJson) || active.QuestionsJson == "[]")
            {
                var bank = FoundationalQuestionBank.GetBaseFiveForVersion(active.Version);

                active.QuestionsJson = JsonSerializer.Serialize(
                    bank.Select(q => new { id = q.Id, text = q.Text, pillars = q.Pillars })
                );

                active.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            return (true, active.Version, hard);
        }

        // ✅ Find last answered
        var lastAnswered = await _db.UserFoundationalQuestionSets
            .Where(x => x.UserId == userId && x.AnsweredAt != null)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        // ✅ No history => create v1 now (hard-block)
        if (lastAnswered == null)
        {
            await CreateSet(userId, version: 1, ct);
            return (true, 1, true);
        }

        // ✅ Not yet eligible
        if (DateTime.UtcNow < lastAnswered.ExpiresAt)
            return (false, null, false);

        // ✅ Create next
        var nextVersion = lastAnswered.Version + 1;
        await CreateSet(userId, nextVersion, ct);

        // v2+ soft-block
        return (true, nextVersion, false);
    }

    public Task<UserFoundationalQuestionSet?> GetActiveAsync(int userId, CancellationToken ct)
        => _db.UserFoundationalQuestionSets
            .Where(x => x.UserId == userId && x.AnsweredAt == null)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

    public async Task DeferActiveAsync(int userId, TimeSpan duration, CancellationToken ct)
    {
        var active = await GetActiveAsync(userId, ct);
        if (active == null) return;

        // v1 cannot defer
        if (active.Version == 1) return;

        active.DeferredUntil = DateTime.UtcNow.Add(duration);
        active.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ✅ Call this from your PUT /onboarding/foundational endpoint AFTER saving answers.
    /// It sets ExpiresAt to control when the next version becomes eligible.
    /// </summary>
    public DateTime ComputeNextEligibleAt(int version, DateTime answeredAtUtc)
    {
        var days = version switch
        {
            1 => V1_INTERVAL_DAYS,
            2 => V2_INTERVAL_DAYS,
            _ => V3PLUS_INTERVAL_DAYS
        };

        return answeredAtUtc.AddDays(days);
    }

    private async Task CreateSet(int userId, int version, CancellationToken ct)
    {
        // ✅ Bank = source of truth (stable IDs + stable meaning)
        var bank = FoundationalQuestionBank.GetBaseFiveForVersion(version);

        // ✅ Load user context from DB for personalization
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

        var style = "warm, human, dating app";

        // ✅ Rewrite ONCE per version with user context for personalization
        var rewritten = await _openAi.RewriteAsync(
            bank,
            new OpenAiRewriteService.RewriteUserContext(firstName, userProfile?.Gender, intent, userId),
            style,
            ct
        );

        var set = new UserFoundationalQuestionSet
        {
            UserId = userId,
            Version = version,

            // ✅ Freeze rewritten questions for this version
            QuestionsJson = JsonSerializer.Serialize(
                rewritten.Select(q => new { id = q.Id, text = q.Text, pillars = q.Pillars })
            ),

            AnswersJson = "[]",
            SignalsJson = "{}", // placeholder for extraction later

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,

            // ✅ IMPORTANT: ExpiresAt should mean "next eligible at"
            // For an active set, it should NOT block creating the next version.
            // The correct value is set on answer time.
            ExpiresAt = DateTime.UtcNow,

            AnsweredAt = null,
            DeferredUntil = null
        };

        _db.UserFoundationalQuestionSets.Add(set);
        await _db.SaveChangesAsync(ct);
    }

    private static string? ExtractFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }
}
