using System.Text.Json;

namespace WovenBackend.Services;

public interface IIdempotencyService
{
    /// <summary>
    /// Check if an idempotency key has been processed before.
    /// If yes, returns the cached response.
    /// If no, returns null and caller should proceed with operation.
    /// </summary>
    Task<IdempotencyResult?> CheckAsync(string key, int userId, string endpoint, CancellationToken ct = default);

    /// <summary>
    /// Store the result of an operation for future idempotent requests.
    /// </summary>
    Task StoreAsync(string key, int userId, string endpoint, int statusCode, object responseBody, CancellationToken ct = default);
}

public record IdempotencyResult(int StatusCode, string ResponseBodyJson);
