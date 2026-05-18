namespace WovenBackend.Services.Analytics;

// No-op implementation used until Phase 5C replaces it with AnalyticsService.
public class NullAnalyticsService : IAnalyticsService
{
    public Task TrackAsync(int? userId, string? sessionId, string eventType, object? properties = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<string> GetOrAssignVariantAsync(int userId, string experimentId, CancellationToken ct = default)
        => Task.FromResult("control");

    public Task TrackAbConversionAsync(int userId, string experimentId, string conversionType, CancellationToken ct = default)
        => Task.CompletedTask;
}
