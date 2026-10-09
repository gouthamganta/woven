# Backend Implementation

**Last Updated:** 2026-08-17

## Overview

The Chats feature backend is built with ASP.NET Core 10 Minimal APIs, Entity Framework Core 10, and PostgreSQL 16.

**Key files:**
- `ChatEndpoints.cs` — All chat-related HTTP endpoints
- `ChatThread.cs` — Entity (table: `chat_threads`)
- `ChatMessage.cs` — Entity (table: `chat_messages`)
- `Match.cs` — Entity (table: `matches`, balloon state tracked here)
- `ChatService.cs` — Business logic (if exists, not found in current review)

---

## Endpoint Registration

**File:** `ChatEndpoints.cs`

**Extension method pattern:**
```csharp
public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/chats");
        group.RequireAuthorization();
        
        group.MapGet("", async (...) => { /* list */ });
        group.MapPost("/start", async (...) => { /* start */ });
        group.MapGet("/{threadId:guid}", async (...) => { /* get thread */ });
        // ... more endpoints
    }
}
```

**Code reference:** Line 26-29

**Called from:** `Program.cs`

```csharp
app.MapChatEndpoints();
```

---

## Entities

### ChatThread

**Table:** `chat_threads`  
**File:** `backend/WovenBackend/data/Entities/Moments/ChatThread.cs`

**Columns:**
```csharp
public class ChatThread
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int MessageCount { get; set; } = 0;
    public long? AvgResponseTimeMs { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
}
```

**Purpose:**
- One thread per match (1:1 relationship)
- Tracks message count, average response time
- `UpdatedAt` used for sorting chat list (most recent first)

**Indexes:**
- Primary: `id`
- Foreign key: `match_id`

---

### ChatMessage

**Table:** `chat_messages`  
**File:** `backend/WovenBackend/data/Entities/Moments/ChatMessage.cs`

**Columns:**
```csharp
public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ThreadId { get; set; }
    public int SenderUserId { get; set; }
    public string Body { get; set; } = "";
    public string MessageType { get; set; } = "";  // "" | "SYSTEM" | "GAME" | "VOICE"
    public string MetaJson { get; set; } = "{}";    // JSONB in PostgreSQL
    public DateTimeOffset CreatedAt { get; set; }
}
```

**MessageType values:**
- `""` or `"CHAT"` — User text message
- `"VOICE"` — Voice note (MetaJson: `{ audioUrl, durationSecs }`)
- `"SYSTEM"` — Platform message (nudges, tips)
- `"GAME"` — Game invite (MetaJson: `{ sessionId, gameType }`)

**Indexes:**
- Primary: `id`
- Foreign key: `thread_id`
- Composite: `(thread_id, created_at DESC)` — for chat history query

---

### Match

**Table:** `matches`  
**File:** `backend/WovenBackend/data/Entities/Moments/Match.cs`

**Key fields for chat:**
```csharp
public class Match
{
    public Guid Id { get; set; }
    public int UserAId { get; set; }
    public int UserBId { get; set; }
    
    public BalloonState BalloonState { get; set; }  // ACTIVE | CLOSED
    public ClosedReason? ClosedReason { get; set; }  // POP | EXPIRE | UNMATCH | BLOCK
    
    public DateTimeOffset? BothMessagedAt { get; set; }
    public DateTimeOffset? FindLoveAt { get; set; }
    
    // Trial fields
    public bool IsTrial { get; set; } = false;
    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset? TrialUserAOpenedAt { get; set; }
    public DateTimeOffset? TrialUserBOpenedAt { get; set; }
    public string? UserADecision { get; set; }  // "CONTINUE" | "END"
    public string? UserBDecision { get; set; }
    public string? TrialEndReason { get; set; }  // "no_spark" | "wrong_timing" | "not_my_type"
    
    // Date coordination
    public bool DateIdeaInterestedA { get; set; }
    public bool DateIdeaInterestedB { get; set; }
    public DateTimeOffset? DateIdeaInterestedAt { get; set; }
}
```

**Enums:**
```csharp
public enum BalloonState { ACTIVE = 1, CLOSED = 2 }
public enum ClosedReason { POP = 1, EXPIRE = 2, UNMATCH = 3, BLOCK = 4 }
```

---

## Endpoints

### GET /chats (List Active Threads)

**Returns:** All ACTIVE chat threads for the current user, sorted by `UpdatedAt DESC`.

