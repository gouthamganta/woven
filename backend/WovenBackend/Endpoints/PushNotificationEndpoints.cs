using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.PushNotifications;

namespace WovenBackend.Endpoints;

public static class PushNotificationEndpoints
{
    public static RouteGroupBuilder MapPushNotificationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/vapid-public-key", GetVapidPublicKey)
            .WithName("GetVapidPublicKey")
            .AllowAnonymous();

        group.MapPost("/subscribe", Subscribe)
            .WithName("SubscribePushNotifications")
            .RequireAuthorization();

        group.MapPost("/unsubscribe", Unsubscribe)
            .WithName("UnsubscribePushNotifications")
            .RequireAuthorization();

        return group;
    }

    private static IResult GetVapidPublicKey(IWebPushService webPush)
    {
        var publicKey = webPush.GetPublicVapidKey();
        return Results.Ok(new { publicKey });
    }

    private static async Task<IResult> Subscribe(
        HttpContext http,
        WovenDbContext db,
        [FromBody] SubscribeRequest req,
        CancellationToken ct)
    {
        var userId = EndpointHelper.GetUserId(http.User);

        if (string.IsNullOrWhiteSpace(req.Endpoint) ||
            string.IsNullOrWhiteSpace(req.P256dh) ||
            string.IsNullOrWhiteSpace(req.Auth))
        {
            return Results.BadRequest(new { error = "INVALID_SUBSCRIPTION" });
        }

        // Check if subscription already exists
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == req.Endpoint, ct);

        if (existing != null)
        {
            return Results.Ok(new { status = "already_subscribed", id = existing.Id });
        }

        // Create new subscription
        var subscription = new UserPushSubscription
        {
            UserId = userId,
            Endpoint = req.Endpoint,
            P256dh = req.P256dh,
            Auth = req.Auth,
            UserAgent = http.Request.Headers.UserAgent.ToString()
        };

        db.PushSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { status = "subscribed", id = subscription.Id });
    }

    private static async Task<IResult> Unsubscribe(
        HttpContext http,
        WovenDbContext db,
        [FromBody] UnsubscribeRequest req,
        CancellationToken ct)
    {
        var userId = EndpointHelper.GetUserId(http.User);

        if (string.IsNullOrWhiteSpace(req.Endpoint))
        {
            return Results.BadRequest(new { error = "INVALID_ENDPOINT" });
        }

        var subscription = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == req.Endpoint, ct);

        if (subscription == null)
        {
            return Results.Ok(new { status = "not_found" });
        }

        db.PushSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { status = "unsubscribed" });
    }

    private record SubscribeRequest(string Endpoint, string P256dh, string Auth);
    private record UnsubscribeRequest(string Endpoint);
}
