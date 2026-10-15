using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using WovenBackend.Auth;
using WovenBackend.Endpoints;

namespace WovenBackend.Tests;

public class JwtRejectionContractTests
{
    private const string Key = "LocalSyntheticJwtKeyOnly012345678901234567890123456789";
    private static TokenValidationParameters Parameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
        ValidateIssuer = true,
        ValidIssuer = "WovenBackend",
        ValidateAudience = true,
        ValidAudience = "WovenFrontend",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    private static string Token(string issuer = "WovenBackend", string audience = "WovenFrontend", string algorithm = SecurityAlgorithms.HmacSha256, int minutes = 5, bool signed = true, int notBefore = -10)
    {
        var jwt = new JwtSecurityToken(issuer, audience, [new Claim("uid", "1")], DateTime.UtcNow.AddMinutes(notBefore), DateTime.UtcNow.AddMinutes(minutes),
            signed ? new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), algorithm) : null);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [Theory]
    [InlineData("WovenBackend", "WovenFrontend", -1, -10, true)]
    [InlineData("foreign-issuer", "WovenFrontend", 5, -10, true)]
    [InlineData("WovenBackend", "foreign-audience", 5, -10, true)]
    [InlineData("WovenBackend", "WovenFrontend", 5, 1, true)]
    [InlineData("WovenBackend", "WovenFrontend", 5, -10, false)]
    public void Validation_RejectsExpiredForeignPrematureAndUnsignedTokens(string issuer, string audience, int expiry, int start, bool signed)
        => Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(Token(issuer, audience, minutes: expiry, signed: signed, notBefore: start), Parameters(), out _));

    [Fact]
    public void Validation_RejectsAlternateAlgorithmEvenWithSameKeyAndValidSignature()
        => Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(Token(algorithm: SecurityAlgorithms.HmacSha384), Parameters(), out _));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public void Validation_RejectsMalformedToken(string token)
        => Assert.ThrowsAny<Exception>(() => new JwtSecurityTokenHandler().ValidateToken(token, Parameters(), out _));

    [Fact]
    public void Validation_RejectsTamperedPayload()
    {
        var parts = Token().Split('.'); parts[1] = Base64UrlEncoder.Encode("{\"uid\":\"99\",\"iss\":\"WovenBackend\",\"aud\":\"WovenFrontend\"}");
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(string.Join('.', parts), Parameters(), out _));
    }

    [Fact]
    public void SignedTokenWithoutIdentityCannotResolveApplicationUser()
    {
        var token = new JwtSecurityToken("WovenBackend", "WovenFrontend", [], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        var handler = new JwtSecurityTokenHandler(); var principal = handler.ValidateToken(handler.WriteToken(token), Parameters(), out _);
        Assert.Throws<UnauthorizedAccessException>(() => EndpointHelper.GetUserId(principal));
    }

    [Theory]
    [InlineData(null, "60")]
    [InlineData("short", "60")]
    [InlineData(Key, "bad")]
    public void TokenCreation_InvalidConfigurationFailsInsteadOfIssuingUsableToken(string? key, string expiry)
    {
        var service = new JwtTokenService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Key"] = key, ["Jwt:ExpiryMinutes"] = expiry, ["Jwt:Issuer"] = "WovenBackend", ["Jwt:Audience"] = "WovenFrontend" }).Build());
        Assert.ThrowsAny<Exception>(() => service.CreateAccessToken(1, "unit@woven.invalid"));
    }
}
