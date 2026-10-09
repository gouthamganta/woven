# Notification Triggers

**Last Updated:** 2026-08-17  
**Interface:** `INotificationService`  
**Implementation:** `NotificationService`

---

## Overview

Woven sends notifications in response to 12 different event types. Each event triggers both **SignalR (in-app)** and optionally **Web Push (background)**.

**Design principle:** Only high-priority, user-actionable events trigger Web Push (to avoid notification fatigue).

---

## Event Types

### 1. MomentReceived

**When:** User receives a new match (someone chose them in daily deck).

**SignalR payload:**
```json
{
  "matchId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fromUserId": 42
}
```

**Web Push:**
```
Title: "You have a new match! 🎉"
Body: "Someone chose you — open Woven to see who."
URL: /moments
```

**Triggered by:**
- User A swipes MAGICAL/RESONANT on User B
- User B already swiped on User A → creates EDGE match
- OR both chose same → creates PURE match

**Code:**
```csharp
await _notificationService.MomentReceivedAsync(
    recipientUserId: userB.Id,
    matchId: match.Id,
    fromUserId: userA.Id,
    ct: ct
);
```

**UX:** User taps notification → opens `/moments` tab → sees new match card.

---

### 2. NewChatMessage

**When:** User receives a new chat message.

**SignalR payload:**
```json
{
  "threadId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "messageId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "body": "Hey, how's it going?",
  "senderUserId": 42,
  "createdAt": "2026-08-17T10:30:00Z"
}
```

**Web Push:**
```
Title: "New message"
Body: "Hey, how's it going?" (truncated to 80 chars)
URL: /chats/{threadId}
```

**Triggered by:**
- `POST /chats/{threadId}/send` (text message)
- `POST /chats/{threadId}/voice-message` (voice note)
- Chat note submit (not visible to user, but logged)

**Code:**
```csharp
await _notificationService.NewChatMessageAsync(
    recipientUserId: recipientId,
    threadId: threadId,
    messageId: message.Id,
    body: message.Body,
    senderUserId: senderId,
    createdAt: message.CreatedAt,
    ct: ct
);
```

**UX:**
- User in-app → message appears instantly in chat thread (SignalR)
- User offline → browser notification (Web Push), tapping opens chat thread

**Frequency limits:** None (every message triggers notification). Future: batch multiple messages ("3 new messages from Alice").

---

### 3. SendPush

**When:** General-purpose in-app push (manual admin trigger or automated nudges).

**SignalR payload:**
```json
{
  "message": "Your daily deck is ready! 💫"
}
```

**Web Push:**
```
Title: "Woven"
Body: "Your daily deck is ready! 💫"
URL: /
```

**Use cases:**
- Ghost refund notifications ("You got 0.5 sparks back")
- Digest messages ("3 people liked you this week")
- Admin announcements ("New feature: voice notes!")
- Coaching nudges ("Your match is waiting — say hi!")

**Code:**
```csharp
await _notificationService.SendPushAsync(
    userId: userId,
    message: "Your daily deck is ready! 💫",
    ct: ct
);
```

**UX:** Generic notification, opens to home screen.

---

### 4. DeckReady

**When:** User's daily deck has been generated (nightly at 06:00 local time).

**SignalR payload:**
```json
{
  "date": "2026-08-17"
}
```

**Web Push:** ❌ None (low-priority, users check app organically).

**Triggered by:** `DailyDeckOrchestrator` batch worker.

**Code:**
```csharp
await _notificationService.DeckReadyAsync(
    userId: userId,
    date: DateOnly.FromDateTime(DateTime.UtcNow),
    ct: ct
);
```

**UX:** If user in-app → UI refreshes deck tab. Otherwise, silent (user sees new deck on next app open).

---

### 5. MomentExpired

