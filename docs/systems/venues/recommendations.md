# Venue Recommendation Algorithm

**Last Updated:** 2026-10-07

---

## Overview

VenueService uses a **quality-first, location-aware filtering algorithm** to suggest date venues. This doc covers the ranking logic, filters, and future personalization plans.

---

## Current Algorithm (v1 — Quality Filtering)

### Design Philosophy

**Simple beats smart (for now).**  
- ✅ High-quality venues (rating ≥ 4.0)
- ✅ Affordable pricing (level 1-3)
- ✅ Convenient location (3km radius)
- ❌ No personalization yet (everyone sees same venues in a city)

**Why simple?**  
1. **Cold start** — no user venue preferences collected yet
2. **Data quality** — Google Places ratings are highly reliable
3. **Friction reduction** — any venue is better than "where should we go?" debate

---

## Filtering Pipeline

### Stage 1: Location Resolution

```
INPUT: userId, partnerUserId

1. Fetch UserProfiles (userId, partnerUserId)
   → city, state, lat, lng

2. IF UserA.City == UserB.City:
      location = UserA.City center
   ELSE:
      location = UserA.City  // viewer's city

3. IF lat/lng is null:
      Google Geocoding API: city → (lat, lng)

OUTPUT: (lat, lng) for venue search
```

**Location priority:**
1. ✅ **Shared city** (ideal — both users see same venues)
2. ✅ **Viewer's city** (fallback — asymmetric suggestions)

---

### Stage 2: Google Places Query

```
INPUT: (lat, lng)

Google Places Nearby Search:
  - location: lat,lng
  - radius: 3000 meters (3km)
  - type: cafe|restaurant|park
  - minprice: 1
  - maxprice: 3
  - opennow: false

OUTPUT: Raw venue list (up to 20 results)
```

**Type rationale:**

| Type | Date Appropriateness | Coverage |
|---|---|---|
| `cafe` | ✅ Casual, conversation-friendly | High in cities |
| `restaurant` | ✅ Classic dinner date | Universal |
| `park` | ✅ Low-pressure, active | Common in India |

**Excluded types:**
- ❌ `bar` / `night_club` — not universally appropriate (alcohol, late hours)
- ❌ `movie_theater` — discourages conversation
- ❌ `shopping_mall` — too broad, not date-specific

**Price filter:**

| Level | Meaning | VenueService Filter |
|---|---|---|
| 0 | Free | ❌ Excluded (parks already covered by `type=park`) |
| 1 | Cheap | ✅ Included |
| 2 | Moderate | ✅ Included |
| 3 | Expensive | ✅ Included |
| 4 | Very Expensive | ❌ Excluded (not affordable for most users) |

**Radius:** 3km (~30min walk, 10min drive/auto)  
**Why 3km?** Balance between convenience and variety. Indian cities have dense venue clusters.

---

### Stage 3: Quality Filtering

```
INPUT: Raw venue list

FOR EACH venue:
  IF rating < 4.0:
      SKIP  // Low quality

  IF suggestions.Count >= 3:
      BREAK  // Limit to top 3

  ADD to suggestions

OUTPUT: List<VenueSuggestion> (max 3)
```

**Rating threshold:** ≥4.0 (top ~25% of venues)  
**Why 4.0?** Balance between quality and availability. Most cities have 10+ venues ≥4.0 within 3km.

**Limit to 3:**  
- ✅ Decision fatigue reduction (fewer choices = faster decision)
- ✅ UI simplicity (fits in chat overlay)
- ✅ Implicit diversity (Google returns varied types)

---

### Stage 4: Result Assembly

```
INPUT: Filtered venue list

FOR EACH venue:
  VenueSuggestion(
      Name: venue.name,
      Address: venue.vicinity,
      Rating: venue.rating,
      PriceLevel: venue.price_level ?? 0,
      GoogleMapsUrl: "https://maps.google.com/?q={place_id}"
  )

OUTPUT: List<VenueSuggestion>
```

