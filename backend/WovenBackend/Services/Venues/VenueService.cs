using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services.Security;

namespace WovenBackend.Services.Venues;

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

    public async Task<List<VenueSuggestion>> GetVenueSuggestionsAsync(
        int userId, int partnerUserId, CancellationToken ct = default)
    {
        try
        {
            var cacheKey = $"venues:{userId}:{partnerUserId}";
            var cached = await _cache.GetAsync<List<VenueSuggestion>>(cacheKey, ct);
            if (cached != null) return cached;

            var profiles = await _db.UserProfiles.AsNoTracking()
                .Where(p => p.UserId == userId || p.UserId == partnerUserId)
                .Select(p => new { p.UserId, p.City, p.State, p.Lat, p.Lng })
                .ToListAsync(ct);

            var myProfile = profiles.FirstOrDefault(p => p.UserId == userId);
            var partnerProfile = profiles.FirstOrDefault(p => p.UserId == partnerUserId);

            string city;
            string? state;
            double? lat;
            double? lng;

            if (myProfile?.City == partnerProfile?.City && myProfile?.City != null)
            {
                city = myProfile.City;
                state = myProfile.State;
                lat = myProfile.Lat;
                lng = myProfile.Lng;
            }
            else
            {
                city = myProfile?.City ?? string.Empty;
                state = myProfile?.State;
                lat = myProfile?.Lat;
                lng = myProfile?.Lng;
            }

            if (string.IsNullOrEmpty(city)) return new List<VenueSuggestion>();

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

            if (suggestions.Count > 0)
                await _cache.SetAsync(cacheKey, suggestions, TimeSpan.FromHours(24), ct);

            _audit.Log("external_api_call", userId: userId, service: "VenueService",
                resourceType: "GooglePlaces", resourceId: "venue_search", piiStripped: true);

            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VenueService failed for user {UserId}", userId);
            return new List<VenueSuggestion>();
        }
    }
}
