using Microsoft.Extensions.Configuration;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Tests;

public class TrustedOAuthClientTests
{
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:OAuth:TrustedClients:ReportsPortal:ClientId"] = "reports-portal",
            ["PlaceContext:OAuth:TrustedClients:ReportsPortal:RedirectUri"] = "https://reports.example/auth/callback",
            ["PlaceContext:OAuth:TrustedClients:ReportsPortal:Name"] = "Reports portal",
            ["PlaceContext:OAuth:TrustedClients:Crm:ClientId"] = "example-crm",
            ["PlaceContext:OAuth:TrustedClients:Crm:RedirectUri"] = "https://crm.example/oauth/mcp/callback",
            ["PlaceContext:OAuth:TrustedClients:Crm:Name"] = "Example CRM",
        })
        .Build();

    [Theory]
    [InlineData("reports-portal", "https://reports.example/auth/callback", "Reports portal")]
    [InlineData("example-crm", "https://crm.example/oauth/mcp/callback", "Example CRM")]
    public void Resolves_each_configured_trusted_web_client(string clientId, string redirectUri, string name)
    {
        var client = OAuthServer.TrustedWebClient(_configuration, clientId, redirectUri);

        Assert.NotNull(client);
        Assert.Equal(clientId, client.ClientId);
        Assert.Equal(new[] { redirectUri }, client.RedirectUris);
        Assert.Equal(name, client.Name);
    }

    [Theory]
    [InlineData("example-crm", "https://attacker.example/callback")]
    [InlineData("unknown-client", "https://crm.example/oauth/mcp/callback")]
    public void Rejects_unknown_client_and_redirect_combinations(string clientId, string redirectUri)
    {
        Assert.Null(OAuthServer.TrustedWebClient(_configuration, clientId, redirectUri));
    }
}
