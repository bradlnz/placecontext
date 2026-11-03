using PlaceContext.Host.Auth;
namespace PlaceContext.Host.Tests;

public class OAuthResourceTests
{
    [Fact]
    public void Resource_must_match_the_issued_token_audience()
    {
        Assert.True(OAuthServer.ValidResource("https://placecontext.example/mcp", "https://placecontext.example"));
        Assert.True(OAuthServer.ValidResource("", "https://placecontext.example"));
        Assert.False(OAuthServer.ValidResource("https://attacker.example/mcp", "https://placecontext.example"));
    }

}
