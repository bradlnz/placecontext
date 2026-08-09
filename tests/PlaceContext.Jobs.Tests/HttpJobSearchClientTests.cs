using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PlaceContext.Jobs.Infrastructure.Integration;

namespace PlaceContext.Jobs.Tests;

public sealed class HttpJobSearchClientTests
{
    [Fact]
    public async Task Sends_authenticated_caller_local_payload_to_Search()
    {
        var handler = new CapturingHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlaceContext:Jobs:Search:BaseAddress"] = "https://search.internal/base",
                ["PlaceContext:Api:Key"] = "service-key",
            })
            .Build();
        var client = new HttpJobSearchClient(
            new StubHttpClientFactory(new HttpClient(handler)),
            configuration,
            NullLogger<HttpJobSearchClient>.Instance);
        var runId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        await client.IndexRunOutputAsync(runId, jobId, projectId, "organized output");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://search.internal/base/api/search/internal/run-outputs",
            handler.RequestUri?.ToString());
        Assert.Equal("service-key", handler.ApiKey);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(runId, body.RootElement.GetProperty("runId").GetGuid());
        Assert.Equal(jobId, body.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal(projectId, body.RootElement.GetProperty("projectId").GetGuid());
        Assert.Equal("organized output", body.RootElement.GetProperty("text").GetString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("X-Api-Key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
