namespace WovenBackend.Services;

public static class CacheKeys
{
    public static string DailyDeck(int userId, DateOnly date)      => $"deck:{userId}:{date:yyyy-MM-dd}";
    public static string UserSession(int userId)                   => $"session:{userId}";
    public static string PillarEmbedding(int userId)               => $"embedding:{userId}";
    public static string SparkCounter(int userId, DateOnly date)   => $"counter:spark:{userId}:{date:yyyy-MM-dd}";
    public static string PendingCounter(int userId, DateOnly date) => $"counter:pending:{userId}:{date:yyyy-MM-dd}";
    public static string GamesCounter(int userId, DateOnly date)   => $"counter:games:{userId}:{date:yyyy-MM-dd}";
    public static string CommonsFeed(int userId, DateOnly date)    => $"commons:{userId}:{date:yyyy-MM-dd}";
    public static string HotTile(int userId)                       => $"hot-tile:{userId}";
}

public static class CacheTtl
{
    public static TimeSpan UntilMidnightUtc()
    {
        var now     = DateTime.UtcNow;
        var midnight = now.Date.AddDays(1);
        var ttl     = midnight - now;
        // Never return zero or negative — minimum 1 second so Redis doesn't reject
        return ttl > TimeSpan.Zero ? ttl : TimeSpan.FromSeconds(1);
    }

    public static readonly TimeSpan Session        = TimeSpan.FromHours(1);
    public static readonly TimeSpan HotTile        = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CommonsFeed    = TimeSpan.FromHours(1);
    public static readonly TimeSpan EmbeddingLookup = TimeSpan.FromDays(1);
}
