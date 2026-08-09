namespace PlaceContext.Application.Dtos;

public sealed record SaveOpenSearchDashboardRequest(
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
    Guid? DashboardId = null);
