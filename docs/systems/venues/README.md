# Venues System

**Status:** ✅ IMPLEMENTED  
**Phase:** 4D (Pre-Date Bridge)  
**Last Updated:** 2026-10-07

---

## Overview

The Venues system provides AI-curated date venue recommendations to matched pairs after they reach **Find Love** unlock (mutual interest confirmed). Uses Google Places API to suggest real, high-quality venues near both users.

### Purpose

- **Reduce date planning friction** — couples don't need to debate "where should we go?"
- **Quality curation** — only venues with 4.0+ ratings, reasonable pricing
- **Location-aware** — prioritizes venues in shared city, falls back to viewer's city
- **Privacy-safe** — no exact addresses exposed until users click through to Google Maps

---

## Core Concepts

### When Venues Appear

Venue suggestions are available when:
1. ✅ Match exists (UserA + UserB)
2. ✅ Mutual interest confirmed (`BothMessagedAt` non-null)
3. ✅ Trial period completed successfully (if trial match)
4. ✅ Both users are chatting (not blocked/unmatched)

**UI trigger:** User taps "Date Ideas" in chat header → `GET /chats/{threadId}/venue-suggestions`

### What Gets Suggested

Each response returns **up to 3 venues**:
- **One active/outdoor** (e.g., park, hiking trail)
- **One social/cultural** (e.g., café, art gallery)
- **One casual/low-key** (e.g., coffee shop, casual restaurant)

Venues are filtered by:
- ⭐ **Rating ≥ 4.0** (high quality only)
- 💰 **Price level 1-3** (affordable to moderate)
- 📍 **Within 3km radius** of city center
- 🏷️ **Types:** café, restaurant, park

### Data Flow

```
User taps "Date Ideas"
    ↓
Frontend: GET /chats/{threadId}/venue-suggestions
    ↓
Backend: Check cache (24h TTL)
    ↓ (cache miss)
Fetch user profiles (city, lat/lng)
    ↓
Geocode city if lat/lng missing
    ↓
Google Places Nearby Search
    ↓
Filter by rating + price + limit to 3
    ↓
Cache results (24h)
    ↓
Log security audit event
    ↓
Return VenueSuggestion[]
```

---

## Key Files

| File | Purpose |
|---|---|
| [`Services/Venues/IVenueService.cs`](../../../backend/WovenBackend/Services/Venues/IVenueService.cs) | Service interface |
| [`Services/Venues/VenueService.cs`](../../../backend/WovenBackend/Services/Venues/VenueService.cs) | Implementation (Google Places integration) |
| [`Endpoints/ChatEndpoints.cs`](../../../backend/WovenBackend/Endpoints/ChatEndpoints.cs) | HTTP endpoint (line 1147) |
| [`Program.cs`](../../../backend/WovenBackend/Program.cs) | Service registration (line 666) |

---

## Response Schema

### `VenueSuggestion` Record

```csharp
public record VenueSuggestion(
    string Name,           // "Blue Tokai Coffee Roasters"
    string Address,        // "Hauzkhas Village, New Delhi"
    double Rating,         // 4.5
    int PriceLevel,        // 2 (0=free, 1=cheap, 2=moderate, 3=expensive, 4=very expensive)
    string GoogleMapsUrl   // "https://maps.google.com/?q=ChIJ..."
);
```

### Example Response

```json
{
  "venues": [
    {
      "name": "Blue Tokai Coffee Roasters",
      "address": "Hauzkhas Village, New Delhi",
      "rating": 4.5,
      "priceLevel": 2,
      "googleMapsUrl": "https://maps.google.com/?q=ChIJabcdef123456"
    },
    {
      "name": "Lodhi Garden",
      "address": "Lodhi Road, New Delhi",
      "rating": 4.7,
      "priceLevel": 0,
      "googleMapsUrl": "https://maps.google.com/?q=ChIJghijkl789012"
    }
  ]
}
```

---

## Location Logic

### Shared City (Ideal)

```
IF UserA.City == UserB.City AND both non-null:
    → Use shared city coordinates
    → Both users see venues equidistant from shared center
```

### Different Cities (Fallback)

```
IF cities differ OR partner city is null:
    → Use viewer's city (UserA sees venues near UserA.City)
    → Each user sees different suggestions near their own location
```

### Missing Coordinates

```
IF UserProfile.Lat/Lng is null:
    → Call Google Geocoding API to resolve "City, State" → (lat, lng)
    → Cache coordinates for future requests (implicit via UserProfile)
```

---

## Caching Strategy

### Cache Key Format
```
venues:{userId}:{partnerUserId}
```

### TTL
- **24 hours** — venues don't change frequently, safe to cache
- Cache shared by both users in the pair (symmetric lookup)

### Cache Miss Flow
1. Check Redis: `venues:123:456`
2. Miss → Google Places API call
3. Store result in Redis (24h TTL)
4. Return to client

---

## Security & Privacy

