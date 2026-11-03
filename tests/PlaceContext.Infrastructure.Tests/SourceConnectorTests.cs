using System.Net;
using System.Text.Json;
using PlaceContext.Application.DataSources;
using PlaceContext.Infrastructure.DataSources;

namespace PlaceContext.Infrastructure.Tests;

public sealed class SourceConnectorTests
{
    [Theory]
    [InlineData("mssql", "[public].[notes]")]
    [InlineData("mysql", "`public`.`notes`")]
    [InlineData("snowflake", "\"public\".\"notes\"")]
    public void Sql_connectors_quote_and_bound_selected_fields(string provider, string table)
    {
        var query = SqlSourceConnector.Select(Source(provider));
        Assert.Contains(table, query);
        Assert.Contains("1001", query);
        Assert.DoesNotContain("*", query);
        Assert.Throws<ArgumentException>(() => SqlSourceConnector.Select(Source(provider) with { Table = "notes; DROP TABLE users" }));
    }

    [Fact]
    public void Connection_validation_rejects_file_options_and_unsafe_endpoints()
    {
        Assert.Throws<ArgumentException>(() => SourceSettings.Parse("mysql", "{\"password\":\"private\",\"sslFile\":\"/etc/private\"}"));
        var settings = new Dictionary<string, string> { ["endpoint"] = "http://example.com", ["token"] = "private", ["warehouse"] = "w", ["database"] = "d" };
        Assert.DoesNotContain("private", Assert.Throws<ArgumentException>(() => SourceSettings.Parse("snowflake", JsonSerializer.Serialize(settings))).Message);
        settings["endpoint"] = "https://example.com/?token=private";
        Assert.Throws<ArgumentException>(() => SourceSettings.Parse("snowflake", JsonSerializer.Serialize(settings)));
        Assert.Throws<ArgumentException>(() => SourceSettings.Parse("feed", "{\"token\":\"short\"}"));
    }

    [Theory]
    [InlineData("snowflake")]
    [InlineData("databricks")]
    public async Task Warehouse_connectors_follow_bounded_results_without_forwarding_credentials_to_result_urls(string provider)
    {
        var responses = provider == "snowflake" ? new[]
        {
            """{"statementHandle":"query-1","resultSetMetaData":{"numRows":2,"rowType":[{"name":"id"},{"name":"body"}],"partitionInfo":[{},{}]},"data":[["1","kiwi"]]}""",
            """{"data":[["2","pear"]]}"""
        } : new[]
        {
            """{"statement_id":"query-1","status":{"state":"SUCCEEDED"},"manifest":{"total_row_count":2,"schema":{"columns":[{"name":"id"},{"name":"body"}]}},"result":{"data_array":[["1","kiwi"]],"next_chunk_index":1,"next_chunk_internal_link":"https://attacker.example/steal"}}""",
            """{"data_array":[["2","pear"]]}"""
        };
        var handler = new Responses(responses);
        using var client = new HttpClient(handler);
        var connector = new WarehouseSourceConnector(client);
        var rows = await connector.ReadAsync(Source(provider), Settings(), default);
        Assert.Equal(2, rows.Count);
        Assert.Equal("2", rows[1].Key);
        Assert.All(handler.Requests, uri => Assert.Equal("warehouse.example", uri.Host));
    }

    [Theory]
    [InlineData("snowflake", """{"statementHandle":"query-1","resultSetMetaData":{"numRows":2,"rowType":[{"name":"id"},{"name":"body"}]},"data":[["1","kiwi"]]}""")]
    [InlineData("databricks", """{"statement_id":"query-1","status":{"state":"SUCCEEDED"},"manifest":{"truncated":true}}""")]
    [InlineData("databricks", """{"statement_id":"query-1","status":{"state":"FAILED"}}""")]
    public async Task Warehouse_partial_or_failed_snapshots_are_rejected(string provider, string response)
    {
        using var client = new HttpClient(new Responses([response]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WarehouseSourceConnector(client).ReadAsync(Source(provider), Settings(), default));
    }

    [Fact]
    public async Task Lake_authentication_and_file_read_keep_content_out_of_discovery()
    {
        var handler = new Responses([
            """{"access_token":"lake-token"}""",
            """{"paths":[{"name":"folder/notes.txt","isDirectory":false}]}""",
            "kiwi"
        ], "lake-token");
        using var client = new HttpClient(handler);
        var settings = JsonSerializer.Serialize(new Dictionary<string, string> { ["endpoint"] = "https://account.dfs.core.windows.net", ["bucket"] = "files", ["prefix"] = "folder", ["tenant"] = Guid.NewGuid().ToString(), ["username"] = Guid.NewGuid().ToString(), ["password"] = "secret" });
        var rows = await new LakeSourceConnector(client).ReadAsync("adls", settings, false, default);
        Assert.Equal("folder/notes.txt", Assert.Single(rows).Key);
        Assert.Contains("kiwi", rows[0].Json);
        Assert.Equal("login.microsoftonline.com", handler.Requests[0].Host);
        Assert.Equal("account.dfs.core.windows.net", handler.Requests[2].Host);
    }

    private static DataSourceDefinition Source(string provider) => new(Guid.NewGuid(), Guid.NewGuid(), "Notes", "public", "notes", "id", ["id", "body"], [], false, Guid.NewGuid(), Provider: provider);
    private static string Settings() => JsonSerializer.Serialize(new Dictionary<string, string> { ["endpoint"] = "https://warehouse.example", ["token"] = "private", ["warehouse"] = "w", ["database"] = "d" });

    private sealed class Responses(string[] responses, string token = "private") : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "login.microsoftonline.com")
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(token, request.Headers.Authorization?.Parameter);
            }
            Requests.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses[Requests.Count - 1], System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
