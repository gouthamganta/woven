# Profile Feature — Backend Implementation

**Last Updated:** 2026-10-07  
**Framework:** ASP.NET Core 10 (Minimal API)  
**Database:** PostgreSQL 16 + EF Core 10  
**Status:** ACTIVE

---

## Endpoint Overview

| Endpoint Group | File | Routes |
|---|---|---|
| User Data & Privacy | `UserDataEndpoints.cs` | `/me/data-summary`, `/me/data-export`, `/me/blocks`, `/me/account`, preference resets |
| Preferences & Insights | `MeEndpoints.cs` | `/me/insights`, `/me/accessibility` |
| Push Notifications | `PushEndpoints.cs` (within UserDataEndpoints.cs) | `/me/vapid-public-key`, `/me/push-subscription` |

All endpoints:
- **Authentication required:** `.RequireAuthorization()`
- **User ID extraction:** Via `GetUserId(ClaimsPrincipal)` helper
- **Claims chain:** `"uid"` → `"sub"` → `ClaimTypes.NameIdentifier`
- **Error on missing claim:** Throws `UnauthorizedAccessException` → 401

---

## UserDataEndpoints.cs

**Location:** `backend/WovenBackend/Endpoints/UserDataEndpoints.cs`

### GET /me/data-summary

**Purpose:** Lightweight GDPR overview — counts of stored data

**Response:**
```json
{
  "userId": 123,
  "tiles": 42,
  "momentResponses": 156,
  "chatMessages": 89,
  "photos": 6,
  "thirdPartyProcessors": [
    "OpenAI (semantic embeddings)",
    "Replicate (photo embeddings)"
  ]
}
```

**Implementation:**
```csharp
var tileCount = await db.Tiles.CountAsync(t => t.UserId == userId, ct);
var momentCount = await db.MomentResponses.CountAsync(m => m.FromUserId == userId, ct);
var chatCount = await db.ChatMessages.CountAsync(m => m.SenderUserId == userId, ct);
var photoCount = await db.PhotoEmbeddings.CountAsync(p => p.UserId == userId, ct);
```

**Security:**
- Read-only operation
- No rate limit (lightweight query)
- Returns counts only, no actual data

---

### GET /me/data-export

**Purpose:** Full GDPR data export — complete user data dump

**Rate Limit:**
- **1 request per 30 days** (TTL cache key: `data-export:{userId}`)
- Returns **429** if flagged
- Cache managed via `ICacheService`

**Response:**
```json
{
  "exportedAt": "2026-10-07T12:34:56Z",
  "note": "AI processors (OpenAI, Replicate) may retain data per their own retention policies.",
  "profile": {
    "email": "user@example.com",
    "fullName": "John Doe",
    "createdAt": "2026-01-15T08:30:00Z"
  },
  "tiles": [
    { "id": "abc", "mediaUrl": "https://...", "createdAt": "..." }
  ],
  "chatMessages": [
    { "id": 1, "threadId": "xyz", "body": "Hello!", "createdAt": "..." }
  ],
  "visualPreferences": {
    "yesSampleCount": 42,
    "noSampleCount": 18,
    "updatedAt": "..."
  }
}
```

**Implementation:**
```csharp
// Rate limit check
var flagged = await cache.GetAsync<string>(rateLimitKey, ct);
if (flagged != null) return Results.StatusCode(429);

// Fetch minimal user data (no password hash)
var user = await db.Users
    .AsNoTracking()
    .Where(u => u.Id == userId)
    .Select(u => new { u.Email, u.FullName, u.CreatedAt })
    .FirstOrDefaultAsync(ct);

// Fetch tiles (no embeddings)
var tiles = await db.Tiles
    .AsNoTracking()
    .Where(t => t.UserId == userId)
    .Select(t => new { t.Id, t.MediaUrl, t.CreatedAt })
    .ToListAsync(ct);

// Fetch messages (sender's messages only)
var messages = await db.ChatMessages
    .AsNoTracking()
    .Where(m => m.SenderUserId == userId)
    .Select(m => new { m.Id, m.ThreadId, m.Body, m.CreatedAt })
    .ToListAsync(ct);

// Set rate limit flag
await cache.SetAsync(rateLimitKey, "exported", TimeSpan.FromDays(30), ct);

// Audit log
audit.Log("bulk_data_export", userId: userId, resourceType: "User", resourceId: userId.ToString());
```

