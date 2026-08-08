using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PlaceContext.Search.Infrastructure.OpenSearch;

namespace PlaceContext.Search.Tests;

public sealed class OpenSearchSyncGatewayTests
{
    [Fact]
    public async Task Trigger_posts_to_the_secured_collector_endpoint()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    "{\"accepted\":true,\"status\":\"queued\",\"message\":\"Collector sync queued.\"}",
                    Encoding.UTF8,
                    "application/json"),
            });
        });
        var gateway = new OpenSearchSyncGateway(
            new StubHttpClientFactory(handler),
            Options.Create(new OpenSearchOptions
            {
                SyncEndpoint = "https://collector.test/v1/sync",
                SyncToken = "test-token",
            }));

        var result = await gateway.TriggerAsync();

        Assert.True(result.Accepted);
        Assert.Equal("queued", result.Status);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("https://collector.test/v1/sync", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Trigger_rejects_missing_configuration()
    {
        var gateway = new OpenSearchSyncGateway(
            new StubHttpClientFactory(new StubHandler(_ => throw new InvalidOperationException())),
            Options.Create(new OpenSearchOptions()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.TriggerAsync());

        Assert.Equal("Manual OpenSearch sync is not configured.", error.Message);
    }

    [Fact]
    public async Task Trigger_reports_an_already_running_collector()
    {
        var gateway = new OpenSearchSyncGateway(
            new StubHttpClientFactory(new StubHandler(_ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        "{\"accepted\":false,\"status\":\"running\",\"message\":\"Collector sync is already running.\"}",
                        Encoding.UTF8,
                        "application/json"),
                }))),
            Options.Create(new OpenSearchOptions
            {
                SyncEndpoint = "https://collector.test/v1/sync",
                SyncToken = "test-token",
            }));

        var result = await gateway.TriggerAsync();

        Assert.False(result.Accepted);
        Assert.Equal("running", result.Status);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
