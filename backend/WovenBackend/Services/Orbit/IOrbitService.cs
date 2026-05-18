namespace WovenBackend.Services.Orbit;

public record OrbitResult(string RelationshipType, bool MutualDetected);

public interface IOrbitService
{
    Task<OrbitResult> OrbitTileAsync(int orbiterId, Guid tileId, CancellationToken ct = default);
}
