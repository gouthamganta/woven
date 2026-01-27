using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;

namespace WovenBackend.Endpoints;

public static class OnboardingEndpoints
{
    private static int GetUserId(ClaimsPrincipal user)
    {
        // Primary: uid (your JWT)
        var uid = user.FindFirstValue("uid");
        if (int.TryParse(uid, out var id)) return id;

        // Fallback: sub / NameIdentifier (just in case)
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(sub, out id)) return id;

        throw new UnauthorizedAccessException("Missing user id claim");
    }

    // DTOs
    public record LocationDto(string City, string State, double? Lat, double? Lng);

    public record BasicsRequest(
        string? FullName,
        int Age,
        string Gender,
        string[] InterestedIn,
        int DistanceMiles,
        int AgeMin,
        int AgeMax,
        LocationDto Location,
        string? RelationshipStructure // ✅ ADD THIS (optional to not break existing clients)
    );

    public record PhotoDto(string Url, string? Caption, int SortOrder);
    public record PhotosRequest(PhotoDto[] Photos);

    public record IntentRequest(
        string PrimaryIntent,
        string[] Openness,
        string ReflectionSentence
    );

    // ✅ IMPORTANT: Map JSON keys to match stored QuestionsJson: { "id": "...", "text": "..." }
    public record FoundationalQuestionDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text
    );

    // ✅ Keep request as-is (frontend sends QuestionId + Answer)
    public record FoundationalAnswerDto(string QuestionId, string Answer);
    public record FoundationalRequest(FoundationalAnswerDto[] Answers);

    public record OptionalFieldDto(string Key, string Value, VisibilityLevel Visibility);

    public record DetailsRequest(
        string Bio,
        OptionalFieldDto[] OptionalFields,
        string? WeeklyVibe
    );

    // ✅ Public preview DTOs
    public record PublicPreviewIntentDto(string PrimaryIntent, string[] Openness);
    public record PublicPreviewPhotoDto(string Url, string? Caption, int SortOrder);
    public record PublicOptionalFieldPublicDto(string Key, string Value);

    // ✅ Review-only DTOs (stable JSON contracts for UI)
    public record FoundationalStoredAnswerDto(string Id, string A);
    public record FoundationalQaDto(string Id, string Q, string A);

    public static void MapOnboardingEndpoints(this WebApplication app)
    {
        // ✅ 1) GET /onboarding/state
        app.MapGet("/onboarding/state", async (
            WovenDbContext db,
            FoundationalCycleService foundational,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var u = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == userId, ct);

            if (u == null) return Results.Unauthorized();

            // Only trigger recurring foundational cycles AFTER onboarding is complete
            if (u.ProfileStatus == ProfileStatus.COMPLETE)
            {
                var (due, version, hardBlock) = await foundational.GetDueStateAsync(userId, ct);

                if (due)
                {
                    return Results.Ok(new
                    {
                        profileStatus = "FOUNDATIONAL_DUE",
                        nextRoute = "/onboarding/foundational",
                        version,
                        hardBlock,
                        allowSkip = !hardBlock
                    });
                }
            }

            var nextRoute = u.ProfileStatus switch
            {
                ProfileStatus.INCOMPLETE => "/onboarding/start",
                ProfileStatus.WELCOME_DONE => "/onboarding/basics",
                ProfileStatus.BASICS_DONE => "/onboarding/intent",
                ProfileStatus.INTENT_DONE => "/onboarding/foundational",
                ProfileStatus.FOUNDATION_DONE => "/onboarding/details",
                ProfileStatus.DETAILS_DONE => "/onboarding/review",
                ProfileStatus.COMPLETE => "/home",
                _ => "/onboarding/start"
            };

            var completed = u.ProfileStatus switch
            {
                ProfileStatus.INCOMPLETE => Array.Empty<string>(),
                ProfileStatus.WELCOME_DONE => new[] { "welcome" },
                ProfileStatus.BASICS_DONE => new[] { "welcome", "basics" },
                ProfileStatus.INTENT_DONE => new[] { "welcome", "basics", "intent" },
                ProfileStatus.FOUNDATION_DONE => new[] { "welcome", "basics", "intent", "foundational" },
                ProfileStatus.DETAILS_DONE => new[] { "welcome", "basics", "intent", "foundational", "details" },
                ProfileStatus.COMPLETE => new[] { "welcome", "basics", "intent", "foundational", "details", "review" },
                _ => Array.Empty<string>()
            };

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),
                nextRoute,
                completed
            });
        })
        .WithName("OnboardingState")
        .RequireAuthorization();

        // ✅ 2) POST /onboarding/welcome
        app.MapPost("/onboarding/welcome", async (
            WovenDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            if (u.ProfileStatus < ProfileStatus.WELCOME_DONE)
            {
                u.ProfileStatus = ProfileStatus.WELCOME_DONE;
            }

            u.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),
                nextRoute = "/onboarding/basics"
            });
        })
        .WithName("OnboardingWelcome")
        .RequireAuthorization();

        // ✅ 3) PUT /onboarding/basics
        app.MapPut("/onboarding/basics", async (
            BasicsRequest req,
            WovenDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            if (req.Age < 18)
                return Results.BadRequest(new { error = "Age must be 18+" });

            if (req.DistanceMiles < 15 || req.DistanceMiles > 100)
                return Results.BadRequest(new { error = "Distance must be between 15 and 100 miles" });

            if (req.AgeMin < 18)
                return Results.BadRequest(new { error = "AgeMin must be 18+" });

            if (req.AgeMax > 99)
                return Results.BadRequest(new { error = "AgeMax must be 99 or less" });

            if (req.AgeMin > req.AgeMax)
                return Results.BadRequest(new { error = "AgeMin cannot be greater than AgeMax" });

            if (req.InterestedIn == null || req.InterestedIn.Length == 0)
                return Results.BadRequest(new { error = "InterestedIn is required" });

            if (string.IsNullOrWhiteSpace(req.Gender))
                return Results.BadRequest(new { error = "Gender is required" });

            if (req.Location == null ||
                string.IsNullOrWhiteSpace(req.Location.City) ||
                string.IsNullOrWhiteSpace(req.Location.State))
                return Results.BadRequest(new { error = "Location city/state is required" });

            // ✅ STEP 4: Coordinate validation
            if (req.Location.Lat == null || req.Location.Lng == null)
                return Results.BadRequest(new { error = "Location coordinates are required." });

            // Reject 0,0 (invalid placeholder)
            if (req.Location.Lat == 0 && req.Location.Lng == 0)
                return Results.BadRequest(new { error = "Invalid location coordinates." });

            // Reject impossible ranges
            if (req.Location.Lat < -90 || req.Location.Lat > 90 || 
                req.Location.Lng < -180 || req.Location.Lng > 180)
                return Results.BadRequest(new { error = "Invalid location coordinates." });

            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            if (!string.IsNullOrWhiteSpace(req.FullName))
                u.FullName = req.FullName.Trim();

            u.UpdatedAt = DateTime.UtcNow;

            var profile = await db.UserProfiles.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (profile == null)
            {
                profile = new UserProfile
                {
                    UserId = userId,
                    Age = req.Age,
                    Gender = req.Gender.Trim(),
                    City = req.Location.City.Trim(),
                    State = req.Location.State.Trim(),
                    Lat = req.Location.Lat,
                    Lng = req.Location.Lng,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.UserProfiles.Add(profile);
            }
            else
            {
                profile.Age = req.Age;
                profile.Gender = req.Gender.Trim();
                profile.City = req.Location.City.Trim();
                profile.State = req.Location.State.Trim();
                profile.Lat = req.Location.Lat;
                profile.Lng = req.Location.Lng;
                profile.UpdatedAt = DateTime.UtcNow;
            }

            var pref = await db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            var interestedInJson = JsonSerializer.Serialize(req.InterestedIn);

            // ✅ Parse relationship structure (defaults to OPEN)
            var relationshipStructure = WovenBackend.Data.Entities.RelationshipStructure.OPEN;
            if (!string.IsNullOrWhiteSpace(req.RelationshipStructure))
            {
                if (Enum.TryParse<WovenBackend.Data.Entities.RelationshipStructure>(
                    req.RelationshipStructure.Trim(),
                    ignoreCase: true,
                    out var parsed))
                {
                    relationshipStructure = parsed;
                }
            }

            if (pref == null)
            {
                pref = new UserPreference
                {
                    UserId = userId,
                    DistanceMiles = req.DistanceMiles,
                    AgeMin = req.AgeMin,
                    AgeMax = req.AgeMax,
                    InterestedInJson = interestedInJson,
                    RelationshipStructure = relationshipStructure, // ✅ ADD THIS
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.UserPreferences.Add(pref);
            }
            else
            {
                pref.DistanceMiles = req.DistanceMiles;
                pref.AgeMin = req.AgeMin;
                pref.AgeMax = req.AgeMax;
                pref.InterestedInJson = interestedInJson;
                pref.RelationshipStructure = relationshipStructure; // ✅ ADD THIS
                pref.UpdatedAt = DateTime.UtcNow;
            }

            if (u.ProfileStatus < ProfileStatus.BASICS_DONE)
                u.ProfileStatus = ProfileStatus.BASICS_DONE;

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),
                nextRoute = "/onboarding/intent"
            });
        })
        .WithName("OnboardingBasics")
        .RequireAuthorization();

        // ✅ 4) PUT /onboarding/photos
        app.MapPut("/onboarding/photos", async (
            PhotosRequest req,
            WovenDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            if (req.Photos == null || req.Photos.Length < 3 || req.Photos.Length > 6)
                return Results.BadRequest(new { error = "Photos must be between 3 and 6" });

            foreach (var p in req.Photos)
            {
                if (string.IsNullOrWhiteSpace(p.Url))
                    return Results.BadRequest(new { error = "Photo URL is required" });

                if (p.Caption != null && p.Caption.Length > 40)
                    return Results.BadRequest(new { error = "Caption must be 40 characters or less" });
            }

            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            var existing = await db.UserPhotos.Where(x => x.UserId == userId).ToListAsync(ct);
            if (existing.Count > 0)
                db.UserPhotos.RemoveRange(existing);

            var newPhotos = req.Photos
                .OrderBy(x => x.SortOrder)
                .Select(x => new UserPhoto
                {
                    UserId = userId,
                    Url = x.Url.Trim(),
                    Caption = string.IsNullOrWhiteSpace(x.Caption) ? null : x.Caption.Trim(),
                    SortOrder = x.SortOrder,
                    CreatedAt = DateTime.UtcNow
                })
                .ToList();

            db.UserPhotos.AddRange(newPhotos);

            u.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                message = "Photos saved",
                count = newPhotos.Count
            });
        })
        .WithName("OnboardingPhotos")
        .RequireAuthorization();

        // ✅ 5) PUT /onboarding/intent
        app.MapPut("/onboarding/intent", async (
            IntentRequest req,
            WovenDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            if (string.IsNullOrWhiteSpace(req.PrimaryIntent))
                return Results.BadRequest(new { error = "PrimaryIntent is required" });

            if (req.Openness == null)
                req = req with { Openness = Array.Empty<string>() };

            if (string.IsNullOrWhiteSpace(req.ReflectionSentence))
                return Results.BadRequest(new { error = "ReflectionSentence is required" });

            if (req.ReflectionSentence.Length > 200)
                return Results.BadRequest(new { error = "ReflectionSentence must be 200 characters or less" });

            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            var opennessJson = JsonSerializer.Serialize(req.Openness);

            var intent = await db.UserIntents.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (intent == null)
            {
                intent = new UserIntent
                {
                    UserId = userId,
                    PrimaryIntent = req.PrimaryIntent.Trim(),
                    OpennessJson = opennessJson,
                    ReflectionSentence = req.ReflectionSentence.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.UserIntents.Add(intent);
            }
            else
            {
                intent.PrimaryIntent = req.PrimaryIntent.Trim();
                intent.OpennessJson = opennessJson;
                intent.ReflectionSentence = req.ReflectionSentence.Trim();
                intent.UpdatedAt = DateTime.UtcNow;
            }

            if (u.ProfileStatus < ProfileStatus.INTENT_DONE)
                u.ProfileStatus = ProfileStatus.INTENT_DONE;

            u.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),
                nextRoute = "/onboarding/foundational"
            });
        })
        .WithName("OnboardingIntent")
        .RequireAuthorization();

        // ✅ 6A) GET /onboarding/foundational/questions
        app.MapGet("/onboarding/foundational/questions", async (
            WovenDbContext db,
            FoundationalCycleService foundational,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var (due, _, _) = await foundational.GetDueStateAsync(userId, ct);
            if (!due)
            {
                var maybeActive = await foundational.GetActiveAsync(userId, ct);
                if (maybeActive == null)
                    return Results.BadRequest(new { error = "Not due for foundational questions." });
            }

            var set = await foundational.GetActiveAsync(userId, ct);
            if (set == null) return Results.BadRequest(new { error = "No active foundational question set." });

            if (string.IsNullOrWhiteSpace(set.QuestionsJson) || set.QuestionsJson == "[]")
            {
                var fallback = new[]
                {
                    new { id = "q1", text = "When you have a free evening, what do you usually crave doing most?", pillars = new[] { "Lifestyle", "Energy" } },
                    new { id = "q2", text = "What kind of connection makes you feel most comfortable with someone new?", pillars = new[] { "Connection", "Attachment" } },
                    new { id = "q3", text = "What's a small habit or routine that makes your life better?", pillars = new[] { "Habits", "Stability" } },
                    new { id = "q4", text = "What's something you're proud of that doesn't show up on a resume?", pillars = new[] { "Identity", "SelfWorth" } },
                    new { id = "q5", text = "What does a good relationship feel like to you in everyday moments?", pillars = new[] { "Relationship", "ConflictRepair" } }
                };

                set.QuestionsJson = JsonSerializer.Serialize(fallback);
                set.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync(ct);
            }

            FoundationalQuestionDto[] questions;
            try
            {
                questions = JsonSerializer.Deserialize<FoundationalQuestionDto[]>(
                    set.QuestionsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? Array.Empty<FoundationalQuestionDto>();
            }
            catch
            {
                questions = Array.Empty<FoundationalQuestionDto>();
            }

            var needsHeal =
                questions.Length != 5 ||
                questions.Any(q => string.IsNullOrWhiteSpace(q.Id) || string.IsNullOrWhiteSpace(q.Text));

            if (needsHeal)
            {
                try
                {
                    var bank = FoundationalQuestionBank.GetBaseFiveForVersion(set.Version);

                    set.QuestionsJson = JsonSerializer.Serialize(
                        bank.Select(q => new { id = q.Id, text = q.Text, pillars = q.Pillars })
                    );

                    set.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    questions = bank.Select(q => new FoundationalQuestionDto(q.Id, q.Text)).ToArray();
                }
                catch
                {
                    var fallbackHeal = new[]
                    {
                        new FoundationalQuestionDto("q1", "When you have a free evening, what do you usually crave doing most?"),
                        new FoundationalQuestionDto("q2", "What kind of connection makes you feel most comfortable with someone new?"),
                        new FoundationalQuestionDto("q3", "What's a small habit or routine that makes your life better?"),
                        new FoundationalQuestionDto("q4", "What's something you're proud of that doesn't show up on a resume?"),
                        new FoundationalQuestionDto("q5", "What does a good relationship feel like to you in everyday moments?")
                    };

                    questions = fallbackHeal;
                }
            }

            return Results.Ok(new
            {
                version = set.Version,
                questions
            });
        })
        .WithName("FoundationalQuestions")
        .RequireAuthorization();

        // ✅ 6) PUT /onboarding/foundational
        app.MapPut("/onboarding/foundational", async (
            FoundationalRequest req,
            WovenDbContext db,
            FoundationalCycleService foundational,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            if (req.Answers == null || req.Answers.Length != 5)
                return Results.BadRequest(new { error = "Exactly 5 answers are required" });

            foreach (var a in req.Answers)
            {
                if (string.IsNullOrWhiteSpace(a.QuestionId) || string.IsNullOrWhiteSpace(a.Answer))
                    return Results.BadRequest(new { error = "QuestionId and Answer cannot be empty" });

                if (a.Answer.Trim().Length > 400)
                    return Results.BadRequest(new { error = "Each answer must be 400 characters or less" });
            }

            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            var set = await foundational.GetActiveAsync(userId, ct);
            if (set == null) return Results.BadRequest(new { error = "No active foundational question set." });

            FoundationalQuestionDto[] stored;
            try
            {
                stored = JsonSerializer.Deserialize<FoundationalQuestionDto[]>(
                    set.QuestionsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? Array.Empty<FoundationalQuestionDto>();
            }
            catch
            {
                return Results.BadRequest(new { error = "Stored questions are invalid. Please reload." });
            }

            if (stored.Length != 5)
                return Results.BadRequest(new { error = "Question set not ready yet. Reload the page." });

            var storedIds = new HashSet<string>(stored.Select(q => q.Id));
            var submittedIds = req.Answers.Select(a => a.QuestionId.Trim()).ToArray();

            if (submittedIds.Distinct().Count() != 5)
                return Results.BadRequest(new { error = "Duplicate QuestionIds submitted." });

            if (submittedIds.Any(id => !storedIds.Contains(id)))
                return Results.BadRequest(new { error = "Answers include invalid QuestionIds." });

            set.AnswersJson = JsonSerializer.Serialize(
                req.Answers.Select(a => new { id = a.QuestionId.Trim(), a = a.Answer.Trim() })
            );

            set.AnsweredAt = DateTime.UtcNow;
            set.DeferredUntil = null;

            set.ExpiresAt = DateTime.UtcNow.AddDays(
                set.Version == 1 ? 15 :
                set.Version == 2 ? 45 :
                60
            );

            set.UpdatedAt = DateTime.UtcNow;

            if (u.ProfileStatus < ProfileStatus.FOUNDATION_DONE)
                u.ProfileStatus = ProfileStatus.FOUNDATION_DONE;

            u.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            var next = u.ProfileStatus == ProfileStatus.COMPLETE ? "/home" : "/onboarding/details";

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),
                nextRoute = next
            });
        })
        .WithName("OnboardingFoundational")
        .RequireAuthorization();

        // ✅ 7) PUT /onboarding/details
        app.MapPut("/onboarding/details", async (
            DetailsRequest req,
            WovenDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            if (string.IsNullOrWhiteSpace(req.Bio) || req.Bio.Length > 200)
                return Results.BadRequest(new { error = "Bio is required and must be 200 characters or less" });

            var allowedDetailKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "job","education","school","pets","habits","hobbies","children","languages","zodiac","diet","hometown"
            };

            var allowedPreferenceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pref_ethnicity","pref_religion","pref_height","pref_work","pref_smoking","pref_drinking","pref_workout"
            };

            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            var existingBio = await db.UserOptionalFields
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Key == "bio", ct);

            if (existingBio == null)
            {
                db.UserOptionalFields.Add(new UserOptionalField
                {
                    UserId = userId,
                    Key = "bio",
                    Value = req.Bio.Trim(),
                    Visibility = VisibilityLevel.Public,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existingBio.Value = req.Bio.Trim();
                existingBio.UpdatedAt = DateTime.UtcNow;
            }

            var existing = await db.UserOptionalFields
                .Where(x => x.UserId == userId && x.Key != "bio")
                .ToListAsync(ct);

            if (existing.Count > 0)
                db.UserOptionalFields.RemoveRange(existing);

            foreach (var f in req.OptionalFields ?? Array.Empty<OptionalFieldDto>())
            {
                var key = (f.Key ?? "").Trim();
                var value = (f.Value ?? "").Trim();

                if (string.IsNullOrWhiteSpace(key))
                    return Results.BadRequest(new { error = "Optional field key is required" });

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var isPreference = key.StartsWith("pref_", StringComparison.OrdinalIgnoreCase);

                if (isPreference)
                {
                    if (!allowedPreferenceKeys.Contains(key))
                        return Results.BadRequest(new { error = $"Invalid preference key: {key}" });

                    db.UserOptionalFields.Add(new UserOptionalField
                    {
                        UserId = userId,
                        Key = key,
                        Value = value,
                        Visibility = VisibilityLevel.MatchingOnly,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    if (!allowedDetailKeys.Contains(key))
                        return Results.BadRequest(new { error = $"Invalid field key: {key}" });

                    db.UserOptionalFields.Add(new UserOptionalField
                    {
                        UserId = userId,
                        Key = key,
                        Value = value,
                        Visibility = f.Visibility,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(req.WeeklyVibe))
            {
                var vibe = await db.UserWeeklyVibes.FirstOrDefaultAsync(x => x.UserId == userId, ct);

                if (vibe == null)
                {
                    db.UserWeeklyVibes.Add(new UserWeeklyVibe
                    {
                        UserId = userId,
                        Text = req.WeeklyVibe.Trim(),
                        ExpiresAt = DateTime.UtcNow.AddDays(7),
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    vibe.Text = req.WeeklyVibe.Trim();
                    vibe.ExpiresAt = DateTime.UtcNow.AddDays(7);
                }
            }
            else
            {
                var vibe = await db.UserWeeklyVibes.FirstOrDefaultAsync(x => x.UserId == userId, ct);
                if (vibe != null) db.UserWeeklyVibes.Remove(vibe);
            }

            if (u.ProfileStatus < ProfileStatus.DETAILS_DONE)
                u.ProfileStatus = ProfileStatus.DETAILS_DONE;

            u.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),
                nextRoute = "/onboarding/review"
            });
        })
        .WithName("OnboardingDetails")
        .RequireAuthorization();

        // ✅ 8) GET /onboarding/review
        app.MapGet("/onboarding/review", async (
            WovenDbContext db,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            var pref = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            var photos = await db.UserPhotos.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            var intent = await db.UserIntents.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);

            var foundationalSet = await db.UserFoundationalQuestionSets.AsNoTracking()
                .Where(x => x.UserId == userId && x.AnsweredAt != null)
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync(ct);

            var optional = await db.UserOptionalFields.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
            var vibe = await db.UserWeeklyVibes.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);

            string[] interestedIn = Array.Empty<string>();
            if (pref != null && !string.IsNullOrWhiteSpace(pref.InterestedInJson))
            {
                try { interestedIn = JsonSerializer.Deserialize<string[]>(pref.InterestedInJson) ?? Array.Empty<string>(); }
                catch { interestedIn = Array.Empty<string>(); }
            }

            string[] openness = Array.Empty<string>();
            if (intent != null && !string.IsNullOrWhiteSpace(intent.OpennessJson))
            {
                try { openness = JsonSerializer.Deserialize<string[]>(intent.OpennessJson) ?? Array.Empty<string>(); }
                catch { openness = Array.Empty<string>(); }
            }

            FoundationalStoredAnswerDto[] fAnswers = Array.Empty<FoundationalStoredAnswerDto>();
            FoundationalQuestionDto[] fQuestions = Array.Empty<FoundationalQuestionDto>();
            FoundationalQaDto[] fQa = Array.Empty<FoundationalQaDto>();

            if (foundationalSet != null)
            {
                if (!string.IsNullOrWhiteSpace(foundationalSet.AnswersJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(foundationalSet.AnswersJson);

                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<FoundationalStoredAnswerDto>();

                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                if (el.ValueKind != JsonValueKind.Object) continue;

                                string? id = null;
                                string? a = null;

                                if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                                    id = idProp.GetString();

                                if (el.TryGetProperty("a", out var aProp) && aProp.ValueKind == JsonValueKind.String)
                                    a = aProp.GetString();

                                if (string.IsNullOrWhiteSpace(id) &&
                                    el.TryGetProperty("questionId", out var qidProp) &&
                                    qidProp.ValueKind == JsonValueKind.String)
                                    id = qidProp.GetString();

                                if (string.IsNullOrWhiteSpace(a) &&
                                    el.TryGetProperty("answer", out var ansProp) &&
                                    ansProp.ValueKind == JsonValueKind.String)
                                    a = ansProp.GetString();

                                if (string.IsNullOrWhiteSpace(a) &&
                                    el.TryGetProperty("Answer", out var ansProp2) &&
                                    ansProp2.ValueKind == JsonValueKind.String)
                                    a = ansProp2.GetString();

                                id = (id ?? "").Trim();
                                a = (a ?? "").Trim();

                                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(a))
                                    list.Add(new FoundationalStoredAnswerDto(id, a));
                            }

                            fAnswers = list.ToArray();
                        }
                    }
                    catch
                    {
                        fAnswers = Array.Empty<FoundationalStoredAnswerDto>();
                    }
                }

                if (!string.IsNullOrWhiteSpace(foundationalSet.QuestionsJson))
                {
                    try
                    {
                        fQuestions = JsonSerializer.Deserialize<FoundationalQuestionDto[]>(
                            foundationalSet.QuestionsJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        ) ?? Array.Empty<FoundationalQuestionDto>();
                    }
                    catch { fQuestions = Array.Empty<FoundationalQuestionDto>(); }
                }

                if (fAnswers.Length > 0 && fQuestions.Length > 0)
                {
                    var qMap = fQuestions
                        .Where(q => !string.IsNullOrWhiteSpace(q.Id))
                        .ToDictionary(q => q.Id.Trim(), q => (q.Text ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

                    fQa = fAnswers
                        .Select(a =>
                        {
                            var id = a.Id.Trim();
                            var qText = qMap.TryGetValue(id, out var qt) ? qt : "";
                            return new FoundationalQaDto(id, qText, (a.A ?? "").Trim());
                        })
                        .ToArray();
                }
            }

            var bio = optional.FirstOrDefault(x => x.Key == "bio")?.Value ?? "";

            var detailOptional = optional
                .Where(x => x.Key != "bio" && !x.Key.StartsWith("pref_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var preferenceFields = optional
                .Where(x => x.Key.StartsWith("pref_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var publicOptional = detailOptional
                .Where(x => x.Visibility == VisibilityLevel.Public)
                .Select(x => new PublicOptionalFieldPublicDto(x.Key, x.Value))
                .ToList();

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),

                self = new
                {
                    u.FullName,
                    u.Email,
                    u.ProfilePhoto,

                    basics = new
                    {
                        age = profile?.Age,
                        gender = profile?.Gender,
                        location = new { city = profile?.City, state = profile?.State },
                        distanceMiles = pref?.DistanceMiles,
                        interestedIn,
                        relationshipStructure = pref?.RelationshipStructure.ToString() // ✅ ADD THIS
                    },

                    photos = photos.Select(p => new { p.Url, p.Caption, p.SortOrder }),

                    intent = intent == null ? null : new
                    {
                        primaryIntent = intent.PrimaryIntent,
                        openness,
                        reflectionSentence = intent.ReflectionSentence
                    },

                    foundational = new
                    {
                        version = foundationalSet?.Version,
                        answers = fAnswers.Select(a => new { id = a.Id, a = a.A }).ToArray(),
                        qa = fQa.Select(x => new { id = x.Id, q = x.Q, a = x.A }).ToArray()
                    },

                    details = new
                    {
                        bio,
                        weeklyVibe = (vibe != null && vibe.ExpiresAt > DateTime.UtcNow) ? vibe.Text : null,

                        optionalFields = detailOptional.Select(x => new { x.Key, x.Value, visibility = x.Visibility.ToString() }),
                        preferenceFields = preferenceFields.Select(x => new { x.Key, x.Value, visibility = x.Visibility.ToString() })
                    }
                },

                publicPreview = new
                {
                    name = u.FullName,
                    age = profile?.Age,
                    gender = profile?.Gender,
                    location = profile == null ? null : $"{profile.City}, {profile.State}",
                    bio,

                    intent = intent == null
                        ? null
                        : new PublicPreviewIntentDto(intent.PrimaryIntent, openness),

                    photos = photos
                        .Select(p => new PublicPreviewPhotoDto(p.Url, p.Caption, p.SortOrder))
                        .OrderBy(p => p.SortOrder)
                        .ToList(),

                    optionalPublic = publicOptional
                }
            });
        })
        .WithName("OnboardingReview")
        .RequireAuthorization();

        // ✅ 9) POST /onboarding/complete
        app.MapPost("/onboarding/complete", async (
            WovenDbContext db,
            ClaimsPrincipal user,
            HttpContext http, // ✅ ADD THIS (needed for RequestServices)
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (u == null) return Results.Unauthorized();

            var profile = await db.UserProfiles.AsNoTracking().AnyAsync(x => x.UserId == userId, ct);
            var pref = await db.UserPreferences.AsNoTracking().AnyAsync(x => x.UserId == userId, ct);
            var photosCount = await db.UserPhotos.AsNoTracking().CountAsync(x => x.UserId == userId, ct);
            var intent = await db.UserIntents.AsNoTracking().AnyAsync(x => x.UserId == userId, ct);
            var foundational = await db.UserFoundationalQuestionSets.AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.AnsweredAt != null, ct);

            var bio = await db.UserOptionalFields.AsNoTracking()
                .Where(x => x.UserId == userId && x.Key == "bio")
                .Select(x => x.Value)
                .FirstOrDefaultAsync(ct);

            if (!profile) return Results.BadRequest(new { error = "Missing basics profile" });
            if (!pref) return Results.BadRequest(new { error = "Missing preferences" });
            if (photosCount < 3) return Results.BadRequest(new { error = "At least 3 photos required" });
            if (!intent) return Results.BadRequest(new { error = "Missing intent" });
            if (!foundational) return Results.BadRequest(new { error = "Missing foundational intake" });
            if (string.IsNullOrWhiteSpace(bio)) return Results.BadRequest(new { error = "Bio is required" });

            u.ProfileStatus = ProfileStatus.COMPLETE;
            u.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            // ✅ TRIGGER: Build initial user vector (v1) (non-blocking) - FIX B (PROD SAFE)
            // IMPORTANT: DO NOT capture scoped services (DbContext) from the request scope.
            // Create a new scope inside the background task.
            try
            {
                var scopeFactory = http.RequestServices.GetRequiredService<IServiceScopeFactory>();
                var logger = http.RequestServices.GetRequiredService<ILogger<Program>>();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var vectorBuilder = scope.ServiceProvider
                            .GetRequiredService<WovenBackend.Services.Matchmaking.IUserVectorBuilder>();

                        await vectorBuilder.BuildAndSaveV1Async(userId, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Onboarding] Failed to build vector for user {UserId}", userId);
                    }
                });
            }
            catch
            {
                // Silent fail - vector build is non-critical for onboarding
            }

            return Results.Ok(new
            {
                profileStatus = u.ProfileStatus.ToString(),
                nextRoute = "/home"
            });
        })
        .WithName("OnboardingComplete")
        .RequireAuthorization();

        // ✅ 10) POST /onboarding/foundational/defer
        app.MapPost("/onboarding/foundational/defer", async (
            WovenDbContext db,
            FoundationalCycleService foundational,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);

            var set = await foundational.GetActiveAsync(userId, ct);
            if (set == null) return Results.BadRequest(new { error = "No active set to defer." });

            if (set.Version == 1)
                return Results.BadRequest(new { error = "v1 cannot be deferred." });

            await foundational.DeferActiveAsync(userId, TimeSpan.FromHours(24), ct);

            return Results.Ok(new
            {
                message = "Deferred",
                nextRoute = "/home"
            });
        })
        .WithName("FoundationalDefer")
        .RequireAuthorization();
    }
}