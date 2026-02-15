using Microsoft.Extensions.Configuration;
using WovenBackend.Auth;

namespace WovenBackend.Tests;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(string key = "ThisIsATestKeyThatMustBeAtLeast32Characters!")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "WovenBackend",
                ["Jwt:Audience"] = "WovenFrontend",
                ["Jwt:Key"] = key,
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();

        return new JwtTokenService(config);
    }

    [Fact]
    public void CreateAccessToken_ReturnsNonEmptyJwt()
    {
        var svc = CreateService();
        var token = svc.CreateAccessToken(1, "test@example.com");

        Assert.False(string.IsNullOrWhiteSpace(token));
        // JWT has 3 dot-separated parts
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void CreateAccessToken_ContainsExpectedClaims()
    {
        var svc = CreateService();
        var token = svc.CreateAccessToken(42, "user@woven.app");

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("42", jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal("user@woven.app", jwt.Claims.First(c => c.Type == "email").Value);
        Assert.Equal("42", jwt.Claims.First(c => c.Type == "uid").Value);
        Assert.Equal("WovenBackend", jwt.Issuer);
        Assert.Contains("WovenFrontend", jwt.Audiences);
    }

    [Fact]
    public void CreateAccessToken_DifferentUsers_ProduceDifferentTokens()
    {
        var svc = CreateService();
        var token1 = svc.CreateAccessToken(1, "a@test.com");
        var token2 = svc.CreateAccessToken(2, "b@test.com");

        Assert.NotEqual(token1, token2);
    }
}
