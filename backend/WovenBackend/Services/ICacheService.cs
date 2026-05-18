namespace WovenBackend.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, TimeSpan? expireIn = null, CancellationToken ct = default);
    Task<long> GetCounterAsync(string key, CancellationToken ct = default);
    Task<bool> CheckRateLimitAsync(string key, int limit, TimeSpan ttl, CancellationToken ct = default);
}
