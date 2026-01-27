namespace WovenBackend.Services.Moments;

public static class MomentsRules
{
    public const int DailyTotalCap = 5;
    public const int DailyPendingCap = 2;
    public static readonly TimeSpan BalloonLifetime = TimeSpan.FromHours(36);

    public static DateOnly UtcToday() => DateOnly.FromDateTime(DateTime.UtcNow);

    public static DateTimeOffset NowUtc() => DateTimeOffset.UtcNow;

    public static DateTimeOffset ComputeExpiresAt(DateTimeOffset createdAtUtc)
        => createdAtUtc.Add(BalloonLifetime);

    // Always store pairs in normalized order to avoid (A,B) and (B,A)
    public static (int A, int B) NormalizePair(int user1, int user2)
        => user1 < user2 ? (user1, user2) : (user2, user1);
}
