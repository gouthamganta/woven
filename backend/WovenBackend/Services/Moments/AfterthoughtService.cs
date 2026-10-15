using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;

namespace WovenBackend.Services.Moments;

public interface IAfterthoughtService
{
    Task RecordAsync(
        Guid matchId,
        int fromUserId,
        int aboutUserId,
        string closePath,
        short? rating,
        string? reasonTag,
        string? noteText,
        bool isSafety,
        CancellationToken ct = default);
}

public class AfterthoughtService : IAfterthoughtService
{
    private readonly WovenDbContext _db;

    public AfterthoughtService(WovenDbContext db)
    {
        _db = db;
    }

    public async Task RecordAsync(
        Guid matchId,
        int fromUserId,
        int aboutUserId,
        string closePath,
        short? rating,
        string? reasonTag,
        string? noteText,
        bool isSafety,
        CancellationToken ct = default)
    {
        // Validate rating range (1-5 or null)
        if (rating.HasValue && (rating.Value < 1 || rating.Value > 5))
            throw new ArgumentException("Rating must be between 1-5", nameof(rating));

        // Validate close path
        var validPaths = new[] { "trial_end", "unmatch", "expiry", "block" };
        if (!validPaths.Contains(closePath))
            throw new ArgumentException($"Invalid close path: {closePath}", nameof(closePath));

        // Expiry and block paths should not have star ratings
        if (closePath == "expiry" && rating.HasValue)
            throw new ArgumentException("Expiry path should not have a star rating", nameof(closePath));

        if (closePath == "block" && rating.HasValue)
            throw new ArgumentException("Block path should not have a star rating", nameof(closePath));

        // Block path must be safety
        if (closePath == "block" && !isSafety)
            throw new ArgumentException("Block path must set isSafety=true", nameof(isSafety));

        var afterthought = new MatchAfterthought
        {
            MatchId = matchId,
            FromUserId = fromUserId,
            AboutUserId = aboutUserId,
            ClosePath = closePath,
            Rating = rating,
            ReasonTag = reasonTag?.Trim(),
            NoteText = noteText?.Trim(),
            IsSafety = isSafety,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.MatchAfterthoughts.Add(afterthought);
        await _db.SaveChangesAsync(ct);
    }
}
