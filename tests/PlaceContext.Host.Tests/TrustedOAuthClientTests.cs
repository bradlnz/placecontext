using Microsoft.Extensions.Configuration;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Tests;

public class TrustedOAuthClientTests
{
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:OAuth:TrustedClients:AdminPortal:ClientId"] = "ossen-reports-admin",
            ["PlaceContext:OAuth:TrustedClients:AdminPortal:RedirectUri"] = "https://reports.example/auth/callback",
            ["PlaceContext:OAuth:TrustedClients:AdminPortal:Name"] = "Reports admin",
            ["PlaceContext:OAuth:TrustedClients:OssenCrm:ClientId"] = "ossen-crm",
            ["PlaceContext:OAuth:TrustedClients:OssenCrm:RedirectUri"] = "https://crm.example/oauth/mcp/callback",
            ["PlaceContext:OAuth:TrustedClients:OssenCrm:Name"] = "Ossen CRM",
        })
        .Build();

    [Theory]
    [InlineData("ossen-reports-admin", "https://reports.example/auth/callback", "Reports admin")]
    [InlineData("ossen-crm", "https://crm.example/oauth/mcp/callback", "Ossen CRM")]
    public void Resolves_each_configured_trusted_web_client(string clientId, string redirectUri, string name)
    {
        var client = OAuthServer.TrustedWebClient(_configuration, clientId, redirectUri);

        Assert.NotNull(client);
        Assert.Equal(clientId, client.ClientId);
        Assert.Equal(new[] { redirectUri }, client.RedirectUris);
        Assert.Equal(name, client.Name);
    }

    [Theory]
    [InlineData("ossen-crm", "https://attacker.example/callback")]
    [InlineData("unknown-client", "https://crm.example/oauth/mcp/callback")]
    public void Rejects_unknown_client_and_redirect_combinations(string clientId, string redirectUri)
    {
        Assert.Null(OAuthServer.TrustedWebClient(_configuration, clientId, redirectUri));
    }
}
