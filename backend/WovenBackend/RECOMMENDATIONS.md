# Dating App Personalization - Recommendations & Next Steps

## Executive Summary

This document outlines recommendations for continuing to improve the AI personalization system in the Woven dating app backend. The refactor addressed core issues around data vacuum (AI prompts not receiving user context) and generic outputs (template-like responses).

---

## 1. Security Recommendations

### 1.1 Implemented (In This Refactor)

- **PII Sanitization**: `AiProfileService.SanitizeForAi()` removes emails and phone numbers before AI prompts
- **Prompt Injection Protection**: Regex patterns detect and replace common injection attempts
- **Authorization Checks**: Debug endpoints require authentication, users can only access their own data

### 1.2 Recommended Additions

#### Rate Limiting on Debug Endpoints
```csharp
// Add to Program.cs or middleware
app.MapGet("/debug/...", ...).RequireRateLimiting("debug-api");

// Configure rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("debug-api", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 30;
    });
});
```

#### Input Validation on Game Endpoints
- Validate `matchId` exists and user is a participant
- Validate `gameType` is a valid enum value
- Add model validation attributes to DTOs

#### AI Response Validation
```csharp
// In game agents, validate AI response structure
private bool ValidateQuestionResponse(string json)
{
    try
    {
        var questions = JsonSerializer.Deserialize<List<QuestionData>>(json);
        return questions?.Count == 3 && questions.All(q =>
            !string.IsNullOrWhiteSpace(q.Text) &&
            q.Options?.Count >= 2);
    }
    catch { return false; }
}
```

---

## 2. Performance Recommendations

### 2.1 Caching Layer

**Problem**: `AiProfileService.GetProfileAsync()` makes 4 separate DB queries per call.

**Solution**: Add a short-lived cache (5-10 minutes) for AI profiles:

```csharp
public interface IAiProfileCache
{
    Task<AiProfile?> GetOrCreateAsync(int userId, Func<Task<AiProfile?>> factory);
    void Invalidate(int userId);
}

public class AiProfileCache : IAiProfileCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _expiry = TimeSpan.FromMinutes(5);

    public async Task<AiProfile?> GetOrCreateAsync(int userId, Func<Task<AiProfile?>> factory)
    {
        var key = $"ai-profile:{userId}";
        if (_cache.TryGetValue(key, out AiProfile? cached))
            return cached;

        var profile = await factory();
        if (profile != null)
            _cache.Set(key, profile, _expiry);
        return profile;
    }
}
```

**Invalidation Points**:
- When UserVector is updated
- When UserIntent changes
- When UserVectorTags are modified

### 2.2 Batch Loading for Game Context

**Current**: Multiple sequential queries in `BuildGameContextAsync()`

**Recommended**: Combine into single optimized query:

```csharp
// Load all data for both users in parallel
var (userAData, userBData) = await Task.WhenAll(
    LoadUserCompleteDataAsync(match.UserAId, ct),
    LoadUserCompleteDataAsync(match.UserBId, ct)
);
```

### 2.3 OpenAI Response Caching

For deterministic prompts (same user context = same questions could be cached):

```csharp
// Cache key based on user profile hash
var cacheKey = $"game-questions:{gameType}:{ComputeProfileHash(targetProfile)}:{difficulty}";
```

---

## 3. Feedback Loop Recommendations

### 3.1 AI Prompt Metrics (Recommended Addition)

Track which AI outputs perform well:

```csharp
public class AiPromptMetric
{
    public Guid Id { get; set; }
    public string PromptType { get; set; } // "game_question", "match_explanation"
    public string PromptHash { get; set; } // Hash of input context
    public string ResponseSample { get; set; } // First 500 chars of response
    public int UsageCount { get; set; }
    public double AvgEngagementScore { get; set; } // Computed from outcomes
    public DateTimeOffset CreatedAt { get; set; }
}
```

### 3.2 A/B Testing Infrastructure

```csharp
public interface IABTestService
{
    string GetVariant(int userId, string experimentName);
    Task RecordOutcome(int userId, string experimentName, string outcome);
}

// Usage in game agents:
var promptVersion = _abTest.GetVariant(userId, "game-prompt-v2");
var prompt = promptVersion == "control" ? BuildPromptV1() : BuildPromptV2();
```

### 3.3 User Feedback Collection

Add post-game feedback option:

```csharp
public class GameFeedback
{
    public Guid SessionId { get; set; }
    public int UserId { get; set; }
    public int QualityRating { get; set; } // 1-5
    public bool WereQuestionsRelevant { get; set; }
    public bool WouldPlayAgain { get; set; }
    public string? FreeformFeedback { get; set; }
}
```

