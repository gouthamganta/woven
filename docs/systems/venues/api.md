# Venues API Reference

**Last Updated:** 2026-10-07

---

## Overview

HTTP endpoint for fetching venue suggestions for a matched pair. Part of the Chat API group.

---

## Endpoint

### `GET /chats/{threadId}/venue-suggestions`

**Purpose:** Fetch date venue recommendations for a matched pair

**Authentication:** Required (JWT Bearer token or HttpOnly cookie)

**Rate limit:** None (24h cache prevents abuse)

---

## Request

### Path Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `threadId` | `Guid` | ✅ | Chat thread ID (UUID format) |

### Headers

| Header | Value | Required |
|---|---|---|
| `Authorization` | `Bearer {token}` | ✅ (or cookie) |
| `X-Correlation-Id` | `{16-char hex}` | ❌ (auto-generated if missing) |

### Example

```http
GET /chats/a1b2c3d4-e5f6-7890-abcd-ef1234567890/venue-suggestions HTTP/1.1
Host: api.wooven.me
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

---

## Response

### Success (200 OK)

```json
{
  "venues": [
    {
      "name": "Blue Tokai Coffee Roasters",
      "address": "Hauzkhas Village, New Delhi",
      "rating": 4.5,
      "priceLevel": 2,
      "googleMapsUrl": "https://maps.google.com/?q=ChIJN1t_tDeuEmsRUsoyG83frY4"
    },
    {
      "name": "Lodhi Garden",
      "address": "Lodhi Road, New Delhi",
      "rating": 4.7,
      "priceLevel": 0,
      "googleMapsUrl": "https://maps.google.com/?q=ChIJabcdef123456"
    },
    {
      "name": "Indian Accent",
      "address": "The Lodhi, New Delhi",
      "rating": 4.6,
      "priceLevel": 3,
      "googleMapsUrl": "https://maps.google.com/?q=ChIJghijkl789012"
    }
  ]
}
```

### Empty Results (200 OK)

```json
{
  "venues": []
}
```

**Reasons for empty results:**
- No venues in city match filters (rating ≥4.0, price 1-3)
- Small city with limited Google Places data
- User profile missing city
- Google API error (graceful degradation)

---

## Response Schema

### `VenueSuggestion` Object

| Field | Type | Description | Example |
|---|---|---|---|
| `name` | `string` | Venue name | `"Blue Tokai Coffee Roasters"` |
| `address` | `string` | Short address (vicinity) | `"Hauzkhas Village, New Delhi"` |
| `rating` | `double` | Google Places rating (0.0-5.0) | `4.5` |
| `priceLevel` | `int` | Price level (0-4) | `2` |
| `googleMapsUrl` | `string` | Google Maps link | `"https://maps.google.com/?q=ChIJ..."` |

**Price level key:**
- `0` — Free
- `1` — Cheap (₹)
- `2` — Moderate (₹₹)
- `3` — Expensive (₹₹₹)
- `4` — Very Expensive (₹₹₹₹)

**Rating:**
- Always ≥4.0 (VenueService filters out lower ratings)
- Decimal precision (e.g., 4.5, 4.7)

**Google Maps URL:**
- Opens in Google Maps app (mobile) or web (desktop)
- Shows venue details, photos, reviews, directions
- Format: `https://maps.google.com/?q={place_id}`

---

## Error Responses

### 401 Unauthorized

```json
{
  "error": "Unauthorized",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56.789Z"
}
```

**Cause:** Missing or invalid JWT token

**Fix:** Include valid `Authorization` header or HttpOnly cookie

---

### 403 Forbidden (Mutual Interest Required)

```json
{
  "error": "MUTUAL_INTEREST_REQUIRED"
}
```

**Cause:** Match exists but mutual interest not confirmed (`BothMessagedAt` is null)

**Fix:** Both users must exchange at least one message first

---

### 404 Not Found

```json
{
  "error": "Thread not found"
}
```

**Cause:** Invalid `threadId` (no ChatThread exists)

**Fix:** Verify threadId from `/chats` list endpoint

---

### 500 Internal Server Error

```json
{
  "error": "Internal server error",
  "correlationId": "a1b2c3d4e5f67890",
  "timestamp": "2026-10-07T12:34:56.789Z"
}
```

**Cause:** Unexpected server failure (database down, Redis unavailable)

**Note:** VenueService catches most errors and returns empty `venues: []` instead of HTTP 500.  
If you see 500, it's likely an endpoint-level failure (before VenueService called).

---

## Endpoint Implementation

### Code Location

