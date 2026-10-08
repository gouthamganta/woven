using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using WovenBackend.Auth;

namespace WovenBackend.Tests;

public class JwtSecurityContractTests
{
    private const string Key = "LocalOnlySyntheticSigningKeyForJwtRegressionTests12345";

    private static JwtTokenService Service(string? expiry = null) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "WovenBackend",
            ["Jwt:Audience"] = "WovenFrontend",
            ["Jwt:Key"] = Key,
            ["Jwt:ExpiryMinutes"] = expiry
        }).Build());

    private static TokenValidationParameters Validation(string key = Key) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateIssuer = true,
        ValidIssuer = "WovenBackend",
        ValidateAudience = true,
        ValidAudience = "WovenFrontend",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    [Fact]
    public void OrdinaryToken_IsCryptographicallyValidAndHasNoAdminRole()
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(Service().CreateAccessToken(42, "qa@woven.invalid"), Validation(), out var validated);
        Assert.Equal("42", principal.FindFirst("uid")?.Value);
        Assert.DoesNotContain(principal.Claims, c => c.Type == "role");
        Assert.Equal(SecurityAlgorithms.HmacSha256, Assert.IsType<JwtSecurityToken>(validated).Header.Alg);
    }

    [Fact]
    public void AdminToken_HasExplicitRoleAndSignedIdentity()
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(Service().CreateAdminToken(7, "admin@woven.invalid"), Validation(), out _);
        Assert.Equal("admin", principal.FindFirst("role")?.Value);
        Assert.Equal("7", principal.FindFirst("uid")?.Value);
    }

    [Fact]
    public void OrdinaryToken_RejectsDifferentSigningKey()
    {
        var token = Service().CreateAccessToken(42, "qa@woven.invalid");
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(
            token, Validation("ADifferentLocalSyntheticSigningKey1234567890123456789"), out _));
    }

    [Fact]
    public void OrdinaryToken_RejectsDifferentAudience()
    {
        var parameters = Validation(); parameters.ValidAudience = "untrusted-audience";
        Assert.Throws<SecurityTokenInvalidAudienceException>(() => new JwtSecurityTokenHandler().ValidateToken(
            Service().CreateAccessToken(42, "qa@woven.invalid"), parameters, out _));
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData("1", 1)]
    [InlineData("45", 45)]
    public void OrdinaryToken_ExpiryRespectsConfiguredMinutesOrDefault(string? configured, int expectedMinutes)
    {
        var before = DateTime.UtcNow;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Service(configured).CreateAccessToken(42, "qa@woven.invalid"));
        var after = DateTime.UtcNow;
        Assert.InRange(jwt.ValidTo, before.AddMinutes(expectedMinutes).AddSeconds(-1), after.AddMinutes(expectedMinutes));
    }
}
