namespace WovenBackend.Services.PushNotifications;

/// <summary>
/// Web Push notification service interface.
/// Handles VAPID authentication and push notification delivery.
/// Uses existing UserPushSubscription entity (table: user_push_subscriptions).
/// </summary>
public interface IWebPushService
{
    /// <summary>
    /// Get the public VAPID key for frontend subscription.
    /// This key is safe to expose publicly.
    /// </summary>
    string GetPublicVapidKey();

    /// <summary>
    /// Send a push notification to all subscriptions for a given user.
    /// Automatically removes expired/invalid subscriptions.
    /// </summary>
    Task SendToUserAsync(
        int userId,
        string title,
        string body,
        string? icon = null,
        string? url = null,
        string? data = null,
        CancellationToken ct = default);
}
