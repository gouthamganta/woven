namespace WovenBackend.Services.Matchmaking;

public interface IOpenAiTaggingService
{
    Task<IntentMetadata?> ExtractIntentMetadataAsync(
        string primaryIntent,
        string reflectionSentence,
        CancellationToken ct = default);

    Task<PillarScores?> ComputePillarScoresAsync(
        string answersJson,
        string questionsJson,
        CancellationToken ct = default);

    Task<Dictionary<string, List<string>>?> ExtractTagsAsync(
        string answersJson,
        string questionsJson,
        CancellationToken ct = default);
}