**Google Maps URL:**  
- Links directly to venue in Google Maps
- Opens in app (mobile) or web (desktop)
- Users can see photos, reviews, directions

---

## Edge Cases

### Empty Results

```
IF suggestions.Count == 0:
    RETURN []
```

**Possible causes:**
1. Small city with few venues ≥4.0
2. Strict price filter (1-3) excludes all venues
3. Google API returned `ZERO_RESULTS`
4. All venues in radius <4.0 rating

**Handling:**  
- Frontend shows "No venues found" message
- Suggests manual venue selection or date idea from MatchExplanation

### Different Cities

```
IF UserA.City != UserB.City:
    → Each user sees different suggestions
    → Must coordinate offline ("I'll come to your city")
```

**Future:** Detect this case, suggest midpoint city or popular date destinations.

### Missing City

```
IF city is null OR empty:
    RETURN []
```

**Rare** — onboarding requires city selection. Only legacy users or incomplete profiles.

---

## Ranking Logic (Current)

### Order of Results

Google Places returns venues in **descending relevance** (Google's internal ranking):
- Prominence (popular venues rank higher)
- Distance from center (closer ranks higher)
- Rating (higher ranks higher)

VenueService **preserves Google's order** — no custom re-ranking yet.

**Filters applied:**
1. ❌ Remove rating <4.0
2. ✅ Take top 3 remaining

**Example:**

| Google Rank | Name | Rating | VenueService Rank |
|---|---|---|---|
| 1 | Popular Café | 4.6 | ✅ 1 |
| 2 | Trendy Restaurant | 3.8 | ❌ Filtered |
| 3 | Cozy Coffee Shop | 4.2 | ✅ 2 |
| 4 | Local Park | 4.5 | ✅ 3 |
| 5 | Hidden Gem | 4.7 | ❌ Limit reached |

---

## Future Algorithm (v2 — Personalization)

### Planned Enhancements

#### 1. ECHO Signal Integration

**Use behavioral data to rank venues:**

```sql
-- Example: Users who liked "hiking" hobbies → rank parks higher
SELECT venue_type_preference
FROM user_interactions
WHERE user_id = {userId}
  AND interaction_type IN ('orbit_tile', 'chat_depth')
```

**Signals to leverage:**
- `OrbitGravity` on outdoor tiles → prioritize parks
- `ChatDepthMessages` on food topics → prioritize restaurants
- `DateIdeaAccepted` history → learn preferred venue types

#### 2. Shared Interest Mapping

**Link MatchExplanation.DateIdeasJson to venue types:**

| Date Idea Category | Venue Type Boost |
|---|---|
| "Hike to viewpoint" | `park` +10 |
| "Coffee shop conversation" | `cafe` +10 |
| "Try new cuisine" | `restaurant` +10 |

**Flow:**
1. Check MatchExplanation for this pair
2. Parse DateIdeasJson
3. Classify ideas into categories
4. Boost venue types that align

#### 3. Availability Filtering

**Enable `opennow=true` in Google Places query:**

```diff
- &opennow=false
+ &opennow=true
```

**Benefit:** Only show venues open at request time  
**Risk:** May exclude popular venues with limited hours (e.g., brunch-only spots)

**Decision:** Keep `false` for now, let users plan for later.

#### 4. Dietary Restrictions

**Check UserProfile for dietary tags:**

```csharp
IF user.dietary_restrictions.Contains("vegetarian"):
    → Filter restaurants by cuisine type
    → Boost cafes (safer choice)
```

**Blocker:** No dietary data collected yet (not in onboarding).

#### 5. Accessibility Needs

**Filter venues by wheelchair access:**

```
Google Places → wheelchair_accessible_entrance: true
```

**Blocker:** Google data quality varies (many venues missing this field).

---

## Diversity Rules

### Implicit Diversity (Current)

Google Places returns **varied venue types** naturally:
- Café, restaurant, park all included in `type` filter
- Price levels 1-3 allow budget diversity
- Rating filter (≥4.0) doesn't bias toward expensive venues

**No explicit diversity enforcement yet** — if all top 3 are cafés, we return 3 cafés.

### Explicit Diversity (Planned)

```
GOAL: 1 active/outdoor + 1 social/cultural + 1 casual/low-key

FOR EACH venue:
  Classify type:
    park → "active"
    cafe → "casual"
    restaurant → "social"

  IF category not filled:
      ADD to suggestions
  ELSE:
      SKIP (category quota met)
```

**Benefit:** Guarantees varied date options  
**Risk:** May exclude high-quality venues to hit category quota

---

## Testing Algorithm Changes

### A/B Test Setup (Future)

1. **Control:** Current quality filtering (v1)
2. **Variant:** ECHO-personalized ranking (v2)

**Metrics:**
- Click-through rate (venues viewed → clicked)
- Date coordination rate (both users agree on venue)
- Post-date feedback (venue satisfaction)

**Hypothesis:** Personalized venues increase date completion by 20%.

---

## Known Limitations

1. **No personalization** — everyone in a city sees same venues
2. **No diversity enforcement** — may return 3 cafés, 0 parks
3. **No time-of-day filtering** — shows dinner spots at 10am
4. **No budget awareness** — doesn't consider user income/spending signals
5. **No accessibility filtering** — may suggest inaccessible venues
6. **No dietary filtering** — may suggest steakhouse to vegetarian user

---

## Alternatives Considered

### 1. Machine Learning Ranking

**Approach:** Train ML model on user preferences → rank venues

**Pros:**  
- ✅ Personalized results
- ✅ Learns from feedback

**Cons:**  
- ❌ Cold start (no training data yet)
- ❌ Complexity (overkill for v1)
- ❌ Explainability (users can't see why venue recommended)

**Verdict:** Defer to v3

### 2. Manual Venue Curation

**Approach:** Woven team curates "best date spots" per city

**Pros:**  
- ✅ Guaranteed quality
- ✅ Editorial control

**Cons:**  
- ❌ Does not scale (100s of cities)
- ❌ Stale data (venues close, hours change)
- ❌ Bias toward team's preferences

**Verdict:** Not viable at scale

### 3. User-Generated Venue Lists

**Approach:** Let users submit/vote on date venues

**Pros:**  
- ✅ Crowdsourced quality
- ✅ Community engagement

**Cons:**  
- ❌ Spam/low-quality submissions
- ❌ Requires moderation
- ❌ Cold start (no submissions yet)

**Verdict:** Consider for v4 (community feature)

---

## Performance Considerations

### Query Cost

**Geocoding:** ~200ms (only when lat/lng missing)  
**Places API:** ~300ms (cached for 24h)  
**Filtering:** <1ms (in-memory)

**Total latency:** ~500ms (cache miss), <10ms (cache hit)

### Cache Effectiveness

**Cache key:** `venues:{userId}:{partnerUserId}`  
**TTL:** 24 hours

**Expected hit rate:**  
- ✅ 90%+ for active matches (users request venues multiple times)
- ✅ 0% for first-time requests (cold cache)

**Optimization:**  
- **Prefetch** — generate venues for all Find Love matches nightly
- **Shared cache** — `venues:123:456` == `venues:456:123` (symmetric lookup)

---

## Related Documentation

- **[Venues System Overview](./README.md)** — high-level design
- **[Google Places Integration](./google-places.md)** — API details
- **[Implementation Guide](./implementation.md)** — VenueService code
- **[API Reference](./api.md)** — HTTP endpoints

---

## Changelog

**2026-06-04** — v1.0 Quality Filtering  
- Rating ≥4.0, price 1-3, 3km radius
- No personalization, no diversity enforcement

**Future** — v2.0 ECHO Personalization  
- Integrate user behavioral signals
- Shared interest mapping
- Availability + dietary filtering