**Security:**
- **Audit logged:** Every export logged via `ISecurityAuditService`
- **No embeddings/vectors:** Excludes AI-generated data (size + privacy)
- **Third-party disclaimer:** Response includes note about OpenAI/Replicate retention
- **AsNoTracking:** Read-only, no change tracking overhead

---

### GET /me/blocks

**Purpose:** List all users blocked by the caller

**Response:**
```json
[
  {
    "userId": 456,
    "name": "Jane Smith",
    "photo": "https://...",
    "blockedAt": "2026-09-15T10:20:30Z"
  }
]
```

**Implementation:**
```csharp
var blocked = await db.Blocks
    .Where(b => b.BlockerId == userId)
    .Join(db.Users,
        b => b.BlockedId,
        u => u.Id,
        (b, u) => new
        {
            userId      = u.Id,
            name        = u.FullName ?? "Unknown",
            photo       = u.ProfilePhoto,
            blockedAt   = b.CreatedAt
        })
    .OrderByDescending(x => x.blockedAt)
    .ToListAsync(ct);
```

**Notes:**
- Ordered by most recent block first
- Returns minimal user data (id, name, photo)
- No pagination (blocks are rare — typical user has 0-3)

---

### DELETE /me/blocks/{targetUserId}

**Purpose:** Unblock a user

**Response:**
```json
{ "unblocked": true }
```

**Error (404):**
```json
{ "unblocked": false }
```

**Implementation:**
```csharp
var deleted = await db.Blocks
    .Where(b => b.BlockerId == userId && b.BlockedId == targetUserId)
    .ExecuteDeleteAsync(ct);
return deleted > 0 ? Results.Ok(new { unblocked = true }) : Results.NotFound();
```

**Security:**
- **Ownership check:** `BlockerId == userId` (can't unblock someone else's blocks)
- **Bulk delete:** `ExecuteDeleteAsync` (no load-to-memory)
- **Idempotent:** Returns 404 if block doesn't exist (safe to retry)

---

### POST /me/visual-preference/reset

**Purpose:** Clear learned photo preferences (Yes/No swipe history)

**Response:**
```json
{ "reset": true }
```

**Implementation:**
```csharp
var pref = await db.UserVisualPreferences.FindAsync([userId], ct);
if (pref is not null)
{
    db.UserVisualPreferences.Remove(pref);
    await db.UserVisualDecisions
        .Where(d => d.ViewerUserId == userId)
        .ExecuteDeleteAsync(ct);
    await db.SaveChangesAsync(ct);
}
audit.Log("preference_reset", userId: userId, resourceType: "VisualPreference", resourceId: userId.ToString());
```

**Side effects:**
- Deletes `UserVisualPreferences` row (aggregated model)
- Deletes all `UserVisualDecisions` (raw swipe history)
- Triggers **preference re-learning** on next deck generation

**Audit:** Logged as `preference_reset` event

---

### POST /me/voice-preference/reset

**Purpose:** Clear learned voice note preferences

**Response:**
```json
{ "reset": true }
```

**Implementation:**
```csharp
var pref = await db.UserVoicePreferences.FindAsync([userId], ct);
if (pref is not null)
{
    db.UserVoicePreferences.Remove(pref);
    await db.SaveChangesAsync(ct);
}
audit.Log("preference_reset", userId: userId, resourceType: "VoicePreference", resourceId: userId.ToString());
```

**Notes:**
- Only deletes preference model (no raw decision log for voice)
- Less data than visual reset (voice preference is newer feature)

---

### DELETE /me/account

**Purpose:** Hard delete user account (GDPR right to deletion)

**Response:**
```json
{
  "deleted": true,
  "note": "AI processors (OpenAI, Replicate) may retain embeddings per their own retention policies. Contact support to submit deletion requests to those providers."
}
```

