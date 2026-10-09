using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebPush;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.PushNotifications;

// Registered as a singleton (consumed by singleton NotificationService), so it must not
// capture a scoped WovenDbContext. Each DB operation creates its own scope instead.
public class WebPushService : IWebPushService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WebPushService> _logger;
    private readonly WebPushClient _client;
    private readonly string _publicKey;
    private readonly string _privateKey;
    private readonly string _subject;

    public WebPushService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<WebPushService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;

        _publicKey = _config["Vapid:PublicKey"]
            ?? throw new InvalidOperationException("Vapid:PublicKey not configured");
        _privateKey = _config["Vapid:PrivateKey"]
            ?? throw new InvalidOperationException("Vapid:PrivateKey not configured");
        _subject = _config["Vapid:Subject"] ?? "mailto:support@wooven.me";

        _client = new WebPushClient();
    }

    public string GetPublicVapidKey() => _publicKey;

    public async Task SendToUserAsync(
        int userId,
        string title,
        string body,
        string? icon = null,
        string? url = null,
        string? data = null,
        CancellationToken ct = default)
    {
        List<UserPushSubscription> subscriptions;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
            subscriptions = await db.PushSubscriptions
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .ToListAsync(ct);
        }

        if (subscriptions.Count == 0)
        {
            _logger.LogDebug("[WebPush] No subscriptions for user {UserId}", userId);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            icon = icon ?? "/assets/icon-192.png",
            url = url ?? "/",
            data
        });

        // Sends run concurrently, so expired subscriptions are collected here and removed
        // afterwards in a single dedicated scope (a DbContext is not safe for concurrent use).
        var expiredIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        var tasks = subscriptions.Select(async sub =>
        {
            try
            {
                var subscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                var vapidDetails = new VapidDetails(_subject, _publicKey, _privateKey);

                await _client.SendNotificationAsync(subscription, payload, vapidDetails, ct);

                _logger.LogInformation("[WebPush] Sent notification | UserId={UserId} Endpoint={Endpoint}",
                    userId, sub.Endpoint);
            }
            catch (WebPushException ex)
            {
                _logger.LogWarning(ex, "[WebPush] Failed to send | StatusCode={StatusCode} UserId={UserId}",
                    ex.StatusCode, userId);

                // Remove expired subscriptions (410 Gone, 404 Not Found)
                if (ex.StatusCode == System.Net.HttpStatusCode.Gone ||
                    ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    expiredIds.Add(sub.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WebPush] Unexpected error | UserId={UserId}", userId);
            }
        });

        await Task.WhenAll(tasks);

        if (!expiredIds.IsEmpty)
        {
            var ids = expiredIds.ToList();
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();
            var expired = await db.PushSubscriptions
                .Where(s => ids.Contains(s.Id))
                .ToListAsync(ct);

            db.PushSubscriptions.RemoveRange(expired);
            await db.SaveChangesAsync(ct);

            foreach (var sub in expired)
            {
                _logger.LogInformation("[WebPush] Removed expired subscription | Id={Id} UserId={UserId}",
                    sub.Id, userId);
            }
        }

        _logger.LogInformation("[WebPush] Sent notification to {Count} subscriptions | UserId={UserId}",
            subscriptions.Count, userId);
    }
}
