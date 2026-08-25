using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Tests;

public sealed class DesktopOAuthScopeTests
{
    [Theory]
    [InlineData("desktop")]
    [InlineData("mcp")]
    [InlineData("identity")]
    public void Recognizes_supported_oauth_scopes(string scope)
    {
        Assert.True(OAuthServer.IsSupportedScope(scope));
    }

    [Theory]
    [InlineData("")]
    [InlineData("coreapi.projects.read")]
    [InlineData("admin")]
    public void Rejects_unknown_oauth_scopes(string scope)
    {
        Assert.False(OAuthServer.IsSupportedScope(scope));
    }

    [Theory]
    [InlineData("desktop", "https://placecontext.example/api/desktop")]
    [InlineData("desktop mcp", "https://placecontext.example/api/desktop")]
    [InlineData("mcp", "https://placecontext.example/mcp")]
    public void Selects_resource_audience_from_granted_scope(string scope, string expected)
    {
        Assert.Equal(expected, OAuthServer.AudienceForScope("https://placecontext.example", scope));
    }
}
