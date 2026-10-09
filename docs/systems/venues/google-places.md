# Google Places API Integration

**Last Updated:** 2026-10-07

---

## Overview

VenueService integrates with **Google Places API (Nearby Search)** and **Google Geocoding API** to fetch real-world venue data. This doc covers API client setup, request patterns, error handling, and cost optimization.

---

## API Credentials

### Configuration

Stored in Azure Key Vault (production) / User Secrets (local dev):

```json
{
  "Google": {
    "PlacesApiKey": "AIzaSyC..."
  }
}
```

**Key requirements:**
- ✅ Must have **Places API** enabled (Nearby Search)
- ✅ Must have **Geocoding API** enabled (city → lat/lng)
- ✅ Must have billing account linked (Google charges per request)
- ✅ Restrict key to server IP (Azure Container Apps egress IP)

### Local Development Setup

```powershell
# Initialize User Secrets (one-time)
cd backend/WovenBackend
dotnet user-secrets init

# Set Google API key
dotnet user-secrets set "Google:PlacesApiKey" "YOUR_API_KEY_HERE"
```

See [`SECRETS_SETUP.md`](../../../backend/WovenBackend/SECRETS_SETUP.md) for full guide.

---

## HTTP Client Setup

### Registration (Program.cs)

```csharp
builder.Services.AddHttpClient("google", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", "Woven/1.0");
});
```

**Named client:** `"google"`  
**Timeout:** 10 seconds (Google APIs usually respond <2s)  
**User-Agent:** Required by Google ToS

### Client Injection

```csharp
public class VenueService : IVenueService
{
    private readonly IHttpClientFactory _httpFactory;

    public VenueService(IHttpClientFactory httpFactory, ...)
    {
        _httpFactory = httpFactory;
    }

    public async Task<List<VenueSuggestion>> GetVenueSuggestionsAsync(...)
    {
        var http = _httpFactory.CreateClient("google");
        // ...
    }
}
```

---

## API Endpoints

### 1. Geocoding API (City → Coordinates)

**Purpose:** Convert "City, State" string to (latitude, longitude)  
**When:** UserProfile has city name but missing `Lat`/`Lng` columns

#### Request

```
GET https://maps.googleapis.com/maps/api/geocode/json
    ?address={city}, {state}
    &key={apiKey}
```

#### Example

```http
GET https://maps.googleapis.com/maps/api/geocode/json
    ?address=New Delhi, Delhi
    &key=AIzaSyC...
```

#### Response (Simplified)

```json
{
  "results": [
    {
      "geometry": {
        "location": {
          "lat": 28.7041,
          "lng": 77.1025
        }
      },
      "formatted_address": "New Delhi, Delhi, India"
    }
  ],
  "status": "OK"
}
```

#### Status Codes

| Status | Meaning | VenueService Action |
|---|---|---|
| `OK` | Success | Extract lat/lng from `results[0].geometry.location` |
| `ZERO_RESULTS` | City not found | Return empty list |
| `INVALID_REQUEST` | Malformed query | Return empty list |
| `OVER_QUERY_LIMIT` | Quota exceeded | Return empty list + log warning |
| `REQUEST_DENIED` | API key invalid | Return empty list + log error |

#### Code

```csharp
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
```

---

### 2. Places API (Nearby Search)

**Purpose:** Find venues near coordinates  
**When:** After geocoding or using existing UserProfile lat/lng

#### Request

```
GET https://maps.googleapis.com/maps/api/place/nearbysearch/json
    ?location={lat},{lng}
    &radius={meters}
    &type={type1}|{type2}|...
    &minprice={0-4}
    &maxprice={0-4}
    &opennow={true|false}
    &key={apiKey}
```

#### Woven Parameters

| Parameter | Value | Rationale |
|---|---|---|
| `location` | `{lat},{lng}` | City center or user location |
| `radius` | `3000` | 3km (~30min walk, 10min drive) |
| `type` | `cafe\|restaurant\|park` | Date-appropriate venue types |
| `minprice` | `1` | Exclude free venues (parks already included) |
| `maxprice` | `3` | Affordable to moderate (0-4 scale) |
| `opennow` | `false` | Don't filter by hours (user may plan for later) |

**Note:** `type` is pipe-delimited (`%7C` URL-encoded)

#### Example Request

```http
GET https://maps.googleapis.com/maps/api/place/nearbysearch/json
    ?location=28.7041,77.1025
    &radius=3000
    &type=cafe%7Crestaurant%7Cpark
    &minprice=1
    &maxprice=3
    &opennow=false
    &key=AIzaSyC...
```

#### Response (Simplified)

