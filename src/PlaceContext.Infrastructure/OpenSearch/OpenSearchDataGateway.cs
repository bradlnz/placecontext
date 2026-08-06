using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.OpenSearch;

public sealed class OpenSearchDataGateway : IOpenSearchDataGateway
{
    private const int MaxPageSize = 100;
    private const int MaxSqlRows = 500;
    private const int MaxSqlLength = 16000;
    private const int BulkChunkSize = 1000;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOpenSearchConnectionResolver _connections;

    public OpenSearchDataGateway(
        IHttpClientFactory httpFactory, IOpenSearchConnectionResolver connections)
        => (_httpFactory, _connections) = (httpFactory, connections);

    public async Task<IReadOnlyList<OpenSearchIndexView>> ListIndicesAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var connection = await RequiredConnectionAsync(projectId, ct);
        var pattern = SafeIndex(connection.DefaultIndexPattern);
        using var response = await SendAsync(connection, HttpMethod.Get,
            $"/_cat/indices/{pattern}?format=json&h=index,docs.count,store.size", null, ct);
        using var document = await ReadSuccessAsync(response, ct);
        return document.RootElement.EnumerateArray()
            .Select(item => new OpenSearchIndexView(
                item.TryGetProperty("index", out var name) ? name.GetString() ?? "" : "",
                item.TryGetProperty("docs.count", out var count)
                && long.TryParse(count.GetString(), out var parsed) ? parsed : 0,
                item.TryGetProperty("store.size", out var size) ? size.GetString() : null))
            .Where(index => !string.IsNullOrWhiteSpace(index.Name)
                            && !index.Name.StartsWith('.'))
            .OrderBy(index => index.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<OpenSearchFieldView>> ListFieldsAsync(
        Guid projectId, string indexPattern, CancellationToken ct = default)
    {
        var connection = await RequiredConnectionAsync(projectId, ct);
        using var response = await SendAsync(connection, HttpMethod.Get,
            $"/{SafeIndex(indexPattern)}/_field_caps?fields=*", null, ct);
        using var document = await ReadSuccessAsync(response, ct);
        if (!document.RootElement.TryGetProperty("fields", out var fields)
            || fields.ValueKind != JsonValueKind.Object)
            return Array.Empty<OpenSearchFieldView>();

        var result = new List<OpenSearchFieldView>();
        foreach (var field in fields.EnumerateObject())
        {
            if (field.Name.StartsWith('_') || field.Value.ValueKind != JsonValueKind.Object) continue;
            var type = field.Value.EnumerateObject().FirstOrDefault();
            if (type.Value.ValueKind != JsonValueKind.Object) continue;
            result.Add(new OpenSearchFieldView(
                field.Name,
                type.Name,
                Bool(type.Value, "searchable"),
                Bool(type.Value, "aggregatable")));
        }
        return result.OrderBy(field => field.Name).ToList();
    }

    public async Task<OpenSearchLastUpdatedView> GetLastUpdatedAsync(
        Guid projectId,
        string indexPattern,
        IReadOnlyList<string> candidateFields,
        CancellationToken ct = default)
    {
        var field = candidateFields
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(SafeField)
            .FirstOrDefault();
        if (field is null) return new OpenSearchLastUpdatedView(null, null);

        var connection = await RequiredConnectionAsync(projectId, ct);
        var body = new JsonObject
        {
            ["size"] = 0,
            ["aggs"] = new JsonObject
            {
                ["last_updated"] = new JsonObject
                {
                    ["max"] = new JsonObject
                    {
                        ["field"] = field,
                        ["format"] = "strict_date_time",
                    },
                },
            },
        };
        using var response = await SendAsync(connection, HttpMethod.Post,
            $"/{SafeIndex(indexPattern)}/_search", body.ToJsonString(), ct);
        using var document = await ReadSuccessAsync(response, ct);
        var aggregation = document.RootElement.GetProperty("aggregations")
            .GetProperty("last_updated");
        if (aggregation.TryGetProperty("value_as_string", out var value)
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
            return new OpenSearchLastUpdatedView(parsed, field);
        return new OpenSearchLastUpdatedView(null, field);
    }

    public async Task<OpenSearchSearchView> SearchAsync(
        OpenSearchSearchRequest request, CancellationToken ct = default)
    {
        var connection = await RequiredConnectionAsync(request.ProjectId, ct);
        var body = BuildSearchBody(request);
        using var response = await SendAsync(connection, HttpMethod.Post,
            $"/{SafeIndex(request.IndexPattern)}/_search",
            body.ToJsonString(), ct);
        using var document = await ReadSuccessAsync(response, ct);
        var root = document.RootElement;
        var took = root.TryGetProperty("took", out var tookElement) ? tookElement.GetInt32() : 0;
        var hitsRoot = root.GetProperty("hits");
        var total = ParseTotal(hitsRoot.GetProperty("total"));
        var hits = hitsRoot.GetProperty("hits").EnumerateArray()
            .Select(ParseHit)
            .ToList();
        var chart = ParseChart(root, request);
        return new OpenSearchSearchView(total, took, hits, chart?.ToJson());
    }

    public async Task CreateIndexAsync(
        Guid projectId, string indexName, IReadOnlyList<OpenSearchMappingField> mappingFields,
        CancellationToken ct = default)
    {
        var connection = await RequiredConnectionAsync(projectId, ct);
        var safeIndex = SafeIndex(indexName);
        var properties = new JsonObject();
        foreach (var field in mappingFields)
            properties[field.Name] = new JsonObject { ["type"] = field.OpenSearchType };
        var body = new JsonObject
        {
            ["settings"] = new JsonObject { ["number_of_shards"] = 1 },
            ["mappings"] = new JsonObject { ["properties"] = properties },
        };
        using var response = await SendAsync(connection, HttpMethod.Put,
            $"/{safeIndex}", body.ToJsonString(), ct);
        await ReadSuccessAsync(response, ct);
    }

    public async Task<int> IndexBulkAsync(
        Guid projectId, string indexName, IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default,
        IReadOnlyList<string>? jsonColumnNames = null)
    {
        var connection = await RequiredConnectionAsync(projectId, ct);
        var safeIndex = SafeIndex(indexName);
        var jsonColumns = jsonColumnNames is { Count: > 0 }
            ? new HashSet<string>(jsonColumnNames, StringComparer.Ordinal)
            : null;
        var indexed = 0;
        foreach (var chunk in Chunks(rows, BulkChunkSize))
        {
            var body = new StringBuilder();
            foreach (var row in chunk)
            {
                body.Append("{\"index\":{\"_index\":\"")
                    .Append(safeIndex)
                    .Append("\"}}\n")
                    .Append(RowJson(columnNames, row, jsonColumns))
                    .Append('\n');
            }
            using var response = await SendAsync(connection, HttpMethod.Post,
                "/_bulk", body.ToString(), ct, "application/x-ndjson");
            using var document = await ReadSuccessAsync(response, ct);
            var failures = BulkFailures(document.RootElement);
            if (failures.Count > 0)
                throw new InvalidOperationException(
                    $"OpenSearch rejected {failures.Count:N0} document(s) while indexing into '{indexName}'"
                    + (failures.FirstReason is null ? "." : $" — {failures.FirstReason}"));
            indexed += chunk.Count;
        }
        return indexed;
    }

    public async Task DeleteIndexAsync(
        Guid projectId, string indexName, CancellationToken ct = default)
    {
        var connection = await RequiredConnectionAsync(projectId, ct);
        var safeIndex = SafeIndex(indexName);
        using var response = await SendAsync(connection, HttpMethod.Delete,
            $"/{safeIndex}", null, ct);
        if ((int)response.StatusCode == 404) return; // already gone
        await ReadSuccessAsync(response, ct);
    }

    private static IEnumerable<IReadOnlyList<IReadOnlyList<string?>>> Chunks(
        IReadOnlyList<IReadOnlyList<string?>> rows, int size)
    {
        for (var i = 0; i < rows.Count; i += size)
            yield return rows.Skip(i).Take(size).ToList();
    }

    /// <summary>One row as a JSON object. Values are JSON strings (null stays null) unless the column
    /// is in <paramref name="jsonColumns"/> — then the raw text is emitted so <c>object</c>-mapped
    /// jsonb values index as real JSON.</summary>
    private static string RowJson(IReadOnlyList<string> columns, IReadOnlyList<string?> row,
        HashSet<string>? jsonColumns)
    {
        var sb = new StringBuilder("{");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonSerializer.Serialize(columns[i])).Append(':');
            var value = row[i];
            if (value is null)
                sb.Append("null");
            else if (jsonColumns is not null && jsonColumns.Contains(columns[i]))
                sb.Append(value);
            else
                sb.Append(JsonSerializer.Serialize(value));
        }
        return sb.Append('}').ToString();
    }

