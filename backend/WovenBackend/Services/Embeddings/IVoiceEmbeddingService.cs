namespace WovenBackend.Services.Embeddings;

public interface IVoiceEmbeddingService
{
    /// <summary>
    /// Fetches the audio at <paramref name="audioUrl"/>, runs speechbrain_embed.py to produce
    /// a 192-dim ECAPA-TDNN embedding, stores it on the tile, and updates user_voice_preference.
    /// </summary>
    Task EmbedVoiceAsync(Guid tileId, int userId, string audioUrl, CancellationToken ct = default);
}
