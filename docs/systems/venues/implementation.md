# VenueService Implementation Guide

**Last Updated:** 2026-10-07

---

## Overview

Deep dive into `VenueService.cs` implementation — code structure, dependencies, error handling, and extension points.

---

## File Location

```
backend/WovenBackend/Services/Venues/
├── IVenueService.cs      ← Interface
└── VenueService.cs       ← Implementation
```

---

## Interface Definition

### `IVenueService.cs`

```csharp
namespace WovenBackend.Services.Venues;

public record VenueSuggestion(
    string Name,
    string Address,
    double Rating,
    int PriceLevel,
    string GoogleMapsUrl);

public interface IVenueService
{
    Task<List<VenueSuggestion>> GetVenueSuggestionsAsync(
        int userId, 
        int partnerUserId, 
        CancellationToken ct = default);
}
```

**Design notes:**
- ✅ `VenueSuggestion` is a **record** (immutable, value equality)
- ✅ Single public method (focused interface)
- ✅ Returns `List<T>` (never null — empty list on failure)
- ✅ `CancellationToken` for long-running HTTP calls

---

## Implementation Structure

### Constructor Injection

```csharp
public class VenueService : IVenueService
{
    private readonly WovenDbContext _db;
    private readonly ICacheService _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ISecurityAuditService _audit;
    private readonly ILogger<VenueService> _logger;

    public VenueService(
        WovenDbContext db,
        ICacheService cache,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ISecurityAuditService audit,
        ILogger<VenueService> logger)
    {
        _db = db;
        _cache = cache;
        _httpFactory = httpFactory;
        _config = config;
        _audit = audit;
        _logger = logger;
    }
}
```

**Dependencies:**

| Service | Purpose |
|---|---|
| `WovenDbContext` | Fetch user profiles (city, lat/lng) |
| `ICacheService` | 24h venue caching (Redis) |
| `IHttpClientFactory` | Google API HTTP client |
| `IConfiguration` | Read `Google:PlacesApiKey` |
| `ISecurityAuditService` | Log external API calls |
| `ILogger` | Warning/error logging |

---

## Method Breakdown

### `GetVenueSuggestionsAsync`

**Full signature:**
```csharp
public async Task<List<VenueSuggestion>> GetVenueSuggestionsAsync(
    int userId, 
    int partnerUserId, 
    CancellationToken ct = default)
```

**Flow:**
1. Check cache
2. Fetch user profiles
3. Resolve location
4. Geocode if needed
5. Query Google Places
6. Filter + transform results
7. Cache + audit log
8. Return suggestions

---

### Step 1: Cache Lookup

```csharp
var cacheKey = $"venues:{userId}:{partnerUserId}";
var cached = await _cache.GetAsync<List<VenueSuggestion>>(cacheKey, ct);
if (cached != null) return cached;
```

**Cache key format:**  
- `venues:{userId}:{partnerUserId}`
- Example: `venues:123:456`

**Cache backend:** Redis (via `ICacheService`)  
**TTL:** 24 hours (set at cache write, see Step 7)

**Early return:**  
- ✅ Cache hit → skip all API calls, return immediately
- ❌ Cache miss → proceed to Step 2

---

### Step 2: Fetch User Profiles

```csharp
var profiles = await _db.UserProfiles.AsNoTracking()
    .Where(p => p.UserId == userId || p.UserId == partnerUserId)
    .Select(p => new { p.UserId, p.City, p.State, p.Lat, p.Lng })
    .ToListAsync(ct);

var myProfile = profiles.FirstOrDefault(p => p.UserId == userId);
var partnerProfile = profiles.FirstOrDefault(p => p.UserId == partnerUserId);
```

**Optimizations:**
- ✅ `AsNoTracking()` — read-only query (faster)
- ✅ `Select(...)` — project only needed columns (reduces data transfer)
- ✅ Single query — fetch both users at once (1 DB round-trip)

**Null safety:**
- `FirstOrDefault()` returns `null` if profile missing
- Handled in Step 3

---

### Step 3: Resolve Location

