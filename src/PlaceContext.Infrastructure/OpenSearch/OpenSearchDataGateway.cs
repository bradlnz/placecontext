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
    private const int ExportPageSize = 500;
    private const int DefaultExportRows = 10000;
    private const int MaxExportRows = 100000;
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

    public async Task<OpenSearchExportView> ExportIndexAsync(
        Guid projectId, string indexPattern, int maxRows = DefaultExportRows, CancellationToken ct = default)
    {
        var connection = await RequiredConnectionAsync(projectId, ct);
        var safePattern = SafeIndex(indexPattern);
        var rowCap = Math.Clamp(maxRows, 1, MaxExportRows);

        // Schema: the same _field_caps call the fields list uses, mapped onto the store's Postgres
        // type allow-list. Object/nested/alias parents are skipped — their flattened leaves carry
        // the data, so a parent column would just be all nulls.
        var fields = new List<OpenSearchExportField>();
        using (var capsResponse = await SendAsync(connection, HttpMethod.Get,
                   $"/{safePattern}/_field_caps?fields=*", null, ct))
        using (var caps = await ReadSuccessAsync(capsResponse, ct))
        {
            if (!caps.RootElement.TryGetProperty("fields", out var fieldsJson)
                || fieldsJson.ValueKind != JsonValueKind.Object)
                return new OpenSearchExportView(
                    Array.Empty<OpenSearchExportField>(), Array.Empty<IReadOnlyList<string?>>(), false);

            foreach (var field in fieldsJson.EnumerateObject())
            {
                if (field.Name.StartsWith('_') || field.Value.ValueKind != JsonValueKind.Object) continue;
                var type = field.Value.EnumerateObject().FirstOrDefault();
                if (type.Value.ValueKind != JsonValueKind.Object) continue;
                var postgres = PostgresTypeFor(type.Name);
                if (postgres is null) continue;
                fields.Add(new OpenSearchExportField(field.Name, type.Name, postgres));
            }
        }
        fields.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        if (fields.Count == 0)
            return new OpenSearchExportView(fields, Array.Empty<IReadOnlyList<string?>>(), false);

        // Docs paged with search_after on the _doc tiebreaker — stable across pages and cheap at
        // any depth (unlike from+size), which matters for a full-index export.
        var rows = new List<IReadOnlyList<string?>>();
        JsonNode? searchAfter = null;
        var truncated = false;
        while (rows.Count < rowCap)
        {
            var wanted = Math.Min(ExportPageSize, rowCap - rows.Count);
            var body = new JsonObject
            {
                ["size"] = wanted,
                ["sort"] = new JsonArray { new JsonObject { ["_doc"] = "asc" } },
                ["query"] = new JsonObject { ["match_all"] = new JsonObject() },
            };
            if (searchAfter is not null) body["search_after"] = new JsonArray { searchAfter };

            using var response = await SendAsync(connection, HttpMethod.Post,
                $"/{safePattern}/_search", body.ToJsonString(), ct);
            using var document = await ReadSuccessAsync(response, ct);
            var hits = document.RootElement.GetProperty("hits").GetProperty("hits");
            var pageCount = 0;
            foreach (var hit in hits.EnumerateArray())
            {
                var flat = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                if (hit.TryGetProperty("_source", out var source)
                    && source.ValueKind == JsonValueKind.Object)
                    Flatten(source, null, flat);
                var row = new string?[fields.Count];
                for (var i = 0; i < fields.Count; i++)
                {
                    var value = flat.TryGetValue(fields[i].Name, out var v) ? v : null;
                    // Date fields stored as epoch_millis wouldn't cast to timestamptz — normalise first.
                    if (value is not null && IsDateField(fields[i].Type) && LooksLikeEpochMillis(value))
                        value = EpochMillisToIso(value);
                    row[i] = value;
                }
                rows.Add(row);
                pageCount++;
            }

            if (pageCount == 0) break;
            searchAfter = ParseSearchAfter(hits.EnumerateArray().Last());
            if (searchAfter is null) break;
            if (pageCount < wanted) break; // short page — the index is exhausted
            if (rows.Count >= rowCap)
            {
                truncated = await HasMoreAsync(connection, safePattern, searchAfter, ct);
                break;
            }
        }

        return new OpenSearchExportView(fields, rows, truncated);
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
        IReadOnlyList<string> columns;
        List<IReadOnlyList<string?>> rows;

        if (root.TryGetProperty("columns", out var columnsJson)
            && root.TryGetProperty("rows", out var rowsJson))
        {
            columns = ColumnNames(columnsJson);
            rows = rowsJson.EnumerateArray()
                .Select(row => (IReadOnlyList<string?>)row.EnumerateArray()
                    .Select(ScalarText).ToList())
                .ToList();
        }
        else if (root.TryGetProperty("schema", out var schemaJson)
                 && root.TryGetProperty("datarows", out var datarowsJson))
        {
            columns = ColumnNames(schemaJson);
            rows = datarowsJson.EnumerateArray()
                .Select(row => (IReadOnlyList<string?>)row.EnumerateArray()
                    .Select(ScalarText).ToList())
                .ToList();
        }
        else
        {
            throw new InvalidOperationException(
                "OpenSearch SQL returned an unexpected response shape.");
        }

        var total = root.TryGetProperty("total", out var totalElement)
            && totalElement.ValueKind == JsonValueKind.Number
            ? totalElement.GetInt64()
            : rows.Count;
        return new ProjectQueryResult(columns, rows, 0, total > rows.Count);
    }

    private static IReadOnlyList<string> ColumnNames(JsonElement columns) =>
        columns.EnumerateArray()
            .Select(column => column.ValueKind == JsonValueKind.String
                ? column.GetString() ?? ""
                : column.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "")
            .ToList();

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

    /// <summary>
    /// OpenSearch type → the store's Postgres allow-list. Returns null for structural parents that
    /// have no own value (object/nested/flattened) and aliases (their real field carries the data).
    /// </summary>
    private static string? PostgresTypeFor(string openSearchType) => openSearchType switch
    {
        "long" or "unsigned_long" or "short" or "byte" => "bigint",
        "integer" => "integer",
        "double" or "float" or "half_float" or "scaled_float" or "rank_feature" or "rank_features" => "numeric",
        "boolean" => "boolean",
        "date" or "date_nanos" => "timestamptz",
        "object" or "nested" or "flattened" or "alias" => null,
        _ => "text",
    };

    private static bool IsDateField(string openSearchType)
        => openSearchType is "date" or "date_nanos";

    private static bool LooksLikeEpochMillis(string value)
        => value.Length is >= 10 and <= 16 && value.All(char.IsAsciiDigit);

    private static string EpochMillisToIso(string value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(value, CultureInfo.InvariantCulture))
            .UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFF'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The last hit's sort token for the next search_after page. <c>_doc</c> values come back either
    /// as plain numbers or (beyond int32) as <c>{"$numberLong":"..."}</c> — re-parsing the raw JSON
    /// token preserves whichever shape the server sent so the round trip is exact.
    /// </summary>
    private static JsonNode? ParseSearchAfter(JsonElement hit)
    {
        if (!hit.TryGetProperty("sort", out var sort)
            || sort.ValueKind != JsonValueKind.Array || sort.GetArrayLength() == 0)
            return null;
        try
        {
            return JsonNode.Parse(sort[0].GetRawText());
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> HasMoreAsync(
        OpenSearchConnection connection, string safePattern, JsonNode searchAfter, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["size"] = 1,
            ["sort"] = new JsonArray { new JsonObject { ["_doc"] = "asc" } },
            ["search_after"] = new JsonArray { searchAfter },
            ["query"] = new JsonObject { ["match_all"] = new JsonObject() },
        };
        using var response = await SendAsync(connection, HttpMethod.Post,
            $"/{safePattern}/_search", body.ToJsonString(), ct);
        using var document = await ReadSuccessAsync(response, ct);
        return document.RootElement.GetProperty("hits").GetProperty("hits").GetArrayLength() > 0;
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
        CancellationToken ct)
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
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
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
