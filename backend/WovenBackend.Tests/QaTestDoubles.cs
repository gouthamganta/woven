using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Services;

namespace WovenBackend.Tests;

// This adapter verifies domain behavior only. PostgreSQL constraints,
// pgvector queries and serializable transactions require separate integration tests.
internal sealed class QaMemoryContext(DbContextOptions<WovenDbContext> options) : WovenDbContext(options)
{
    public static QaMemoryContext Create() => new(new DbContextOptionsBuilder<WovenDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Ignore<Pgvector.Vector>();
        foreach (var entity in builder.Model.GetEntityTypes().ToList())
            foreach (var property in entity.ClrType.GetProperties().Where(p => p.PropertyType == typeof(Pgvector.Vector)))
                builder.Entity(entity.ClrType).Ignore(property.Name);
    }
}

internal sealed class QaCache : ICacheService
{
    public Dictionary<string, object> Values { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<string> Incremented { get; } = [];
    public long MissingCounter { get; set; } = -1;
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(Values.TryGetValue(key, out var value) ? (T?)value : default);
    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) { Values[key] = value!; return Task.CompletedTask; }
    public Task DeleteAsync(string key, CancellationToken ct = default) { Deleted.Add(key); Values.Remove(key); return Task.CompletedTask; }
    public Task<long> GetCounterAsync(string key, CancellationToken ct = default) => Task.FromResult(Values.TryGetValue(key, out var value) ? Convert.ToInt64(value) : MissingCounter);
    public Task<long> IncrementAsync(string key, TimeSpan? expireIn = null, CancellationToken ct = default)
    {
        Incremented.Add(key);
        var value = Values.TryGetValue(key, out var old) ? Convert.ToInt64(old) + 1 : 1;
        Values[key] = value;
        return Task.FromResult(value);
    }
    public Task<bool> CheckRateLimitAsync(string key, int limit, TimeSpan ttl, CancellationToken ct = default) => throw new NotSupportedException("This test double does not model distributed rate limiting.");
    public Task<bool> AcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken ct = default) => throw new NotSupportedException("This test double does not model distributed locks.");
    public Task ReleaseLockAsync(string lockKey, CancellationToken ct = default) => throw new NotSupportedException("This test double does not model distributed locks.");
}