```csharp
string city;
string? state;
double? lat;
double? lng;

if (myProfile?.City == partnerProfile?.City && myProfile?.City != null)
{
    // Shared city (ideal case)
    city = myProfile.City;
    state = myProfile.State;
    lat = myProfile.Lat;
    lng = myProfile.Lng;
}
else
{
    // Different cities → use viewer's city
    city = myProfile?.City ?? string.Empty;
    state = myProfile?.State;
    lat = myProfile?.Lat;
    lng = myProfile?.Lng;
}

if (string.IsNullOrEmpty(city)) return new List<VenueSuggestion>();
```

**Location priority:**
1. ✅ Both users in same city → shared city
2. ✅ Different cities → viewer's city (userId)
3. ❌ No city → return empty list

**Coordinates:**
- Prefer `UserProfile.Lat/Lng` if populated
- Fall back to geocoding (Step 4) if null

---

### Step 4: Geocoding (Conditional)

```csharp
var apiKey = _config["Google:PlacesApiKey"];
if (string.IsNullOrEmpty(apiKey)) return new List<VenueSuggestion>();

var http = _httpFactory.CreateClient("google");

if (lat == null || lng == null)
{
    var geocodeUrl = $"https://maps.googleapis.com/maps/api/geocode/json" +
        $"?address={Uri.EscapeDataString(city + (state != null ? ", " + state : ""))}" +
        $"&key={apiKey}";

    var geoResp = await http.GetAsync(geocodeUrl, ct);
    if (!geoResp.IsSuccessStatusCode) return new List<VenueSuggestion>();

    var geoJson = await geoResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    var results = geoJson.GetProperty("results");
    if (results.GetArrayLength() == 0) return new List<VenueSuggestion>();

    var loc = results[0].GetProperty("geometry").GetProperty("location");
    lat = loc.GetProperty("lat").GetDouble();
    lng = loc.GetProperty("lng").GetDouble();
}
```

**When geocoding runs:**
- ❌ `UserProfile.Lat` or `UserProfile.Lng` is null
- ✅ City name is non-null

**Error handling:**
- HTTP failure → return empty list
- Zero results → return empty list
- No retry logic (Google API highly available)

**Performance:**
- ~200ms API latency
- Only runs once per user (lat/lng should be set at onboarding)

---

### Step 5: Google Places Query

```csharp
var placesUrl = "https://maps.googleapis.com/maps/api/place/nearbysearch/json" +
    $"?location={lat},{lng}" +
    $"&radius=3000" +
    $"&type=cafe%7Crestaurant%7Cpark" +
    $"&minprice=1&maxprice=3" +
    $"&opennow=false" +
    $"&key={apiKey}";

var placesResp = await http.GetAsync(placesUrl, ct);
if (!placesResp.IsSuccessStatusCode) return new List<VenueSuggestion>();

var placesJson = await placesResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
var places = placesJson.GetProperty("results");
```

