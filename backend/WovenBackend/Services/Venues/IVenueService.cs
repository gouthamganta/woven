namespace WovenBackend.Services.Venues;

public record VenueSuggestion(
    string Name,
    string Address,
    double Rating,
    int PriceLevel,
    string GoogleMapsUrl);

public interface IVenueService
{
    Task<List<VenueSuggestion>> GetVenueSuggestionsAsync(int userId, int partnerUserId, CancellationToken ct = default);
}
