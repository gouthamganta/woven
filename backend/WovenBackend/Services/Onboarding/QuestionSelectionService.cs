using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Onboarding;

public interface IQuestionSelectionService
{
    Task<List<SelectedQuestionDto>> SelectQuestionsForUser(int userId, CancellationToken ct = default);
    Task RecordQuestionsShown(int userId, List<Guid> questionIds, CancellationToken ct = default);
}

public class QuestionSelectionService : IQuestionSelectionService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<QuestionSelectionService> _logger;

    // Target distribution: how many questions per pillar
    private readonly Dictionary<string, int> _targetDistribution = new()
    {
        ["Energy"] = 2,
        ["Stability"] = 1,
        ["Ambition"] = 2,
        ["Curiosity"] = 1,
        ["Communication"] = 2,
        ["Lifestyle"] = 1,
        ["Values"] = 2,
        ["Affection"] = 1
    };

    public QuestionSelectionService(WovenDbContext db, ILogger<QuestionSelectionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<SelectedQuestionDto>> SelectQuestionsForUser(
        int userId,
        CancellationToken ct = default)
    {
        var selected = new List<SelectedQuestionDto>();

        foreach (var (pillar, count) in _targetDistribution)
        {
            // Get unseen questions for this pillar
            var unseenQuestions = await _db.FoundationalQuestions
                .Where(q => q.Pillar == pillar && q.IsActive)
                .Where(q => !_db.UserQuestionHistory
                    .Any(h => h.UserId == userId && h.QuestionId == q.Id))
                .OrderBy(_ => Guid.NewGuid()) // Random
                .Take(count)
                .ToListAsync(ct);

            // If not enough unseen, get least recently shown
            if (unseenQuestions.Count < count)
            {
                var needed = count - unseenQuestions.Count;
                var leastRecentlyShown = await _db.FoundationalQuestions
                    .Where(q => q.Pillar == pillar && q.IsActive)
                    .Where(q => !unseenQuestions.Select(u => u.Id).Contains(q.Id))
                    .OrderBy(q => _db.UserQuestionHistory
                        .Where(h => h.UserId == userId && h.QuestionId == q.Id)
                        .Max(h => (DateTime?)h.ShownAt) ?? DateTime.MinValue)
                    .Take(needed)
                    .ToListAsync(ct);

                unseenQuestions.AddRange(leastRecentlyShown);
            }

            // Add to selected list
            foreach (var question in unseenQuestions)
            {
                selected.Add(new SelectedQuestionDto(
                    QuestionId: question.Id,
                    Pillar: question.Pillar,
                    GameType: question.GameType,
                    QuestionData: question.QuestionData,
                    Source: question.SourceInstrument
                ));
            }
        }

        // Shuffle to randomize order
        var shuffled = selected.OrderBy(_ => Guid.NewGuid()).ToList();

        _logger.LogInformation(
            "[QuestionSelection] Selected {Count} questions for user {UserId}",
            shuffled.Count, userId);

        return shuffled;
    }

    public async Task RecordQuestionsShown(
        int userId,
        List<Guid> questionIds,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        foreach (var questionId in questionIds)
        {
            _db.UserQuestionHistory.Add(new UserQuestionHistory
            {
                UserId = userId,
                QuestionId = questionId,
                ShownAt = now
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[QuestionSelection] Recorded {Count} questions shown to user {UserId}",
            questionIds.Count, userId);
    }
}

public record SelectedQuestionDto(
    Guid QuestionId,
    string Pillar,
    string GameType,
    JsonDocument QuestionData,
    string Source
);
