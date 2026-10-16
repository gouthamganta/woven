using Microsoft.EntityFrameworkCore;
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
    private readonly IAfterthoughtIntegrationService _integration;
    private readonly ILogger<AfterthoughtService> _logger;

    public AfterthoughtService(
        WovenDbContext db,
        IAfterthoughtIntegrationService integration,
        ILogger<AfterthoughtService> logger)
    {
        _db = db;
        _integration = integration;
        _logger = logger;
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

        // P2: Validate text lengths (EF maxLength is not API validation)
        var trimmedReasonTag = reasonTag?.Trim();
        var trimmedNoteText = noteText?.Trim();

        if (!string.IsNullOrEmpty(trimmedReasonTag) && trimmedReasonTag.Length > 50)
            throw new ArgumentException("Reason tag cannot exceed 50 characters", nameof(reasonTag));

        if (!string.IsNullOrEmpty(trimmedNoteText) && trimmedNoteText.Length > 300)
            throw new ArgumentException("Note text cannot exceed 300 characters", nameof(noteText));

        // P4: Check for existing afterthought (idempotency - unique constraint will also enforce this)
        var exists = await _db.MatchAfterthoughts
            .AnyAsync(a => a.MatchId == matchId && a.FromUserId == fromUserId, ct);

        if (exists)
            throw new InvalidOperationException("Afterthought already submitted for this match");

        var afterthought = new MatchAfterthought
        {
            MatchId = matchId,
            FromUserId = fromUserId,
            AboutUserId = aboutUserId,
            ClosePath = closePath,
            Rating = rating,
            ReasonTag = string.IsNullOrEmpty(trimmedReasonTag) ? null : trimmedReasonTag,
            NoteText = string.IsNullOrEmpty(trimmedNoteText) ? null : trimmedNoteText,
            IsSafety = isSafety,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.MatchAfterthoughts.Add(afterthought);
        await _db.SaveChangesAsync(ct);

        // Immediately integrate into ECHO (matching feedback) or TrustService (safety flags)
        try
        {
            await _integration.ProcessAfterthoughtAsync(afterthought.Id, ct);
        }
        catch (Exception ex)
        {
            // Non-critical: Afterthought is saved, integration can be retried
            _logger.LogError(ex, "[Afterthought] Integration failed for {Id} — feedback saved but not processed", afterthought.Id);
        }
    }
}