**When:** Match balloon expires (both users saw match but didn't POP within 72h).

**SignalR payload:**
```json
{
  "matchId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Web Push:** ❌ None (negative signal, no action needed).

**Triggered by:** `BalloonExpirationWorker` (checks every 15 min).

**Code:**
```csharp
await _notificationService.MomentExpiredAsync(
    userAId: match.UserAId,
    userBId: match.UserBId,
    matchId: match.Id,
    ct: ct
);
```

**UX:** Match card grays out, shows "Expired" state.

---

### 6. GameInviteReceived

**When:** User receives a game invite (KnowMe, Red Green Flag, etc.).

**SignalR payload:**
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "matchId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "gameType": "KNOW_ME",
  "expiresAt": "2026-08-17T12:00:00Z"
}
```

**Web Push:** ❌ None (in-chat feature, user sees invite when opening chat).

**Triggered by:** `POST /games/invite` (sender invites recipient to play game).

**Code:**
```csharp
await _notificationService.GameInviteReceivedAsync(
    recipientUserId: recipientId,
    sessionId: session.Id,
    matchId: matchId,
    gameType: gameType,
    expiresAt: expiresAt,
    ct: ct
);
```

**UX:** In-chat game invite card appears, user can accept/decline.

---

### 7. GameStarted

**When:** Both users accepted game invite → game session begins.

**SignalR payload:**
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "matchId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "gameType": "KNOW_ME"
}
```

**Web Push:** ❌ None (both users in-app already).

**Triggered by:** Second user accepts invite (`POST /games/{sessionId}/accept`).

**Code:**
```csharp
await _notificationService.GameStartedAsync(
    userAId: session.UserAId,
    userBId: session.UserBId,
    sessionId: session.Id,
    matchId: session.MatchId,
    gameType: session.GameType,
    ct: ct
);
```

**UX:** Game UI loads for both users, first question appears.

---

### 8. GameCompleted

**When:** Game session ends (both users completed all rounds).

**SignalR payload:**
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "matchId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "gameType": "KNOW_ME",
  "winnerUserId": 42  // null for tie
}
```

**Web Push:** ❌ None (both users in-app).

**Triggered by:** Last user submits final answer (`POST /games/{sessionId}/answer`).

**Code:**
```csharp
await _notificationService.GameCompletedAsync(
    userAId: session.UserAId,
    userBId: session.UserBId,
    sessionId: session.Id,
    matchId: session.MatchId,
    gameType: session.GameType,
    winnerUserId: winnerId,
    ct: ct
);
```

**UX:** Results screen shows scores, winner (if any), unlocked chat depth.

---

### 9. FriendBridgeProposal

**When:** Match reaches "Find Love" stage → system proposes friend-to-friend intro.