    /// <summary>Count of failed bulk items plus the first failure reason, for a helpful error.</summary>
    private static (int Count, string? FirstReason) BulkFailures(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("items", out var items))
            return (0, null);
        var count = 0;
        string? firstReason = null;
        foreach (var item in items.EnumerateArray())
        {
            var action = item.EnumerateObject().FirstOrDefault();
            if (action.Value.ValueKind != JsonValueKind.Object) continue;
            var status = action.Value.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32() : 0;
            if (status >= 400)
            {
                count++;
                firstReason ??= action.Value.TryGetProperty("error", out var e)
                    && e.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString() : "index failed";
            }
        }
        return (count, firstReason);
    }

    public async Task<ProjectQueryResult> SearchSqlAsync(
        Guid projectId, string sql, CancellationToken ct = default)
    {
        var trimmed = (sql ?? "").Trim();
        if (trimmed.Length is 0 or > MaxSqlLength)
            throw new ArgumentException("Write a query up to 16,000 characters.");
        if (!IsReadOnlySql(trimmed))
            throw new ArgumentException("OpenSearch SQL here is read-only — start with SELECT.");

        var connection = await RequiredConnectionAsync(projectId, ct);
        var body = new JsonObject
        {
            ["query"] = trimmed,
            ["fetch_size"] = MaxSqlRows,
        };
        using var response = await SendAsync(connection, HttpMethod.Post,
            "/_plugins/_sql", body.ToJsonString(), ct);
        using var document = await ReadSuccessAsync(response, ct);
        return ParseSqlResult(document.RootElement);
    }

    private static bool IsReadOnlySql(string sql)
    {
        var first = sql.Split(new[] { ' ', '\t', '\r', '\n', '(', ';' },
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        return first.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
            || first.Equals("SHOW", StringComparison.OrdinalIgnoreCase)
            || first.Equals("DESCRIBE", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectQueryResult ParseSqlResult(JsonElement root)
    {
        if (!TryGetSqlPayload(root, out var payload))
            throw new InvalidOperationException(
                "OpenSearch SQL returned an unexpected response shape.");

        if (TryGetSqlError(root, out var errorMessage))
            throw new InvalidOperationException($"OpenSearch SQL error: {errorMessage}");

        if (!TryGetSqlColumns(payload, out var columns)
            || !TryGetSqlRows(payload, columns, out var rows))
            throw new InvalidOperationException(
                "OpenSearch SQL returned an unexpected response shape.");

        var total = GetSqlTotal(payload, rows.Count);
        var hasMore = (payload.TryGetProperty("cursor", out var cursor)
                           && cursor.ValueKind == JsonValueKind.String
                           && !string.IsNullOrWhiteSpace(cursor.GetString()))
            || total > rows.Count;
        return new ProjectQueryResult(columns, rows, 0, hasMore);
    }

    private static bool TryGetSqlPayload(JsonElement root, out JsonElement payload)
    {
        if (TryGetObjectProperty(root, "response", out payload))
            return true;

        if (TryGetObjectProperty(root, "result", out payload))
            return true;

        payload = root;
        return true;
    }

    private static bool TryGetSqlError(JsonElement root, out string message)
    {
        message = "";
        if (!root.TryGetProperty("error", out var error))
            return false;

        if (error.ValueKind == JsonValueKind.String)
        {
            message = error.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(message);
        }

        if (error.ValueKind != JsonValueKind.Object)
            return false;

        if (error.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            message = reason.GetString() ?? "";
        if (error.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            message = (string.IsNullOrWhiteSpace(message) ? "" : $"{message} — ") + type.GetString();
        if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.String)
            message = (string.IsNullOrWhiteSpace(message) ? "" : $"{message} — ") + details.GetString();

        return !string.IsNullOrWhiteSpace(message);
    }

    private static bool TryGetSqlColumns(JsonElement root, out IReadOnlyList<string> columns)
    {
        if (TryGetArrayProperty(root, "columns", out var columnsJson))
        {
            columns = ColumnNames(columnsJson);
            return columns.Count > 0;
        }

        if (TryGetArrayProperty(root, "schema", out var schemaJson))
        {
            columns = ColumnNames(schemaJson);
            return columns.Count > 0;
        }

        if (TryGetArrayProperty(root, "column", out var columnJson))
        {
            columns = ColumnNames(columnJson);
            return columns.Count > 0;
        }

        if (TryGetArrayProperty(root, "column_names", out var columnNamesJson))
        {
            columns = columnNamesJson
                .EnumerateArray()
                .Select(GetString)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            return columns.Count > 0;
        }

        if (TryGetObjectProperty(root, "result", out var resultJson)
            && TryGetSqlColumns(resultJson, out columns))
            return columns.Count > 0;

        columns = Array.Empty<string>();
        return false;
    }

    private static bool TryGetSqlRows(
        JsonElement root, IReadOnlyList<string> columns, out List<IReadOnlyList<string?>> rows)
    {
        if (TryGetArrayProperty(root, "rows", out var rowsJson))
        {
            rows = MapRows(rowsJson, columns);
            return true;
        }

        if (TryGetArrayProperty(root, "datarows", out var datarowsJson))
        {
            rows = MapRows(datarowsJson, columns);
            return true;
        }

        if (TryGetArrayProperty(root, "data", out var dataJson))
        {
            rows = MapRows(dataJson, columns);
            return true;
        }

        if (TryGetArrayProperty(root, "values", out var valuesJson))
        {
            rows = MapRows(valuesJson, columns);
            return true;
        }

        if (TryGetObjectProperty(root, "result", out var resultJson)
            && TryGetSqlRows(resultJson, columns, out rows))
            return true;

        rows = [];
        return false;
    }

    private static bool TryGetObjectProperty(JsonElement root, string property, out JsonElement nested)
    {
        if (root.TryGetProperty(property, out nested) && nested.ValueKind == JsonValueKind.Object)
            return true;
        return false;
    }

    private static List<IReadOnlyList<string?>> MapRows(
        JsonElement rowsJson, IReadOnlyList<string> columns)
    {
        return rowsJson.EnumerateArray()
            .Select(row => row.ValueKind == JsonValueKind.Object
                ? (IReadOnlyList<string?>)columns.Select(column =>
                    row.TryGetProperty(column, out var value)
                        ? ScalarText(value) : null).ToList()
                : (IReadOnlyList<string?>)(row.ValueKind == JsonValueKind.Array
                    ? row.EnumerateArray().Select(ScalarText).ToList()
                    : new[] { ScalarText(row) }.ToList()))
            .ToList();
    }

    private static long GetSqlTotal(JsonElement root, int fallback)
    {
        if (root.TryGetProperty("total", out var total)
            && TryGetLong(total, out var totalValue))
            return totalValue;

        if (root.TryGetProperty("row_count", out var rowCount)
            && TryGetLong(rowCount, out var rowCountValue))
            return rowCountValue;

        if (TryGetObjectProperty(root, "result", out var resultJson))
        {
            if (resultJson.TryGetProperty("total", out total)
                && TryGetLong(total, out totalValue))
                return totalValue;
            if (resultJson.TryGetProperty("row_count", out rowCount)
                && TryGetLong(rowCount, out rowCountValue))
                return rowCountValue;
        }

        return fallback;
    }

    private static bool TryGetLong(JsonElement value, out long result)
    {
        result = 0;

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("value", out var nested)
                && TryGetLong(nested, out var nestedValue))
            {
                result = nestedValue;
                return true;
            }
            return false;
        }

        if (value.ValueKind != JsonValueKind.Number)
            return value.ValueKind == JsonValueKind.String
                && long.TryParse(value.GetString(), out result);

        return value.TryGetInt64(out result);
    }

    private static bool TryGetArrayProperty(JsonElement root, string property, out JsonElement array)
    {
        if (!root.TryGetProperty(property, out array) || array.ValueKind != JsonValueKind.Array)
            return false;
        return true;
    }

    private static IReadOnlyList<string> ColumnNames(JsonElement columns) =>
        columns.EnumerateArray()
            .Select(ColumnName)
            .ToList();

    private static string ColumnName(JsonElement column)
    {
        if (column.ValueKind == JsonValueKind.String)
            return column.GetString() ?? "";

        if (column.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in column.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    return item.GetString() ?? "";
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var nestedName))
                    return nestedName.GetString() ?? "";
            }
            return "";
        }

        if (column.ValueKind == JsonValueKind.Object)
            return GetColumnNameFromObject(column);

        return "";
    }

    private static string GetColumnNameFromObject(JsonElement column) =>
        column.TryGetProperty("name", out var name)
            ? name.GetString() ?? ""
            : column.TryGetProperty("label", out var label)
            ? label.GetString() ?? ""
            : column.TryGetProperty("alias", out var alias)
            ? alias.GetString() ?? ""
            : column.TryGetProperty("field", out var field)
            ? field.GetString() ?? ""
            : "";

    private static string? GetString(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
            return item.GetString();
        return null;
    }

    private static string? ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        _ => value.GetRawText().Length > 800 ? value.GetRawText()[..800] : value.GetRawText(),
    };

    private static JsonObject BuildSearchBody(OpenSearchSearchRequest request)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var root = new JsonObject
        {
            ["from"] = (page - 1) * size,
            ["size"] = size,
            ["track_total_hits"] = true,
            ["query"] = string.IsNullOrWhiteSpace(request.QueryText)
                ? new JsonObject { ["match_all"] = new JsonObject() }
                : new JsonObject
                {
                    ["simple_query_string"] = new JsonObject
                    {
                        ["query"] = request.QueryText.Trim(),
                        ["default_operator"] = "and",
                    },
                },
        };

        if (string.IsNullOrWhiteSpace(request.BucketField)) return root;
        var bucketField = SafeField(request.BucketField);
        JsonObject bucket;
        if (request.BucketType == "date_histogram")
        {
            bucket = new JsonObject
            {
                ["date_histogram"] = new JsonObject
                {
                    ["field"] = bucketField,
                    ["calendar_interval"] = SafeInterval(request.DateInterval),
                    ["min_doc_count"] = 1,
                },
            };
        }
        else
        {
            bucket = new JsonObject
            {
                ["terms"] = new JsonObject
                {
                    ["field"] = bucketField,
                    ["size"] = ChartSpec.MaxLabels,
                    ["order"] = new JsonObject { ["_count"] = "desc" },
                },
            };
        }

        if (request.MetricType != "count")
        {
            var metric = request.MetricType is "sum" or "avg" or "min" or "max"
                ? request.MetricType
                : throw new ArgumentException("Unsupported OpenSearch chart metric.");
            bucket["aggs"] = new JsonObject
            {
                ["metric"] = new JsonObject
                {
                    [metric] = new JsonObject
                    {
                        ["field"] = SafeField(request.MetricField
                            ?? throw new ArgumentException("A numeric metric field is required.")),
                    },
                },
            };
        }
        root["aggs"] = new JsonObject { ["chart"] = bucket };
        return root;
    }

    private static ChartSpec? ParseChart(JsonElement root, OpenSearchSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BucketField)
            || !root.TryGetProperty("aggregations", out var aggregations)
            || !aggregations.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("buckets", out var buckets))
            return null;

        var labels = new List<string>();
        var values = new List<double>();
        foreach (var bucket in buckets.EnumerateArray())
        {
            labels.Add(bucket.TryGetProperty("key_as_string", out var formatted)
                ? formatted.GetString() ?? ""
                : bucket.GetProperty("key").ToString());
            values.Add(request.MetricType == "count"
                ? bucket.GetProperty("doc_count").GetDouble()
                : bucket.TryGetProperty("metric", out var metric)
                  && metric.TryGetProperty("value", out var value)
                  && value.ValueKind == JsonValueKind.Number
                    ? value.GetDouble()
                    : 0);
        }
        if (labels.Count == 0) return null;
        return new ChartSpec(
            request.ChartType is "bar" or "line" or "pie" ? request.ChartType : "bar",
            request.QueryText,
            labels,
            new[] { new ChartSeries(request.MetricType, values) });
    }

    private static OpenSearchHitView ParseHit(JsonElement hit)
    {
        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        if (hit.TryGetProperty("_source", out var source))
            Flatten(source, null, fields);
        return new OpenSearchHitView(
            hit.TryGetProperty("_index", out var index) ? index.GetString() ?? "" : "",
            hit.TryGetProperty("_id", out var id) ? id.GetString() ?? "" : "",
            hit.TryGetProperty("_score", out var score) && score.ValueKind == JsonValueKind.Number
                ? score.GetDouble()
                : null,
            fields);
    }

    private static void Flatten(
        JsonElement element, string? prefix, IDictionary<string, string?> target)
    {
        if (target.Count >= 80) return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                Flatten(property.Value, prefix is null ? property.Name : $"{prefix}.{property.Name}", target);
            return;
        }
        target[prefix ?? "value"] = element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => element.ToString(),
        };
    }

    private async Task<OpenSearchConnection> RequiredConnectionAsync(
        Guid projectId, CancellationToken ct)
        => await _connections.ResolveAsync(projectId, ct)
           ?? throw new InvalidOperationException(
               "OpenSearch is not configured. Add OPENSEARCH_URL to this project's Vault.");

    private async Task<HttpResponseMessage> SendAsync(
        OpenSearchConnection connection,
        HttpMethod method,
        string path,
        string? json,
        CancellationToken ct,
        string? contentType = null)
    {
        var client = _httpFactory.CreateClient("opensearch");
        using var request = new HttpRequestMessage(method, connection.Endpoint + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(connection.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{connection.Username}:{connection.Password ?? ""}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, contentType ?? "application/json");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static async Task<JsonDocument> ReadSuccessAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = content.Length > 500 ? content[..500] : content;
            throw new InvalidOperationException(
                $"OpenSearch returned {(int)response.StatusCode}: {detail}");
        }
        return JsonDocument.Parse(content);
    }

    private static string SafeIndex(string value)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length is < 1 or > 200
            || trimmed.StartsWith('.')
            || trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '*' or ',')))
            throw new ArgumentException("The OpenSearch index pattern contains unsupported characters.");
        return trimmed;
    }

    private static string SafeField(string value)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length is < 1 or > 250
            || trimmed.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
            throw new ArgumentException("The OpenSearch field contains unsupported characters.");
        return trimmed;
    }

    private static string SafeInterval(string? value)
        => value is "hour" or "day" or "week" or "month" ? value : "day";

    private static bool Bool(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.True;

    private static long ParseTotal(JsonElement total)
        => total.ValueKind == JsonValueKind.Number
            ? total.GetInt64()
            : total.TryGetProperty("value", out var value) ? value.GetInt64() : 0;
}
