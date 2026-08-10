using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Fact]
    public async Task Export_aligns_rows_to_field_caps_and_paginates_with_search_after()
    {
        var searchBodies = new List<string>();
        var handler = new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("_field_caps"))
                return Json("""
                    {
                      "fields": {
                        "@timestamp": {"date": {"type":"date","searchable":true,"aggregatable":true}},
                        "status": {"keyword": {"type":"keyword","searchable":true,"aggregatable":true}},
                        "customer.city": {"text": {"type":"text","searchable":true,"aggregatable":false}},
                        "customer": {"object": {"type":"object","searchable":false,"aggregatable":false}},
                        "nope": {"alias": {"type":"alias","searchable":true,"aggregatable":false}}
                      }
                    }
                    """);
            searchBodies.Add(await request.Content!.ReadAsStringAsync());
            if (searchBodies.Count == 1)
            {
                // A full page (500) must force a follow-up page so search_after is exercised.
                var hits = new JsonArray();
                for (var i = 0; i < 500; i++)
                {
                    hits.Add(new JsonObject
                    {
                        ["_index"] = "customers",
                        ["_id"] = i.ToString(CultureInfo.InvariantCulture),
                        ["_source"] = i == 0
                            ? new JsonObject
                            {
                                ["@timestamp"] = 1785556800000,
                                ["status"] = "active",
                                ["customer"] = new JsonObject { ["city"] = "Brisbane" },
                            }
                            : new JsonObject { ["status"] = "other" },
                        ["sort"] = new JsonArray { new JsonObject
                            { ["$numberLong"] = (i + 1).ToString(CultureInfo.InvariantCulture) } },
                    });
                }
                return Json(new JsonObject
                {
                    ["hits"] = new JsonObject
                    {
                        ["total"] = new JsonObject { ["value"] = 501, ["relation"] = "eq" },
                        ["hits"] = hits,
                    },
                }.ToJsonString());
            }
            return Json("""{ "hits": {"total":{"value":501,"relation":"eq"},"hits":[]} }""");
        });
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(handler), new StubConnectionResolver());

        var result = await gateway.ExportIndexAsync(Guid.NewGuid(), "customers-*");

        // object/alias parents are skipped; leaves are sorted by field name.
        Assert.Equal(3, result.Fields.Count);
        Assert.Equal("@timestamp", result.Fields[0].Name);
        Assert.Equal("timestamptz", result.Fields[0].PostgresType);
        Assert.Equal("customer.city", result.Fields[1].Name);
        Assert.Equal("text", result.Fields[1].PostgresType);
        Assert.Equal("status", result.Fields[2].Name);
        Assert.False(result.Truncated);
        Assert.Equal(500, result.Rows.Count);
        Assert.Equal("2026-08-01T04:00:00Z", result.Rows[0][0]); // epoch_millis normalised to ISO
        Assert.Equal("Brisbane", result.Rows[0][1]);
        Assert.Equal("active", result.Rows[0][2]);
        Assert.Equal("other", result.Rows[1][2]);
        Assert.Null(result.Rows[1][0]);

        // First search page carries no cursor and sorts on _doc; the second page is keyed by the
        // previous page's last hit sort token (the $numberLong shape is round-tripped verbatim).
        using var first = JsonDocument.Parse(searchBodies[0]);
        Assert.Equal(500, first.RootElement.GetProperty("size").GetInt32());
        Assert.False(first.RootElement.TryGetProperty("search_after", out _));
        Assert.Equal("asc", first.RootElement.GetProperty("sort")[0].GetProperty("_doc").GetString());
        using var second = JsonDocument.Parse(searchBodies[1]);
        Assert.Equal("500", second.RootElement.GetProperty("search_after")[0]
            .GetProperty("$numberLong").GetString());
    }

    [Fact]
    public async Task Export_maps_open_search_types_onto_the_postgres_allow_list()
    {
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(async request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("_field_caps"))
                    return Json("""
                        {
                          "fields": {
                            "count": {"long": {"type":"long"}},
                            "price": {"double": {"type":"double"}},
                            "active": {"boolean": {"type":"boolean"}},
                            "when": {"date": {"type":"date"}},
                            "label": {"match_only_text": {"type":"match_only_text"}},
                            "parent": {"nested": {"type":"nested"}},
                            "blob": {"flattened": {"type":"flattened"}}
                          }
                        }
                        """);
                return Json("""{ "hits": {"total":{"value":0,"relation":"eq"},"hits":[]} }""");
            })),
            new StubConnectionResolver());

        var result = await gateway.ExportIndexAsync(Guid.NewGuid(), "customers-*");

        var mapped = result.Fields.ToDictionary(field => field.Name, field => field.PostgresType);
        Assert.Equal("bigint", mapped["count"]);
        Assert.Equal("numeric", mapped["price"]);
        Assert.Equal("boolean", mapped["active"]);
        Assert.Equal("timestamptz", mapped["when"]);
        Assert.Equal("text", mapped["label"]);
        Assert.False(mapped.ContainsKey("parent"));
        Assert.False(mapped.ContainsKey("blob"));
    }

    [Fact]
    public async Task Export_flags_truncation_when_more_documents_follow_the_cap()
    {
        var searchCalls = 0;
        var gateway = new OpenSearchDataGateway(
            new StubHttpClientFactory(new StubHandler(async request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("_field_caps"))
                    return Json("""
                        { "fields": { "message": {"text": {"type":"text"}} } }
                        """);
                searchCalls++;
                return Json($$"""
                    {
                      "hits": {"total":{"value":2,"relation":"eq"},"hits":[{
                        "_index": "logs", "_id": "{{searchCalls}}",
                        "_source": {"message": "m"},
                        "sort": [{{searchCalls}}]
                      }]}
                    }
                    """);
            })),
            new StubConnectionResolver());

        // Cap of 1 row: the first page fills the cap, and the probe (a further search) finds more.
        var result = await gateway.ExportIndexAsync(Guid.NewGuid(), "logs-*", maxRows: 1);

        Assert.Single(result.Rows);
        Assert.True(result.Truncated);
        Assert.Equal(2, searchCalls); // page + truncation probe
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