---

## 4. Content Quality Recommendations

### 4.1 Expand Banned Phrases List

Current banned phrases are a good start. Recommend expanding based on actual generated content:

```csharp
private static readonly HashSet<string> BannedPhrases = new(StringComparer.OrdinalIgnoreCase)
{
    // Generic relationship phrases
    "meaningful connection",
    "genuine conversation",
    "good vibes",
    "real talk",
    "keeping it real",

    // Overused dating app cliches
    "partner in crime",
    "Netflix and chill",
    "looking for my person",
    "here for a good time",

    // Vague personality descriptors
    "down to earth",
    "easy going",
    "love to laugh",
    "work hard play hard"
};
```

### 4.2 Add Question Diversity Tracking

Prevent repeating similar questions across sessions:

```csharp
public class QuestionHistory
{
    public int UserId { get; set; }
    public string QuestionHash { get; set; } // Hash of question text
    public string Category { get; set; }
    public DateTimeOffset AskedAt { get; set; }
}

// In game agents:
var recentQuestions = await GetRecentQuestionsAsync(userId, TimeSpan.FromDays(7));
// Pass to prompt: "Avoid these recently asked topics: {topics}"
```

### 4.3 Pillar-Specific Question Templates

Create question templates tied to specific pillars for fallback:

```csharp
public static class PillarQuestionTemplates
{
    public static readonly Dictionary<string, string[]> Templates = new()
    {
        ["Communication"] = new[]
        {
            "When {name} needs to share difficult news, do they prefer to...",
            "After a disagreement, {name} most likely...",
        },
        ["Affection"] = new[]
        {
            "{name}'s ideal way to feel appreciated is...",
            "When {name} wants to show they care, they typically...",
        }
        // ... etc
    };
}
```

---

## 5. Observability Recommendations

### 5.1 Structured Logging

Add structured logging for AI operations:

```csharp
_logger.LogInformation(
    "AI generation completed {OperationType} for user {UserId}. " +
    "Tokens: {TokensUsed}, Latency: {LatencyMs}ms, Cached: {WasCached}",
    operationType, userId, tokensUsed, latencyMs, wasCached);
```

### 5.2 Metrics Dashboard

Track key metrics:
- AI prompt latency (p50, p95, p99)
- Cache hit rates
- Game completion rates by difficulty/tone
- Question diversity scores
- User feedback ratings

### 5.3 Alert Thresholds

Set up alerts for:
- AI latency > 5s
- Game abandonment rate > 40%
- Cache hit rate < 50%
- Error rate > 1%

---

## 6. Missing Game Expiration Worker

**Gap Identified**: Currently, game sessions are only marked as EXPIRED when someone tries to accept them. Active games that are abandoned mid-game are not tracked.

**Recommendation**: Create a `GameExpiryWorker`:

```csharp
public class GameExpiryWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExpireGamesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ExpireGamesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Expire pending sessions
        var pendingExpired = await _db.GameSessions
            .Where(s => s.Status == "PENDING")
            .Where(s => s.ExpiresAt <= now)
            .ToListAsync(ct);

        // Expire active sessions (abandoned mid-game)
        var activeExpired = await _db.GameSessions
            .Where(s => s.Status == "ACTIVE")
            .Where(s => s.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var session in pendingExpired.Concat(activeExpired))
        {
            session.Status = "EXPIRED";

            // Record outcome for active games
            if (session.Status == "ACTIVE")
            {
                await _outcomeService.RecordOutcomeAsync(session.Id, new GameOutcomeData
                {
                    CompletionStatus = "ABANDONED",
                    // ... other fields
                }, ct);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
```

---

## 7. Priority Order for Implementation

### High Priority (Implement Now)
1. Rate limiting on debug endpoints
2. Game expiration worker
3. AI profile caching

### Medium Priority (Next Sprint)
1. Input validation on all endpoints
2. Batch loading optimization
3. Question diversity tracking

### Lower Priority (Future)
1. A/B testing infrastructure
2. User feedback collection
3. AI prompt metrics

---

## 8. Testing Checklist

Before deploying these changes:

- [ ] Unit tests for `AiProfileService` pillar parsing
- [ ] Unit tests for PII sanitization
- [ ] Unit tests for prompt injection detection
- [ ] Integration tests for game flow
- [ ] Load tests for AI endpoints
- [ ] Manual QA of generated content quality
- [ ] Review of AI prompt outputs in staging

---

**Document Version**: 1.0
**Last Updated**: 2026-01-25
**Author**: AI Implementation Assistant