**Implementation:**
```csharp
// 1. Audit BEFORE deletion (so we have a trail)
audit.Log("account_deletion", userId: userId, resourceType: "User", resourceId: userId.ToString());

// 2. Delete all Azure blobs (photos, voice notes, etc.)
await media.DeleteAllForUserAsync(userId, ct);

// 3. Anonymize matches (preserve other participant's record)
var matchesAsA = await db.Matches.Where(m => m.UserAId == userId).ToListAsync(ct);
var matchesAsB = await db.Matches.Where(m => m.UserBId == userId).ToListAsync(ct);
foreach (var m in matchesAsA) m.UserAId = 0;  // 0 = deleted user
foreach (var m in matchesAsB) m.UserBId = 0;

// 4. Bulk-delete owned data (no cascade rules)
await db.Tiles.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
await db.ChatMessages.Where(m => m.SenderUserId == userId).ExecuteDeleteAsync(ct);
await db.PhotoEmbeddings.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
await db.UserVisualDecisions.Where(d => d.ViewerUserId == userId).ExecuteDeleteAsync(ct);
await db.UserVectors.Where(v => v.UserId == userId).ExecuteDeleteAsync(ct);
await db.UserVisualPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
await db.UserVoicePreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
await db.UserMatchingWeights.Where(w => w.UserId == userId).ExecuteDeleteAsync(ct);

// 5. Delete user row itself
await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

await db.SaveChangesAsync(ct);
```

