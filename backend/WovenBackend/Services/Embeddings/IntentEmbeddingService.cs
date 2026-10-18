using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using WovenBackend.Data;
using WovenBackend.Services.Matchmaking;

namespace WovenBackend.Services.Embeddings;

public class IntentEmbeddingService : IIntentEmbeddingService
{
    private const int EmbeddingDims = 1536;

    private readonly WovenDbContext _db;
    private readonly IOpenAiClient _openAi;
    private readonly ILogger<IntentEmbeddingService> _logger;

    public IntentEmbeddingService(
        WovenDbContext db,
        IOpenAiClient openAi,
        ILogger<IntentEmbeddingService> logger)
    {
        _db = db;
        _openAi = openAi;
        _logger = logger;
    }

    public async Task ComputeIntentEmbeddingAsync(int userId, CancellationToken ct = default)
    {
        var vector = await _db.UserVectors
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        if (vector == null)
        {
            _logger.LogInformation("[IntentEmbedding] No vector found for user {UserId}", userId);
            return;
        }

        // Parse intent metadata from VectorJson
        IntentMetadata? intent = null;
        try
        {
            using var doc = JsonDocument.Parse(vector.VectorJson);
            if (doc.RootElement.TryGetProperty("intent", out var intentElement))
            {
                intent = JsonSerializer.Deserialize<IntentMetadata>(intentElement.GetRawText());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IntentEmbedding] Failed to parse intent for user {UserId}", userId);
            return;
        }

        if (intent == null)
        {
            _logger.LogInformation("[IntentEmbedding] No intent metadata for user {UserId}", userId);
            return;
        }

        // Build natural language description of intent
        var description = BuildIntentDescription(intent);

        // Get embedding from OpenAI
        var embedding = await _openAi.EmbedAsync(description, EmbeddingDims, ct);
        if (embedding == null)
        {
            _logger.LogWarning("[IntentEmbedding] Failed to get embedding for user {UserId}", userId);
            return;
        }

        // Store embedding
        vector.IntentEmbedding = new Vector(embedding);
        vector.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[IntentEmbedding] Computed intent embedding for user {UserId}", userId);
    }

    private static string BuildIntentDescription(IntentMetadata intent)
    {
        var sb = new StringBuilder();

        // Seriousness (0 = casual, 1 = very serious)
        sb.Append("Relationship approach: ");
        if (intent.Seriousness < 0.3)
            sb.Append("casual, exploring without pressure. ");
        else if (intent.Seriousness < 0.7)
            sb.Append("balanced, open to both casual and serious connections. ");
        else
            sb.Append("serious, looking for meaningful long-term relationship. ");

        // Commitment readiness (0 = exploring, 1 = ready now)
        sb.Append("Commitment timeline: ");
        if (intent.CommitmentReadiness < 0.3)
            sb.Append("exploring, no rush to commit. ");
        else if (intent.CommitmentReadiness < 0.7)
            sb.Append("open to commitment when it feels right. ");
        else
            sb.Append("ready for commitment, looking to settle down. ");

        // Flexibility (0 = rigid, 1 = very flexible)
        sb.Append("Flexibility: ");
        if (intent.Flexibility < 0.3)
            sb.Append("knows exactly what they want, clear preferences. ");
        else if (intent.Flexibility < 0.7)
            sb.Append("some flexibility, willing to adjust. ");
        else
            sb.Append("very flexible, open to many possibilities. ");

        // Intent tags
        if (intent.Tags != null && intent.Tags.Count > 0)
        {
            sb.Append("Relationship intentions: ");
            sb.Append(string.Join(", ", intent.Tags));
            sb.Append(". ");
        }

        return sb.ToString().Trim();
    }
}
