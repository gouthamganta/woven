using Microsoft.EntityFrameworkCore;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Tests;

public class UserDataIsolationContractTests
{
    [Fact]
    public async Task Summary_CountsOnlyTheCallersData()
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        h.Db.MomentResponses.AddRange(new MomentResponse { FromUserId = 1, ToUserId = 2 }, new MomentResponse { FromUserId = 2, ToUserId = 1 });
        h.Db.ChatMessages.AddRange(new ChatMessage { SenderUserId = 1, ThreadId = h.ThreadId, Body = "Own" }, new ChatMessage { SenderUserId = 2, ThreadId = h.ThreadId, Body = "Foreign" });
        await h.Db.SaveChangesAsync();
        var response = await h.Call("/me/data-summary", method: "GET"); Assert.Equal(200, response.Status);
        var data = response.Json!.Value; Assert.Equal(1, data.GetProperty("userId").GetInt32());
        Assert.Equal(1, data.GetProperty("momentResponses").GetInt32()); Assert.Equal(1, data.GetProperty("chatMessages").GetInt32());
        Assert.Equal(0, data.GetProperty("tiles").GetInt32()); Assert.Equal(0, data.GetProperty("photos").GetInt32());
    }

    [Theory]
    [InlineData(1, "one@unit.invalid")]
    [InlineData(2, "two@unit.invalid")]
    public async Task Export_ReturnsOwnProfileMessagesAndRateLimitsOnlyThatUser(int actor, string email)
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        h.Db.ChatMessages.AddRange(new ChatMessage { SenderUserId = 1, ThreadId = h.ThreadId, Body = "Message One" }, new ChatMessage { SenderUserId = 2, ThreadId = h.ThreadId, Body = "Message Two" }); await h.Db.SaveChangesAsync();
        var response = await h.Call("/me/data-export", actor: actor, method: "GET"); Assert.Equal(200, response.Status);
        Assert.Equal(email, response.Json!.Value.GetProperty("profile").GetProperty("email").GetString());
        var message = Assert.Single(response.Json.Value.GetProperty("chatMessages").EnumerateArray());
        Assert.Equal(actor == 1 ? "Message One" : "Message Two", message.GetProperty("body").GetString());
        Assert.Equal("exported", h.Cache.Values[$"data-export:{actor}"]);
        Assert.Equal(429, (await h.Call("/me/data-export", actor: actor, method: "GET")).Status);
        Assert.Single(h.Audit.Calls); Assert.Equal("bulk_data_export", h.Audit.Calls[0].Args[0]); Assert.Equal(actor, h.Audit.Calls[0].Args[1]);
        Assert.Equal(200, (await h.Call("/me/data-export", actor: actor == 1 ? 2 : 1, method: "GET")).Status);
        Assert.Equal(2, h.Audit.Calls.Count);
    }

    [Fact]
    public async Task Export_MissingAccountDoesNotConsumeQuotaOrProduceAudit()
    {
        await using var h = ChatHarness.Create();
        Assert.Equal(404, (await h.Call("/me/data-export", actor: 99, method: "GET")).Status);
        Assert.Empty(h.Cache.Values); Assert.Empty(h.Audit.Calls);
    }

    [Fact]
    public async Task Blocks_ListIncludesOnlyOutboundBlocksAndNoForeignOrIncomingRecords()
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        h.Db.Blocks.AddRange(new Block { BlockerId = 1, BlockedId = 2 }, new Block { BlockerId = 2, BlockedId = 1 }); await h.Db.SaveChangesAsync();
        var response = await h.Call("/me/blocks", method: "GET"); Assert.Equal(200, response.Status);
        var item = Assert.Single(response.Json!.Value.EnumerateArray()); Assert.Equal(2, item.GetProperty("userId").GetInt32()); Assert.Equal("Unit Two", item.GetProperty("name").GetString());
        Assert.False(item.TryGetProperty("email", out _));
        Assert.Empty((await h.Call("/me/blocks", actor: 3, method: "GET")).Json!.Value.EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VoicePreferenceReset_RemovesOnlyOwnRecordAndIsRepeatable(bool exists)
    {
        await using var h = ChatHarness.Create(); await h.Seed();
        if (exists) h.Db.UserVoicePreferences.Add(new UserVoicePreference { UserId = 1, YesSampleCount = 5 });
        h.Db.UserVoicePreferences.Add(new UserVoicePreference { UserId = 2, YesSampleCount = 7 }); await h.Db.SaveChangesAsync();
        Assert.Equal(200, (await h.Call("/me/voice-preference/reset")).Status);
        Assert.Equal(200, (await h.Call("/me/voice-preference/reset")).Status);
        var remaining = await h.Db.UserVoicePreferences.SingleAsync(); Assert.Equal(2, remaining.UserId); Assert.Equal(7, remaining.YesSampleCount);
        Assert.Equal(2, h.Audit.Calls.Count); Assert.All(h.Audit.Calls, c => Assert.Equal(1, c.Args[1]));
    }
}