**Key patterns:**
- **Audit first:** Log before deletion (audit row persists)
- **Blob cleanup:** `IMediaService.DeleteAllForUserAsync` — deletes from all containers (photos, voice, tiles)
- **Match anonymization:** Sets `UserAId`/`UserBId` to `0` instead of deleting (preserves other user's history)
- **Bulk deletes:** `ExecuteDeleteAsync` — no load-to-memory, generates efficient SQL
- **No cascade:** EF Core cascades disabled for safety — explicit deletes only

**Third-party retention:**
- OpenAI embeddings: retained per their policy (30 days default, configurable)
- Replicate embeddings: retained per their policy
- Response includes **disclaimer** directing user to support for third-party deletion requests

---

## MeEndpoints.cs

**Location:** `backend/WovenBackend/Endpoints/MeEndpoints.cs`

### GET /me/insights

**Purpose:** Fetch user-facing insights + opinion prompt (if triggered)

**Response:**
```json
{
  "insights": [
    "You respond fastest to profiles with creative hobbies",
    "Your trial conversations tend to go deeper when voice notes are exchanged"
  ],
  "shouldAskOpinion": true,
  "opinionTrigger": "no_dates_yet",
  "opinionPrompt": "You've had great conversations, but no dates scheduled yet. What's holding you back?"
}
```

**Implementation:**
```csharp
var row = await db.UserInsights.AsNoTracking()
    .FirstOrDefaultAsync(x => x.UserId == userId, ct);

var insightList = JsonSerializer.Deserialize<List<string>>(row?.InsightsJson ?? "[]")
                  ?? new List<string>();

var (shouldAsk, trigger, prompt) = await insights.ShouldAskOpinionAsync(userId, ct);

_ = analytics.TrackAsync(userId, null, AnalyticsEvents.InsightViewed,
    new { hasInsights = insightList.Count > 0, shouldAskOpinion = shouldAsk });

return Results.Ok(new
{
    insights = insightList,
    shouldAskOpinion = shouldAsk,
    opinionTrigger = trigger,
    opinionPrompt = prompt
});
```

**Triggers (opinionTrigger):**
- `no_dates_yet` — Many chats, no dates scheduled
- `pattern_shift` — Recent behavior differs from past
- `high_rejection` — Above-average trial ends with "no spark"
- `low_depth` — Messages shallow compared to match quality

**Analytics:**
- Fire-and-forget: `_ = analytics.TrackAsync(...)`
- Event: `InsightViewed`
- Properties: `hasInsights`, `shouldAskOpinion`

---

### POST /me/insights/opinion

**Purpose:** Submit user feedback in response to opinion prompt

**Request:**
```json
{
  "text": "I'm nervous about suggesting a date too early and seeming pushy.",
  "trigger": "no_dates_yet"
}
```

**Response:**
```json
{ "submitted": true }
```

**Rate Limit:**
- **1 opinion per calendar month** (e.g., `yyyy-MM`)
- Key: `rl:opinion:{userId}:{monthYear}`
- TTL: 31 days
- Returns **429** with `Retry-After` header on limit

**Validation:**
- Text required, max 300 chars
- Trigger must be in `KnownTriggers` set (case-insensitive)

**Implementation:**
```csharp
// Rate limit
var monthYear = DateTime.UtcNow.ToString("yyyy-MM");
var rlKey = $"rl:opinion:{userId}:{monthYear}";
var allowed = await cache.CheckRateLimitAsync(rlKey, 1, TimeSpan.FromDays(31), ct);
if (!allowed)
{
    var now = DateTime.UtcNow;
    var nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
    http.Response.Headers["Retry-After"] = ((int)(nextMonth - now).TotalSeconds).ToString();
    return Results.StatusCode(429);
}

// Validation
if (string.IsNullOrWhiteSpace(req.Text))
    return Results.BadRequest(new { error = "TEXT_REQUIRED" });

if (req.Text.Length > 300)
    return Results.BadRequest(new { error = "TEXT_TOO_LONG" });

if (string.IsNullOrWhiteSpace(req.Trigger) || !KnownTriggers.Contains(req.Trigger))
    return Results.BadRequest(new { error = "INVALID_TRIGGER" });

// Submit
await insights.SubmitOpinionAsync(userId, req.Text, req.Trigger, ct);

return Results.Ok(new { submitted = true });
```

**Known triggers set:**
```csharp
private static readonly HashSet<string> KnownTriggers =
    new(StringComparer.OrdinalIgnoreCase)
    {
        "no_dates_yet", "pattern_shift", "high_rejection", "low_depth"
    };
```

**Use case:**
- ECHO asks users for qualitative feedback at strategic moments
- Opinions inform matching algorithm tuning (human-in-the-loop)
- Not shown back to users (research data only)

---

### GET /me/accessibility

**Purpose:** Fetch accessibility preferences

**Response:**
```json
{
  "reduceMotion": false,
  "highContrast": false,
  "displayPronouns": "they/them"
}
```

**Implementation:**
```csharp
var pref = await db.UserPreferences.AsNoTracking()
    .FirstOrDefaultAsync(p => p.UserId == userId, ct);
var profile = await db.UserProfiles.AsNoTracking()
    .FirstOrDefaultAsync(p => p.UserId == userId, ct);

return Results.Ok(new
{
    reduceMotion = pref?.ReduceMotion ?? false,
    highContrast = pref?.HighContrast ?? false,
    displayPronouns = profile?.DisplayPronouns
});
```

**Notes:**
- Defaults: `false` for toggles, `null` for pronouns
- Pronouns stored in `UserProfiles` (public-facing)
- Motion/contrast stored in `UserPreferences` (private settings)

---

### PUT /me/accessibility

**Purpose:** Update accessibility preferences

**Request:**
```json
{
  "reduceMotion": true,
  "highContrast": false,
  "displayPronouns": "she/her"
}
```

**Response:**
```json
{ "updated": true }
```

**Validation:**
- `displayPronouns`: max 50 chars
- Returns **400** if too long

**Implementation:**
```csharp
if (req.DisplayPronouns != null && req.DisplayPronouns.Length > 50)
    return Results.BadRequest(new { error = "PRONOUNS_TOO_LONG" });

var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
if (pref != null)
{
    if (req.ReduceMotion.HasValue) pref.ReduceMotion = req.ReduceMotion.Value;
    if (req.HighContrast.HasValue) pref.HighContrast = req.HighContrast.Value;
}

var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
if (profile != null && req.DisplayPronouns != null)
    profile.DisplayPronouns = req.DisplayPronouns;

await db.SaveChangesAsync(ct);
```

**Partial updates:**
- Only updates fields present in request
- `null` in request = no change (not a reset)
- Requires both `UserPreferences` and `UserProfiles` rows to exist (created during onboarding)

---

## PushEndpoints.cs

**Location:** `backend/WovenBackend/Endpoints/UserDataEndpoints.cs` (same file, separate static class)

### GET /me/vapid-public-key

**Purpose:** Return VAPID public key for Web Push subscription

**Response:**
```json
{
  "publicKey": "BKxT...eFg="
}
```

**Implementation:**
```csharp
group.MapGet("/vapid-public-key", (IWebPushService push) =>
    Results.Ok(new { publicKey = push.GetPublicVapidKey() }));
```

**Notes:**
- No auth required (public key is public)
- Used by frontend to call `pushManager.subscribe(...)`

---

### POST /me/push-subscription

**Purpose:** Register a browser push subscription

**Request:**
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/...",
  "p256dh": "BGt...",
  "auth": "r3K...",
  "userAgent": "Mozilla/5.0 ..."
}
```

**Response:**
```json
{ "registered": true }
```

**Implementation:**
```csharp
var userId = GetPushUserId(principal);

