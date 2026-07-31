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
