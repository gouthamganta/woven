namespace WovenBackend.Data.Entities;

/// <summary>
/// Stores idempotency keys to prevent duplicate execution of critical mutations.
/// Used for: balloon pop, trial decision, spark spend, unmatch operations.
/// </summary>
public class IdempotencyRecord
{
    public long Id { get; set; }

    /// <summary>
    /// Client-provided idempotency key (typically UUID)
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// User ID who initiated the request
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Endpoint path (e.g., "/matches/{id}/pop", "/matches/{id}/trial-decision")
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// HTTP status code of the original response
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// JSON-serialized response body (returned for duplicate requests)
    /// </summary>
    public string ResponseBody { get; set; } = "";

    /// <summary>
    /// When this operation was first executed
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// TTL: records older than 24 hours can be purged
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
}
