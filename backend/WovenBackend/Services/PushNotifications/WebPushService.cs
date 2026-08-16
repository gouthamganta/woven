using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebPush;
using WovenBackend.Data;

namespace WovenBackend.Services.PushNotifications;

public class WebPushService : IWebPushService
{
    private readonly WovenDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<WebPushService> _logger;
    private readonly WebPushClient _client;
    private readonly string _publicKey;
    private readonly string _privateKey;
    private readonly string _subject;

    public WebPushService(
        WovenDbContext db,
        IConfiguration config,
        ILogger<WebPushService> logger)
    {
        _db = db;
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
        var subscriptions = await _db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

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
                    _db.PushSubscriptions.Remove(sub);
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("[WebPush] Removed expired subscription | Id={Id} UserId={UserId}",
                        sub.Id, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WebPush] Unexpected error | UserId={UserId}", userId);
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("[WebPush] Sent notification to {Count} subscriptions | UserId={UserId}",
            subscriptions.Count, userId);
    }
}