**SignalR payload:**
```json
{
  "bridgeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Web Push:** ❌ None (experimental feature, not high priority).

**Triggered by:** `POST /matches/{matchId}/propose-friend-bridge`.

**Code:**
```csharp
await _notificationService.SendFriendBridgeProposalAsync(
    userAId: match.UserAId,
    userBId: match.UserBId,
    bridgeId: bridge.Id,
    ct: ct
);
```

**UX:** In-chat card appears: "Introduce Alice to your friend Bob?"

---

### 10. FriendBridgeActivated

**When:** Both users accept friend bridge → friend receives intro notification.

**SignalR payload:**
```json
{
  "bridgeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Web Push:** ❌ None.

**Triggered by:** Second user accepts bridge (`POST /friend-bridge/{bridgeId}/accept`).

**Code:**
```csharp
await _notificationService.SendFriendBridgeActivatedAsync(
    userAId: bridge.UserAId,
    userBId: bridge.UserBId,
    bridgeId: bridge.Id,
    ct: ct
);
```

**UX:** Both users see confirmation, friend receives intro message.

---

### 11. SeasonResponseSubmitted

**When:** User submits response to Commons season prompt.

**SignalR payload:**
```json
{
  "submitted": true
}
```

**Web Push:** ❌ None (confirmation, no action needed).

**Triggered by:** `POST /commons/season-response`.

**Code:**
```csharp
await _notificationService.SeasonResponseSubmittedAsync(
    userId: userId,
    ct: ct
);
```

**UX:** Tile post appears in Commons feed, user sees confirmation toast.

---

### 12. NewSeasonStarted

**When:** New Commons season begins (weekly prompt refresh).

**SignalR payload:**
```json
{
  "seasonNumber": 23,
  "promptText": "What's your favorite memory from this year?"
}
```

**Web Push:** ❌ None (low priority, users see new prompt on next app open).

**Triggered by:** `SeasonRotationWorker` (runs weekly).

**Code:**
```csharp
await _notificationService.NewSeasonStartedAsync(
    userId: userId,
    seasonNumber: season.Number,
    promptText: season.PromptText,
    ct: ct
);
```

**UX:** Commons tab shows new prompt, badge indicates "new season."

---

## Summary Table

| Event | SignalR | Web Push | Priority | User Action Required |
|---|---|---|---|---|
| MomentReceived | ✅ | ✅ | High | Open match, POP balloon |
| NewChatMessage | ✅ | ✅ | High | Reply to message |
| SendPush | ✅ | ✅ | Medium | Context-dependent |
| DeckReady | ✅ | ❌ | Low | None (passive discovery) |
| MomentExpired | ✅ | ❌ | Low | None (passive state change) |
| GameInviteReceived | ✅ | ❌ | Medium | Accept/decline invite |
| GameStarted | ✅ | ❌ | Low | None (both users in-app) |
| GameCompleted | ✅ | ❌ | Low | None (both users in-app) |
| FriendBridgeProposal | ✅ | ❌ | Low | Accept/decline proposal |
| FriendBridgeActivated | ✅ | ❌ | Low | None (confirmation) |
| SeasonResponseSubmitted | ✅ | ❌ | Low | None (confirmation) |
| NewSeasonStarted | ✅ | ❌ | Low | None (passive content refresh) |

**Rule:** Only events with **High priority + user action required** trigger Web Push.

---

## Notification Frequency

### Current (No Limits)

Every event triggers notification immediately (no batching, no quiet hours).

**Risk:** Notification fatigue (especially NewChatMessage for active chats).

### Future Enhancements

1. **Batching:** Combine multiple messages ("3 new messages from Alice" instead of 3 separate notifications).
2. **Quiet hours:** User-specified hours to mute Web Push (e.g., 23:00-07:00).
3. **Notification preferences:** Let users opt-in/out per event type (e.g., disable GameInvite but keep NewChatMessage).
4. **Smart throttling:** Max 1 Web Push per user per 5 min (consolidate rapid events).

---

## Adding New Triggers

### 1. Define Interface Method

```csharp
// INotificationService.cs
Task NewFeatureEventAsync(int userId, Guid resourceId, CancellationToken ct = default);
```

### 2. Implement in NotificationService

```csharp
public async Task NewFeatureEventAsync(int userId, Guid resourceId, CancellationToken ct = default)
{
    try
    {
        // SignalR (in-app)
        await Send(_hub.Clients.Group(WovenHub.UserGroup(userId)), "NewFeatureEvent",
            new { resourceId }, ct);

        // Web Push (background) — only if high priority
        _ = _webPush.SendToUserAsync(userId,
            title: "New feature!",
            body: "Something happened — check it out.",
            url: "/feature",
            ct: ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[Notify] NewFeatureEvent failed for user {UserId}", userId);
    }
}
```

### 3. Call from Endpoint/Service

```csharp
await _notificationService.NewFeatureEventAsync(userId, resourceId, ct);
```

### 4. Frontend SignalR Handler

```typescript
// woven.service.ts
this.hubConnection.on('NewFeatureEvent', (envelope) => {
  const payload = this.verifySignature(envelope);
  // Update UI
});
```

### 5. Update Documentation

Add to this file (triggers.md) + frontend.md.

---

## Testing Triggers

### Manual Test (Backend)

```csharp
// In any endpoint/service
await _notificationService.SendPushAsync(userId, "Test notification!", ct);
```

### DevTools (Frontend)

1. Open browser DevTools → Network → WS (WebSocket)
2. Trigger event (e.g., send chat message)
3. See SignalR message: `{"type": 1, "target": "NewChatMessage", ...}`
4. Check Application → Service Workers → Push (if Web Push enabled)

### Production Monitoring

```sql
-- Check notification delivery logs
SELECT * FROM logs 
WHERE message LIKE '%[Notify]%' 
ORDER BY timestamp DESC 
LIMIT 100;

-- Check Web Push subscription count
SELECT COUNT(*) FROM user_push_subscriptions;
```

---

## Error Scenarios

### SignalR Delivery Failure

**Cause:** User offline, WebSocket disconnected, SignalR hub error.

**Handling:**
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "[Notify] NewChatMessage failed for user {UserId}", userId);
    // DOES NOT throw — delivery is best-effort
}
```

**Result:** User gets notification when they reconnect (via app state refresh, not retried SignalR message).

### Web Push Delivery Failure

**Cause:** Subscription expired (410 Gone), invalid subscription (404), rate limit (429).

**Handling:**
```csharp
catch (WebPushException ex)
{
    if (ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
    {
        _db.PushSubscriptions.Remove(sub);  // Auto-cleanup
        await _db.SaveChangesAsync(ct);
    }
}
```

**Result:** Expired subscriptions removed, user needs to re-subscribe (next login).

---

## Related Documentation

- [README.md](./README.md) — Notification system overview
- [web-push.md](./web-push.md) — Web Push API, VAPID
- [backend.md](./backend.md) — NotificationService implementation
- [frontend.md](./frontend.md) — Frontend handlers
