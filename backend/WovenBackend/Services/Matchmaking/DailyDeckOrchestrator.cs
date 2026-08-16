using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services.Matchmaking;

public class DailyDeckOrchestrator : IDailyDeckOrchestrator
{
    private readonly WovenDbContext _db;
    private readonly ICandidatePoolService _candidatePool;
    private readonly IHardFilterService _hardFilter;
    private readonly IMatchScoringService _scoring;
    private readonly IDeckSelectionService _selection;
    private readonly IMatchExplanationService _explanation;
    private readonly IDeliveryBoostService _deliveryBoost;
    private readonly ILinUcbService _linUcb;
    private readonly WovenBackend.Services.ICacheService _cache;
    private readonly WovenBackend.Services.INotificationService _notifications;
    private readonly IMatchNarratorService? _narrator;
    private readonly ILogger<DailyDeckOrchestrator> _logger;

    public DailyDeckOrchestrator(
        WovenDbContext db,
        ICandidatePoolService candidatePool,
        IHardFilterService hardFilter,
        IMatchScoringService scoring,
        IDeckSelectionService selection,
        IMatchExplanationService explanation,
        IDeliveryBoostService deliveryBoost,
        ILinUcbService linUcb,
        WovenBackend.Services.ICacheService cache,
        WovenBackend.Services.INotificationService notifications,
        ILogger<DailyDeckOrchestrator> logger,
        IMatchNarratorService? narrator = null)
    {
        _db = db;
        _candidatePool = candidatePool;
        _hardFilter = hardFilter;
        _scoring = scoring;
        _selection = selection;
        _explanation = explanation;
        _deliveryBoost = deliveryBoost;
        _linUcb = linUcb;
        _cache = cache;
        _notifications = notifications;
        _narrator = narrator;
        _logger = logger;
    }

    public async Task<DailyDeckResult> GetOrCreateDeckAsync(int userId, DateOnly dateUtc, CancellationToken ct = default)
    {
        _logger.LogInformation("[DeckOrchestrator] Getting deck for user {UserId} on {Date}", userId, dateUtc);

        // Phase 1B: Redis fast-path — avoids a DB round-trip for repeat calls on the same day
        var cacheKey = WovenBackend.Services.CacheKeys.DailyDeck(userId, dateUtc);
        var cached = await _cache.GetAsync<List<DeckItem>>(cacheKey, ct);
        if (cached != null)
        {
            _logger.LogInformation("[DeckOrchestrator] Redis cache hit for user {UserId}", userId);
            return new DailyDeckResult { Items = cached, Generated = false };
        }

        // Check if deck already exists in DB
        var existingDeck = await _db.DailyDecks
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DateUtc == dateUtc, ct);

        if (existingDeck != null)
        {
            _logger.LogInformation("[DeckOrchestrator] Deck already exists in DB, backfilling Redis cache");
            var items = JsonSerializer.Deserialize<List<DeckItem>>(existingDeck.ItemsJson)
                ?? new List<DeckItem>();

            await _cache.SetAsync(cacheKey, items, WovenBackend.Services.CacheTtl.UntilMidnightUtc(), ct);

            return new DailyDeckResult
            {
                Items = items,
                Generated = false,
                MoodLine = existingDeck.MoodLine
            };
        }

        // Generate new deck
        _logger.LogInformation("[DeckOrchestrator] Generating new deck for user {UserId}", userId);

        // 1. Get candidate pool (social exclusions: blocks, active balloons, shown-today, gender, trust)
        var candidateIds = await _candidatePool.GetEligibleCandidatesAsync(userId, ct);
        if (candidateIds.Count == 0)
        {
            _logger.LogWarning("[DeckOrchestrator] No eligible candidates for user {UserId}", userId);
            return new DailyDeckResult { Items = new List<DeckItem>(), Generated = true };
        }

        // 1b. ECHO hard filters — only age reciprocity + distance (binary exclusions)
        candidateIds = await _hardFilter.ApplyAsync(userId, candidateIds, ct);
        if (candidateIds.Count == 0)
        {
            _logger.LogWarning("[DeckOrchestrator] All candidates filtered by hard filters for user {UserId}", userId);
            return new DailyDeckResult { Items = new List<DeckItem>(), Generated = true };
        }

