using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Search;

public sealed record SearchDashboardRecord(
    Guid Id,
    Guid ProjectId,
    string Name,
    string IndexPattern,
    string? QueryText,
    string BucketField,
    string BucketType,
    string ChartType,
    string MetricType,
    string? MetricField,
    string? DateInterval,
    string ChartSpecJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