**File:** [`backend/WovenBackend/Endpoints/ChatEndpoints.cs`](../../../backend/WovenBackend/Endpoints/ChatEndpoints.cs#L1147)

### Minimal API Registration

```csharp
group.MapGet("/{threadId:guid}/venue-suggestions", async (
    Guid threadId,
    WovenDbContext db,
    IVenueService venues,
    IAnalyticsService analytics,
    HttpContext http,
    CancellationToken ct) =>
{
    // Implementation below
});
```

**Route group:** `/chats`  
**Full path:** `/chats/{threadId}/venue-suggestions`  
**HTTP method:** `GET`

---

### Authorization Check

```csharp
var me = EndpointHelper.GetUserId(http.User);
// Throws UnauthorizedAccessException → 401 if claim invalid
```

**Claim sources:**
1. `"uid"` (custom claim)
2. `"sub"` (standard JWT claim)
3. `ClaimTypes.NameIdentifier` (fallback)

**Error:** `UnauthorizedAccessException` → handled by `AuthExceptionHandler` → HTTP 401

---

### Thread Ownership Validation

```csharp
var thread = await db.ChatThreads.AsNoTracking()
    .FirstOrDefaultAsync(t => t.Id == threadId, ct);

if (thread == null) return Results.NotFound(new { error = "Thread not found" });

var match = await db.Matches.AsNoTracking()
    .FirstOrDefaultAsync(m => m.Id == thread.MatchId, ct);

if (match == null) return Results.NotFound(new { error = "Match not found" });
if (match.UserAId != me && match.UserBId != me)
    return Results.Forbid();
```

**Checks:**
1. ✅ ChatThread exists
2. ✅ Match exists (via `thread.MatchId`)
3. ✅ User is participant (UserA or UserB)

**Errors:**
- `404` — thread or match not found
- `403` — user not participant in match

---

### Mutual Interest Check

```csharp
if (match.BothMessagedAt == null)
    return Results.Json(new { error = "MUTUAL_INTEREST_REQUIRED" }, statusCode: 403);
```

**Requirement:** Both users must have sent at least one message (`BothMessagedAt` non-null)

**Rationale:** Venues are a "Find Love" feature — only available after mutual engagement confirmed.

---

### Fetch Venues

```csharp
var partnerId = match.UserAId == me ? match.UserBId : match.UserAId;
var suggestions = await venues.GetVenueSuggestionsAsync(me, partnerId, ct);
```

**Partner resolution:** If `me == UserAId`, partner is `UserBId` (and vice versa)

**VenueService call:** `GetVenueSuggestionsAsync(userId, partnerUserId, ct)`
- Never throws exceptions (returns empty list on failure)
- Handles caching internally (24h TTL)

---

### Analytics Tracking

```csharp
_ = analytics.TrackAsync(me, null, AnalyticsEvents.VenueSuggestionsViewed,
    new { threadId, venueCount = suggestions.Count });
```

**Event:** `venue_suggestions_viewed`  
**Metadata:** `{ threadId, venueCount }`  
**Fire-and-forget:** `_ =` (don't await, runs async)

**Future events (not implemented):**
- `venue_clicked` — user taps a venue
- `venue_date_agreed` — both users agree on a venue

---

### Return Response

```csharp
return Results.Ok(new { venues = suggestions });
```

**Always returns HTTP 200** (even if `venues` is empty list)

---

## Usage Examples

### cURL

```bash
curl -X GET "https://api.wooven.me/chats/a1b2c3d4-e5f6-7890-abcd-ef1234567890/venue-suggestions" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

### JavaScript (fetch)

```javascript
const threadId = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';
const response = await fetch(`/chats/${threadId}/venue-suggestions`, {
  headers: {
    'Authorization': `Bearer ${token}`
  }
});

const { venues } = await response.json();
console.log(`Found ${venues.length} venues`);
```

### Angular Service

```typescript
// chat.service.ts
getVenueSuggestions(threadId: string): Observable<VenueSuggestion[]> {
  return this.http.get<{ venues: VenueSuggestion[] }>(
    `${this.apiBase}/chats/${threadId}/venue-suggestions`
  ).pipe(
    map(res => res.venues)
  );
}

// Usage in component
this.chatService.getVenueSuggestions(this.threadId).subscribe(venues => {
  this.venues = venues;
});
```

---

## Caching Behavior

### Client-Side Caching

**Recommendation:** Cache response in Angular service (1 hour TTL)

```typescript
private venueCache = new Map<string, { data: VenueSuggestion[], expires: number }>();

getVenueSuggestions(threadId: string): Observable<VenueSuggestion[]> {
  const cached = this.venueCache.get(threadId);
  if (cached && Date.now() < cached.expires) {
    return of(cached.data);
  }

  return this.http.get<{ venues: VenueSuggestion[] }>(
    `${this.apiBase}/chats/${threadId}/venue-suggestions`
  ).pipe(
    map(res => res.venues),
    tap(venues => {
      this.venueCache.set(threadId, {
        data: venues,
        expires: Date.now() + 3600_000  // 1 hour
      });
    })
  );
}
```

### Server-Side Caching

**Backend:** 24h Redis cache (automatic, no client action needed)

**Cache key:** `venues:{userId}:{partnerUserId}`

**Cache invalidation:**
- ❌ No manual invalidation (venues don't change frequently)
- ✅ Automatic expiry after 24h

---

## Rate Limiting

**Current:** None (24h cache prevents abuse)

**Future:** Consider rate limit (e.g., 10 requests/minute per user) if API abuse detected.

**Implementation:**
```csharp
var rateLimitKey = $"venue-suggestions:{me}";
var allowed = await _rateLimit.CheckAsync(rateLimitKey, maxRequests: 10, window: TimeSpan.FromMinutes(1));
if (!allowed) return Results.StatusCode(429);  // Too Many Requests
```

---

## Testing

### Manual Test Flow

1. **Setup:** Create match between UserA (Mumbai) + UserB (Mumbai)
2. **Exchange messages:** Both users send ≥1 message → `BothMessagedAt` set
3. **Fetch venues:**
   ```bash
   curl -X GET "http://localhost:5135/chats/{threadId}/venue-suggestions" \
     -H "Authorization: Bearer {userA_token}"
   ```
4. **Verify response:**
   - ✅ HTTP 200
   - ✅ `venues` array with 1-3 items
   - ✅ All ratings ≥4.0
   - ✅ All price levels 1-3
   - ✅ Google Maps URLs work

### Error Case Tests

**Test 1: No mutual interest**
```bash
# Create match but don't exchange messages
curl -X GET ".../venue-suggestions" -H "Authorization: Bearer {token}"
# Expected: HTTP 403, { error: "MUTUAL_INTEREST_REQUIRED" }
```

**Test 2: Invalid thread**
```bash
curl -X GET "/chats/00000000-0000-0000-0000-000000000000/venue-suggestions" \
  -H "Authorization: Bearer {token}"
# Expected: HTTP 404, { error: "Thread not found" }
```

**Test 3: Unauthorized user**
```bash
# UserC tries to access UserA + UserB thread
curl -X GET ".../venue-suggestions" -H "Authorization: Bearer {userC_token}"
# Expected: HTTP 403 (Forbid)
```

---

## Analytics

### Tracked Events

| Event | When | Metadata | Source |
|---|---|---|---|
| `venue_suggestions_viewed` | User requests venues | `{ threadId, venueCount }` | ChatEndpoints.cs |

**Future events (not implemented):**

| Event | When | Metadata |
|---|---|---|
| `venue_clicked` | User taps Google Maps link | `{ threadId, placeId, venueName }` |
| `venue_date_agreed` | Both users agree on venue | `{ threadId, placeId, agreedAt }` |
| `venue_date_completed` | Post-date feedback | `{ threadId, placeId, satisfaction }` |

---

## Security Considerations

### PII Exposure

- ✅ **Venue names/addresses** — public Google Places data (safe to expose)
- ✅ **City names** — already in UserProfile (safe)
- ❌ **User home addresses** — NEVER exposed (only city-level geocoding)

### API Key Security

- ✅ **Server-side only** — `Google:PlacesApiKey` never sent to frontend
- ✅ **Azure Key Vault** — stored securely in production
- ✅ **IP-restricted** — Google API key limited to Azure egress IP

### Audit Logging

```csharp
_audit.Log("external_api_call", 
    userId: userId, 
    service: "VenueService",
    resourceType: "GooglePlaces", 
    resourceId: "venue_search", 
    piiStripped: true);
```

**Logged for every venue request** — helps detect abuse, debugging, compliance.

---

## Related Documentation

- **[Venues System Overview](./README.md)** — high-level design
- **[Google Places Integration](./google-places.md)** — API client details
- **[Recommendation Algorithm](./recommendations.md)** — filtering logic
- **[Implementation Guide](./implementation.md)** — VenueService internals

---

## Changelog

**2026-06-04** — Initial implementation  
- Created endpoint at `/chats/{threadId}/venue-suggestions`
- Integrated with VenueService + Google Places API
- Added mutual interest check (`BothMessagedAt` validation)
- Added analytics tracking (`venue_suggestions_viewed`)

**Future** — Planned enhancements  
- Frontend UI (Angular component to display venues)
- Click tracking (`venue_clicked` event)
- Date coordination (both users agree on venue)
- Post-date feedback (venue satisfaction rating)