```json
{
  "results": [
    {
      "place_id": "ChIJN1t_tDeuEmsRUsoyG83frY4",
      "name": "Blue Tokai Coffee Roasters",
      "vicinity": "Hauzkhas Village, New Delhi",
      "rating": 4.5,
      "price_level": 2,
      "types": ["cafe", "food"]
    },
    {
      "place_id": "ChIJabcdef123456",
      "name": "Lodhi Garden",
      "vicinity": "Lodhi Road, New Delhi",
      "rating": 4.7,
      "price_level": 0,
      "types": ["park", "tourist_attraction"]
    }
  ],
  "status": "OK"
}
```

#### Status Codes

| Status | Meaning | VenueService Action |
|---|---|---|
| `OK` | Success | Parse results |
| `ZERO_RESULTS` | No venues found | Return empty list |
| `INVALID_REQUEST` | Bad parameters | Return empty list |
| `OVER_QUERY_LIMIT` | Quota exceeded | Return empty list + log warning |
| `REQUEST_DENIED` | API key invalid | Return empty list + log error |

---

## Response Parsing

### Extract Venue Data

```csharp
var placesJson = await placesResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
var places = placesJson.GetProperty("results");

var suggestions = new List<VenueSuggestion>();
foreach (var place in places.EnumerateArray())
{
    if (suggestions.Count >= 3) break;  // Limit to 3

    // Filter by rating (≥4.0)
    var rating = place.TryGetProperty("rating", out var ratingEl)
        ? ratingEl.GetDouble() : 0.0;
    if (rating < 4.0) continue;

    // Extract fields
    var name = place.GetProperty("name").GetString() ?? string.Empty;
    var address = place.TryGetProperty("vicinity", out var vic)
        ? vic.GetString() ?? string.Empty : string.Empty;
    var priceLevel = place.TryGetProperty("price_level", out var pl)
        ? pl.GetInt32() : 0;
    var placeId = place.TryGetProperty("place_id", out var pid)
        ? pid.GetString() ?? string.Empty : string.Empty;

    // Generate Google Maps URL
    var mapsUrl = $"https://maps.google.com/?q={Uri.EscapeDataString(placeId)}";

    suggestions.Add(new VenueSuggestion(name, address, rating, priceLevel, mapsUrl));
}
```

### Field Extraction Rules

| Field | Source | Fallback | Notes |
|---|---|---|---|
| `name` | `place.name` | `""` | Required by Google (always present) |
| `address` | `place.vicinity` | `""` | Short address (e.g., "Hauzkhas Village") |
| `rating` | `place.rating` | `0.0` | Decimal (e.g., 4.5) |
| `priceLevel` | `place.price_level` | `0` | 0=free, 1=cheap, 2=moderate, 3=expensive, 4=very expensive |
| `placeId` | `place.place_id` | `""` | Unique Google identifier |

---

## Google Maps URL Generation

### Format

```
https://maps.google.com/?q={placeId}
```

