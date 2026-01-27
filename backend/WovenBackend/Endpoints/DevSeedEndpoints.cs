using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Matchmaking;
using WovenBackend.Services.Moments;

namespace WovenBackend.Endpoints;

public static class DevSeedEndpoints
{
    public static void MapDevSeedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dev/seed");

        // POST /dev/seed/full?count=20&buildVectors=true&seedPulse=true
        group.MapPost("/full", async (
            int count,
            bool buildVectors,
            bool seedPulse,
            WovenDbContext db,
            IUserVectorBuilder vectorBuilder,
            CancellationToken ct) =>
        {
            count = Math.Clamp(count <= 0 ? 20 : count, 1, 200);

            // Find max existing id so emails are unique - EF-safe version
            var maxId = await db.Users.MaxAsync(u => (int?)u.Id, ct);
            var nextId = (maxId ?? 0) + 1;

            var rand = new Random();

            // 1) Create Users
            var newUsers = new List<User>();
            for (int i = 0; i < count; i++)
            {
                var idHint = nextId + i;

                var user = new User
                {
                    Email = $"seed{idHint}@woven.dev",

                    // IMPORTANT: set to whatever lets them use the app
                    // Use safe parsing or use DETAILS_DONE if that exists in your enum
                    ProfileStatus = Enum.TryParse<ProfileStatus>("DETAILS_DONE", out var ps)
                        ? ps
                        : default,

                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                newUsers.Add(user);
            }

            db.Users.AddRange(newUsers);
            await db.SaveChangesAsync(ct);

            // 2) Create Profile + Preferences + Intent + Foundational + OptionalFields (+ Dynamic Intake)
            var profiles = new List<UserProfile>();
            var prefs = new List<UserPreference>();
            var intents = new List<UserIntent>();
            var foundationSets = new List<UserFoundationalQuestionSet>();
            var optionalFields = new List<UserOptionalField>();
            var vibes = new List<UserWeeklyVibe>();
            var dynSets = new List<UserDynamicIntakeSet>();

            // reusable foundational Q bank
            var questions = new[]
            {
                new { id="q1", text="When you have an evening free, what do you most enjoy doing?", pillars=new[]{"Lifestyle","Energy"} },
                new { id="q2", text="What makes you feel truly understood by someone?", pillars=new[]{"Emotional","Communication"} },
                new { id="q3", text="What does a good relationship feel like day-to-day?", pillars=new[]{"Intent","Values"} },
                new { id="q4", text="What's a small habit you love in a partner?", pillars=new[]{"Lifestyle","Affection"} },
                new { id="q5", text="What's something you're currently improving about yourself?", pillars=new[]{"Growth","Self-awareness"} },
            };

            foreach (var user in newUsers)
            {
                // simple alternating gender for pool formation
                var gender = (user.Id % 2 == 0) ? "male" : "female";
                var interestedInJson = (gender == "male") ? "[\"female\"]" : "[\"male\"]";

                var age = rand.Next(22, 36);

                // PROFILE
                profiles.Add(new UserProfile
                {
                    UserId = user.Id,
                    Age = age,
                    Gender = gender,

                    // adjust if your model differs
                    City = "Austin",
                    State = "TX",
                    Lat = 30.2672,
                    Lng = -97.7431
                });

                // PREFS
                prefs.Add(new UserPreference
                {
                    UserId = user.Id,
                    AgeMin = 21,
                    AgeMax = 40,
                    DistanceMiles = 25,
                    InterestedInJson = interestedInJson,

                    // choose a compatible default
                    RelationshipStructure = RelationshipStructure.OPEN
                });

                // INTENT (affects intent score)
                var primaryIntent = (user.Id % 3 == 0) ? "long_term" : "relationship";
                intents.Add(new UserIntent
                {
                    UserId = user.Id,
                    PrimaryIntent = primaryIntent,
                    ReflectionSentence = "I value consistency, kindness, and shared experiences."
                });

                // FOUNDATIONAL (affects pillar scores + foundational tags)
                // Keep AnsweredAt != null so BuildVectorAsync includes it.
                var answers = new Dictionary<string, string>
                {
                    ["q1"] = (user.Id % 2 == 0)
                        ? "I like being active, exploring new places, and trying new foods."
                        : "I love cozy evenings, deep conversations, and meaningful time together.",
                    ["q2"] = "When someone listens, remembers details, and shows up consistently.",
                    ["q3"] = "Peaceful, supportive, playful, and aligned on where we're going.",
                    ["q4"] = "A partner who communicates clearly and makes effort without being asked.",
                    ["q5"] = "Building better routines and staying present instead of overthinking."
                };

                foundationSets.Add(new UserFoundationalQuestionSet
                {
                    UserId = user.Id,
                    Version = 1,
                    QuestionsJson = JsonSerializer.Serialize(questions),
                    AnswersJson = JsonSerializer.Serialize(answers),
                    CreatedAt = DateTime.UtcNow,
                    AnsweredAt = DateTime.UtcNow
                });

                // OPTIONAL FIELDS (affects lifestyle section)
                optionalFields.Add(new UserOptionalField
                {
                    UserId = user.Id,
                    Key = "hobbies",
                    Value = (user.Id % 2 == 0) ? "gym, hiking, cooking" : "reading, yoga, coffee walks",
                    Visibility = VisibilityLevel.MatchingOnly
                });

                optionalFields.Add(new UserOptionalField
                {
                    UserId = user.Id,
                    Key = "pets",
                    Value = (user.Id % 4 == 0) ? "dog" : "none",
                    Visibility = VisibilityLevel.Public
                });

                optionalFields.Add(new UserOptionalField
                {
                    UserId = user.Id,
                    Key = "diet",
                    Value = (user.Id % 5 == 0) ? "vegetarian" : "anything",
                    Visibility = VisibilityLevel.MatchingOnly
                });

                // WEEKLY VIBE (use Text property, not Vibe)
                var weeklyVibeText = (user.Id % 2 == 0) ? "adventurous" : "calm";
                vibes.Add(new UserWeeklyVibe
                {
                    UserId = user.Id,
                    Text = weeklyVibeText,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                });

                // DYNAMIC INTAKE SET (use CreatedAtUtc, not CreatedAt)
                var battery = (user.Id % 3 == 0) ? "low" : (user.Id % 3 == 1 ? "medium" : "high");
                var tone = (user.Id % 2 == 0) ? "calm" : "playful";
                var role = (user.Id % 4 == 0) ? "pilot" : "copilot";

                // Compute cycle info
                var cycleId = MomentsRules.UtcToday().ToString("yyyy-MM-dd");
                var cycleStartUtc = DateTime.UtcNow.Date;
                var cycleEndUtc = cycleStartUtc.AddDays(1);

                // Simple feature mapping without service dependency
                var featuresJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["battery"] = battery,
                    ["tone"] = tone,
                    ["role"] = role
                });
                var mappingVersion = 1;

                dynSets.Add(new UserDynamicIntakeSet
                {
                    UserId = user.Id,
                    CycleId = cycleId,
                    CycleStartUtc = cycleStartUtc,
                    CycleEndUtc = cycleEndUtc,
                    VariantJson = "[]",
                    AnswersJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["d1_battery"] = battery,
                        ["d2_tone"] = tone,
                        ["d3_role"] = role
                    }),
                    FeaturesJson = featuresJson,
                    MappingVersion = mappingVersion,
                    VariantSource = "base",
                    GenerationMetaJson = "{}",
                    AnsweredAtUtc = DateTime.UtcNow,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            // Save all seeded onboarding data
            db.UserProfiles.AddRange(profiles);
            db.UserPreferences.AddRange(prefs);
            db.UserIntents.AddRange(intents);
            db.UserFoundationalQuestionSets.AddRange(foundationSets);

            // optional fields are unique by (UserId, Key) in your model,
            // so this is safe for fresh seed users
            db.UserOptionalFields.AddRange(optionalFields);

            // weekly vibes are 1:1 unique, safe for fresh users
            db.UserWeeklyVibes.AddRange(vibes);

            db.UserDynamicIntakeSets.AddRange(dynSets);

            await db.SaveChangesAsync(ct);

            // 3) Build vectors + seed pulse into vector.pulse
            var built = 0;
            var pulsed = 0;

            if (buildVectors)
            {
                foreach (var user in newUsers)
                {
                    await vectorBuilder.BuildAndSaveV1Async(user.Id, ct);
                    built++;

                    if (seedPulse)
                    {
                        // match your UpdatePulseAsync signature (Dictionary<string,string>)
                        var intake = dynSets.First(x => x.UserId == user.Id);
                        var pulseAnswers = JsonSerializer.Deserialize<Dictionary<string, string>>(intake.AnswersJson)
                                          ?? new Dictionary<string, string>();

                        await vectorBuilder.UpdatePulseAsync(user.Id, pulseAnswers, ct);
                        pulsed++;
                    }
                }
            }

            return Results.Ok(new
            {
                seededUsers = newUsers.Count,
                builtVectors = built,
                seededPulse = pulsed,
                userIds = newUsers.Select(u => u.Id).ToList()
            });
        });

        // ✅ NEW: POST /dev/seed/rebuild-vectors
        group.MapPost("/rebuild-vectors", async (
            WovenDbContext db,
            IUserVectorBuilder vectorBuilder,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            // Find users with COMPLETE profile but no vector
            var usersNeedingVectors = await db.Users
                .Where(u => u.ProfileStatus == ProfileStatus.COMPLETE)
                .Where(u => !db.UserVectors.Any(v => v.UserId == u.Id))
                .Select(u => u.Id)
                .ToListAsync(ct);

            logger.LogInformation("[Rebuild] Found {Count} users needing vectors", usersNeedingVectors.Count);

            var rebuilt = 0;
            var failed = new List<int>();

            foreach (var userId in usersNeedingVectors)
            {
                try
                {
                    logger.LogInformation("[Rebuild] Building vector for user {UserId}", userId);
                    await vectorBuilder.BuildAndSaveV1Async(userId, ct);
                    rebuilt++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Rebuild] Failed to rebuild vector for user {UserId}", userId);
                    failed.Add(userId);
                }
            }

            return Results.Ok(new
            {
                foundUsers = usersNeedingVectors.Count,
                rebuilt,
                failed = failed.Count,
                failedUserIds = failed
            });
        });
    }
}