using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Matchmaking;
using WovenBackend.Services.Onboarding;

namespace WovenBackend.Endpoints;

public static class OnboardingGamesEndpointsV2
{
    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;

        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;

        throw new UnauthorizedAccessException("Missing user id claim");
    }

    // DTOs
    public record GameQuestionDto(
        [property: JsonPropertyName("questionId")] Guid QuestionId,
        [property: JsonPropertyName("pillar")] string Pillar,
        [property: JsonPropertyName("gameType")] string GameType,
        [property: JsonPropertyName("question")] JsonElement QuestionData,
        [property: JsonPropertyName("source")] string Source
    );

    public record GameAnswerDto(
        [property: JsonPropertyName("questionId")] Guid QuestionId,
        [property: JsonPropertyName("pillar")] string Pillar,
        [property: JsonPropertyName("gameType")] string GameType,
        [property: JsonPropertyName("answer")] JsonElement Answer
    );

    public record SubmitGamesRequest(
        [property: JsonPropertyName("answers")] GameAnswerDto[] Answers
    );

    public static void MapOnboardingGamesEndpointsV2(this WebApplication app)
    {
        // GET /onboarding/games/questions
        // Returns 12 random questions from the question bank
        app.MapGet("/onboarding/games/questions", async (
            IQuestionSelectionService questionService,
            ClaimsPrincipal user,
            ILogger<object> logger,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            logger.LogInformation("[OnboardingGamesV2] User {UserId} requesting questions", userId);

            var questions = await questionService.SelectQuestionsForUser(userId, ct);

            // Record that user saw these questions
            await questionService.RecordQuestionsShown(
                userId,
                questions.Select(q => q.QuestionId).ToList(),
                ct);

            var dtos = questions.Select(q => new GameQuestionDto(
                q.QuestionId,
                q.Pillar,
                q.GameType,
                JsonDocument.Parse(q.QuestionData.RootElement.GetRawText()).RootElement,
                q.Source
            )).ToList();

            return Results.Ok(new { questions = dtos, total = dtos.Count });
        })
        .WithName("GetOnboardingQuestionsV2")
        .RequireAuthorization();

        // POST /onboarding/games/submit
        // Uses LLM to interpret answers and compute pillar scores
        app.MapPost("/onboarding/games/submit-v2", async (
            [FromBody] SubmitGamesRequest request,
            WovenDbContext db,
            IGameScoringService scoringService,
            IUserVectorBuilder vectorBuilder,
            ClaimsPrincipal user,
            ILogger<object> logger,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            logger.LogInformation("[OnboardingGamesV2] User {UserId} submitted {Count} answers",
                userId, request.Answers.Length);

            // Fetch question metadata for LLM context
            var questionIds = request.Answers.Select(a => a.QuestionId).ToList();
            var questions = await db.FoundationalQuestions
                .Where(q => questionIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, ct);

            // Build answers for LLM scoring
            var answersForScoring = request.Answers
                .Where(a => questions.ContainsKey(a.QuestionId))
                .Select(a =>
                {
                    var question = questions[a.QuestionId];
                    var questionText = ExtractQuestionText(question.QuestionData);

                    return new GameAnswerForScoring(
                        a.QuestionId,
                        a.Pillar,
                        a.GameType,
                        questionText,
                        JsonDocument.Parse(a.Answer.GetRawText())
                    );
                })
                .ToList();

            // LLM interpretation
            var pillarScores = await scoringService.ScoreAnswersAsync(answersForScoring, ct);

            logger.LogInformation("[OnboardingGamesV2] LLM scored {Count} pillars for user {UserId}",
                pillarScores.Count, userId);

            // Store raw responses + LLM scores
            foreach (var answer in request.Answers)
            {
                if (!questions.ContainsKey(answer.QuestionId)) continue;

                var llmScore = pillarScores.ContainsKey(answer.Pillar)
                    ? (decimal)pillarScores[answer.Pillar].Score
                    : 0.5m;

                var llmInterpretation = pillarScores.ContainsKey(answer.Pillar)
                    ? pillarScores[answer.Pillar].Interpretation
                    : null;

                db.UserGameResponses.Add(new UserGameResponse
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    QuestionId = answer.QuestionId,
                    Pillar = answer.Pillar,
                    GameType = answer.GameType,
                    AnswerJson = JsonDocument.Parse(answer.Answer.GetRawText()),
                    LlmInterpretedScore = llmScore,
                    LlmInterpretation = llmInterpretation,
                    RespondedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync(ct);

            // Convert to format expected by UserVector
            var scoresForVector = pillarScores.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Score
            );

            // Update or create user vector
            var vector = await db.UserVectors
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync(ct);

            if (vector == null)
            {
                vector = new UserVector
                {
                    UserId = userId,
                    Version = 1,
                    VectorJson = "{}",
                    PillarScoresJson = JsonSerializer.Serialize(scoresForVector),
                    UpdatedAt = DateTime.UtcNow
                };
                db.UserVectors.Add(vector);
            }
            else
            {
                vector.PillarScoresJson = JsonSerializer.Serialize(scoresForVector);
                vector.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            // Rebuild full vector in background (includes embedding generation)
            _ = Task.Run(async () =>
            {
                try
                {
                    await vectorBuilder.BuildAndSaveV1Async(userId, CancellationToken.None);
                    logger.LogInformation("[OnboardingGamesV2] Vector rebuild complete for user {UserId}", userId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[OnboardingGamesV2] Vector rebuild failed for user {UserId}", userId);
                }
            });

            // Build response with scores and interpretations
            var pillarResults = pillarScores.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    score = kvp.Value.Score,
                    interpretation = kvp.Value.Interpretation
                }
            );

            return Results.Ok(new
            {
                message = "Games submitted successfully",
                pillarScores = scoresForVector, // For UserVector (backward compat)
                pillars = pillarResults,         // For results page display
                nextRoute = "/onboarding/games/results"
            });
        })
        .WithName("SubmitOnboardingGamesV2")
        .RequireAuthorization();
    }

    private static string ExtractQuestionText(JsonDocument questionData)
    {
        try
        {
            if (questionData.RootElement.TryGetProperty("question", out var question))
            {
                return question.GetString() ?? "Unknown question";
            }
        }
        catch
        {
            // Fall through
        }

        return "Unknown question";
    }
}
