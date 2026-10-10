using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace WovenBackend.Tests;

// A real loopback HTTP listener executes auth/authorization and the registered
// chat routes. This intentionally excludes Program startup, migrations,
// cookie/CSRF/CORS wiring, workers and real relational/provider semantics.
public class AuthenticatedChatHttpContractTests
{
    private static string Token(int actor = 1, string issuer = "WovenBackend", string audience = "WovenFrontend", string key = ChatHarness.SigningKey, int expiry = 5, bool signed = true, bool admin = false)
    {
        var claims = new List<Claim> { new("uid", actor.ToString()), new("sub", actor.ToString()) };
        if (admin) claims.Add(new("role", "admin"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience, claims, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(expiry),
            signed ? new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256) : null));
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("unsigned")]
    [InlineData("malformed")]
    public async Task Authentication_DeniesUntrustedHttpRequestBeforeExecutingChatMutation(string invalid)
    {
        await using var h = ChatHarness.Create(jwt: true); await h.Seed(); using var client = await h.StartHttp();
        var token = invalid switch
        {
            "anonymous" => null,
            "expired" => Token(expiry: -1),
            "issuer" => Token(issuer: "foreign"),
            "audience" => Token(audience: "foreign"),
            "signature" => Token(key: "DifferentUnitSigningKey012345678901234567890123456789"),
            "unsigned" => Token(signed: false),
            _ => "not-a-token"
        };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.PostAsJsonAsync($"/chats/{h.ThreadId}/messages", new { body = "Must not persist" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Empty(await h.Db.ChatMessages.ToListAsync()); Assert.Empty(h.Cache.RateLimitChecks); Assert.Empty(h.Notify.Calls); Assert.Empty(h.Signals.Calls);
    }

    [Theory]
    [InlineData(1, false, 200)]
    [InlineData(2, false, 200)]
    [InlineData(3, false, 403)]
    [InlineData(3, true, 403)]
    public async Task Authorization_ValidTokenStillRequiresPairMembershipEvenForAdmin(int actor, bool admin, int status)
    {
        await using var h = ChatHarness.Create(jwt: true); var match = await h.Seed(); using var client = await h.StartHttp();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(actor, admin: admin));
        using var response = await client.PostAsJsonAsync($"/chats/{h.ThreadId}/messages", new { body = "Unit greeting" });
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 200) { Assert.Equal(actor, (await h.Db.ChatMessages.SingleAsync()).SenderUserId); Assert.Equal(actor == 1 ? 2 : 1, Assert.Single(h.Notify.Calls).Args[0]); }
        else { Assert.Empty(await h.Db.ChatMessages.ToListAsync()); Assert.Empty(h.Notify.Calls); Assert.Null(match.BothMessagedAt); }
    }

    [Fact]
    public async Task Authentication_AnonymousReadCannotObtainOpeningNotes()
    {
        await using var h = ChatHarness.Create(jwt: true); await h.Seed(); using var client = await h.StartHttp();
        using var response = await client.GetAsync($"/chats/{h.ThreadId}"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }
}
