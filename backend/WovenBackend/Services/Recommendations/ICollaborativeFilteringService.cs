namespace WovenBackend.Services.Recommendations;

public interface ICollaborativeFilteringService
{
    Task RunAsync(CancellationToken ct = default);
}
