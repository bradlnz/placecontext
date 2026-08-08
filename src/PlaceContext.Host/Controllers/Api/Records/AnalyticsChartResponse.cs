using System.Text.Json.Nodes;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record AnalyticsChartResponse(
    string TableName,
    string Name,
    DateTimeOffset GeneratedAt,
    string GeneratedAtDisplay,
    JsonNode? Spec,
    string? LegacyHtml,
    string? Sql,
    string ChartType);
