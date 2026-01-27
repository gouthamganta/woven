using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WovenBackend.Data;
using WovenBackend.Services;

namespace WovenBackend.Endpoints;

public static class DynamicIntakeEndpoints
{
    public static IEndpointRouteBuilder MapDynamicIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/intake/dynamic")
            .WithTags("Dynamic Intake")
            .RequireAuthorization();

        // ✅ GET /intake/dynamic/current
        group.MapGet("/current", async (
            WovenDbContext db,
            DynamicIntakeCycleService cycles,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            int userId;
            try { userId = GetUserId(user); }
            catch { return Results.Unauthorized(); }

            var set = await cycles.GetOrCreateCurrentAsync(userId, ct);

            object[] questions;
            try
            {
                questions = JsonSerializer.Deserialize<object[]>(
                    set.VariantJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? Array.Empty<object>();
            }
            catch
            {
                questions = Array.Empty<object>();
            }

            object answersObj;
            try
            {
                answersObj = JsonSerializer.Deserialize<object>(
                    set.AnswersJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new { };
            }
            catch
            {
                answersObj = new { };
            }

            return Results.Ok(new
            {
                cycleId = set.CycleId,
                cycleStartUtc = set.CycleStartUtc,
                cycleEndUtc = set.CycleEndUtc,
                answered = set.AnsweredAtUtc != null,
                answers = answersObj,
                questions
            });
        })
        .WithName("DynamicIntakeCurrent");

        // ✅ PUT /intake/dynamic
        group.MapPut("", async (
            DynamicIntakeSubmitRequest req,
            WovenDbContext db,
            DynamicIntakeCycleService cycles,
            ClaimsPrincipal user,
            HttpContext http, // ✅ ADD THIS (needed for RequestServices)
            CancellationToken ct) =>
        {
            int userId;
            try { userId = GetUserId(user); }
            catch { return Results.Unauthorized(); }

            var set = await cycles.GetOrCreateCurrentAsync(userId, ct);

            if (req?.Answers == null)
                return Results.BadRequest(new { error = "answers is required" });

            // ✅ Ensure canonical keys exist
            if (!req.Answers.TryGetValue("d1_battery", out var battery) ||
                !req.Answers.TryGetValue("d2_tone", out var tone) ||
                !req.Answers.TryGetValue("d3_role", out var role))
            {
                return Results.BadRequest(new { error = "answers must include d1_battery, d2_tone, d3_role" });
            }

            battery = (battery ?? "").Trim().ToLowerInvariant();
            tone = (tone ?? "").Trim().ToLowerInvariant();
            role = (role ?? "").Trim().ToLowerInvariant();

            // ✅ Validate allowed values from bank
            if (!DynamicQuestionBank.KeysFor("d1_battery").Contains(battery))
                return Results.BadRequest(new { error = "Invalid d1_battery" });

            if (!DynamicQuestionBank.KeysFor("d2_tone").Contains(tone))
                return Results.BadRequest(new { error = "Invalid d2_tone" });

            if (!DynamicQuestionBank.KeysFor("d3_role").Contains(role))
                return Results.BadRequest(new { error = "Invalid d3_role" });

            set.AnswersJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["d1_battery"] = battery,
                ["d2_tone"] = tone,
                ["d3_role"] = role
            });

            var (featuresJson, mappingVersion) = DynamicIntakeCycleService.ComputeFeaturesJson(battery, tone, role);

            set.FeaturesJson = featuresJson;
            set.MappingVersion = mappingVersion;

            set.AnsweredAtUtc ??= DateTime.UtcNow;
            set.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            // ✅ TRIGGER: Update user vector pulse section (non-blocking)
            // Use IServiceScopeFactory to create a new scope for background work
            try
            {
                var scopeFactory = http.RequestServices
                    .GetRequiredService<IServiceScopeFactory>();
                var answersSnapshot = new Dictionary<string, string>(req.Answers);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var vectorBuilder = scope.ServiceProvider
                            .GetRequiredService<WovenBackend.Services.Matchmaking.IUserVectorBuilder>();
                        await vectorBuilder.UpdatePulseAsync(userId, answersSnapshot, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        using var scope = scopeFactory.CreateScope();
                        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                        logger.LogError(ex, "[DynamicIntake] Failed to update pulse for user {UserId}", userId);
                    }
                });
            }
            catch
            {
                // Silent fail - pulse update is non-critical
            }

            return Results.Ok(new
            {
                ok = true,
                cycleId = set.CycleId,
                answeredAtUtc = set.AnsweredAtUtc,
                mappingVersion = set.MappingVersion
            });
        })
        .WithName("DynamicIntakeSubmit");

        return app;
    }

    public class DynamicIntakeSubmitRequest
    {
        public Dictionary<string, string>? Answers { get; set; }
    }

    // ✅ SAME AS MomentsEndpoints: uid first, then sub
    private static int GetUserId(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;

        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;

        throw new UnauthorizedAccessException("Missing user id claim");
    }
}
