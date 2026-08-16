using Microsoft.AspNetCore.Http;

namespace WovenBackend.Auth;

/// <summary>
/// Helper methods for managing JWT tokens in httpOnly cookies.
/// Provides secure, XSS-resistant authentication token storage.
/// </summary>
public static class CookieAuthHelper
{
    private const string AccessTokenCookie = "woven_access_token";
    private const string RefreshTokenCookie = "woven_refresh_token";

    /// <summary>
    /// Sets the access token in an httpOnly, secure, SameSite=Strict cookie.
    /// </summary>
    public static void SetAccessTokenCookie(HttpResponse response, string token, int expiryMinutes = 60)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,                    // Prevents JavaScript access (XSS protection)
            Secure = true,                      // HTTPS only
            SameSite = SameSiteMode.Strict,     // CSRF protection
            Expires = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes),
            Path = "/",
            Domain = null                       // Same domain only
        };

        response.Cookies.Append(AccessTokenCookie, token, cookieOptions);
    }

    /// <summary>
    /// Sets the refresh token in an httpOnly, secure, SameSite=Strict cookie.
    /// </summary>
    public static void SetRefreshTokenCookie(HttpResponse response, string token, int expiryDays = 30)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(expiryDays),
            Path = "/",
            Domain = null
        };

        response.Cookies.Append(RefreshTokenCookie, token, cookieOptions);
    }

    /// <summary>
    /// Reads the access token from the cookie.
    /// </summary>
    public static string? GetAccessTokenFromCookie(HttpRequest request)
    {
        return request.Cookies.TryGetValue(AccessTokenCookie, out var token) ? token : null;
    }

    /// <summary>
    /// Reads the refresh token from the cookie.
    /// </summary>
    public static string? GetRefreshTokenFromCookie(HttpRequest request)
    {
        return request.Cookies.TryGetValue(RefreshTokenCookie, out var token) ? token : null;
    }

    /// <summary>
    /// Clears authentication cookies (logout).
    /// </summary>
    public static void ClearAuthCookies(HttpResponse response)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(-1),  // Expire immediately
            Path = "/"
        };

        response.Cookies.Append(AccessTokenCookie, "", cookieOptions);
        response.Cookies.Append(RefreshTokenCookie, "", cookieOptions);
    }
}
