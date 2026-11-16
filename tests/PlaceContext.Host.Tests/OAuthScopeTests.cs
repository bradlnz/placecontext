using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Tests;

public sealed class OAuthScopeTests
{
    [Theory]
    [InlineData("identity", false)]
    [InlineData("", false)]
    [InlineData("mcp-admin", false)]
    [InlineData("MCP", false)]
    [InlineData("mcp", true)]
    [InlineData("identity mcp", true)]
    public void Mcp_requires_its_own_scope(string scope, bool allowed)
    {
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("scope", scope)], "Bearer"));
        Assert.Equal(allowed, AgentAuthenticationDefaults.CanAccessMcp(user));
    }

    [Fact]
    public void Mcp_keeps_personal_tokens_but_rejects_anonymous_scope_claims()
    {
        Assert.True(AgentAuthenticationDefaults.CanAccessMcp(new(new System.Security.Claims.ClaimsIdentity(UserApiTokenAuthenticationHandler.SchemeName))));
        Assert.False(AgentAuthenticationDefaults.CanAccessMcp(new(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("scope", "mcp")]))));
    }
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
