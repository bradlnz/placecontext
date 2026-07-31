using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.OpenSearch;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class OpenSearchDataGatewayTests
{
    [Fact]
    public async Task Project_vault_connection_is_exposed_to_jobs_as_runtime_environment()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(dbOptions, tenant);
        var projectId = Guid.NewGuid();
        var secrets = new EfProjectSecretRepository(db);
        await secrets.AddAsync(projectId, "OPENSEARCH_URL", "https://vault-search.test", DateTimeOffset.UtcNow);
        await secrets.AddAsync(projectId, "OPENSEARCH_USERNAME", "job-user", DateTimeOffset.UtcNow);
        await secrets.AddAsync(projectId, "OPENSEARCH_PASSWORD", "job-password", DateTimeOffset.UtcNow);
        await secrets.AddAsync(projectId, "OPENSEARCH_INDEX", "project-*", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var resolver = new OpenSearchConnectionResolver(
            Options.Create(new OpenSearchOptions()),
            secrets,
            new PlaintextProtector());

        var env = await resolver.GetJobEnvironmentAsync(projectId);

        Assert.Equal("https://vault-search.test", env["OPENSEARCH_URL"]);
        Assert.Equal("job-user", env["OPENSEARCH_USERNAME"]);
        Assert.Equal("job-password", env["OPENSEARCH_PASSWORD"]);
        Assert.Equal("project-*", env["OPENSEARCH_INDEX"]);
    }

    [Fact]
    public async Task Search_builds_constrained_query_and_maps_hits_and_chart()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return Json("""
                {
                  "took": 7,
                  "hits": {
                    "total": {"value": 2, "relation": "eq"},
                    "hits": [{
                      "_index": "customers",
                      "_id": "a1",
                      "_score": 1.2,
                      "_source": {"status":"active","customer":{"city":"Brisbane"}}
                    }]
                  },
                  "aggregations": {
                    "chart": {
                      "buckets": [
                        {"key":"active","doc_count":12},
                        {"key":"inactive","doc_count":3}
                      ]
                    }
                  }
                }
                """);
        });
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(handler), new StubConnectionResolver());

        var result = await gateway.SearchAsync(new OpenSearchSearchRequest(
            Guid.NewGuid(), "customers-*", "status:active", BucketField: "status.keyword"));

        Assert.Equal(2, result.Total);
        Assert.Equal(7, result.TookMs);
        Assert.Single(result.Hits);
        Assert.Equal("Brisbane", result.Hits[0].Fields["customer.city"]);
        Assert.NotNull(result.ChartSpecJson);
        Assert.Contains("\"labels\":[\"active\",\"inactive\"]", result.ChartSpecJson);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.EndsWith("/customers-*/_search", captured.RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(25, body.RootElement.GetProperty("size").GetInt32());
        Assert.Equal("status:active", body.RootElement.GetProperty("query")
            .GetProperty("simple_query_string").GetProperty("query").GetString());
        Assert.Equal("status.keyword", body.RootElement.GetProperty("aggs")
            .GetProperty("chart").GetProperty("terms").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Field_caps_exposes_only_server_reported_capabilities()
    {
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(_ => Task.FromResult(Json("""
                {
                  "fields": {
                    "createdAt": {"date": {"type":"date","searchable":true,"aggregatable":true}},
                    "message": {"text": {"type":"text","searchable":true,"aggregatable":false}}
                  }
                }
                """)))),
            new StubConnectionResolver());

        var fields = await gateway.ListFieldsAsync(Guid.NewGuid(), "logs-*");

        Assert.Equal(2, fields.Count);
        Assert.True(fields.Single(field => field.Name == "createdAt").Aggregatable);
        Assert.False(fields.Single(field => field.Name == "message").Aggregatable);
    }

    [Theory]
    [InlineData("../_cluster")]
    [InlineData("..")]
    [InlineData(".system")]
    [InlineData("logs/_search")]
    [InlineData("logs?pretty=true")]
    public async Task Index_patterns_cannot_escape_the_search_proxy(string unsafeIndex)
    {
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(
                _ => throw new InvalidOperationException("HTTP must not be called"))),
            new StubConnectionResolver());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            gateway.SearchAsync(new OpenSearchSearchRequest(
                Guid.NewGuid(), unsafeIndex, null)));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubConnectionResolver : IOpenSearchConnectionResolver
    {
        public Task<OpenSearchConnection?> ResolveAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<OpenSearchConnection?>(new(
                "https://search.example.test", "user", "pass", "*"));

        public Task<IReadOnlyDictionary<string, string>> GetJobEnvironmentAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>());
    }

    private sealed class PlaintextProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => _send(request);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
