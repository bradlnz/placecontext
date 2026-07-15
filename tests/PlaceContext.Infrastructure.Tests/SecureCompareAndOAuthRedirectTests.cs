using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Infrastructure.Tests;

public class SecureCompareAndOAuthRedirectTests
{
    [Fact]
    public void SecureCompare_equal_keys_match()
        => Assert.True(SecureCompare.Equals("secret-key", "secret-key"));

    [Fact]
    public void SecureCompare_different_lengths_do_not_throw_and_return_false()
        => Assert.False(SecureCompare.Equals("short", "a-much-longer-configured-key"));

    [Fact]
    public void SecureCompare_null_presented_is_false()
        => Assert.False(SecureCompare.Equals(null, "configured"));

    [Theory]
    [InlineData("https://example.com/cb", false)] // public DCR: loopback only (no remote phishing)
    [InlineData("https://evil.example/steal", false)]
    [InlineData("http://localhost:1234/cb", true)]
    [InlineData("http://127.0.0.1:8765/oauth", true)]
    [InlineData("https://127.0.0.1/cb", true)]
    [InlineData("http://evil.example/steal", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void Safe_redirect_uri_rules(string uri, bool expected)
        => Assert.Equal(expected, EfOAuthClientStore.IsSafeRedirectUri(uri));

    [Theory]
    [InlineData("http://localhost:3/x", true)]
    [InlineData("https://127.0.0.1/x", true)]
    [InlineData("https://app.example.com/cb", false)]
    public void Loopback_only_self_heal_rules(string uri, bool expected)
        => Assert.Equal(expected, EfOAuthClientStore.IsLoopbackRedirectUri(uri));
}