        // 2. Score all candidates
        var scores = await _scoring.ScoreCandidatesAsync(userId, candidateIds, ct);
        if (scores.Count == 0)
        {
            _logger.LogWarning("[DeckOrchestrator] No scores computed for user {UserId}", userId);
            return new DailyDeckResult { Items = new List<DeckItem>(), Generated = true };
        }

        // 3. Get boost maps and select top 5 with diversity
        // deliveryBoostMap: recency / re-delivery weighting (0–10)
        // ucbBoostMap:      LinUCB exploration bonus (0–10)
        // Both are additive on TotalScore (0–100); combined cap is ~20 which biases but does not override.
        var deliveryBoostMap = await _deliveryBoost.GetBoostMapAsync(userId, candidateIds, dateUtc, ct);
        var ucbBoostMap      = await _linUcb.GetBoostMapAsync(userId, candidateIds, ct: ct);

        var boostMap = new Dictionary<int, double>(candidateIds.Count);
        foreach (var cid in candidateIds)
        {
            var d = deliveryBoostMap.GetValueOrDefault(cid);
            var u = ucbBoostMap.GetValueOrDefault(cid);
            boostMap[cid] = d + u;
        }

        var selected = _selection.SelectTop5(scores, boostMap);

        // 3.5 Generate mood line from the full scored pool
        var moodLine = GenerateMoodLine(scores);

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

        // 4.25: Enrich deck items with cinematic narrator fields (Ken Burns + quote + TTS)
        // Runs in parallel — 5 concurrent TTS calls max.
        // NarrationUrl=null if TTS fails or isn't configured; frontend silently skips audio.
        if (_narrator != null)
        {
            var narratorTasks = deckItems.Select(async item =>
            {
                try
                {
                    var result = await _narrator.BuildNarratorFieldsAsync(item.CandidateId, dateUtc, ct);
                    item.KenBurnsPhotoUrls = result.KenBurnsPhotoUrls;
                    item.CuratedQuote      = result.CuratedQuote;
                    item.NarrationUrl      = result.NarrationUrl;
                    item.NarrationExposed  = result.NarrationExposed;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DeckOrchestrator] Narrator enrichment failed for candidate {CandidateId}", item.CandidateId);
                }
            });
            await Task.WhenAll(narratorTasks);
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

        // 5. Save deck (dual-write: ItemsJson for rollback + DailyDeckItems for analytics)
        var deck = new DailyDeck
        {
            UserId = userId,
            DateUtc = dateUtc,
            GeneratedAt = DateTime.UtcNow,
            ItemsJson = JsonSerializer.Serialize(deckItems),
            MoodLine = moodLine
        };

        _db.DailyDecks.Add(deck);
        await _db.SaveChangesAsync(ct);

        // Write normalized deck items for queryability
        var deckItemEntities = deckItems.Select((item, index) => new DailyDeckItem
        {
            DeckId = deck.Id,
            CandidateId = item.CandidateId,
            Score = item.Score,
            Bucket = item.Bucket,
            Rank = index + 1,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();

        _db.DailyDeckItems.AddRange(deckItemEntities);
        await _db.SaveChangesAsync(ct);

        // Phase 1B: cache the freshly generated deck until UTC midnight
        await _cache.SetAsync(cacheKey, deckItems, WovenBackend.Services.CacheTtl.UntilMidnightUtc(), ct);

        // Phase 1C: notify the user in real-time that their deck is ready
        await _notifications.DeckReadyAsync(userId, dateUtc, ct);

        _logger.LogInformation("[DeckOrchestrator] Generated and saved deck with {Count} items for user {UserId}",
            deckItems.Count, userId);

        return new DailyDeckResult
        {
            Items = deckItems,
            Generated = true,
            MoodLine = moodLine
        };
    }

    private static string? GenerateMoodLine(List<MatchScore> scores)
    {
        if (scores.Count == 0) return null;

        var topFive = scores.OrderByDescending(s => s.TotalScore).Take(5).ToList();
        if (topFive.Count == 0) return null;

        var avg = topFive.Average(s => s.TotalScore);
        var top = topFive.First().TotalScore;
        var poolSize = scores.Count;

        if (poolSize < 8)
            return "ECHO is still building your pool.";

        if (top >= 85 && avg >= 80)
            return "Something feels right about today.";

        if (avg >= 77)
            return "Your deck today is sharp.";

        if (avg < 58 && poolSize >= 15)
            return "ECHO is still finding your frequency.";

        return null;
    }
}