### PII Handling
- ✅ **City names** exposed (already in UserProfile)
- ✅ **Venue names + addresses** exposed (public Google Places data)
- ❌ **User home addresses** NEVER exposed (only city-level geocoding)

### Audit Logging
Every venue lookup logs:
```csharp
_audit.Log("external_api_call", 
    userId: userId, 
    service: "VenueService",
    resourceType: "GooglePlaces", 
    resourceId: "venue_search", 
    piiStripped: true);
```

### Rate Limiting
- Google Places API has daily quota limits (billed per request)
- 24h cache reduces API calls by ~95% for active pairs

---

## Error Handling

### Graceful Degradation

| Error | Behavior |
|---|---|
| Missing `Google:PlacesApiKey` | Return empty list `[]` |
| User has no city | Return empty list `[]` |
| Geocoding API fails | Return empty list `[]` |
| Places API HTTP error | Return empty list `[]` |
| No venues match filters | Return empty list `[]` (possible in small cities) |
| Service exception | Log warning + return empty list `[]` |

**Never throws exceptions to client** — always returns `List<VenueSuggestion>` (empty on failure).

---

## Analytics

### Tracked Events

| Event | When | Metadata |
|---|---|---|
| `venue_suggestions_viewed` | User requests venues | `{ threadId, venueCount }` |

**Future events (not implemented):**
- `venue_clicked` — user taps a venue to open Google Maps
- `venue_date_agreed` — both users agree on a venue
- `venue_date_completed` — post-date feedback references a suggested venue

---

## Integration Points

### Upstream Dependencies
- **UserProfile** — city, state, lat/lng coordinates
- **Match** — validates mutual interest, checks blocked/unmatched state
- **ChatThread** — endpoint scoped to thread (not match)

### Downstream Consumers
- **ChatEndpoints** — exposes HTTP API
- **AnalyticsService** — tracks venue views
- **SecurityAuditService** — logs external API calls

---

## Related Systems

- **[Google Places API Integration](./google-places.md)** — API client details
- **[Recommendation Algorithm](./recommendations.md)** — filtering + ranking logic
- **[Implementation Guide](./implementation.md)** — VenueService internals
- **[API Reference](./api.md)** — endpoint specs

---

## Future Enhancements

### Planned (not implemented)
1. **Personalized ranking** — use ECHO signals to rank venues by user preferences
2. **Date idea → venue mapping** — link MatchExplanation.DateIdeasJson to venue types
3. **Availability detection** — filter by `opennow=true` (currently disabled)
4. **User feedback loop** — learn from accepted/rejected venues
5. **Frontend UI** — currently backend-only (no Angular component)

### Open Questions
- Should venues persist to DB for date coordination tracking?
- Should we show different venues to UserA vs UserB (asymmetric recommendations)?
- How to handle international/multi-city matches?

---

## Known Limitations

1. **No frontend UI** — endpoint exists, no Angular component consumes it yet
2. **Simple filtering** — doesn't consider user hobbies, dietary restrictions, accessibility needs
3. **India-centric** — Google Places works globally but quality varies by region
4. **No cost optimization** — every cache miss hits paid API (could batch/prefetch)
5. **No offline fallback** — requires network + Google API availability

---

## Decision Log

**2026-06-04** — Created VenueService as part of Phase 4D (Pre-Date Bridge)  
**Why:** Reduce date planning friction, increase offline conversion rate  
**Alternatives considered:** Manual venue entry, third-party date planning apps  
**Chosen:** Google Places — widely trusted, comprehensive data, India coverage

**2026-06-04** — 24h cache TTL  
**Why:** Venues don't change frequently, reduce API costs  
**Alternatives:** 1h (too aggressive), 7d (too stale)  

**2026-06-04** — Return empty list on errors (no HTTP 500)  
**Why:** Venues are supplementary feature, app should work without them  
**Alternatives:** Return HTTP 503 (breaks chat UX)  

---

## Testing

### Manual Test Flow

1. Create a match between UserA (Mumbai) + UserB (Mumbai)
2. Exchange messages → trigger `BothMessagedAt`
3. Complete trial → confirm mutual interest
4. `GET /chats/{threadId}/venue-suggestions`
5. Verify:
   - ✅ Returns 1-3 venues
   - ✅ All ratings ≥ 4.0
   - ✅ All price levels 1-3
   - ✅ Venues near Mumbai
   - ✅ Google Maps URLs work
   - ✅ Second request hits cache (check logs)

### Test Data Needs

- Users in major Indian cities (Delhi, Mumbai, Bangalore) — good Google Places coverage
- Users in small towns (e.g., Dharamshala) — test empty result handling
- Users with missing lat/lng — test geocoding fallback
- Users in different cities — test fallback to viewer's city

---

## Ownership

**Implemented by:** gouthamganta (ECHO assistance)  
**Maintained by:** Backend team  
**Prod status:** DEPLOYED (wooven.me)  
**Monitoring:** Security audit logs (`external_api_call` events)
