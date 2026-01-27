using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities.Games;
using WovenBackend.Services.Games;

namespace WovenBackend.Endpoints;

public static class GameEndpoints
{
    public record CreateGameRequest(string GameType);
    public record SubmitAnswersRequest(Dictionary<string, string> Answers);

    public static void MapGameEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/games");
        group.RequireAuthorization();

        // ----------------------------------------------------
        // GET /games/matches/{matchId}/availability
        // ----------------------------------------------------
        group.MapGet("/matches/{matchId:guid}/availability", async (
            Guid matchId,
            IGameService gameService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var availability = await gameService.CheckAvailabilityAsync(matchId, userId, ct);

            return Results.Ok(new
            {
                available = availability.Available,
                gamesRemaining = availability.GamesRemaining,
                reason = availability.Reason,
                games = new[]
                {
                    new
                    {
                        type = "KNOW_ME",
                        name = "Know Me",
                        description = "Guess what they picked",
                        duration = "2 min",
                        icon = "🎯"
                    },
                    new
                    {
                        type = "RED_GREEN_FLAG",
                        name = "Red / Green Flag",
                        description = "React to scenarios and learn each other fast",
                        duration = "2 min",
                        icon = "🚦"
                    }
                }
            });
        });

        // ----------------------------------------------------
        // POST /games/matches/{matchId}/sessions
        // ----------------------------------------------------
        group.MapPost("/matches/{matchId:guid}/sessions", async (
            Guid matchId,
            CreateGameRequest req,
            IGameService gameService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            if (!Enum.TryParse<GameSessionType>(req.GameType, true, out var gameType))
                return Results.BadRequest(new { error = "Invalid game type" });

            try
            {
                var session = await gameService.CreateSessionAsync(matchId, userId, gameType, ct);
                return Results.Ok(session);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });

        // ----------------------------------------------------
        // POST /games/sessions/{sessionId}/accept
        // ----------------------------------------------------
        group.MapPost("/sessions/{sessionId:guid}/accept", async (
            Guid sessionId,
            IGameService gameService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var accepted = await gameService.AcceptSessionAsync(sessionId, userId, ct);

            return accepted
                ? Results.Ok(new { status = "ACTIVE", message = "Game started!" })
                : Results.BadRequest(new { error = "Cannot accept game" });
        });

        // ----------------------------------------------------
        // POST /games/sessions/{sessionId}/reject
        // ----------------------------------------------------
        group.MapPost("/sessions/{sessionId:guid}/reject", async (
            Guid sessionId,
            IGameService gameService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var rejected = await gameService.RejectSessionAsync(sessionId, userId, ct);

            return rejected
                ? Results.Ok(new { status = "REJECTED" })
                : Results.BadRequest(new { error = "Cannot reject game" });
        });

        // ----------------------------------------------------
        // GET /games/sessions/{sessionId}/round
        // ----------------------------------------------------
        group.MapGet("/sessions/{sessionId:guid}/round", async (
            Guid sessionId,
            IGameService gameService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var round = await gameService.GetCurrentRoundAsync(sessionId, userId, ct);

            return round == null
                ? Results.NotFound(new { error = "Round not found or game not active" })
                : Results.Ok(round);
        });

        // ----------------------------------------------------
        // POST /games/sessions/{sessionId}/answers
        // ----------------------------------------------------
        group.MapPost("/sessions/{sessionId:guid}/answers", async (
            Guid sessionId,
            SubmitAnswersRequest req,
            IGameService gameService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            try
            {
                var result = await gameService.SubmitAnswersAsync(sessionId, userId, req.Answers, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ----------------------------------------------------
        // POST /games/sessions/{sessionId}/target-answers
        // ----------------------------------------------------
        group.MapPost("/sessions/{sessionId:guid}/target-answers", async (
            Guid sessionId,
            SubmitAnswersRequest req,
            IGameService gameService,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            try
            {
                var result = await gameService.SubmitTargetAnswersAsync(sessionId, userId, req.Answers, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // ----------------------------------------------------
        // GET /games/sessions/{sessionId}/result
        // ----------------------------------------------------
        group.MapGet("/sessions/{sessionId:guid}/result", async (
            Guid sessionId,
            IGameService gameService,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var result = await gameService.GetFinalResultAsync(sessionId, ct);
            if (result == null)
                return Results.NotFound(new { error = "Result not found" });

            var userA = await db.Users.FirstOrDefaultAsync(u => u.Id == result.UserAId, ct);
            var userB = await db.Users.FirstOrDefaultAsync(u => u.Id == result.UserBId, ct);

            return Results.Ok(new
            {
                sessionId = result.SessionId,
                gameType = result.GameType,
                yourScore = userId == result.UserAId ? result.UserAScore : result.UserBScore,
                theirScore = userId == result.UserAId ? result.UserBScore : result.UserAScore,
                youWon = result.WinnerUserId == userId,
                isTie = result.WinnerUserId == null,
                aiInsight = result.AiInsight,
                userAName = userA?.FullName,
                userBName = userB?.FullName
            });
        });

        // ----------------------------------------------------
        // GET /games/matches/{matchId}/active
        // ----------------------------------------------------
        group.MapGet("/matches/{matchId:guid}/active", async (
            Guid matchId,
            WovenDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            var activeSession = await db.GameSessions
                .Where(g => g.MatchId == matchId &&
                           (g.Status == GameSessionStatus.PENDING.ToString() ||
                            g.Status == GameSessionStatus.ACTIVE.ToString()))
                .OrderByDescending(g => g.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (activeSession == null)
                return Results.Ok(new { hasActive = false });

            return Results.Ok(new
            {
                hasActive = true,
                sessionId = activeSession.Id,
                gameType = activeSession.GameType,
                status = activeSession.Status,
                expiresAt = activeSession.ExpiresAt,
                isInitiator = activeSession.InitiatorUserId == userId
            });
        });
    }

    // ----------------------------------------------------
    // Helpers
    // ----------------------------------------------------
    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id))
            return id;

        var sub = user.FindFirstValue("sub")
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (int.TryParse(sub, out id))
            return id;

        throw new UnauthorizedAccessException("Missing user id claim");
    }
}
