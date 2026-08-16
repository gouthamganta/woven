using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WovenBackend.Data;
using WovenBackend.Data.Entities;

namespace WovenBackend.Services;

public class IdempotencyService : IIdempotencyService
{
    private readonly WovenDbContext _db;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(WovenDbContext db, ILogger<IdempotencyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IdempotencyResult?> CheckAsync(string key, int userId, string endpoint, CancellationToken ct = default)
    {
        var record = await _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key && r.UserId == userId, ct);

        if (record == null)
        {
            return null;
        }

        // Found existing record - return cached response
        _logger.LogInformation(
            "[Idempotency] Key found | Key={Key} UserId={UserId} Endpoint={Endpoint} OriginalStatusCode={StatusCode}",
            key, userId, endpoint, record.StatusCode);

        return new IdempotencyResult(record.StatusCode, record.ResponseBody);
    }

    public async Task StoreAsync(string key, int userId, string endpoint, int statusCode, object responseBody, CancellationToken ct = default)
    {
        try
        {
            var record = new IdempotencyRecord
            {
                Key = key,
                UserId = userId,
                Endpoint = endpoint,
                StatusCode = statusCode,
                ResponseBody = JsonSerializer.Serialize(responseBody),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
            };

            _db.IdempotencyRecords.Add(record);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[Idempotency] Stored | Key={Key} UserId={UserId} Endpoint={Endpoint} StatusCode={StatusCode}",
                key, userId, endpoint, statusCode);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("uq_idempotency_key_user") == true)
        {
            // Race condition: another request stored this key between our check and insert
            // This is fine - the first request won, and this request's response will be identical
            _logger.LogDebug(
                "[Idempotency] Race condition on store | Key={Key} UserId={UserId}",
                key, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Idempotency] Failed to store | Key={Key} UserId={UserId}", key, userId);
            // Don't throw - idempotency is best-effort, operation already succeeded
        }
    }
}
