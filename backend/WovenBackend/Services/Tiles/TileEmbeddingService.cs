using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;

namespace WovenBackend.Services.Tiles;

// Singleton: uses IServiceScopeFactory for DB access so it stays safe when called
// from fire-and-forget Task.Run after the originating request scope has ended.
public class TileEmbeddingService
{
    private const string EmbeddingEndpoint = "https://api.openai.com/v1/embeddings";
    private const string EmbeddingModel    = "text-embedding-3-small";
    private const int    EmbeddingDims     = 1536;
    private const int    LastNTiles        = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory   _httpFactory;
    private readonly IConfiguration       _config;
    private readonly ILogger<TileEmbeddingService> _logger;

    public TileEmbeddingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory   httpFactory,
        IConfiguration       config,
        ILogger<TileEmbeddingService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory  = httpFactory;
        _config       = config;
        _logger       = logger;
    }

    public async Task EmbedTileAsync(Guid tileId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WovenDbContext>();

        var tile = await db.Tiles.FirstOrDefaultAsync(t => t.Id == tileId, ct);
        if (tile is null || string.IsNullOrWhiteSpace(tile.ContentText))
            return;

        var embedding = await GetEmbeddingAsync(tile.ContentText, ct);
        if (embedding is null)
        {
            _logger.LogWarning("[TileEmbedding] Null embedding returned for tile {TileId}", tileId);
            return;
        }

        tile.Embedding = embedding;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[TileEmbedding] Embedded tile {TileId} (user {UserId})", tileId, tile.UserId);

        await UpdateExpressionEmbeddingAsync(db, tile.UserId, ct);
    }

    private async Task UpdateExpressionEmbeddingAsync(WovenDbContext db, int userId, CancellationToken ct)
    {
        var recentEmbeddings = await db.Tiles
            .Where(t => t.UserId == userId && t.Embedding != null)
            .OrderByDescending(t => t.CreatedAt)
            .Take(LastNTiles)
            .Select(t => t.Embedding)
            .ToListAsync(ct);

        if (recentEmbeddings.Count == 0) return;

        var mean = new float[EmbeddingDims];
        foreach (var emb in recentEmbeddings)
        {
            var span = emb!.Memory.Span;
            for (var i = 0; i < EmbeddingDims; i++)
                mean[i] += span[i];
        }
        for (var i = 0; i < EmbeddingDims; i++)
            mean[i] /= recentEmbeddings.Count;

        var latestVector = await db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (latestVector is null) return;

        latestVector.ExpressionEmbedding = new Vector(mean);
        latestVector.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[TileEmbedding] Updated ExpressionEmbedding for user {UserId} ({Count} tiles)",
            userId, recentEmbeddings.Count);
    }

    private async Task<Vector?> GetEmbeddingAsync(string text, CancellationToken ct)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[TileEmbedding] OpenAI:ApiKey not configured — skipping");
            return null;
        }

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
            _logger.LogError(ex, "[TileEmbedding] OpenAI call failed");
            return null;
        }
    }
}
