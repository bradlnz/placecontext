namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF row for a <see cref="PlaceContext.Domain.Entities.ProjectChart"/> (table project_charts).</summary>
public sealed class ProjectChartRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string TableName { get; set; } = "";
    public string Html { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
}