// Upsert by endpoint (one browser = one subscription)
var existing = await db.PushSubscriptions
    .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == req.Endpoint, ct);

if (existing is null)
{
    db.PushSubscriptions.Add(new UserPushSubscription
    {
        UserId    = userId,
        Endpoint  = req.Endpoint,
        P256dh    = req.P256dh,
        Auth      = req.Auth,
        UserAgent = req.UserAgent,
    });
    await db.SaveChangesAsync(ct);
}

return Results.Ok(new { registered = true });
```

**Idempotency:**
- Upsert pattern (endpoint is unique per browser)
- Re-registering same endpoint = no-op
- Multiple browsers = multiple rows (one per endpoint)

**Security:**
- `p256dh` and `auth` are public/auth keys from browser
- Stored securely (no encryption needed — they're meant to be sent to FCM/VAPID)

---

### DELETE /me/push-subscription

**Purpose:** Unregister push subscription (toggle off or logout)

**Request:**
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/..."
}
```

**Response:**
```json
{ "unregistered": true }
```

**Implementation:**
```csharp
var userId = GetPushUserId(principal);
await db.PushSubscriptions
    .Where(s => s.UserId == userId && s.Endpoint == req.Endpoint)
    .ExecuteDeleteAsync(ct);
return Results.Ok(new { unregistered = true });
```

**Idempotent:** Safe to call multiple times

---

## Data Models

### User Entity

**File:** `backend/WovenBackend/data/Entities/User.cs`

```csharp
public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }

    // OAuth-first MVP
    public string? PasswordHash { get; set; }
    public string? FullName { get; set; }
    public string? ProfilePhoto { get; set; }

    public ProfileStatus ProfileStatus { get; set; } = ProfileStatus.INCOMPLETE;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Phase 2B: trust scoring (0.0–1.0, default 0.5)
    public float TrustScore { get; set; } = 0.5f;
    public DateTime? TrustUpdatedAt { get; set; }

    // Phase 4A: anti-ghosting signals
    public float GhostScore { get; set; } = 0.5f;
    public DateTimeOffset? LastActiveAt { get; set; }

    // Phase 5A: identity verification
    public bool IsVerified { get; set; } = false;
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationType { get; set; }  // "SELFIE" | "ID_DOCUMENT"

    // Weekly coaching summary opt-out (default false = opted in)
    public bool CoachingOptedOut { get; set; } = false;
}
```

**Key fields:**
- `ProfileStatus` — INCOMPLETE | REVIEW_PENDING | ACTIVE | PAUSED | BANNED
- `TrustScore` — Platform trust (0.0 = flagged, 1.0 = verified + history)
- `GhostScore` — Reply likelihood (0.0 = ghost, 1.0 = consistent responder)
- `IsVerified` — Selfie or ID verification completed
- `CoachingOptedOut` — Weekly reflection opt-out (false = enabled)

**Not stored in User:**
- Password reset tokens → separate `PasswordResetTokens` table
- Refresh tokens → separate `RefreshTokens` table
- Session data → Redis cache

---

### UserProfile Entity

**File:** `backend/WovenBackend/data/Entities/UserProfile.cs`

**Related fields (not full schema):**
```csharp
public string? DisplayPronouns { get; set; }  // "they/them" | "she/her" | "he/him" | custom
```