**Query:**
```csharp
var threads = await (
    from t in db.ChatThreads.AsNoTracking()
    join m in db.Matches.AsNoTracking() on t.MatchId equals m.Id
    where m.BalloonState == BalloonState.ACTIVE
          && (m.UserAId == me || m.UserBId == me)
    orderby t.UpdatedAt descending
    select new { ... }
).ToListAsync(ct);
```

**Code reference:** Line 40-69

**Response fields:**
- `threadId`, `matchId`, `matchType`, `edgeOwnerId`
- `expiresAt`, `bothMessagedAt`, `findLoveAt`
- `showFindLove`, `showBalloonTimer`, `reflectionSecondsLeft`
- `isTrial`, `trialEndsAt`, `trialSecondsLeft`
- `other` (user profile: name, photo, lastActiveAt)
- `lastMessage` (body, createdAt, senderUserId, messageType, metaJson)

**Computed fields:**
```csharp
showFindLove = m.FindLoveAt != null && m.FindLoveAt <= now;
showBalloonTimer = m.BothMessagedAt != null && m.FindLoveAt != null && m.FindLoveAt > now;
reflectionSecondsLeft = (int)Math.Ceiling((m.FindLoveAt.Value - now).TotalSeconds);
trialSecondsLeft = (int)Math.Ceiling((m.TrialEndsAt.Value - now).TotalSeconds);
```

**Photo selection:**  
First photo by sort order (`OrderBy(p => p.SortOrder)`).

---

### POST /chats/start (Create/Get Thread)

**Body:**
```json
{
  "matchId": "abc-123-def"
}
```

**Logic:**
1. Verify match exists
2. Verify user is participant
3. Verify balloon is ACTIVE
4. If thread exists → return existing `threadId`
5. If not → create new thread

**Code reference:** Line 149-186

**Response:**
```json
{
  "threadId": "thread-123",
  "matchId": "abc-123-def"
}
```

---

### GET /chats/{threadId} (Load Thread)

**Returns:** Full thread state (messages, trial status, date ideas, other user profile).

**Side effects:**
1. **Trial timer start:** If `IsTrial` and user hasn't opened yet, sets `TrialUserAOpenedAt` or `TrialUserBOpenedAt`. When both set → `TrialEndsAt = now + 3 minutes`.
2. **Auto-close check:** If trial ended + no messages + no decisions → close as UNMATCH + ghost refund.

**Code reference:** Line 189-384

**Trial timer logic:**
```csharp
if (match.IsTrial)
{
    var meIsUserA = match.UserAId == me;
    var changed = false;

    if (meIsUserA && match.TrialUserAOpenedAt == null)
    {
        match.TrialUserAOpenedAt = now;
        changed = true;
    }
    else if (!meIsUserA && match.TrialUserBOpenedAt == null)
    {
        match.TrialUserBOpenedAt = now;
        changed = true;
    }

    // Both have opened — start the 3-minute window
    if (changed && match.TrialEndsAt == null
        && match.TrialUserAOpenedAt != null && match.TrialUserBOpenedAt != null)
    {
        match.TrialEndsAt = now.AddMinutes(3);
    }

    if (changed)
        await db.SaveChangesAsync(ct);
}
```

**Auto-close logic:**
```csharp
if (match.IsTrial && match.TrialEndsAt != null && match.TrialEndsAt <= now)
{
    var bothDecided = !string.IsNullOrEmpty(match.UserADecision) && !string.IsNullOrEmpty(match.UserBDecision);
    if (!bothDecided)
    {
        var hasTrialMessages = await db.ChatMessages.AsNoTracking()
            .AnyAsync(m => m.ThreadId == threadId && m.CreatedAt >= match.TrialStartedAt, ct);

        if (!hasTrialMessages)
        {
            // Auto-close as UNMATCH
            match.BalloonState = BalloonState.CLOSED;
            match.ClosedReason = ClosedReason.UNMATCH;
            match.ClosedAt = now;
            match.IsTrial = false;
            await db.SaveChangesAsync(ct);

            // Ghost refund
            _ = Task.Run(async () =>
            {
                try { await sparks.GhostRefundAsync(match.UserAId); } catch { }
                try { await sparks.GhostRefundAsync(match.UserBId); } catch { }
            });
        }
    }
}
```

**Messages query:**
```csharp
var rawMessages = await db.ChatMessages.AsNoTracking()
    .Where(m => m.ThreadId == threadId)
    .OrderByDescending(m => m.CreatedAt)
    .Take(50)  // Last 50 messages
    .OrderBy(m => m.CreatedAt)  // Reverse to chronological
    .Select(m => new { ... })
    .ToListAsync(ct);
```

