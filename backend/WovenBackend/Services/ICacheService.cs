namespace WovenBackend.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, TimeSpan? expireIn = null, CancellationToken ct = default);
    Task<long> GetCounterAsync(string key, CancellationToken ct = default);
    Task<bool> CheckRateLimitAsync(string key, int limit, TimeSpan ttl, CancellationToken ct = default);

    // Distributed lock: SETNX-based, auto-expires on pod crash.
    // Returns true  → lock acquired, caller should proceed.
    // Returns false → another instance holds the lock, caller should skip.
    // On Redis failure → returns true (fail open: don't block workers on cache outage).
    Task<bool> AcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken ct = default);
    Task ReleaseLockAsync(string lockKey, CancellationToken ct = default);
}
