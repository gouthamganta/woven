using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Embeddings;

public class ChatNoteEmbeddingWorker : BackgroundService
{
    private const string LockKey = "lock:chatnote-embedding-batch";
    private static readonly TimeSpan LockExpiry = TimeSpan.FromHours(2);

    private const string EmbeddingEndpoint = "https://api.openai.com/v1/embeddings";
    private const string EmbeddingModel    = "text-embedding-3-small";
    private const int    EmbeddingDims     = 1536;
    private const int    LastNNotes        = 20;
    private const int    MinNotes          = 3;
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(4);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory   _httpFactory;
    private readonly IConfiguration       _config;
    private readonly ICacheService        _cache;
    private readonly ILogger<ChatNoteEmbeddingWorker> _logger;

    public ChatNoteEmbeddingWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory   httpFactory,
        IConfiguration       config,
        ICacheService        cache,
        ILogger<ChatNoteEmbeddingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory  = httpFactory;
        _config       = config;
        _cache        = cache;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(RunInterval, ct);
            if (ct.IsCancellationRequested) break;

            if (!await _cache.AcquireLockAsync(LockKey, LockExpiry, ct))
            {
                _logger.LogInformation("[ChatNoteEmbedding] Skipping — another pod holds the lock");
                continue;
            }

            var start = DateTime.UtcNow;
            _logger.LogInformation("[ChatNoteEmbedding] Starting batch run at {Time}", start);

            try { await RunAsync(ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatNoteEmbedding] Batch failed after {Ms}ms",
                    (int)(DateTime.UtcNow - start).TotalMilliseconds);
            }
            finally
            {
                await _cache.ReleaseLockAsync(LockKey, ct);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[ChatNoteEmbedding] OpenAI:ApiKey not configured — skipping");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        // Load all users who have written enough ChatNotes
        var usersWithNotes = await db.ChatNotes
            .GroupBy(n => n.FromUserId)
            .Where(g => g.Count() >= MinNotes)
            .Select(g => g.Key)
            .ToListAsync(ct);

        _logger.LogInformation("[ChatNoteEmbedding] Processing {Count} users", usersWithNotes.Count);

        int updated = 0;
        foreach (var userId in usersWithNotes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await ProcessUserAsync(db, apiKey, userId, ct);
                updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ChatNoteEmbedding] Failed for user {UserId}", userId);
            }
        }

        _logger.LogInformation("[ChatNoteEmbedding] Updated {Count}/{Total} users", updated, usersWithNotes.Count);
    }

    private async Task ProcessUserAsync(WovenDbContext db, string apiKey, int userId, CancellationToken ct)
    {
        var notes = await db.ChatNotes
            .Where(n => n.FromUserId == userId && n.NoteText.Length > 0)
            .OrderByDescending(n => n.CreatedAt)
            .Take(LastNNotes)
            .Select(n => n.NoteText)
            .ToListAsync(ct);

        if (notes.Count < MinNotes) return;

        // Concatenate notes as newline-separated prose — preserves semantic distinctness
        var text = string.Join("\n", notes);

        var embedding = await GetEmbeddingAsync(apiKey, text, ct);
        if (embedding is null) return;

        var vector = await db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector is null) return;

        vector.PreferenceEmbedding = embedding;
        vector.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<Vector?> GetEmbeddingAsync(string apiKey, string text, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model      = EmbeddingModel,
                input      = text,
                dimensions = EmbeddingDims
            });

            using var http    = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, EmbeddingEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var floats = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();

            return new Vector(floats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatNoteEmbedding] OpenAI call failed");
            return null;
        }
    }
}