---

### UserPreferences Entity

**File:** `backend/WovenBackend/data/Entities/UserPreferences.cs`

**Related fields:**
```csharp
public bool ReduceMotion { get; set; } = false;
public bool HighContrast { get; set; } = false;
```

---

### Block Entity

**File:** `backend/WovenBackend/data/Entities/Block.cs`

```csharp
public class Block
{
    public int Id { get; set; }
    public int BlockerId { get; set; }
    public int BlockedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }  // "inappropriate" | "harassment" | "spam" | null
}
```

**Index:** `(BlockerId, BlockedId)` unique

---

### UserPushSubscription Entity

**File:** `backend/WovenBackend/data/Entities/UserPushSubscription.cs`

```csharp
public class UserPushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string Endpoint { get; set; }  // FCM/VAPID endpoint URL
    public required string P256dh { get; set; }    // Encryption key
    public required string Auth { get; set; }      // Auth secret
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Index:** `(UserId, Endpoint)` unique

---

## Services

### IMediaService

**File:** `backend/WovenBackend/Services/MediaService.cs`

**Used in:** Account deletion

**Method:**
```csharp
Task DeleteAllForUserAsync(int userId, CancellationToken ct);
```

**Implementation:**
- Enumerates all Azure Blob containers: `profile-photos`, `voice-notes`, `tiles`
- Deletes all blobs with prefix `{userId}/`
- Parallel deletion across containers
- Logs each deletion: `[Media] Deleted {BlobPath} for user {UserId}`

---

### ICacheService

**File:** `backend/WovenBackend/Services/CacheService.cs`

**Used in:** Rate limiting (data export, opinion submission)

**Methods:**
```csharp
Task<T?> GetAsync<T>(string key, CancellationToken ct);
Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);
Task<bool> CheckRateLimitAsync(string key, int maxCount, TimeSpan window, CancellationToken ct);
```

**Implementation:**
- Backed by **Redis** (Azure Cache for Redis)
- `CheckRateLimitAsync` uses Redis `INCR` + `EXPIRE` (atomic)

---

### ISecurityAuditService

**File:** `backend/WovenBackend/Services/Security/SecurityAuditService.cs`

**Used in:** All sensitive actions (export, reset, deletion)

**Method:**
```csharp
void Log(string action, int? userId = null, string? resourceType = null, string? resourceId = null);
```

**Events logged:**
- `bulk_data_export` — GDPR export
- `preference_reset` — Visual/voice preference reset
- `account_deletion` — Hard delete

**Storage:**
- Writes to `security_audit_logs` table
- Retention: 90 days (cleaned by `SecurityAuditCleanupWorker`)
- Indexed by `userId`, `action`, `created_at`

---

### IInsightService

**File:** `backend/WovenBackend/Services/Insights/InsightService.cs`

**Methods:**
```csharp
Task<(bool shouldAsk, string? trigger, string? prompt)> ShouldAskOpinionAsync(int userId, CancellationToken ct);
Task SubmitOpinionAsync(int userId, string text, string trigger, CancellationToken ct);
```

**Purpose:**
- Determines when to prompt user for qualitative feedback
- Stores opinions in `user_opinions` table
- Used by weight learning pipeline to tune matching algorithm

---

### IAnalyticsService

**File:** `backend/WovenBackend/Services/Analytics/AnalyticsService.cs`

**Method:**
```csharp
Task TrackAsync(int userId, int? resourceId, string eventName, object? properties = null);
```

**Events:**
- `InsightViewed` — User viewed insights page

**Storage:**
- Batched to Azure Application Insights
- Fire-and-forget (non-blocking)

---

## Security Patterns

### User ID Extraction

**Pattern (all endpoints):**
```csharp
private static int GetUserId(ClaimsPrincipal principal)
{
    var raw = principal.FindFirstValue("uid")
           ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new UnauthorizedAccessException("No user ID claim");
    return int.Parse(raw);
}
```

**Claims priority:**
1. `"uid"` — Custom claim (JWT)
2. `ClaimTypes.NameIdentifier` — Standard .NET claim
3. Throw `UnauthorizedAccessException` → mapped to **401** by `AuthExceptionHandler`

**Why separate helper:**
- Consistency across all endpoints
- Single source of truth for claim chain
- Prevents local variations (e.g., `GetUserId` in `CoachingEndpoints.cs` removed in June 3 refactor)

---

### Rate Limiting

**Pattern:**
```csharp
var allowed = await cache.CheckRateLimitAsync(key, maxCount, window, ct);
if (!allowed)
{
    http.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
    return Results.StatusCode(429);
}
```

**Implementations:**
- **Data export:** 1/30 days, string flag with TTL
- **Opinion submission:** 1/calendar month, counter with TTL

**Redis backing:**
- `INCR {key}` (atomic increment)
- `EXPIRE {key} {ttl}` (TTL in seconds)
- Returns count; backend compares to limit

---

### Bulk Deletes

**Pattern (preferred):**
```csharp
await db.Tiles.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
```

**Why not `RemoveRange`:**
```csharp
// ❌ Bad: loads all rows into memory, generates DELETE per row
var tiles = await db.Tiles.Where(t => t.UserId == userId).ToListAsync(ct);
db.Tiles.RemoveRange(tiles);
await db.SaveChangesAsync(ct);
```

**`ExecuteDeleteAsync` benefits:**
- Generates `DELETE FROM tiles WHERE user_id = @userId` (single SQL statement)
- No memory allocation for rows
- No change tracking overhead
- Works with large datasets (1000s of rows)

---

### Audit Before Delete

**Pattern (account deletion):**
```csharp
// 1. Audit BEFORE deletion (so we have a trail)
audit.Log("account_deletion", userId: userId, resourceType: "User", resourceId: userId.ToString());

