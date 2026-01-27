using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;
using WovenBackend.Services.Moments;

namespace WovenBackend.Services.Matchmaking;

public class MatchOutcomeService : IMatchOutcomeService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<MatchOutcomeService> _logger;

    public MatchOutcomeService(WovenDbContext db, ILogger<MatchOutcomeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordChatStartedAsync(Guid matchId, int userId, int candidateId, CancellationToken ct = default)
    {
        _logger.LogInformation("[MatchOutcome] Recording chat started: match={MatchId}, user={UserId}, candidate={CandidateId}",
            matchId, userId, candidateId);

        var today = MomentsRules.UtcToday();

        // Find or create outcome record
        var outcome = await _db.MatchOutcomes
            .FirstOrDefaultAsync(o =>
                o.MatchId == matchId &&
                o.UserId == userId &&
                o.CandidateId == candidateId &&
                o.DateUtc == today, ct);

        if (outcome == null)
        {
            outcome = new MatchOutcome
            {
                MatchId = matchId,
                UserId = userId,
                CandidateId = candidateId,
                DateUtc = today,
                ChatStarted = true,
                Messages24h = 0,
                Expired = false,
                Unmatched = false,
                Blocked = false,
                RecordedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.MatchOutcomes.Add(outcome);
        }
        else
        {
            outcome.ChatStarted = true;
            outcome.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[MatchOutcome] Chat started recorded for match {MatchId}", matchId);
    }

    public async Task RecordUnmatchAsync(Guid matchId, int userId, int candidateId, CancellationToken ct = default)
    {
        _logger.LogInformation("[MatchOutcome] Recording unmatch: match={MatchId}, user={UserId}, candidate={CandidateId}",
            matchId, userId, candidateId);

        var today = MomentsRules.UtcToday();

        var outcome = await _db.MatchOutcomes
            .FirstOrDefaultAsync(o =>
                o.MatchId == matchId &&
                o.UserId == userId &&
                o.CandidateId == candidateId &&
                o.DateUtc == today, ct);

        if (outcome == null)
        {
            outcome = new MatchOutcome
            {
                MatchId = matchId,
                UserId = userId,
                CandidateId = candidateId,
                DateUtc = today,
                ChatStarted = false,
                Messages24h = 0,
                Expired = false,
                Unmatched = true,
                Blocked = false,
                RecordedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.MatchOutcomes.Add(outcome);
        }
        else
        {
            outcome.Unmatched = true;
            outcome.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[MatchOutcome] Unmatch recorded for match {MatchId}", matchId);
    }

    public async Task RecordBlockAsync(int userId, int blockedId, CancellationToken ct = default)
    {
        _logger.LogInformation("[MatchOutcome] Recording block: user={UserId}, blocked={BlockedId}",
            userId, blockedId);

        var today = MomentsRules.UtcToday();

        // Try to find associated match (if it exists)
        var match = await _db.Matches
            .Where(m => (m.UserAId == userId && m.UserBId == blockedId) ||
                       (m.UserAId == blockedId && m.UserBId == userId))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var outcome = new MatchOutcome
        {
            MatchId = match?.Id,
            UserId = userId,
            CandidateId = blockedId,
            DateUtc = today,
            ChatStarted = false,
            Messages24h = 0,
            Expired = false,
            Unmatched = false,
            Blocked = true,
            RecordedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.MatchOutcomes.Add(outcome);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[MatchOutcome] Block recorded for user {UserId}", userId);
    }

    public async Task RecordExpiredAsync(Guid matchId, int userId, int candidateId, CancellationToken ct = default)
    {
        _logger.LogInformation("[MatchOutcome] Recording expired: match={MatchId}, user={UserId}, candidate={CandidateId}",
            matchId, userId, candidateId);

        var today = MomentsRules.UtcToday();

        var outcome = await _db.MatchOutcomes
            .FirstOrDefaultAsync(o =>
                o.MatchId == matchId &&
                o.UserId == userId &&
                o.CandidateId == candidateId &&
                o.DateUtc == today, ct);

        if (outcome == null)
        {
            outcome = new MatchOutcome
            {
                MatchId = matchId,
                UserId = userId,
                CandidateId = candidateId,
                DateUtc = today,
                ChatStarted = false,
                Messages24h = 0,
                Expired = true,
                Unmatched = false,
                Blocked = false,
                RecordedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.MatchOutcomes.Add(outcome);
        }
        else
        {
            outcome.Expired = true;
            outcome.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[MatchOutcome] Expired recorded for match {MatchId}", matchId);
    }
}