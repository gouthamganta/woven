using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using WovenBackend.Auth;

namespace WovenBackend.Tests;

public class CookieContractTests
{
    private static IList<SetCookieHeaderValue> Cookies(HttpContext context) => SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.Select(value => value!).ToArray());

    [Theory]
    [InlineData(false, "woven_access_token", 60)]
    [InlineData(true, "woven_refresh_token", 43200)]
    public void AuthCookies_HaveSecureFlagsAndExpectedDefaultLifetime(bool refresh, string expectedName, int minutes)
    {
        var context = new DefaultHttpContext(); var before = DateTimeOffset.UtcNow;
        if (refresh) CookieAuthHelper.SetRefreshTokenCookie(context.Response, "synthetic-token");
        else CookieAuthHelper.SetAccessTokenCookie(context.Response, "synthetic-token");
        var cookie = Assert.Single(Cookies(context));
        Assert.Equal(expectedName, cookie.Name.ToString()); Assert.True(cookie.HttpOnly); Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString()); Assert.False(cookie.Domain.HasValue);
        Assert.InRange(cookie.Expires!.Value, before.AddMinutes(minutes).AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(minutes));
    }

    [Fact]
    public void Logout_ExpiresBothCookiesWithSecureFlags()
    {
        var context = new DefaultHttpContext(); CookieAuthHelper.ClearAuthCookies(context.Response);
        var cookies = Cookies(context); Assert.Equal(2, cookies.Count);
        Assert.Equal(new[] { "woven_access_token", "woven_refresh_token" }, cookies.Select(c => c.Name.ToString()).Order().ToArray());
        Assert.All(cookies, c => { Assert.True(c.Secure); Assert.True(c.HttpOnly); Assert.Equal("/", c.Path.ToString()); Assert.True(c.Expires < DateTimeOffset.UtcNow); Assert.Equal(string.Empty, c.Value.ToString()); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCookie_ReturnsNoToken(bool refresh)
    {
        var request = new DefaultHttpContext().Request;
        Assert.Null(refresh ? CookieAuthHelper.GetRefreshTokenFromCookie(request) : CookieAuthHelper.GetAccessTokenFromCookie(request));
    }

    [Theory]
    [InlineData(false, "woven_access_token")]
    [InlineData(true, "woven_refresh_token")]
    public void ReadsOnlyTheNamedCookie(bool refresh, string name)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.Cookie = $"unrelated=wrong; {name}=synthetic-token";
        Assert.Equal("synthetic-token", refresh ? CookieAuthHelper.GetRefreshTokenFromCookie(request) : CookieAuthHelper.GetAccessTokenFromCookie(request));
    }
}
