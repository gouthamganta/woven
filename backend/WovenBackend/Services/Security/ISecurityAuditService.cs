namespace WovenBackend.Services.Security;

public interface ISecurityAuditService
{
    /// <summary>
    /// Fire-and-forget insert to security_audit_log. Never throws.
    /// userId and ipAddress are hashed before storage.
    /// </summary>
    void Log(
        string eventType,
        int? userId = null,
        string? service = null,
        string? resourceType = null,
        string? resourceId = null,
        string? dataType = null,
        bool piiStripped = false,
        string? ipAddress = null);
}
