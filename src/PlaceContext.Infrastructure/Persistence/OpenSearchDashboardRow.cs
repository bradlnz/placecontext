namespace PlaceContext.Infrastructure.Persistence;

public sealed class OpenSearchDashboardRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string IndexPattern { get; set; } = "*";
    public string? QueryText { get; set; }
    public string BucketField { get; set; } = "";
    public string BucketType { get; set; } = "terms";
    public string ChartType { get; set; } = "bar";
    public string MetricType { get; set; } = "count";
    public string? MetricField { get; set; }
    public string? DateInterval { get; set; }
    public string ChartSpecJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
