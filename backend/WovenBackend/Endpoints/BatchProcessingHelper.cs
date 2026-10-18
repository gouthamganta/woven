namespace WovenBackend.Endpoints;

/// <summary>
/// Shared helper for processing collections in batches to avoid thread pool exhaustion.
/// </summary>
public static class BatchProcessingHelper
{
    /// <summary>
    /// Default batch size for parallel operations.
    /// Tuned to balance throughput vs resource usage.
    /// </summary>
    public const int DefaultBatchSize = 10;

    /// <summary>
    /// Process a collection in batches, executing async work in parallel within each batch.
    /// </summary>
    /// <typeparam name="TInput">Type of items to process</typeparam>
    /// <typeparam name="TOutput">Type of results</typeparam>
    /// <param name="items">Items to process</param>
    /// <param name="processor">Async function to process each item (should include error handling)</param>
    /// <param name="batchSize">Number of items to process in parallel per batch</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Dictionary mapping each item to its result</returns>
    public static async Task<Dictionary<TKey, TValue>> ProcessInBatchesAsync<TInput, TKey, TValue>(
        IList<TInput> items,
        Func<TInput, Task<(TKey key, TValue value)>> processor,
        int batchSize = DefaultBatchSize,
        CancellationToken ct = default)
        where TKey : notnull
    {
        var results = new Dictionary<TKey, TValue>(capacity: items.Count);

        for (int i = 0; i < items.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();

            // Use index-based slicing (O(n)) instead of Skip().Take() (O(n²))
            int batchEnd = Math.Min(i + batchSize, items.Count);
            var batchTasks = new List<Task<(TKey key, TValue value)>>(batchEnd - i);

            for (int j = i; j < batchEnd; j++)
            {
                batchTasks.Add(processor(items[j]));
            }

            var batchResults = await Task.WhenAll(batchTasks);

            foreach (var (key, value) in batchResults)
            {
                // Validate no duplicate keys (indicates bug in caller's processor function)
                if (results.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        $"Duplicate key '{key}' returned by processor. " +
                        "This indicates the processor function returned the same key for different inputs.");
                }
                results[key] = value;
            }
        }

        return results;
    }
}
