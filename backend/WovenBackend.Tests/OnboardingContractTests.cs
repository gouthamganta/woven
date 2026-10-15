using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services;

namespace WovenBackend.Tests;

public class OnboardingContractTests
{
    private static Dictionary<string, object?> Basics() => new() { ["fullName"] = " Unit Changed ", ["age"] = 30, ["gender"] = " female ", ["distanceMiles"] = 25, ["ageMin"] = 18, ["ageMax"] = 99, ["interestedIn"] = new[] { "male" }, ["location"] = new { city = " Unit City ", state = " IN ", lat = 39d, lng = -86d }, ["relationshipStructure"] = "OPEN" };
    public static IEnumerable<object[]> InvalidBasics()
    {
        yield return ["age", 17, "Age must be 18+"]; yield return ["distanceMiles", 14, "Distance must be between 15 and 100 miles"]; yield return ["distanceMiles", 101, "Distance must be between 15 and 100 miles"];
        yield return ["ageMin", 17, "AgeMin must be 18+"]; yield return ["ageMax", 100, "AgeMax must be 99 or less"]; yield return ["ageMin", 100, "AgeMin cannot be greater than AgeMax"];
        yield return ["interestedIn", Array.Empty<string>(), "InterestedIn is required"]; yield return ["interestedIn", null!, "InterestedIn is required"]; yield return ["gender", " ", "Gender is required"];
        yield return ["location", null!, "Location city/state is required"]; yield return ["location", new { city = "", state = "IN", lat = 1, lng = 1 }, "Location city/state is required"];
        yield return ["location", new { city = "Unit", state = "IN", lat = (double?)null, lng = 1 }, "Location coordinates are required."];
        yield return ["location", new { city = "Unit", state = "IN", lat = 0, lng = 0 }, "Invalid location coordinates."];
        yield return ["location", new { city = "Unit", state = "IN", lat = 91, lng = 1 }, "Invalid location coordinates."]; yield return ["location", new { city = "Unit", state = "IN", lat = -91, lng = 1 }, "Invalid location coordinates."];
        yield return ["location", new { city = "Unit", state = "IN", lat = 1, lng = 181 }, "Invalid location coordinates."]; yield return ["location", new { city = "Unit", state = "IN", lat = 1, lng = -181 }, "Invalid location coordinates."];
    }
    [Theory]
    [MemberData(nameof(InvalidBasics))]
    public async Task BasicsRejectsInvalidBoundariesWithoutSavingProfile(string field, object? value, string error)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var payload = Basics(); payload[field] = value;
        var response = await h.Call("/onboarding/basics", payload, method: "PUT"); Assert.Equal(400, response.Status); Assert.Equal(error, response.Error); Assert.Empty(await h.Db.UserProfiles.ToListAsync()); Assert.Empty(await h.Db.UserPreferences.ToListAsync()); Assert.Empty(h.Analytics.Calls);
    }
    [Theory]
    [InlineData(18, 15, -90d, -180d)]
    [InlineData(99, 100, 90d, 180d)]
    [InlineData(30, 25, 39d, -86d)]
    public async Task BasicsValidBoundariesSaveAndRepeatWithoutDuplicatingOrChangingForeignUser(int age, int miles, double lat, double lng)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var p = Basics(); p["age"] = age; p["distanceMiles"] = miles; p["location"] = new { city = "Unit", state = "IN", lat, lng };
        Assert.Equal(200, (await h.Call("/onboarding/basics", p, method: "PUT")).Status); Assert.Equal(200, (await h.Call("/onboarding/basics", p, method: "PUT")).Status);
        var profile = await h.Db.UserProfiles.SingleAsync(); Assert.Equal(1, profile.UserId); Assert.Equal(age, profile.Age); Assert.Equal(lat, profile.Lat); Assert.Equal("female", profile.Gender);
        var pref = await h.Db.UserPreferences.SingleAsync(); Assert.Equal(miles, pref.DistanceMiles); Assert.Equal(18, pref.AgeMin); Assert.Equal(99, pref.AgeMax);
        Assert.Equal("Unit Changed", (await h.Db.Users.SingleAsync(u => u.Id == 1)).FullName); Assert.Equal("Unit Two", (await h.Db.Users.SingleAsync(u => u.Id == 2)).FullName);
    }
    [Theory]
    [InlineData(ProfileStatus.INCOMPLETE, "/onboarding/start", 0)]
    [InlineData(ProfileStatus.WELCOME_DONE, "/onboarding/basics", 1)]
    [InlineData(ProfileStatus.BASICS_DONE, "/onboarding/intent", 2)]
    [InlineData(ProfileStatus.INTENT_DONE, "/onboarding/foundational", 3)]
    [InlineData(ProfileStatus.FOUNDATION_DONE, "/onboarding/details", 4)]
    [InlineData(ProfileStatus.DETAILS_DONE, "/onboarding/review", 5)]
    [InlineData(ProfileStatus.COMPLETE, "/home", 6)]
    public async Task StateUsesStoredStatusAndCompletedStepInventory(ProfileStatus status, string route, int completed)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var user = await h.Db.Users.SingleAsync(u => u.Id == 1); user.ProfileStatus = status;
        h.Db.UserFoundationalQuestionSets.Add(new UserFoundationalQuestionSet { UserId = 1, Version = 1, AnsweredAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(15) }); await h.Db.SaveChangesAsync();
        var result = await h.Call("/onboarding/state", method: "GET"); Assert.Equal(route, result.Json!.Value.GetProperty("nextRoute").GetString()); Assert.Equal(completed, result.Json.Value.GetProperty("completed").GetArrayLength());
        await h.Call("/onboarding/welcome"); Assert.True(user.ProfileStatus >= status);
    }
    [Theory]
    [InlineData(2, 400)]
    [InlineData(3, 200)]
    [InlineData(6, 200)]
    [InlineData(7, 400)]
    public async Task PhotosCountBoundariesReplaceOnlyOwnPhotos(int count, int status)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); h.Db.UserPhotos.Add(new UserPhoto { UserId = 2, Url = "https://unit.invalid/foreign" }); await h.Db.SaveChangesAsync();
        var photos = Enumerable.Range(0, count).Select(i => new { url = $" https://unit.invalid/{i} ", caption = " Unit ", sortOrder = count - i }).ToArray();
        var result = await h.Call("/onboarding/photos", new { photos }, method: "PUT"); Assert.Equal(status, result.Status);
        if (status == 200) { await h.Call("/onboarding/photos", new { photos }, method: "PUT"); Assert.Equal(count, await h.Db.UserPhotos.CountAsync(p => p.UserId == 1)); Assert.All(await h.Db.UserPhotos.Where(p => p.UserId == 1).ToListAsync(), p => { Assert.Equal("Unit", p.Caption); Assert.DoesNotContain(" ", p.Url); }); }
        else Assert.Equal(0, await h.Db.UserPhotos.CountAsync(p => p.UserId == 1)); Assert.Equal(1, await h.Db.UserPhotos.CountAsync(p => p.UserId == 2));
    }
    [Theory]
    [InlineData("", 0, "Photo URL is required")]
    [InlineData("https://unit.invalid/photo", 41, "Caption must be 40 characters or less")]
    public async Task PhotosRejectBlankUrlAndOversizedCaption(string url, int captionLength, string error)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var photos = Enumerable.Range(0, 3).Select(i => new { url, caption = new string('x', captionLength), sortOrder = i });
        Assert.Equal(error, (await h.Call("/onboarding/photos", new { photos }, method: "PUT")).Error); Assert.Empty(await h.Db.UserPhotos.ToListAsync());
    }
    [Theory]
    [InlineData("", 10, 400)]
    [InlineData("relationship", 0, 400)]
    [InlineData("relationship", 200, 200)]
    [InlineData("relationship", 201, 400)]
    public async Task IntentValidatesReflectionAndUpsertsOnlyOwnRecord(string primaryIntent, int length, int status)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var payload = new { primaryIntent, openness = (string[]?)null, reflectionSentence = new string('x', length) };
        Assert.Equal(status, (await h.Call("/onboarding/intent", payload, method: "PUT")).Status);
        if (status == 200) { await h.Call("/onboarding/intent", payload, method: "PUT"); var intent = await h.Db.UserIntents.SingleAsync(); Assert.Equal(1, intent.UserId); Assert.Equal(primaryIntent, intent.PrimaryIntent); Assert.Equal("[]", intent.OpennessJson); Assert.Equal(length, intent.ReflectionSentence.Length); }
        else Assert.Empty(await h.Db.UserIntents.ToListAsync());
    }
    [Theory]
    [InlineData(0, 400)]
    [InlineData(200, 200)]
    [InlineData(201, 400)]
    public async Task DetailsEnforcesBioLengthAndKeepsPrivatePreferencesOutOfPublicReview(int length, int status)
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var payload = new { bio = new string('b', length), weeklyVibe = " Unit weekly ", optionalFields = new[] { new { key = "hobbies", value = " Unit hiking ", visibility = 0 }, new { key = "pref_smoking", value = "No", visibility = 0 } }, displayPronouns = " they/them ", reduceMotion = true, highContrast = true };
        var response = await h.Call("/onboarding/details", payload, method: "PUT"); Assert.Equal(status, response.Status);
        if (status != 200) { Assert.Empty(await h.Db.UserOptionalFields.ToListAsync()); return; }
        var preference = await h.Db.UserOptionalFields.SingleAsync(f => f.Key == "pref_smoking"); Assert.Equal(VisibilityLevel.MatchingOnly, preference.Visibility);
        var review = await h.Call("/onboarding/review", method: "GET"); var preview = review.Json!.Value.GetProperty("publicPreview"); Assert.DoesNotContain("pref_smoking", preview.GetRawText()); Assert.DoesNotContain("Email", preview.GetRawText());
        Assert.Equal("Unit weekly", review.Json.Value.GetProperty("self").GetProperty("details").GetProperty("weeklyVibe").GetString());
    }
    [Theory]
    [InlineData(0, "Missing basics profile")]
    [InlineData(1, "Missing preferences")]
    [InlineData(2, "At least 3 photos required")]
    [InlineData(3, "Missing intent")]
    [InlineData(4, "Missing foundational intake")]
    [InlineData(5, "Bio is required")]
    public async Task CompleteRequiresEachPrerequisiteAndDoesNotAdvanceOnFailure(int level, string error)
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        if (level >= 1) h.Db.UserProfiles.Add(new UserProfile { UserId = 1, Age = 30 }); if (level >= 2) h.Db.UserPreferences.Add(new UserPreference { UserId = 1 });
        if (level >= 3) foreach (var i in Enumerable.Range(0, 3)) h.Db.UserPhotos.Add(new UserPhoto { UserId = 1, Url = $"https://unit.invalid/{i}" });
        if (level >= 4) h.Db.UserIntents.Add(new UserIntent { UserId = 1, PrimaryIntent = "relationship", ReflectionSentence = "Unit" });
        if (level >= 5) h.Db.UserFoundationalQuestionSets.Add(new UserFoundationalQuestionSet { UserId = 1, Version = 1, AnsweredAt = DateTime.UtcNow }); await h.Db.SaveChangesAsync();
        Assert.Equal(error, (await h.Call("/onboarding/complete")).Error); Assert.NotEqual(ProfileStatus.COMPLETE, (await h.Db.Users.SingleAsync(u => u.Id == 1)).ProfileStatus);
    }
    [Fact]
    public async Task CompleteSavesStatusAfterAllPrerequisitesAndDoesNotExposeMatchingOnlyDetails()
    {
        await using var h = ChatHarness.Create(); await h.Seed(); await h.Call("/onboarding/basics", Basics(), method: "PUT");
        await h.Call("/onboarding/photos", new { photos = Enumerable.Range(0, 3).Select(i => new { url = $"https://unit.invalid/{i}", sortOrder = i }) }, method: "PUT");
        await h.Call("/onboarding/intent", new { primaryIntent = "relationship", reflectionSentence = "Unit reflection", openness = new[] { "friendship" } }, method: "PUT");
        h.Db.UserFoundationalQuestionSets.Add(new UserFoundationalQuestionSet { UserId = 1, Version = 1, AnsweredAt = DateTime.UtcNow }); await h.Db.SaveChangesAsync();
        await h.Call("/onboarding/details", new { bio = "Unit bio", optionalFields = Array.Empty<object>() }, method: "PUT");
        var response = await h.Call("/onboarding/complete"); Assert.Equal(200, response.Status); Assert.Equal("COMPLETE", response.Json!.Value.GetProperty("profileStatus").GetString()); Assert.Equal("/home", response.Json.Value.GetProperty("nextRoute").GetString());
        Assert.Equal(ProfileStatus.COMPLETE, (await h.Db.Users.SingleAsync(u => u.Id == 1)).ProfileStatus);
    }
    [Fact]
    public async Task QuestionsRemainStableAndLaterCyclesCanDeferWithoutChangingAnotherUser()
    {
        await using var h = ChatHarness.Create(); await h.Seed(); var first = await h.Call("/onboarding/foundational/questions", method: "GET"); var second = await h.Call("/onboarding/foundational/questions", method: "GET");
        Assert.Equal(first.Json!.Value.GetRawText(), second.Json!.Value.GetRawText()); var set = await h.Db.UserFoundationalQuestionSets.SingleAsync(); Assert.Equal(1, set.Version); Assert.Equal("v1 cannot be deferred.", (await h.Call("/onboarding/foundational/defer")).Error);
        set.Version = 2; await h.Db.SaveChangesAsync(); var before = DateTime.UtcNow; Assert.Equal(200, (await h.Call("/onboarding/foundational/defer")).Status); Assert.InRange(set.DeferredUntil!.Value, before.AddHours(24), DateTime.UtcNow.AddHours(24));
        Assert.False((await h.Foundational.GetDueStateAsync(1, default)).due); Assert.Equal(1, await h.Db.UserFoundationalQuestionSets.CountAsync());
    }
}
