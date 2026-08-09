using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Search.Infrastructure.OpenSearch;
using PlaceContext.Search.Integration;

namespace PlaceContext.Search.Tests;

public sealed class OpenSearchDataGatewayTests
{
    [Fact]
    public async Task Project_vault_connection_is_exposed_to_jobs_as_runtime_environment()
    {
        var projectId = Guid.NewGuid();
        var secrets = new StubSearchSecretProvider(new Dictionary<string, string>
        {
            [OpenSearchEnvironmentVariables.Endpoint] = "https://vault-search.test",
            [OpenSearchEnvironmentVariables.Username] = "job-user",
            [OpenSearchEnvironmentVariables.Password] = "job-password",
            [OpenSearchEnvironmentVariables.Index] = "project-*",
        });
        var resolver = new OpenSearchConnectionResolver(
            Options.Create(new OpenSearchOptions()),
            secrets);

        var env = await resolver.GetJobEnvironmentAsync(projectId);

        Assert.Equal("https://vault-search.test", env["OPENSEARCH_URL"]);
        Assert.Equal("job-user", env["OPENSEARCH_USERNAME"]);
        Assert.Equal("job-password", env["OPENSEARCH_PASSWORD"]);
        Assert.Equal("project-*", env["OPENSEARCH_INDEX"]);
    }

    [Fact]
    public async Task Search_sql_parses_columns_rows_shape()
    {
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(_ =>
                Task.FromResult(Json("""
                    {
                      "columns": [
                        {"name":"status"},
                        {"name":"count"}
                      ],
                      "rows": [
                        ["active", 12],
                        [null, 3]
                      ],
                      "total": 14
                    }
                    """)))),
            new StubConnectionResolver());

        var result = await gateway.SearchSqlAsync(Guid.NewGuid(), "SELECT status, COUNT(*) FROM logs GROUP BY 1");

        Assert.Equal(["status", "count"], result.Columns);
        Assert.False(result.Truncated);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("active", result.Rows[0][0]);
        Assert.Equal("12", result.Rows[0][1]);
        Assert.Null(result.Rows[1][0]);
        Assert.Equal("3", result.Rows[1][1]);
    }

    [Fact]
    public async Task Search_sql_parses_schema_datarows_with_row_count_and_cursor()
    {
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(_ =>
                Task.FromResult(Json("""
                    {
                      "schema": [{"name":"project_id"},{"name":"size_bytes"}],
                      "datarows": [["p1", 7], ["p2", 4]],
                      "row_count": 3,
                      "cursor": "next-page-token"
                    }
                    """)))),
            new StubConnectionResolver());

        var result = await gateway.SearchSqlAsync(Guid.NewGuid(), "SELECT project_id, size_bytes FROM projects");

        Assert.Equal(["project_id", "size_bytes"], result.Columns);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("7", result.Rows[0][1]);
    }

    [Fact]
    public async Task Search_sql_parses_rows_as_objects()
    {
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(_ =>
                Task.FromResult(Json("""
                    {
                      "columns": [{"name":"status"},{"name":"city"}],
                      "rows": [
                        {"status":"active","city":"Brisbane"},
                        {"status":"idle","city":null}
                      ]
                    }
                    """)))),
            new StubConnectionResolver());

        var result = await gateway.SearchSqlAsync(Guid.NewGuid(), "SELECT status, city FROM logs");

        Assert.Equal("active", result.Rows[0][0]);
        Assert.Equal("Brisbane", result.Rows[0][1]);
        Assert.Equal("idle", result.Rows[1][0]);
        Assert.Null(result.Rows[1][1]);
    }

    [Fact]
    public async Task Search_sql_parses_nested_response_shape()
    {
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(_ =>
                Task.FromResult(Json("""
                    {
                      "response": {
                        "schema": [
                          {"name":"project_id"},
                          {"name":"size_bytes"}
                        ],
                        "datarows": [["p1", 7], ["p2", 4]],
                        "row_count": 2,
                        "cursor": "next-page-token"
                      }
                    }
                    """)))),
            new StubConnectionResolver());

        var result = await gateway.SearchSqlAsync(Guid.NewGuid(), "SELECT project_id, size_bytes FROM projects");

        Assert.Equal(["project_id", "size_bytes"], result.Columns);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("4", result.Rows[1][1]);
    }

    [Fact]
    public async Task Search_builds_free_text_query_and_maps_hits_and_chart()
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
        Assert.False(body.RootElement.GetProperty("query")
            .GetProperty("simple_query_string").TryGetProperty("fields", out _));
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

    [Fact]
    public async Task Last_updated_uses_the_newest_document_timestamp()
    {
        string? capturedBody = null;
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(async request =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync();
                return Json("""
                    {
                      "hits": {"total":{"value":12,"relation":"eq"},"hits":[]},
                      "aggregations": {
                        "last_updated": {
                          "value": 1785556800000,
                          "value_as_string": "2026-08-01T04:00:00.000Z"
                        }
                      }
                    }
                    """);
            })),
            new StubConnectionResolver());

        var result = await gateway.GetLastUpdatedAsync(
            Guid.NewGuid(), "reports-*", ["updated_at", "created_at"]);

        Assert.Equal("updated_at", result.Field);
        Assert.Equal(DateTimeOffset.Parse("2026-08-01T04:00:00Z"), result.Value);
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(0, body.RootElement.GetProperty("size").GetInt32());
        Assert.Equal("updated_at", body.RootElement.GetProperty("aggs")
            .GetProperty("last_updated").GetProperty("max").GetProperty("field").GetString());
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

    private sealed class StubSearchSecretProvider(
        IReadOnlyDictionary<string, string> values) : ISearchSecretProvider
    {
        public Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(
            Guid projectId, IReadOnlyList<string> names, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(names
                .Where(values.ContainsKey)
                .ToDictionary(name => name, name => values[name]));
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
