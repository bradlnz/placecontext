using System.Net;
using System.Text;
using PlaceContext.Application.Auth;
using PlaceContext.Infrastructure.Auth;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ExternalIdentitySecurityTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    public async Task Account_linking_requires_a_verified_email(string verified, bool allowed)
    {
        var responses = new Responses("""{"access_token":"test"}""",
            $$"""{"sub":"attacker","email":"owner@example.test","email_verified":{{verified}}}""");
        var provider = new HttpExternalIdentityProvider(responses);
        var request = provider.ExchangeAsync(new(new("https://identity.example.test"), "client", "https://app.example.test/auth/sso/callback", false), "code", "verifier", default);
        if (allowed) Assert.Equal("owner@example.test", (await request).Email);
        else await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => request);
        Assert.All(responses.ClientNames, name => Assert.Equal("external-identity", name));
    }

    [Fact]
    public async Task Discovery_rejects_a_different_issuer()
    {
        var provider = new HttpExternalIdentityProvider(new Responses("""
            {"issuer":"https://other.example.test","authorization_endpoint":"https://identity.example.test/authorize",
             "token_endpoint":"https://identity.example.test/token","userinfo_endpoint":"https://identity.example.test/userinfo"}
            """));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => provider.ResolveEndpointsAsync(
            new(new("https://identity.example.test"), "client", "https://app.example.test/auth/sso/callback", true), default));
    }

    private sealed class Responses(params string[] bodies) : HttpMessageHandler, IHttpClientFactory
    {
        private int _next;
        public List<string> ClientNames { get; } = [];
        public HttpClient CreateClient(string name) { ClientNames.Add(name); return new(this, false); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bodies[_next++], Encoding.UTF8, "application/json") });
    }
}
