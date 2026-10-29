namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchSearchRequest(
    Guid ProjectId,
    string IndexPattern,
    string? QueryText,
    int Page = 1,
    int PageSize = 25,
    string? BucketField = null,
    string BucketType = "terms",
    string ChartType = "bar",
    string MetricType = "count",
    string? MetricField = null,
    string? DateInterval = null);
