using System.Security.Claims;
using WovenBackend.Services.Seasons;

namespace WovenBackend.Endpoints;

public static class SeasonEndpoints
{
    private record SeasonResponseItem(string PillarId, string QuestionId, string Response);

    public static void MapSeasonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/seasons").RequireAuthorization();

        // GET /seasons/current
        group.MapGet("/current", async (
            ISeasonService seasons,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);
            var result = await seasons.GetCurrentSeasonAsync(userId, ct);
            var signaturePrompt = await seasons.GetSignaturePromptAsync(userId, ct);

            return Results.Ok(new
            {
                season = result.Season,
                userStatus = result.UserStatus,
                responseCount = result.ResponseCount,
                signaturePrompt
            });
        });

        // PUT /seasons/current/responses
        group.MapPut("/current/responses", async (
            List<SeasonResponseItem> body,
            ISeasonService seasons,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http.User);

            if (body is null || body.Count == 0)
                return Results.BadRequest(new { error = "RESPONSES_REQUIRED" });

            try
            {
                var requests = body
                    .Select(r => new SeasonResponseRequest(r.PillarId, r.QuestionId, r.Response))
                    .ToList();

                await seasons.SubmitSeasonResponsesAsync(userId, requests, ct);

                var result = await seasons.GetCurrentSeasonAsync(userId, ct);
                var nextSeasonDate = result.Season?.EndDate.AddDays(1);

                return Results.Ok(new { submitted = true, nextSeasonDate });
            }
            catch (InvalidOperationException ex) when (ex.Message == "NO_ACTIVE_SEASON")
            {
                return Results.BadRequest(new { error = "NO_ACTIVE_SEASON" });
            }
        });
    }

    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;
        throw new UnauthorizedAccessException("Missing user id claim");
    }
}