**MetaJson parsing:**  
Parsed in-memory after query (EF Core can't translate `JsonDocument.Parse` to SQL).

```csharp
meta = string.IsNullOrEmpty(m.metaJson) || m.metaJson == "{}"
    ? (object?)null
    : JsonDocument.Parse(m.metaJson).RootElement
```

---

### POST /chats/{threadId}/messages (Send Message)

**Body:**
```json
{
  "body": "Hello! How are you?"
}
```

**Validation:**
- Body cannot be empty
- Max 1000 characters
- Rate limit: 10 messages per minute per user

**Logic:**
1. Trim body
2. Check rate limit (Redis)
3. Verify thread exists
4. Verify match is ACTIVE
5. Create `ChatMessage` record
6. Update `ChatThread.UpdatedAt`, `LastMessageAt`, `MessageCount`
7. Calculate + update `AvgResponseTimeMs` (rolling average)
8. **First-time unlock check:** If both users have now messaged → set `BothMessagedAt`, `FindLoveAt = now + 5 minutes`

**Code reference:** Line 386-549

**Response time calculation:**
```csharp
var prevMsg = await db.ChatMessages.AsNoTracking()
    .Where(m => m.ThreadId == threadId && m.SenderUserId == otherUserId)
    .OrderByDescending(m => m.CreatedAt)
    .FirstOrDefaultAsync(ct);

if (prevMsg != null)
{
    var responseMs = (long)(now - prevMsg.CreatedAt).TotalMilliseconds;
    thread.MessageCount++;
    thread.AvgResponseTimeMs = thread.AvgResponseTimeMs == null
        ? responseMs
        : (thread.AvgResponseTimeMs * (thread.MessageCount - 1) + responseMs) / thread.MessageCount;
}
```

**BothMessagedAt unlock:**
```csharp
if (match.BothMessagedAt == null)
{
    var otherHasMessaged = await db.ChatMessages.AsNoTracking()
        .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == otherUserId, ct);

    if (otherHasMessaged)
    {
        match.BothMessagedAt = now;
        
        if (match.FindLoveAt == null)
            match.FindLoveAt = now.Add(ReflectionWindow);  // 5 minutes

        await db.SaveChangesAsync(ct);
    }
}
```

**ECHO signals:**
- `MessageSent` (1.0)
- `MessageResponseLatencyMs` (float ms, if replying)
- `TimeToFirstMessageMs` (float ms, if first message ever)

**Realtime notification:**
```csharp
_ = notify.NewChatMessageAsync(otherUserId, threadId, msg.Id, body, me, now, ct);
```

---

### POST /chats/{threadId}/trial-decision (Submit Trial Decision)

**Body:**
```json
{
  "decision": "CONTINUE",  // or "END" or "BLOCK"
  "endReason": "no_spark"  // optional (required for END)
}
```

**Idempotency:** Supports `X-Idempotency-Key` header.

**Logic:**
1. Verify trial is active
2. Verify trial has ended (`TrialEndsAt <= now`)
3. Store decision (`UserADecision` or `UserBDecision`)
4. If BLOCK → close immediately + create `Block` record
5. If both decided CONTINUE → `IsTrial = false`, `FindLoveAt = now`
6. If one or both END → close as UNMATCH + ghost refund if no messages

**Code reference:** Line 602-768

**BLOCK handling:**
```csharp
if (decision == "BLOCK")
{
    var alreadyBlocked = await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == otherUserId, ct);
    if (!alreadyBlocked)
        db.Blocks.Add(new Block { BlockerId = me, BlockedId = otherUserId, CreatedAt = now });

    match.BalloonState = BalloonState.CLOSED;
    match.ClosedReason = ClosedReason.BLOCK;
    match.ClosedAt = now;
    match.IsTrial = false;
    await db.SaveChangesAsync(ct);

    return Results.Ok(new { status = "MATCH_BLOCKED", closedAt: match.ClosedAt });
}
```

**ECHO signals:**
- `TrialAccepted` (1.0) or `TrialRejected` (0.0)
- `TrialMessageCount` (float count)
- `TrialEndedNoSpark` / `TrialEndedWrongTiming` / `TrialEndedNotMyType` (1.0)

---

### POST /chats/{threadId}/voice-message (Send Voice Note)

**Body:**
```json
{
  "audioUrl": "https://wovenprodblob.blob.core.windows.net/voice-notes/abc123.webm",
  "durationSecs": 42
}
```

**Validation:**
- `audioUrl` cannot be empty
- `durationSecs` must be 1-180
- Rate limit: 10 voice notes per minute per user

**Logic:**
1. Create `ChatMessage` with `MessageType = "VOICE"`, `Body = ""`, `MetaJson = { audioUrl, durationSecs }`
2. Update thread stats
3. Record `TimeToFirstMessageMs` signal (if first voice note sent)

**Code reference:** Line 772-858

**ECHO signal:**
```csharp
var isFirstVoice = await db.ChatMessages.AsNoTracking()
    .CountAsync(m => m.ThreadId == threadId
                  && m.SenderUserId == me
                  && m.MessageType == "VOICE", ct) == 1;

if (isFirstVoice)
{
    var elapsedMs = (float)(now - match.CreatedAt).TotalMilliseconds;
    await signals.RecordAsync(me, otherUserId,
        MatchSignalEventTypes.TimeToFirstMessageMs, elapsedMs, ct: ct);
}
```

---

### POST /chats/{threadId}/messages/{messageId}/voice-listened (Track Playback)

**Body:** Empty `{}`

**Logic:**
1. Verify message is a voice note
2. Verify listener is not the sender
3. Record `VoiceNoteListenComplete` signal (1.0)
4. Check for mutual voice exchange → record `MutualVoiceExchange` signal (1.0)

**Code reference:** Line 862-917

**Mutual check:**
```csharp
var senderHasVoice = await db.ChatMessages.AsNoTracking()
    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == senderUserId
                && m.MessageType == "VOICE", ct);
var listenerHasVoice = await db.ChatMessages.AsNoTracking()
    .AnyAsync(m => m.ThreadId == threadId && m.SenderUserId == me
                && m.MessageType == "VOICE", ct);

if (senderHasVoice && listenerHasVoice)
{
    await signals.RecordAsync(me, senderUserId,
        MatchSignalEventTypes.MutualVoiceExchange, 1f, ct: ct);
}
```

---

### POST /chats/{threadId}/date-interest (Express Date Interest)

**Body:**
```json
{
  "ideaIndex": 0,
  "ideaText": "Sunset hike at Nandi Hills"
}
```

**Logic:**
1. Set `DateIdeaInterestedA` or `DateIdeaInterestedB = true`
2. If both interested → set `DateIdeaInterestedAt`, send push notifications
3. Record `DateIdeaAccepted` signal with chosen idea metadata

**Code reference:** Line 1060-1144

**Response (both interested):**
```json
{
  "mutualInterest": true
}
```

**Notification:**
```csharp
if (mutualInterest)
{
    var msg = "You both want to meet up! Check out some nearby spots 🗺️";
    await Task.WhenAll(
        notify.SendPushAsync(match.UserAId, msg, ct),
        notify.SendPushAsync(match.UserBId, msg, ct));

    return Results.Ok(new { mutualInterest = true });
}
```

---

### GET /chats/{threadId}/venue-suggestions (Get Date Venues)

**Requires:** `DateIdeaInterestedA && DateIdeaInterestedB == true`

**Service:** `IVenueService.GetVenueSuggestionsAsync(meUserId, partnerUserId, ct)`

**Returns:** AI-generated venue recommendations based on chosen date idea + user preferences.

**Code reference:** Line 1147-1174

**Response:**
```json
{
  "venues": [
    {
      "name": "Nandi Hills Viewpoint",
      "address": "...",
      "type": "Outdoor",
      "estimatedCost": "₹200/person",
      "whyWeChose": "Perfect sunset spot"
    }
  ]
}
```

---

### POST /chats/{threadId}/close-gracefully (Mutual Walk-Away)

**Body:** Empty `{}`

**Logic:**
1. Close match: `BalloonState = CLOSED`, `ClosedReason = UNMATCH`
2. Ghost refund if no messages exchanged
3. Send notification to both users

**Code reference:** Line 552-597

**Ghost refund check:**
```csharp
var noMessages = match.BothMessagedAt == null;

if (noMessages)
{
    var uA = match.UserAId;
    var uB = match.UserBId;
    _ = Task.Run(async () =>
    {
        try { await sparks.GhostRefundAsync(uA); } catch { }
        try { await sparks.GhostRefundAsync(uB); } catch { }
    });
}
```

---

## Rate Limiting

**Implementation:** Redis-backed sliding window.

**Service:** `ICacheService.CheckRateLimitAsync(key, limit, window, ct)`

**Applied to:**
- `POST /chats/{threadId}/messages` — 10 per minute
- `POST /chats/{threadId}/voice-message` — 10 per minute

**Code reference:**
```csharp
var rateLimitKey = $"ratelimit:chat:message:{me}";
var allowed = await cache.CheckRateLimitAsync(rateLimitKey, 10, TimeSpan.FromMinutes(1), ct);
if (!allowed)
    return Results.Json(new { error = "RATE_LIMIT_EXCEEDED", retryAfter = 60 }, statusCode: 429);
```

**Response on limit:**
```json
{
  "error": "RATE_LIMIT_EXCEEDED",
  "retryAfter": 60
}
```
Status: 429

---

## ECHO Signal Recording

**Service:** `IMatchSignalService.RecordAsync(viewerId, candidateId, eventType, eventValue, metadataJson?, ct)`

**Storage:** `match_signal_logs` table

**All signals recorded in chat:**

| Signal | When | EventValue |
|---|---|---|
| `MessageSent` | Message sent | 1.0 |
| `MessageResponseLatencyMs` | Reply sent | latency (float ms) |
| `TimeToFirstMessageMs` | First message/voice note sent | latency (float ms) |
| `TrialAccepted` | Trial decision CONTINUE | 1.0 |
| `TrialRejected` | Trial decision END | 0.0 |
| `TrialMessageCount` | Trial decision submitted | message count (float) |
| `TrialEndedNoSpark` | Trial END with reason | 1.0 |
| `VoiceNoteListenComplete` | Voice note played to end | 1.0 |
| `MutualVoiceExchange` | Both sent voice notes | 1.0 |
| `DateIdeaAccepted` | Date idea selected | 1.0 |
| `MessageLove` | ❤️ reaction on message | 1.0 |
| `ChatNoteLove` | ❤️ reaction on ChatNote | 1.0 |

**Code pattern:**
```csharp
try { await signals.RecordAsync(me, otherUserId, MatchSignalEventTypes.MessageSent, 1f, ct: ct); }
catch { /* non-critical */ }
```

**Why try-catch?**  
Signal recording is non-critical — never block message send if signal fails.

---

## Authentication

**Helper method:**
```csharp
private static int GetUserId(ClaimsPrincipal user)
{
    var uid = user.FindFirstValue("uid");
    if (int.TryParse(uid, out var id)) return id;

    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (int.TryParse(sub, out id)) return id;

    throw new UnauthorizedAccessException("Missing user id claim");
}
```

**Code reference:** Line 1234-1244

**Replaced by:** `EndpointHelper.GetUserId(http.User)` (June 3 session refactor)

**JWT claims searched (in order):**
1. `"uid"` (custom claim)
2. `"sub"` (standard JWT claim)
3. `ClaimTypes.NameIdentifier` (ASP.NET Core claim)

**Throws:** `UnauthorizedAccessException` if no valid claim found → caught by `AuthExceptionHandler` → HTTP 401

---

## Correlation IDs

**Middleware:** `CorrelationIdMiddleware` (added June 3 session)

**Flow:**
1. Read `X-Correlation-ID` from incoming request header
2. If missing → generate 16-char hex ID
3. Push into Serilog `LogContext`
4. Store in `HttpContext.Items["CorrelationId"]`
5. Echo back in response header: `X-Correlation-ID: <id>`

**Logging example:**
```csharp
_logger.LogInformation(
    "[Chat] Message sent | UserId={UserId} ThreadId={ThreadId} CorrelationId={Cid}",
    userId, threadId, _correlation.CorrelationId);
```

**Every log line carries CorrelationId** → trace entire request flow.

---

## Exception Handling

**Global handlers (added June 3 session):**

1. **DomainExceptionHandler** → HTTP 422
2. **AuthExceptionHandler** → HTTP 401 (catches `UnauthorizedAccessException`)
3. **GlobalExceptionHandler** → HTTP 500

**All handlers return:**
```json
{
  "error": "ERROR_CODE",
  "correlationId": "abc123",
  "timestamp": "2026-08-17T12:34:56Z"
}
```

**Code reference:** `Infrastructure/GlobalExceptionHandler.cs`, `Infrastructure/AuthExceptionHandler.cs`

---

## Database Migrations

**Trial fields added:** Migration `20260603...` (June 3 session)

**ChatNote + reactions:** Migration `20260525...` (May 25)

**Latest snapshot:** `WovenDbContextModelSnapshot.cs` (always current)

**Run migrations:**
```bash
cd backend/WovenBackend
dotnet ef database update
```

---

## Related Documentation

- [Frontend Implementation](./frontend.md)
- [Trial Period Mechanics](./trial-period.md)
- [Voice Notes](./voice-notes.md)
- [Close Paths](./close-paths.md)
- [Balloon State Machine](./balloon-state.md)
