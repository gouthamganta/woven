namespace WovenBackend.Services.Analytics;

public interface IAnalyticsService
{
    Task TrackAsync(int? userId, string? sessionId, string eventType, object? properties = null, CancellationToken ct = default);
    Task<string> GetOrAssignVariantAsync(int userId, string experimentId, CancellationToken ct = default);
    Task TrackAbConversionAsync(int userId, string experimentId, string conversionType, CancellationToken ct = default);
}