**Parameters:**
- `location` — lat,lng from Step 3/4
- `radius` — 3000 meters (3km)
- `type` — `cafe|restaurant|park` (pipe-delimited)
- `minprice` / `maxprice` — 1-3 (affordable to moderate)
- `opennow` — false (don't filter by hours)

**Response handling:**
- HTTP failure → return empty list
- Parse JSON as `JsonElement` (dynamic structure)

---

### Step 6: Filter & Transform Results

```csharp
var suggestions = new List<VenueSuggestion>();
foreach (var place in places.EnumerateArray())
{
    if (suggestions.Count >= 3) break;

    var rating = place.TryGetProperty("rating", out var ratingEl)
        ? ratingEl.GetDouble() : 0.0;
    if (rating < 4.0) continue;

    var name = place.GetProperty("name").GetString() ?? string.Empty;
    var address = place.TryGetProperty("vicinity", out var vic)
        ? vic.GetString() ?? string.Empty : string.Empty;
    var priceLevel = place.TryGetProperty("price_level", out var pl)
        ? pl.GetInt32() : 0;
    var placeId = place.TryGetProperty("place_id", out var pid)
        ? pid.GetString() ?? string.Empty : string.Empty;
    var mapsUrl = $"https://maps.google.com/?q={Uri.EscapeDataString(placeId)}";

    suggestions.Add(new VenueSuggestion(name, address, rating, priceLevel, mapsUrl));
}
```

**Filters:**
1. ❌ `rating < 4.0` → skip
2. ❌ Already have 3 suggestions → break

**Field extraction:**
- `TryGetProperty(...)` — safe null handling (some fields optional)
- `?? string.Empty` — default to empty string (never null)

**Google Maps URL:**
- Format: `https://maps.google.com/?q={placeId}`
- `Uri.EscapeDataString(...)` — URL-encode place ID

---

### Step 7: Cache & Audit

```csharp
if (suggestions.Count > 0)
    await _cache.SetAsync(cacheKey, suggestions, TimeSpan.FromHours(24), ct);

_audit.Log("external_api_call", userId: userId, service: "VenueService",
    resourceType: "GooglePlaces", resourceId: "venue_search", piiStripped: true);

return suggestions;
```

**Cache write:**
- Only if `suggestions.Count > 0` (don't cache empty results)
- TTL: 24 hours
- Key: `venues:{userId}:{partnerUserId}`

**Audit log:**
- Event: `external_api_call`
- Service: `VenueService`
- Resource: `GooglePlaces / venue_search`
- PII stripped: `true` (no user data sent to audit log)

**Return:**
- Always returns `List<VenueSuggestion>` (empty if no results)
- Never throws exceptions to caller

---

### Step 8: Exception Handling

```csharp
try
{
    // Steps 1-7 here
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "VenueService failed for user {UserId}", userId);
    return new List<VenueSuggestion>();
}
```

**Error policy:**
- ✅ Log warning (with exception + userId)
- ✅ Return empty list (graceful degradation)
- ❌ Never throw to caller (venues are optional feature)

**Logged failures:**
- JSON parsing errors
- HTTP client exceptions
- Redis cache failures
- Unexpected Google API responses

---

## Registration (Program.cs)

```csharp
builder.Services.AddScoped<WovenBackend.Services.Venues.IVenueService,
    WovenBackend.Services.Venues.VenueService>();
```

**Lifetime:** Scoped (per HTTP request)  
**Why scoped?** Shares `WovenDbContext` lifetime (also scoped)

---

## Extension Points

### 1. Add Personalization

**Current:**
```csharp
if (rating < 4.0) continue;
```

**Enhanced:**
```csharp
var score = CalculatePersonalizedScore(place, userId, partnerUserId);
if (score < 0.7) continue;
```

**Personalized score formula:**
```csharp
private double CalculatePersonalizedScore(JsonElement place, int userId, int partnerId)
{
    var baseScore = place.GetProperty("rating").GetDouble() / 5.0;  // Normalize to 0-1

    // Boost by user preferences
    var userHobbies = GetUserHobbies(userId);
    var venueTypes = GetVenueTypes(place);
    var hobbyBoost = venueTypes.Intersect(userHobbies).Count() * 0.1;

    return Math.Min(1.0, baseScore + hobbyBoost);
}
```

### 2. Enforce Diversity

**Current:** Take first 3 venues ≥4.0 rating

**Enhanced:** Ensure 1 café + 1 restaurant + 1 park
```csharp
var suggestions = new Dictionary<string, VenueSuggestion>
{
    ["cafe"] = null,
    ["restaurant"] = null,
    ["park"] = null
};

foreach (var place in places.EnumerateArray())
{
    var types = place.GetProperty("types").EnumerateArray()
        .Select(t => t.GetString()).ToList();

    if (types.Contains("cafe") && suggestions["cafe"] == null)
        suggestions["cafe"] = CreateVenueSuggestion(place);
    // ... repeat for restaurant, park
}

return suggestions.Values.Where(v => v != null).ToList();
```

### 3. Add Async Prefetching

**Current:** Venues fetched on-demand (cache miss = 500ms latency)

**Enhanced:** Prefetch venues nightly for all Find Love matches
```csharp
public class VenuePrefetchWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var matches = await _db.Matches
                .Where(m => m.FindLoveAt != null && m.BalloonState == BalloonState.ACTIVE)
                .ToListAsync(ct);

            foreach (var match in matches)
            {
                await _venues.GetVenueSuggestionsAsync(match.UserAId, match.UserBId, ct);
                // → Warms cache, users never hit cold cache
            }

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
```

**Registration:**
```csharp
builder.Services.AddHostedService<VenuePrefetchWorker>();
```

---

## Testing

### Unit Test Example

```csharp
[Fact]
public async Task GetVenueSuggestionsAsync_ReturnsEmptyList_WhenCityMissing()
{
    // Arrange
    var mockDb = CreateMockDbContext(new UserProfile { UserId = 1, City = null });
    var service = new VenueService(mockDb, ...);

    // Act
    var result = await service.GetVenueSuggestionsAsync(1, 2, CancellationToken.None);

    // Assert
    Assert.Empty(result);
}
```

### Integration Test Example

```csharp
[Fact]
public async Task GetVenueSuggestionsAsync_ReturnsVenues_WhenGoogleApiSucceeds()
{
    // Arrange
    var db = CreateRealDbContext();
    await db.UserProfiles.AddAsync(new UserProfile 
    { 
        UserId = 1, 
        City = "New Delhi", 
        Lat = 28.7041, 
        Lng = 77.1025 
    });
    await db.SaveChangesAsync();

    var service = new VenueService(db, realCache, realHttpFactory, ...);

    // Act
    var result = await service.GetVenueSuggestionsAsync(1, 2, CancellationToken.None);

    // Assert
    Assert.NotEmpty(result);
    Assert.All(result, v => Assert.True(v.Rating >= 4.0));
    Assert.True(result.Count <= 3);
}
```

---

## Performance Profiling

### Latency Breakdown (Cache Miss)

| Step | Operation | Latency |
|---|---|---|
| 1 | Cache lookup | ~5ms (Redis) |
| 2 | DB query (UserProfiles) | ~20ms (indexed) |
| 3 | Geocoding API (conditional) | ~200ms |
| 4 | Places API | ~300ms |
| 5 | Filtering + transform | <1ms |
| 6 | Cache write | ~5ms |
| **Total** | | **~530ms** |

### Latency Breakdown (Cache Hit)

| Step | Operation | Latency |
|---|---|---|
| 1 | Cache lookup | ~5ms (Redis) |
| **Total** | | **~5ms** |

**99% improvement with cache hit!**

---

## Monitoring Queries

### Cache Hit Rate

```sql
-- Redis CLI
INFO stats | grep keyspace_hits
INFO stats | grep keyspace_misses

-- Calculate hit rate
hit_rate = keyspace_hits / (keyspace_hits + keyspace_misses)
```

### API Call Volume

```sql
-- Security audit logs
SELECT COUNT(*)
FROM security_audit_logs
WHERE service = 'VenueService'
  AND resource_type = 'GooglePlaces'
  AND created_at >= NOW() - INTERVAL '1 day';
```

### Error Rate

```powershell
# Serilog logs (grep warnings)
cat logs/woven-backend.log | grep "VenueService failed"
```

---

## Debugging Checklist

### Venues Not Returned

1. ✅ Check `Google:PlacesApiKey` exists (User Secrets / Azure Key Vault)
2. ✅ Verify UserProfile has `City` populated
3. ✅ Check Redis cache (may be serving stale empty result)
4. ✅ Test Google API directly (curl)
5. ✅ Check Serilog logs for `VenueService failed`
6. ✅ Verify match has `BothMessagedAt` (endpoint checks this)

### Cache Not Working

1. ✅ Verify Redis connection (check `ICacheService` health)
2. ✅ Check cache key format (`venues:{userId}:{partnerUserId}`)
3. ✅ Verify 24h TTL not expired
4. ✅ Check if `suggestions.Count > 0` (empty results not cached)

---

## Related Documentation

- **[Venues System Overview](./README.md)** — high-level design
- **[Google Places Integration](./google-places.md)** — API details
- **[Recommendation Algorithm](./recommendations.md)** — filtering logic
- **[API Reference](./api.md)** — HTTP endpoints

---

## Code References

- [`IVenueService.cs`](../../../backend/WovenBackend/Services/Venues/IVenueService.cs) — Interface
- [`VenueService.cs`](../../../backend/WovenBackend/Services/Venues/VenueService.cs) — Implementation
- [`ChatEndpoints.cs`](../../../backend/WovenBackend/Endpoints/ChatEndpoints.cs#L1147) — HTTP endpoint
- [`Program.cs`](../../../backend/WovenBackend/Program.cs#L666) — DI registration
