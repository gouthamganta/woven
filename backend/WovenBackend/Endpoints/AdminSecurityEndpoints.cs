using WovenBackend.Services.Security;

namespace WovenBackend.Endpoints;

public static class AdminSecurityEndpoints
{
    public static void MapAdminSecurityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/security").RequireAuthorization("Admin");

        // POST /admin/security/rotate-keys — triggers key rotation check; ops use after deploying new secret
        group.MapPost("/rotate-keys", async (
            KeyRotationWorker rotationWorker,
            ISecurityAuditService audit,
            CancellationToken ct) =>
        {
            audit.Log("admin_key_rotation_triggered", resourceType: "KeyRotation", resourceId: "manual");

            await rotationWorker.CheckAndRotateAsync(ct);

            return Results.Ok(new { triggered = true, note = "Rotation check complete. See logs for outcome." });
        });
    }
}
