using System.Collections.Concurrent;
using System.Text.Json;

namespace WovenBackend.Services;

/// <summary>
/// Tracks OpenAI API usage and costs with daily budget enforcement.
/// </summary>
public interface IOpenAiCostTracker
{
    /// <summary>
    /// Checks if the daily budget has been exceeded.
    /// </summary>
    bool IsBudgetExceeded();

    /// <summary>
    /// Records a successful OpenAI API call with token usage.
    /// </summary>
    void RecordUsage(string operationType, int promptTokens, int completionTokens, double latencyMs);

    /// <summary>
    /// Gets usage statistics for today.
    /// </summary>
    DailyUsageStats GetTodayStats();

    /// <summary>
    /// Resets today's statistics (for testing or manual override).
    /// </summary>
    void ResetToday();
}

public class OpenAiCostTracker : IOpenAiCostTracker
{
    private readonly ILogger<OpenAiCostTracker> _logger;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<string, DailyUsageData> _usageByDate = new();

    // Pricing (as of 2026, adjust based on actual OpenAI pricing)
    // These are example rates - update with actual pricing
    private const double CostPerPromptToken = 0.00001; // $0.01 per 1K tokens
    private const double CostPerCompletionToken = 0.00003; // $0.03 per 1K tokens

    public OpenAiCostTracker(ILogger<OpenAiCostTracker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public bool IsBudgetExceeded()
    {
        var dailyBudget = _config.GetValue<double>("OpenAI:DailyBudgetUsd", 50.0);
        var todayKey = GetTodayKey();
        var stats = GetStatsForDate(todayKey);

        var exceeded = stats.TotalCostUsd >= dailyBudget;

        if (exceeded)
        {
            _logger.LogWarning("[OpenAI Cost] Daily budget EXCEEDED: ${Cost:F2} / ${Budget:F2}",
                stats.TotalCostUsd, dailyBudget);
        }

        return exceeded;
    }

    public void RecordUsage(string operationType, int promptTokens, int completionTokens, double latencyMs)
    {
        var todayKey = GetTodayKey();
        var data = _usageByDate.GetOrAdd(todayKey, _ => new DailyUsageData { Date = todayKey });

        var promptCost = promptTokens * CostPerPromptToken;
        var completionCost = completionTokens * CostPerCompletionToken;
        var totalCost = promptCost + completionCost;

        lock (data.Lock)
        {
            data.TotalRequests++;
            data.TotalPromptTokens += promptTokens;
            data.TotalCompletionTokens += completionTokens;
            data.TotalCostUsd += totalCost;
            data.TotalLatencyMs += latencyMs;

            if (!data.UsageByOperation.ContainsKey(operationType))
            {
                data.UsageByOperation[operationType] = new OperationUsage();
            }

            var opUsage = data.UsageByOperation[operationType];
            opUsage.RequestCount++;
            opUsage.PromptTokens += promptTokens;
            opUsage.CompletionTokens += completionTokens;
            opUsage.CostUsd += totalCost;
            opUsage.LatencyMs += latencyMs;
        }

        _logger.LogInformation(
            "[OpenAI Cost] {OperationType}: {PromptTokens} prompt + {CompletionTokens} completion tokens, " +
            "${Cost:F4}, {Latency:F0}ms. Today total: ${TotalCost:F2}",
            operationType, promptTokens, completionTokens, totalCost, latencyMs, data.TotalCostUsd);

        // Cleanup old data (keep last 7 days)
        CleanupOldData();
    }

    public DailyUsageStats GetTodayStats()
    {
        var todayKey = GetTodayKey();
        return GetStatsForDate(todayKey);
    }

    public void ResetToday()
    {
        var todayKey = GetTodayKey();
        _usageByDate.TryRemove(todayKey, out _);
        _logger.LogWarning("[OpenAI Cost] Today's usage data has been reset");
    }

    private DailyUsageStats GetStatsForDate(string dateKey)
    {
        if (!_usageByDate.TryGetValue(dateKey, out var data))
        {
            return new DailyUsageStats
            {
                Date = dateKey,
                TotalRequests = 0,
                TotalPromptTokens = 0,
                TotalCompletionTokens = 0,
                TotalCostUsd = 0.0,
                AverageLatencyMs = 0.0,
                OperationBreakdown = new Dictionary<string, OperationUsage>()
            };
        }

        lock (data.Lock)
        {
            return new DailyUsageStats
            {
                Date = data.Date,
                TotalRequests = data.TotalRequests,
                TotalPromptTokens = data.TotalPromptTokens,
                TotalCompletionTokens = data.TotalCompletionTokens,
                TotalCostUsd = data.TotalCostUsd,
                AverageLatencyMs = data.TotalRequests > 0 ? data.TotalLatencyMs / data.TotalRequests : 0.0,
                OperationBreakdown = new Dictionary<string, OperationUsage>(data.UsageByOperation)
            };
        }
    }

    private void CleanupOldData()
    {
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");

        var keysToRemove = _usageByDate.Keys
            .Where(k => string.Compare(k, cutoffDate, StringComparison.Ordinal) < 0)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _usageByDate.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogInformation("[OpenAI Cost] Cleaned up {Count} old daily records", keysToRemove.Count);
        }
    }

    private static string GetTodayKey()
    {
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
    }
}

internal class DailyUsageData
{
    public object Lock { get; } = new();
    public string Date { get; set; } = "";
    public int TotalRequests { get; set; } = 0;
    public int TotalPromptTokens { get; set; } = 0;
    public int TotalCompletionTokens { get; set; } = 0;
    public double TotalCostUsd { get; set; } = 0.0;
    public double TotalLatencyMs { get; set; } = 0.0;
    public Dictionary<string, OperationUsage> UsageByOperation { get; set; } = new();
}

public class DailyUsageStats
{
    public string Date { get; set; } = "";
    public int TotalRequests { get; set; }
    public int TotalPromptTokens { get; set; }
    public int TotalCompletionTokens { get; set; }
    public double TotalCostUsd { get; set; }
    public double AverageLatencyMs { get; set; }
    public Dictionary<string, OperationUsage> OperationBreakdown { get; set; } = new();
}

public class OperationUsage
{
    public int RequestCount { get; set; } = 0;
    public int PromptTokens { get; set; } = 0;
    public int CompletionTokens { get; set; } = 0;
    public double CostUsd { get; set; } = 0.0;
    public double LatencyMs { get; set; } = 0.0;

    public double AverageLatencyMs => RequestCount > 0 ? LatencyMs / RequestCount : 0.0;
}
