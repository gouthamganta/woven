using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WovenBackend.Data.Entities;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services;
using WovenBackend.Services.Analytics;
using WovenBackend.Services.Feedback;
using WovenBackend.Services.Security;
using WovenBackend.Services.Trust;

namespace WovenBackend.Tests;

public class FeedbackLifecycleContractTests
{
    private static DateFeedbackService Service(ChatHarness h)
    {
        var services = new ServiceCollection(); var trust = RecordedSideEffects.Create<ITrustService>(services); var provider = services.BuildServiceProvider();
        return new(h.Db, (INotificationService)(object)h.Notify, (ITrustService)(object)trust, (ISecurityAuditService)(object)h.Audit, provider.GetRequiredService<IServiceScopeFactory>(), (IAnalyticsService)(object)h.Analytics, (IMatchSignalService)(object)h.Signals, NullLogger<DateFeedbackService>.Instance);
    }
    [Theory]
    [InlineData(4, false, false)]
    [InlineData(6, false, true)]
    [InlineData(6, true, false)]
    public async Task QueueUsesMutualInterestAgeAndExcludesBlockedPairs(int days, bool blocked, bool expected)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); match.DateIdeaInterestedA = true; match.DateIdeaInterestedB = true; match.DateIdeaInterestedAt = DateTimeOffset.UtcNow.AddDays(-days); if (blocked) match.ClosedReason = ClosedReason.BLOCK; await h.Db.SaveChangesAsync(); var service = Service(h);
        await service.QueueFeedbackPromptsAsync(); await service.QueueFeedbackPromptsAsync(); var prompts = await h.Db.DateFeedbackPrompts.ToListAsync(); Assert.Equal(expected ? 2 : 0, prompts.Count); if (expected) { Assert.Equal(new[] { 1, 2 }, prompts.Select(p => p.UserId).Order()); Assert.All(prompts, p => Assert.Equal("interested_both", p.TriggerType)); }
    }
    [Theory]
    [InlineData(25, 1000, 11, false)]
    [InlineData(26, 1000, 11, true)]
    [InlineData(26, 8000000, 11, false)]
    [InlineData(26, 1000, 9, false)]
    public async Task DeepConversationQueueRespectsMessageLatencyAndAgeThresholds(int count, long latency, int days, bool expected)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); match.FindLoveAt = DateTimeOffset.UtcNow.AddDays(-days); var thread = await h.Db.ChatThreads.SingleAsync(); thread.MessageCount = count; thread.AvgResponseTimeMs = latency; await h.Db.SaveChangesAsync(); await Service(h).QueueFeedbackPromptsAsync(); Assert.Equal(expected ? 2 : 0, await h.Db.DateFeedbackPrompts.CountAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SilentThreadQueueRequiresBothMessagedAndDeduplicates(bool messaged)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); match.BothMessagedAt = messaged ? DateTimeOffset.UtcNow.AddDays(-10) : null; var thread = await h.Db.ChatThreads.SingleAsync(); thread.LastMessageAt = DateTimeOffset.UtcNow.AddDays(-6); await h.Db.SaveChangesAsync(); var service = Service(h); await service.QueueFeedbackPromptsAsync(); await service.QueueFeedbackPromptsAsync(); Assert.Equal(messaged ? 2 : 0, await h.Db.DateFeedbackPrompts.CountAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DueDeliveryMarksSentOnlyWhenProviderAcknowledgesAndNeverSendsFuturePrompt(bool failure)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); var due = new DateFeedbackPrompt { UserId = 1, MatchId = match.Id, ScheduledFor = DateTimeOffset.UtcNow.AddMinutes(-1) }; var future = new DateFeedbackPrompt { UserId = 2, MatchId = match.Id, ScheduledFor = DateTimeOffset.UtcNow.AddDays(1) }; h.Db.DateFeedbackPrompts.AddRange(due, future); await h.Db.SaveChangesAsync(); h.Notify.Fail = failure; await Service(h).SendDuePromptsAsync(); Assert.Equal(!failure, due.SentAt.HasValue); Assert.Null(future.SentAt); var call = Assert.Single(h.Notify.Calls); Assert.Equal(1, call.Args[0]); Assert.Contains("Unit", (string)call.Args[1]!);
    }
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(0, true, false)]
    public async Task PendingPromptsRescheduleTwiceAndNeverRescheduleAnsweredRecords(int previous, bool answered, bool rescheduled)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); var prompt = new DateFeedbackPrompt { UserId = 1, MatchId = match.Id, SentAt = DateTimeOffset.UtcNow.AddDays(-4), RescheduleCount = previous, RespondedAt = answered ? DateTimeOffset.UtcNow : null }; h.Db.DateFeedbackPrompts.Add(prompt); await h.Db.SaveChangesAsync(); var before = DateTimeOffset.UtcNow; await Service(h).ReschedulePendingAsync(); Assert.Equal(previous + (rescheduled ? 1 : 0), prompt.RescheduleCount);
        if (rescheduled) { Assert.Null(prompt.SentAt); Assert.InRange(prompt.ScheduledFor, before.AddDays(5), DateTimeOffset.UtcNow.AddDays(5)); } else Assert.NotNull(prompt.SentAt);
    }
    [Theory]
    [InlineData(true, 1, 0f)]
    [InlineData(true, 5, 1f)]
    [InlineData(false, 5, 1f)]
    public async Task FeedbackUpsertKeepsActorIsolationAndOnlyRetainsStarsForInPersonDate(bool met, int stars, float signal)
    {
        await using var h = ChatHarness.Create(); var match = await h.Seed(); h.Db.DateFeedbackPrompts.Add(new DateFeedbackPrompt { UserId = 1, MatchId = match.Id }); await h.Db.SaveChangesAsync(); var service = Service(h); var dto = new DateFeedbackDto(met, stars, "Unit right", "Unit off", "yes"); await service.SubmitFeedbackAsync(1, match.Id, dto); await service.SubmitFeedbackAsync(1, match.Id, dto); var row = await h.Db.DateFeedbacks.SingleAsync(); Assert.Equal(1, row.UserId); Assert.Equal(met ? (int?)stars : null, row.Stars); Assert.Equal("Unit right", row.FeltRightText); Assert.NotNull((await h.Db.DateFeedbackPrompts.SingleAsync()).RespondedAt);
        Assert.All(h.Signals.Calls, c => { Assert.Equal(1, c.Args[0]); Assert.Equal(2, c.Args[1]); Assert.Equal(signal, c.Args[3]); }); await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitFeedbackAsync(2, match.Id, dto)); Assert.Single(await h.Db.DateFeedbacks.ToListAsync());
    }
}
