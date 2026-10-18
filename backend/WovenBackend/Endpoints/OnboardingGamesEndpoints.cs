using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Endpoints;

public static class OnboardingGamesEndpoints
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
    public record GameAnswerDto(
        [property: JsonPropertyName("gameId")] string GameId,
        [property: JsonPropertyName("pillar")] string Pillar,
        [property: JsonPropertyName("answer")] JsonElement Answer
    );

    public record SubmitGamesRequest(
        [property: JsonPropertyName("answers")] GameAnswerDto[] Answers
    );

    public static void MapOnboardingGamesEndpoints(this WebApplication app)
    {
        // POST /onboarding/games/submit
        app.MapPost("/onboarding/games/submit", async (
            [FromBody] SubmitGamesRequest request,
            WovenDbContext db,
            IUserVectorBuilder vectorBuilder,
            ClaimsPrincipal user,
            ILogger<object> logger,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            logger.LogInformation("[OnboardingGames] Received {Count} game answers for user {UserId}",
                request.Answers.Length, userId);

            // Calculate pillar scores from answers
            var pillarScores = CalculatePillarScores(request.Answers, logger);

            logger.LogInformation("[OnboardingGames] Calculated pillar scores for user {UserId}: {Scores}",
                userId, JsonSerializer.Serialize(pillarScores));

            // Load or create user vector
            var vector = await db.UserVectors
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync(ct);

            if (vector == null)
            {
                logger.LogInformation("[OnboardingGames] Creating new vector for user {UserId}", userId);

                // Create initial vector
                vector = new UserVector
                {
                    UserId = userId,
                    Version = 1,
                    VectorJson = "{}",
                    PillarScoresJson = JsonSerializer.Serialize(pillarScores),
                    UpdatedAt = DateTime.UtcNow
                };
                db.UserVectors.Add(vector);
            }
            else
            {
                logger.LogInformation("[OnboardingGames] Updating existing vector (v{Version}) for user {UserId}",
                    vector.Version, userId);

                // Update pillar scores
                vector.PillarScoresJson = JsonSerializer.Serialize(pillarScores);
                vector.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);

            // Rebuild full vector in background (includes embedding generation)
            _ = Task.Run(async () =>
            {
                try
                {
                    await vectorBuilder.BuildAndSaveV1Async(userId, CancellationToken.None);
                    logger.LogInformation("[OnboardingGames] Vector rebuild complete for user {UserId}", userId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[OnboardingGames] Vector rebuild failed for user {UserId}", userId);
                }
            });

            return Results.Ok(new
            {
                message = "Games submitted successfully",
                pillarScores = pillarScores,
                nextRoute = "/onboarding/details"
            });
        })
        .WithName("SubmitOnboardingGames")
        .RequireAuthorization();
    }

    /// <summary>
    /// Calculates 8 pillar scores (0-1 normalized) from game answers.
    /// Each game maps to one pillar.
    /// </summary>
    private static Dictionary<string, double> CalculatePillarScores(
        GameAnswerDto[] answers,
        ILogger logger)
    {
        var scores = new Dictionary<string, double>
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

        foreach (var answer in answers)
        {
            var pillar = answer.Pillar;

            // Determine game type from gameId pattern
            var gameId = answer.GameId.ToLowerInvariant();

            double score = 0.5; // Default neutral

            if (gameId.Contains("spectrum") || gameId.Contains("compass") || gameId.Contains("slider"))
            {
                // Slider game: answer is a number 0-100
                score = CalculateSliderScore(answer.Answer, logger);
            }
            else if (gameId.Contains("auction") || gameId.Contains("ladder") || gameId.Contains("ranking"))
            {
                // Ranking game: answer is array of ranked items
                score = CalculateRankingScore(answer.Answer, logger);
            }
            else if (gameId.Contains("starter") || gameId.Contains("explorer") || gameId.Contains("swipe"))
            {
                // Swipe game: answer is array of selected options
                score = CalculateSwipeScore(answer.Answer, logger);
            }
            else if (gameId.Contains("language") || gameId.Contains("puzzle") || gameId.Contains("selection"))
            {
                // Selection game: answer is array of selected values
                score = CalculateSelectionScore(answer.Answer, logger);
            }

            scores[pillar] = score;

            logger.LogDebug("[OnboardingGames] {Pillar} = {Score:F2} (game: {GameId})",
                pillar, score, gameId);
        }

        return scores;
    }

    private static double CalculateSliderScore(JsonElement answer, ILogger logger)
    {
        try
        {
            // Answer format: { "value": 0-100 }
            if (answer.TryGetProperty("value", out var valueElement))
            {
                var value = valueElement.GetDouble();
                return Math.Clamp(value / 100.0, 0.0, 1.0); // Normalize to 0-1
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[OnboardingGames] Failed to parse slider answer, using neutral");
        }

        return 0.5; // Neutral fallback
    }

    private static double CalculateRankingScore(JsonElement answer, ILogger logger)
    {
        try
        {
            // Answer format: { "ranked": ["item1", "item2", ...] }
            if (answer.TryGetProperty("ranked", out var rankedElement))
            {
                var ranked = JsonSerializer.Deserialize<string[]>(rankedElement.GetRawText());
                if (ranked == null || ranked.Length == 0)
                    return 0.5;

                // Score based on what they ranked highest
                // More items at top = higher score
                // Use inverse positional weighting: first = 1.0, second = 0.8, third = 0.6, etc.
                var weights = new[] { 1.0, 0.8, 0.6, 0.4, 0.2 };
                var totalWeight = 0.0;
                var weightSum = 0.0;

                for (int i = 0; i < Math.Min(ranked.Length, weights.Length); i++)
                {
                    totalWeight += weights[i];
                    weightSum += weights[i];
                }

                // Normalize to 0-1 range
                return Math.Clamp(totalWeight / weightSum, 0.0, 1.0);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[OnboardingGames] Failed to parse ranking answer, using neutral");
        }

        return 0.5; // Neutral fallback
    }

    private static double CalculateSwipeScore(JsonElement answer, ILogger logger)
    {
        try
        {
            // Answer format: { "selected": ["option1", "option2", ...] }
            if (answer.TryGetProperty("selected", out var selectedElement))
            {
                var selected = JsonSerializer.Deserialize<string[]>(selectedElement.GetRawText());
                if (selected == null)
                    return 0.5;

                // Score = (selected count / total options)
                // Swipe games typically have 5 options
                var totalOptions = 5;
                var selectedCount = selected.Length;

                return Math.Clamp((double)selectedCount / totalOptions, 0.0, 1.0);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[OnboardingGames] Failed to parse swipe answer, using neutral");
        }

        return 0.5; // Neutral fallback
    }

    private static double CalculateSelectionScore(JsonElement answer, ILogger logger)
    {
        try
        {
            // Answer format: { "selected": ["value1", "value2", ...] }
            if (answer.TryGetProperty("selected", out var selectedElement))
            {
                var selected = JsonSerializer.Deserialize<string[]>(selectedElement.GetRawText());
                if (selected == null)
                    return 0.5;

                // Score = (selected count / max selectable)
                // Selection games vary: 2-3 max selectable, 5-6 total options
                var selectedCount = selected.Length;

                // Normalize: assume max select is 2-3, so 2 selected = 0.66-1.0
                var maxSelect = 3; // Conservative estimate
                return Math.Clamp((double)selectedCount / maxSelect, 0.0, 1.0);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[OnboardingGames] Failed to parse selection answer, using neutral");
        }

        return 0.5; // Neutral fallback
    }
}