**Why `place_id`?**  
- ✅ Unique identifier (doesn't change even if venue name changes)
- ✅ Opens directly in Google Maps app (mobile) or web
- ✅ No need to pass lat/lng or address (Google resolves it)

### Example

```csharp
var placeId = "ChIJN1t_tDeuEmsRUsoyG83frY4";
var mapsUrl = $"https://maps.google.com/?q={Uri.EscapeDataString(placeId)}";
// → https://maps.google.com/?q=ChIJN1t_tDeuEmsRUsoyG83frY4
```

**User experience:**  
1. User taps venue in Woven app
2. Opens Google Maps (app or web)
3. Shows venue details (photos, reviews, directions)

---

## Error Handling

### HTTP Failures

```csharp
var placesResp = await http.GetAsync(placesUrl, ct);
if (!placesResp.IsSuccessStatusCode) return new List<VenueSuggestion>();
```

**No retries** — Google APIs are highly available, retries unlikely to help

### JSON Parsing Failures

```csharp
try
{
    var placesJson = await placesResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    var results = placesJson.GetProperty("results");
    // ...
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "VenueService failed for user {UserId}", userId);
    return new List<VenueSuggestion>();
}
```

**Graceful degradation** — never throw to client

### Missing API Key

```csharp
var apiKey = _config["Google:PlacesApiKey"];
if (string.IsNullOrEmpty(apiKey)) return new List<VenueSuggestion>();
```

**Silent failure** — venues are optional feature

---

## Cost Optimization

### Google Places Pricing (as of 2026)

| API | Cost per 1,000 requests | VenueService Usage |
|---|---|---|
| **Geocoding** | $5 | Only when UserProfile missing lat/lng |
| **Places Nearby Search** | $32 | Every cache miss (24h TTL) |

**Example monthly cost:**  
- 10,000 active matches → 10,000 venue requests
- 90% cache hit rate → 1,000 API calls
- 1,000 Places calls = **$32/month**
- 100 Geocoding calls (10% missing coords) = **$0.50/month**
- **Total:** ~$33/month at 10k matches

### Cost Reduction Strategies

1. **24h cache** — reduces API calls by ~95% (implemented ✅)
2. **Prefill UserProfile.Lat/Lng** — avoid Geocoding calls (implemented ✅)
3. **Batch prefetch** — generate venues for all Find Love matches nightly (not implemented)
4. **Static venue DB** — curate own venue list (loses real-time data)

---

## Rate Limiting

### Google Quotas

- **Default:** 100,000 requests/day (shared across all APIs)
- **Burst:** ~100 requests/second

**VenueService never hits limits** — 24h cache + low match volume keeps us under 1,000 requests/day.

### No Client-Side Rate Limiting

VenueService does **not** implement rate limiting because:
1. ✅ Cache handles 90%+ of requests
2. ✅ Match volume naturally limited (not public API)
3. ✅ Google APIs return `OVER_QUERY_LIMIT` if exceeded (graceful degradation)

---

## Security

### API Key Protection

- ✅ **Stored in Azure Key Vault** (production)
- ✅ **Never logged** (appsettings.json excludes Google section from logs)
- ✅ **Server-side only** (never exposed to frontend)
- ✅ **IP-restricted** (Azure egress IP allowlist)

### PII Handling

- ✅ **City names** sent to Google (already public data)
- ❌ **User home addresses** NEVER sent (only city-level)
- ✅ **Venue data** returned to frontend (public Google Places data)

### Audit Logging

```csharp
_audit.Log("external_api_call", 
    userId: userId, 
    service: "VenueService",
    resourceType: "GooglePlaces", 
    resourceId: "venue_search", 
    piiStripped: true);
```

**Tracks:** Every Google API call (for compliance/debugging)

---

## Monitoring

### Key Metrics (not implemented yet)

1. **Google API call volume** — track daily request count
2. **Cache hit rate** — `venues:*` keys in Redis
3. **API error rate** — `OVER_QUERY_LIMIT`, `REQUEST_DENIED` responses
4. **Geocoding fallback rate** — how often lat/lng is missing
5. **Empty result rate** — `ZERO_RESULTS` responses

**Future:** Add Serilog metrics + Azure Monitor alerts

---

## Alternatives Considered

### 1. Foursquare Places API
**Pros:** Better venue categorization, user reviews  
**Cons:** Higher cost ($50/1k requests), weaker India coverage  
**Verdict:** Google chosen for India market

### 2. Yelp Fusion API
**Pros:** Rich review data, business hours  
**Cons:** Primarily US-focused, no India coverage  
**Verdict:** Not viable for Woven

### 3. Static Venue Database
**Pros:** No API costs, full control  
**Cons:** Manual curation, stale data, no real-time hours/ratings  
**Verdict:** Not scalable

---

## Testing

### Manual Test (Geocoding)

```powershell
# Replace YOUR_API_KEY
$key = "AIzaSyC..."
$city = "New Delhi, Delhi"

curl "https://maps.googleapis.com/maps/api/geocode/json?address=$([uri]::EscapeDataString($city))&key=$key"
```

**Expected:** `status: "OK"`, `results[0].geometry.location` has lat/lng

### Manual Test (Places)

```powershell
$key = "AIzaSyC..."
$lat = 28.7041
$lng = 77.1025

curl "https://maps.googleapis.com/maps/api/place/nearbysearch/json?location=$lat,$lng&radius=3000&type=cafe|restaurant|park&minprice=1&maxprice=3&key=$key"
```

**Expected:** `status: "OK"`, `results[]` contains venues with `rating >= 4.0`

---

## Related Documentation

- **[Venues System Overview](./README.md)** — high-level design
- **[Recommendation Algorithm](./recommendations.md)** — filtering logic
- **[Implementation Guide](./implementation.md)** — VenueService code
- **[API Reference](./api.md)** — HTTP endpoints

---

## References

- [Google Places API Documentation](https://developers.google.com/maps/documentation/places/web-service/overview)
- [Nearby Search Request](https://developers.google.com/maps/documentation/places/web-service/search-nearby)
- [Geocoding API Documentation](https://developers.google.com/maps/documentation/geocoding/overview)
- [Google Maps Pricing](https://cloud.google.com/maps-platform/pricing)
