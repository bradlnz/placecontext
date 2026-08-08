namespace PlaceContext.Host.Components.ViewModels;

public sealed record GeneratedChartCandidate(
    string Id,
    string Title,
    string Subtitle,
    string BucketField,
    string BucketType,
    string ChartType,
    string MetricType,
    string? MetricField,
    string? DateInterval);
