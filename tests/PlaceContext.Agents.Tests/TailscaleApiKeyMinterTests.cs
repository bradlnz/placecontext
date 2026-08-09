using System.Net;
using System.Text;
using PlaceContext.Agents.Infrastructure.Cluster;

namespace PlaceContext.Agents.Tests;

/// <summary>
/// Payload/URL construction only — no real network calls. A fake <see cref="HttpMessageHandler"/>
/// captures every outgoing request so we can assert the OAuth token exchange and the tailnet keys
/// POST body without ever hitting api.tailscale.com.
/// </summary>
public sealed class TailscaleApiKeyMinterTests
{
    [Fact]
    public async Task Exchanges_token_then_mints_key_with_expected_payload()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"access_token":"tok-abc","token_type":"Bearer"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"id":"k123","key":"tskey-auth-xyz"}""");

        var minter = new TailscaleApiKeyMinter(new FakeHttpClientFactory(handler));
        var key = await minter.MintEphemeralAgentKeyAsync("client-id-1", "client-secret-1", "tag:agent, tag:extra", default);

        Assert.Equal("tskey-auth-xyz", key);
        Assert.Equal(2, handler.Requests.Count);

        var tokenReq = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, tokenReq.Method);
        Assert.Equal("https://api.tailscale.com/api/v2/oauth/token", tokenReq.Url);
        Assert.Contains("client_id=client-id-1", tokenReq.Body);
        Assert.Contains("client_secret=client-secret-1", tokenReq.Body);

        var keysReq = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, keysReq.Method);
        Assert.Equal("https://api.tailscale.com/api/v2/tailnet/-/keys", keysReq.Url);
        Assert.Equal("Bearer", keysReq.AuthScheme);
        Assert.Equal("tok-abc", keysReq.AuthParameter);
        Assert.Contains("\"reusable\":false", keysReq.Body);
        Assert.Contains("\"ephemeral\":true", keysReq.Body);
        Assert.Contains("\"preauthorized\":true", keysReq.Body);
        Assert.Contains("\"tag:agent\"", keysReq.Body);
        Assert.Contains("\"tag:extra\"", keysReq.Body);
        Assert.Contains("\"expirySeconds\":600", keysReq.Body);
    }

    [Fact]
    public async Task Missing_credentials_returns_null_without_any_request()
    {
        var handler = new FakeHttpMessageHandler();
        var minter = new TailscaleApiKeyMinter(new FakeHttpClientFactory(handler));

        var key = await minter.MintEphemeralAgentKeyAsync("", "secret", "tag:agent", default);

        Assert.Null(key);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Failed_token_exchange_returns_null()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");

        var minter = new TailscaleApiKeyMinter(new FakeHttpClientFactory(handler));
        var key = await minter.MintEphemeralAgentKeyAsync("bad-id", "bad-secret", "tag:agent", default);

        Assert.Null(key);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Failed_key_request_returns_null()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"access_token":"tok-abc"}""");
        handler.Enqueue(HttpStatusCode.Forbidden, """{"error":"no scope"}""");

        var minter = new TailscaleApiKeyMinter(new FakeHttpClientFactory(handler));
        var key = await minter.MintEphemeralAgentKeyAsync("client-id-1", "client-secret-1", "tag:agent", default);

        Assert.Null(key);
        Assert.Equal(2, handler.Requests.Count);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────────────────────────

    private sealed record CapturedRequest(HttpMethod Method, string Url, string? Body, string? AuthScheme, string? AuthParameter);

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
        public List<CapturedRequest> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            var (status, respBody) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.InternalServerError, "{}");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(respBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
