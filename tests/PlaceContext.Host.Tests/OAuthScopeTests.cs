using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Tests;

public sealed class OAuthScopeTests
{
    [Theory]
    [InlineData("mcp")]
    [InlineData("identity")]
    public void Recognizes_supported_oauth_scopes(string scope)
    {
        Assert.True(OAuthServer.IsSupportedScope(scope));
    }

    [Theory]
    [InlineData("")]
    [InlineData("desktop")]
    [InlineData("coreapi.projects.read")]
    [InlineData("admin")]
    public void Rejects_unknown_oauth_scopes(string scope)
    {
        Assert.False(OAuthServer.IsSupportedScope(scope));
    }

    [Theory]
    [InlineData("mcp")]
    [InlineData("mcp identity")]
    public void Selects_mcp_resource_audience(string scope)
    {
        Assert.Equal("https://placecontext.example/mcp",
            OAuthServer.AudienceForScope("https://placecontext.example", scope));
    }
}
