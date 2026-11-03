using System.Net;
using System.Text.Json;
using PlaceContext.Infrastructure.Auth;

namespace PlaceContext.Infrastructure.Tests;

public class RemoteOAuthClientResolverTests
{
    private const string Id = "https://chatgpt.com/oauth/client.json";
    private const string Redirect = "https://chatgpt.com/connector_platform_oauth_redirect";

    [Theory]
    [InlineData(Id, Redirect)]
    [InlineData("https://chatgpt.com/oauth/callback_123-abc/client.json", "https://chatgpt.com/connector/oauth/callback_123-abc")]
    public async Task Resolves_exact_published_callback_with_public_PKCE(string id, string redirect)
    {
        using var http = Client(Document(id, redirect));
        var client = await new RemoteOAuthClientResolver(http).ResolveAsync(id, redirect, default);
        Assert.NotNull(client);
        Assert.Equal(id, client.ClientId);
        Assert.Equal([redirect], client.RedirectUris);
    }

    [Theory]
    [InlineData("http://chatgpt.com/oauth/client.json")]
    [InlineData("https://chatgpt.com.attacker.example/oauth/client.json")]
    [InlineData("https://chatgpt.com@127.0.0.1/oauth/client.json")]
    [InlineData("https://127.0.0.1/oauth/client.json")]
    [InlineData("https://chatgpt.com/oauth/../client.json")]
    [InlineData("https://chatgpt.com:443/oauth/client.json")]
    [InlineData("https://chatgpt.com/oauth/client.json?url=http://localhost")]
    [InlineData("https://chatgpt.com/oauth/client.json#fragment")]
    public async Task Rejects_untrusted_URL_before_fetching(string id)
    {
        using var http = new HttpClient(new Reply(_ => throw new Exception("Must not fetch")));
        Assert.Null(await new RemoteOAuthClientResolver(http).ResolveAsync(id, Redirect, default));
    }

    [Fact]
    public async Task Fails_closed_on_wrong_identity_callback_method_and_malformed_metadata()
    {
        foreach (var json in new[] {
            Document("https://attacker.example/client.json", Redirect),
            Document(Id, "https://attacker.example/callback"),
            Document(Id, Redirect, "private_key_jwt"),
            Document(Id, Redirect).Replace("authorization_code", "client_credentials"),
            Document(Id, Redirect).Replace("\"code\"", "\"token\""),
            "[]", "not json", new string('x', 65537),
        })
        {
            using var http = Client(json);
            Assert.Null(await new RemoteOAuthClientResolver(http).ResolveAsync(Id, Redirect, default));
        }
        using var redirecting = new HttpClient(new Reply(_ => new HttpResponseMessage(HttpStatusCode.Redirect)));
        Assert.Null(await new RemoteOAuthClientResolver(redirecting).ResolveAsync(Id, Redirect, default));
    }

    private static string Document(string id, string redirect, string method = "none") => JsonSerializer.Serialize(new
    {
        client_id = id,
        redirect_uris = new[] { redirect },
        token_endpoint_auth_method = "private_key_jwt",
        token_endpoint_auth_methods_supported = new[] { method },
        grant_types = new[] { "authorization_code", "refresh_token" },
        response_types = new[] { "code" },
    });
    private static HttpClient Client(string json) => new(new Reply(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(json)
    }));
    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(reply(request));
    }
}
