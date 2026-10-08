using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.data.Entities.Moments;
using WovenBackend.Services.Analytics;
using MatchType = WovenBackend.data.Entities.Moments.MatchType;

namespace WovenBackend.Services.Moments;

public class MomentsMatchService
{
    private readonly WovenDbContext _db;
    private readonly WovenBackend.Services.INotificationService _notifications;
    private readonly IAnalyticsService _analytics;

    public MomentsMatchService(WovenDbContext db, WovenBackend.Services.INotificationService notifications, IAnalyticsService analytics)
    {
        _db = db;
        _notifications = notifications;
        _analytics = analytics;
    }

    public sealed record CreateMatchResult(
        bool Created,
        Match? Match,
        string? Reason
    );

    public async Task<CreateMatchResult> CreateActiveMatchAsync(
        int user1Id,
        int user2Id,
        WovenBackend.data.Entities.Moments.MatchType matchType,
        int? edgeOwnerId,
        CancellationToken ct = default)
    {
        if (user1Id == user2Id)
            return new CreateMatchResult(false, null, "CANNOT_MATCH_SELF");

        // Normalize pair so uniqueness index works reliably.
        var (a, b) = MomentsRules.NormalizePair(user1Id, user2Id);

        // Validate EDGE/PURE input consistency
        if (matchType == MatchType.PURE && edgeOwnerId != null)
            return new CreateMatchResult(false, null, "PURE_CANNOT_HAVE_EDGE_OWNER");

        if (matchType == MatchType.EDGE && edgeOwnerId == null)
            return new CreateMatchResult(false, null, "EDGE_REQUIRES_EDGE_OWNER");

        if (edgeOwnerId != null && edgeOwnerId != a && edgeOwnerId != b)
            return new CreateMatchResult(false, null, "EDGE_OWNER_NOT_IN_PAIR");

        var now = MomentsRules.NowUtc();
        var expires = MomentsRules.ComputeExpiresAt(now);

        // Generated once, outside the retry loop, so every attempt inserts the same key.
        // If a commit succeeded but was reported failed, the retry recognises our own row
        // by this ID alone — never by timestamps or other matching column values.
        var matchId = Guid.NewGuid();

        // Only the Match this method adds is touched on retry; caller-tracked notes and
        // responses are left alone (no ChangeTracker.Clear).
        Match? pending = null;
        var commitAttempted = false;

        void DetachPending()
        {
            if (pending is null) return;
            var entry = _db.Entry(pending);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;
            pending = null;
        }

        // The whole serializable transaction runs inside the execution strategy so a
        // transient failure retries the full check-insert unit, not a fragment of it.
        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            DetachPending();

            // SERIALIZABLE prevents two concurrent creates from both passing the "exists" check.
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            try
            {
                var exists = await _db.Matches.AnyAsync(m =>
                    m.UserAId == a &&
                    m.UserBId == b &&
                    m.BalloonState == BalloonState.ACTIVE, ct);

                if (exists)
                {
                    // A previous attempt may have committed before the connection dropped.
                    // Only if the active row carries our exact ID is it ours; report it as
                    // created so the caller links notes and notifications fire exactly once.
                    // Any other active row is an unrelated match and is rejected as before.
                    if (commitAttempted)
                    {
                        var own = await _db.Matches.FirstOrDefaultAsync(m =>
                            m.Id == matchId &&
                            m.UserAId == a &&
                            m.UserBId == b &&
                            m.BalloonState == BalloonState.ACTIVE, ct);

                        if (own != null)
                        {
                            await tx.RollbackAsync(ct);
                            return new CreateMatchResult(true, own, null);
                        }
                    }

                    await tx.RollbackAsync(ct);
                    return new CreateMatchResult(false, null, "ACTIVE_MATCH_ALREADY_EXISTS");
                }

                pending = new Match
                {
                    Id = matchId,
                    UserAId = a,
                    UserBId = b,
                    MatchType = matchType,
                    EdgeOwnerId = edgeOwnerId,
                    BalloonState = BalloonState.ACTIVE,
                    ClosedReason = null,
                    CreatedAt = now,
                    ExpiresAt = expires,
                    ClosedAt = null
                };

                _db.Matches.Add(pending);
                await _db.SaveChangesAsync(ct);

                // Sticky: once any attempt reaches commit, later attempts must check for our row.
                commitAttempted = true;
                await tx.CommitAsync(ct);

                return new CreateMatchResult(true, pending, null);
            }
            catch
            {
                // Don't carry Added/Unchanged state from a failed attempt into the retry.
                DetachPending();
                throw;
            }
        });

        if (!result.Created || result.Match is null)
            return result;

        var match = result.Match;

        // Notifications/analytics run once, after the committed unit — never inside a retry.
        // Phase 1C: notify the recipient(s) in real-time
        if (matchType == MatchType.EDGE && edgeOwnerId.HasValue)
        {
            // One user explicitly sent a Moment — notify the other user
            var recipientId = edgeOwnerId.Value == a ? b : a;
            await _notifications.MomentReceivedAsync(recipientId, match.Id, edgeOwnerId.Value, ct);
        }
        else if (matchType == MatchType.PURE)
        {
            // Algorithm-generated mutual match — notify both users
            await Task.WhenAll(
                _notifications.MomentReceivedAsync(a, match.Id, b, ct),
                _notifications.MomentReceivedAsync(b, match.Id, a, ct)
            );
        }

        _ = _analytics.TrackAsync(match.UserAId, null, AnalyticsEvents.MatchCreated,
            new { matchType = matchType.ToString(), score = (object?)null, bucket = (object?)null });
        _ = _analytics.TrackAsync(match.UserBId, null, AnalyticsEvents.MatchCreated,
            new { matchType = matchType.ToString(), score = (object?)null, bucket = (object?)null });

        return new CreateMatchResult(true, match, null);
    }
}
