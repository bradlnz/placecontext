using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Infrastructure.DataSources;

internal sealed class WarehouseSourceConnector(HttpClient client)
{
    public async Task<IReadOnlyList<SourceDataset>> DiscoverAsync(string provider, string settings, CancellationToken ct)
    {
        var sql = "SELECT table_schema,table_name,column_name,0 AS is_key FROM information_schema.columns WHERE table_schema <> 'INFORMATION_SCHEMA' ORDER BY table_schema,table_name,ordinal_position LIMIT 8001";
        return SqlSourceConnector.Datasets(await QueryAsync(provider, settings, sql, 8001, ct));
    }

    public async Task<IReadOnlyList<SourceRecord>> ReadAsync(DataSourceDefinition source, string settings, CancellationToken ct)
        => SqlSourceConnector.Records(await QueryAsync(source.Provider, settings,
            source.Provider == "databricks" ? SqlSourceConnector.Select(source).Replace('"', '`') : SqlSourceConnector.Select(source),
            DataSourceRules.MaxRecords + 1, ct), source.KeyColumn);

    private async Task<IReadOnlyList<Dictionary<string, object?>>> QueryAsync(string provider, string settings, string sql, int max, CancellationToken ct)
    {
        var values = SourceSettings.Parse(provider, settings);
        var snowflake = provider == "snowflake";
        var path = snowflake ? "/api/v2/statements" : "/api/2.0/sql/statements";
        var payload = snowflake
            ? new JsonObject { ["statement"] = sql, ["database"] = values["database"], ["warehouse"] = values["warehouse"], ["timeout"] = 60 }
            : new JsonObject { ["statement"] = sql, ["catalog"] = values["database"], ["warehouse_id"] = values["warehouse"], ["wait_timeout"] = "10s", ["disposition"] = "INLINE", ["format"] = "JSON_ARRAY" };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        async Task<JsonObject> Send(HttpMethod method, string relative, JsonObject? body = null)
        {
            using var request = new HttpRequestMessage(method, values["endpoint"].TrimEnd('/') + relative);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", values["token"]);
            request.Headers.UserAgent.ParseAdd("PlaceContext/1.0");
            if (snowflake)
            {
                request.Headers.Add("X-Snowflake-Authorization-Token-Type", "OAUTH");
            }
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
            using var response = await client.SendAsync(request, deadline.Token);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonObject>(deadline.Token)) ?? throw new InvalidOperationException("Empty warehouse response.");
        }
        var response = await Send(HttpMethod.Post, path, payload);
        var id = Uri.EscapeDataString((response[snowflake ? "statementHandle" : "statement_id"]?.GetValue<string>()) ?? throw new InvalidOperationException("Missing statement ID."));
        for (var attempt = 0; attempt < 40 && (snowflake ? response["resultSetMetaData"] is null
            : response["status"]?["state"]?.GetValue<string>() is "PENDING" or "RUNNING"); attempt++)
        {
            await Task.Delay(1000, deadline.Token);
            response = await Send(HttpMethod.Get, path + "/" + id);
        }
        if (!snowflake && response["status"]?["state"]?.GetValue<string>() != "SUCCEEDED")
        {
            throw new InvalidOperationException("Warehouse query did not succeed.");
        }
        var metadata = response[snowflake ? "resultSetMetaData" : "manifest"] ?? throw new InvalidOperationException("Missing query metadata.");
        if (metadata["truncated"]?.GetValue<bool>() == true || (metadata[snowflake ? "numRows" : "total_row_count"]?.GetValue<int>() ?? 0) > max)
        {
            throw new InvalidOperationException("Warehouse result exceeds the source limits. Existing data was retained.");
        }
        var columnNodes = snowflake ? metadata["rowType"] : metadata["schema"]?["columns"];
        var columns = columnNodes!.AsArray().Select(column => column!["name"]!.GetValue<string>()).ToArray();
        var rows = new List<Dictionary<string, object?>>();
        void Add(JsonNode? data)
        {
            foreach (var row in data?.AsArray() ?? [])
            {
                if (rows.Count >= max || row!.AsArray().Count != columns.Length)
                {
                    throw new InvalidOperationException("Invalid warehouse result size.");
                }
                rows.Add(columns.Select((name, index) => (name, value: (object?)row[index]?.ToString())).ToDictionary(pair => pair.name, pair => pair.value));
            }
        }
        Add(snowflake ? response["data"] : response["result"]?["data_array"]);
        if (snowflake)
        {
            var partitions = metadata["partitionInfo"]?.AsArray().Count ?? 1;
            if (partitions > 100)
            {
                throw new InvalidOperationException("Too many result partitions.");
            }
            for (var partition = 1; partition < partitions; partition++)
            {
                Add((await Send(HttpMethod.Get, path + "/" + id + "?partition=" + partition))["data"]);
            }
        }
        if (!snowflake)
        {
            var next = response["result"]?["next_chunk_index"]?.GetValue<int>();
            var seen = new HashSet<int>();
            while (next is { } index)
            {
                if (!seen.Add(index) || seen.Count > 100 || index < 0)
                {
                    throw new InvalidOperationException("Invalid warehouse pagination.");
                }
                var chunk = await Send(HttpMethod.Get, path + "/" + id + "/result/chunks/" + index);
                Add(chunk["data_array"]);
                next = chunk["next_chunk_index"]?.GetValue<int>();
            }
        }
        if (rows.Count != metadata[snowflake ? "numRows" : "total_row_count"]!.GetValue<int>())
        {
            throw new InvalidOperationException("Incomplete warehouse snapshot. Existing data was retained.");
        }
        return rows;
    }
}
