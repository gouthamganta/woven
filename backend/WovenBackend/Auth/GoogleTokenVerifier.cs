using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace WovenBackend.Auth;

public record GoogleUserInfo(string Subject, string Email, string? Name, string? Picture);

public class GoogleAuthOptions
{
    public string ClientId { get; set; } = "";
}

public interface IGoogleTokenVerifier
{
    Task<GoogleUserInfo> VerifyAsync(string idToken, CancellationToken ct = default);
}

public class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private const string GoogleJwksUrl = "https://www.googleapis.com/oauth2/v3/certs";
    private static readonly HttpClient _http = new();

    private readonly GoogleAuthOptions _options;
    private JsonWebKeySet? _cachedJwks;
    private DateTime _jwksCachedAtUtc = DateTime.MinValue;

    public GoogleTokenVerifier(IOptions<GoogleAuthOptions> options)
    {
        _options = options.Value;
    }

    public async Task<GoogleUserInfo> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new SecurityTokenException("Missing idToken");

        // Quick sanity check: must look like a JWT (3 parts)
        if (idToken.Split('.').Length != 3)
            throw new SecurityTokenException("Invalid Google token format (expected JWT)");

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new SecurityTokenException("GoogleAuth:ClientId is missing in configuration");

        var jwks = await GetJwksAsync(ct);

        // IMPORTANT: prevent odd inbound claim remapping surprises
        // (we’ll still add robust fallbacks below)
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

        var handler = new JwtSecurityTokenHandler();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "accounts.google.com", "https://accounts.google.com" },

            ValidateAudience = true,
            ValidAudience = _options.ClientId,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),

            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwks.Keys
        };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(idToken, validationParams, out _);
        }
        catch (Exception ex)
        {
            throw new SecurityTokenException($"Invalid Google token: {ex.Message}", ex);
        }

        // Robust claim reads (handles mapping differences)
        var sub =
            principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(sub))
            throw new SecurityTokenException("Google token missing sub");

        var email =
            principal.FindFirstValue("email")
            ?? principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(email))
            throw new SecurityTokenException("Google token missing email");

        var name =
            principal.FindFirstValue("name")
            ?? principal.FindFirstValue(ClaimTypes.Name);

        var picture = principal.FindFirstValue("picture");

        return new GoogleUserInfo(sub, email, name, picture);
    }

    private async Task<JsonWebKeySet> GetJwksAsync(CancellationToken ct)
    {
        // cache for 24 hours
        if (_cachedJwks != null && DateTime.UtcNow - _jwksCachedAtUtc < TimeSpan.FromHours(24))
            return _cachedJwks;

        var json = await _http.GetStringAsync(GoogleJwksUrl, ct);
        _cachedJwks = new JsonWebKeySet(json);
        _jwksCachedAtUtc = DateTime.UtcNow;
        return _cachedJwks;
    }
}