// 2. Delete all blobs
await media.DeleteAllForUserAsync(userId, ct);

// 3. Delete all data
await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
await db.SaveChangesAsync(ct);
```

**Why audit first:**
- Audit row persists after user deletion
- Proves deletion happened (compliance)
- Includes timestamp, user ID, IP address (from middleware)

---

## Error Handling

### Global Exception Handlers

**Order (Program.cs):**
1. `DomainExceptionHandler` — `DomainException` → 422
2. `AuthExceptionHandler` — `UnauthorizedAccessException` → 401
3. `GlobalExceptionHandler` — Unhandled exceptions → 500

**Response format:**
```json
{
  "error": "FRIENDLY_ERROR_CODE",
  "correlationId": "abc123def456",
  "timestamp": "2026-10-07T12:34:56Z"
}
```

**Correlation ID:**
- Set by `CorrelationIdMiddleware` (first in pipeline)
- Reads from `X-Correlation-ID` header or generates 16-char hex
- Pushed into Serilog `LogContext`
- Echoed back in response header
- Included in all error responses

---

### Validation Errors

**Pattern:**
```csharp
if (req.Text.Length > 300)
    return Results.BadRequest(new { error = "TEXT_TOO_LONG" });
```

**Error codes (not exception messages):**
- `TEXT_REQUIRED`
- `TEXT_TOO_LONG`
- `INVALID_TRIGGER`
- `PRONOUNS_TOO_LONG`
- `BLOB_NOT_FOUND`
- `PHOTO_REJECTED`

**Frontend handling:**
- Error codes map to user-facing messages
- English-only for now (i18n planned)

---

## Performance

### AsNoTracking

**Used in:**
- All GET endpoints (read-only queries)
- Data export (no updates)

**Why:**
- Disables change tracking (30-40% faster queries)
- Reduces memory allocation
- Safe for read-only operations

**When NOT to use:**
- PUT/POST endpoints that modify entities
- Queries followed by updates

---

### Parallel Queries

**Pattern (data export):**
```csharp
var tiles = await db.Tiles.AsNoTracking().Where(...).ToListAsync(ct);
var messages = await db.ChatMessages.AsNoTracking().Where(...).ToListAsync(ct);
var prefs = await db.UserVisualPreferences.AsNoTracking().Where(...).FirstOrDefaultAsync(ct);
```

**Could be parallelized:**
```csharp
var (tiles, messages, prefs) = await Task.WhenAll(
    db.Tiles.AsNoTracking().Where(...).ToListAsync(ct),
    db.ChatMessages.AsNoTracking().Where(...).ToListAsync(ct),
    db.UserVisualPreferences.AsNoTracking().Where(...).FirstOrDefaultAsync(ct)
);
```

**Why not done yet:**
- Marginal gain (export is rate-limited to 1/30 days)
- Tuple unpacking requires type inference help (verbose)
- Sequential is clearer for audit trail

---

### Indexing

**Key indexes (relevant to profile endpoints):**

```sql
-- Blocks
CREATE INDEX idx_blocks_blocker ON blocks(blocker_id);
CREATE UNIQUE INDEX idx_blocks_pair ON blocks(blocker_id, blocked_id);

