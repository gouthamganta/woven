using System.Security.Claims;

namespace WovenBackend.Endpoints;

/// <summary>
/// Shared helpers for all Minimal API endpoint groups.
///
/// GetUserId: always throws UnauthorizedAccessException (→ 401) if the JWT
/// claim is missing or invalid — never silently returns 0.
/// GlobalExceptionHandler catches UnauthorizedAccessException and returns 401.
/// </summary>
public static class EndpointHelper
{
    public static int GetUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("uid")
               ?? user.FindFirstValue("sub")
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (int.TryParse(raw, out var id) && id > 0) return id;

        throw new UnauthorizedAccessException("Valid user ID claim is required.");
    }
}
