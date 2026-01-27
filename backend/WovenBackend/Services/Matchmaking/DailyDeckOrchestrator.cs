using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public class DailyDeckOrchestrator : IDailyDeckOrchestrator
{
    private readonly WovenDbContext _db;
    private readonly ICandidatePoolService _candidatePool;
    private readonly IMatchScoringService _scoring;
    private readonly IDeckSelectionService _selection;
    private readonly IMatchExplanationService _explanation;
    private readonly IDeliveryBoostService _deliveryBoost;
    private readonly ILogger<DailyDeckOrchestrator> _logger;

    public DailyDeckOrchestrator(
        WovenDbContext db,
        ICandidatePoolService candidatePool,
        IMatchScoringService scoring,
        IDeckSelectionService selection,
        IMatchExplanationService explanation,
        IDeliveryBoostService deliveryBoost,
        ILogger<DailyDeckOrchestrator> logger)
    {
        _db = db;
        _candidatePool = candidatePool;
        _scoring = scoring;
        _selection = selection;
        _explanation = explanation;
        _deliveryBoost = deliveryBoost;
        _logger = logger;
    }

    public async Task<DailyDeckResult> GetOrCreateDeckAsync(int userId, DateOnly dateUtc, CancellationToken ct = default)
    {
        _logger.LogInformation("[DeckOrchestrator] Getting deck for user {UserId} on {Date}", userId, dateUtc);

        // Check if deck already exists
        var existingDeck = await _db.DailyDecks
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DateUtc == dateUtc, ct);

        if (existingDeck != null)
        {
            _logger.LogInformation("[DeckOrchestrator] Deck already exists, returning cached version");
            var items = JsonSerializer.Deserialize<List<DeckItem>>(existingDeck.ItemsJson)
                ?? new List<DeckItem>();

            return new DailyDeckResult
            {
                Items = items,
                Generated = false
            };
        }

        // Generate new deck
        _logger.LogInformation("[DeckOrchestrator] Generating new deck for user {UserId}", userId);

        // 1. Get candidate pool
        var candidateIds = await _candidatePool.GetEligibleCandidatesAsync(userId, ct);
        if (candidateIds.Count == 0)
        {
            _logger.LogWarning("[DeckOrchestrator] No eligible candidates for user {UserId}", userId);
            return new DailyDeckResult { Items = new List<DeckItem>(), Generated = true };
        }

        // 2. Score all candidates
        var scores = await _scoring.ScoreCandidatesAsync(userId, candidateIds, ct);
        if (scores.Count == 0)
        {
            _logger.LogWarning("[DeckOrchestrator] No scores computed for user {UserId}", userId);
            return new DailyDeckResult { Items = new List<DeckItem>(), Generated = true };
        }

        // 3. Get boost map and select top 5 with diversity
        var boostMap = await _deliveryBoost.GetBoostMapAsync(userId, candidateIds, dateUtc, ct);
        var selected = _selection.SelectTop5(scores, boostMap);

        // 4. Generate explanations for selected 5
        var deckItems = new List<DeckItem>();
        var scoreMap = scores.ToDictionary(s => s.CandidateId, s => s);

        foreach (var (candidateId, bucket) in selected)
        {
            if (!scoreMap.TryGetValue(candidateId, out var score))
                continue;

            var explanationId = await _explanation.GenerateAndSaveExplanationAsync(
                userId,
                candidateId,
                score,
                bucket,
                dateUtc,
                ct);

            deckItems.Add(new DeckItem
            {
                CandidateId = candidateId,
                Score = score.TotalScore,
                Bucket = bucket.ToString(),
                ExplanationId = explanationId
            });
        }

        // ✅ 4.5 Record exposures (delivery memory)
        // Write one exposure row per shown candidate for today.
        try
        {
            // Prevent duplicates if method is called twice somehow
            var shownIds = deckItems.Select(x => x.CandidateId).Distinct().ToList();

            var alreadyExposed = await _db.CandidateExposures.AsNoTracking()
                .Where(e => e.ViewerUserId == userId && e.DateUtc == dateUtc && e.Surface == "DECK")
                .Select(e => e.ShownUserId)
                .ToListAsync(ct);

            var toInsert = deckItems
                .Where(x => !alreadyExposed.Contains(x.CandidateId))
                .Select(x => new CandidateExposure
                {
                    ViewerUserId = userId,
                    ShownUserId = x.CandidateId,
                    Surface = "DECK",
                    Bucket = x.Bucket,
                    ScoreSnapshot = x.Score,
                    DateUtc = dateUtc,
                    CreatedAt = DateTime.UtcNow
                })
                .ToList();

            if (toInsert.Count > 0)
            {
                _db.CandidateExposures.AddRange(toInsert);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeckOrchestrator] Failed to record exposures for user {UserId}", userId);
        }

        // 5. Save deck
        var deck = new DailyDeck
        {
            UserId = userId,
            DateUtc = dateUtc,
            GeneratedAt = DateTime.UtcNow,
            ItemsJson = JsonSerializer.Serialize(deckItems)
        };

        _db.DailyDecks.Add(deck);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[DeckOrchestrator] Generated and saved deck with {Count} items for user {UserId}",
            deckItems.Count, userId);

        return new DailyDeckResult
        {
            Items = deckItems,
            Generated = true
        };
    }
}