-- Push subscriptions
CREATE UNIQUE INDEX idx_push_user_endpoint ON user_push_subscriptions(user_id, endpoint);

-- Tiles (ownership)
CREATE INDEX idx_tiles_user ON tiles(user_id);

-- Chat messages (ownership)
CREATE INDEX idx_chat_messages_sender ON chat_messages(sender_user_id);

-- Photo embeddings (ownership)
CREATE INDEX idx_photo_embeddings_user ON photo_embeddings(user_id);
```

**Missing index (acceptable):**
- `UserVisualDecisions.ViewerUserId` — rare deletes (reset preference)
- `UserVectors.UserId` — rare deletes (account deletion)

---

## GDPR Compliance

### Right to Access

**Endpoint:** `GET /me/data-summary`
- Lightweight overview
- No rate limit
- Counts only (fast response)

### Right to Data Portability

**Endpoint:** `GET /me/data-export`
- Complete user data dump
- Rate-limited (1/30 days) to prevent abuse
- Includes third-party disclaimer

### Right to Deletion

**Endpoint:** `DELETE /me/account`
- Hard delete (not soft delete)
- Anonymizes matches (preserves other user's data)
- Deletes all blobs (photos, voice, tiles)
- Audit logged before deletion
- Third-party retention disclaimer included

### Right to Rectification

**Endpoints:**
- Profile updates: `/onboarding/review` (not in UserDataEndpoints)
- Accessibility: `PUT /me/accessibility`

### Right to Object

**Endpoints:**
- Preference resets: `POST /me/visual-preference/reset`, `POST /me/voice-preference/reset`
- Coaching opt-out: `POST /coaching/opt-out` (in CoachingEndpoints.cs)

---

## Testing Considerations

### Unit Tests (Not yet implemented)

**Target coverage:**
- GetUserId claim chain fallback
- Rate limit edge cases (exactly at limit, expired key)
- Anonymization logic (UserAId/UserBId set to 0)

### Integration Tests (Not yet implemented)

**User flows:**
- Export → rate limit → 429
- Delete account → verify blobs deleted → verify matches anonymized
- Unblock → verify can re-match

---

## Known Gaps

| Gap | Impact | Priority |
|---|---|---|
| No password reset flow in profile | Must go to `/forgot-password` | Low |
| No export format choice (JSON only) | GDPR accepts JSON | Low |
| No batch unblock | Rare use case | Low |
| No delete confirmation email | UX improvement | Medium |
| No "download my data" one-click | Requires separate export call | Medium |
| No third-party deletion automation | Manual support request | High (GDPR) |
| JWT in localStorage | XSS risk | High (see COOKIE_AUTH_MIGRATION.md) |

---

## Related Documentation

- **[Profile Overview](./README.md)** — Feature summary
- **[Frontend Implementation](./frontend.md)** — Angular components
- **[Settings Page](./settings.md)** — Settings UI details
- **[My Tiles Page](./my-tiles.md)** — Tile management UI

---

## Future Considerations

- **Export scheduling:** Weekly/monthly automated exports
- **Retention policies:** Auto-delete after N days of inactivity
- **Granular deletion:** Delete only chats, only tiles, etc.
- **Export formats:** CSV, XML (in addition to JSON)
- **Third-party deletion pipeline:** Auto-submit to OpenAI/Replicate APIs
- **Account pause:** Soft delete (reversible within 30 days)
