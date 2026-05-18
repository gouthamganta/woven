using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using WovenBackend.Services.Security;

namespace WovenBackend.Services;

public class CacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IEncryptionService _enc;
    private readonly ILogger<CacheService> _logger;

    // Derived once at construction — never re-derive per call.
    private readonly byte[] _cacheKey;

    public CacheService(
        IConnectionMultiplexer redis,
        IEncryptionService enc,
        ILogger<CacheService> logger)
    {
        _redis = redis;
        _enc = enc;
        _logger = logger;
        _cacheKey = Convert.FromBase64String(_enc.DeriveKey("cache-encryption-v1"));
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var raw = await db.StringGetAsync(key);
            if (!raw.HasValue) return default;

            string json;
            if (IsSensitiveKey(key))
            {
                var decrypted = _enc.DecryptBytes(Convert.FromBase64String((string)raw!));
                json = Encoding.UTF8.GetString(decrypted);
            }
            else
            {
                json = (string)raw!;
            }

            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] GetAsync miss (Redis unavailable) for key {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value);

            string stored;
            if (IsSensitiveKey(key))
            {
                var encrypted = _enc.EncryptBytes(Encoding.UTF8.GetBytes(json));
                stored = Convert.ToBase64String(encrypted);
            }
            else
            {
                stored = json;
            }

            await db.StringSetAsync(key, stored, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] SetAsync failed (Redis unavailable) for key {Key}", key);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] DeleteAsync failed (Redis unavailable) for key {Key}", key);
        }
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expireIn = null, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var count = await db.StringIncrementAsync(key);
            if (count == 1 && expireIn.HasValue)
                await db.KeyExpireAsync(key, expireIn.Value);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] IncrementAsync failed (Redis unavailable) for key {Key}", key);
            return -1;
        }
    }

    public async Task<long> GetCounterAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);
            if (!value.HasValue) return 0;
            return (long)value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] GetCounterAsync failed (Redis unavailable) for key {Key}", key);
            return -1;
        }
    }

    public async Task<bool> CheckRateLimitAsync(string key, int limit, TimeSpan ttl, CancellationToken ct = default)
    {
        var current = await IncrementAsync(key, ttl, ct);
        if (current == -1) return true; // allow on Redis failure — never block users due to cache outage
        return current <= limit;
    }

    private static bool IsSensitiveKey(string key)
        => key.StartsWith("session:", StringComparison.Ordinal)
        || key.StartsWith("embedding:", StringComparison.Ordinal);
}
