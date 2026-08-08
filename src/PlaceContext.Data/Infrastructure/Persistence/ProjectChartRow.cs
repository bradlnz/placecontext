namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class ProjectChartRow : IDataTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
}
