namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class RecordLinkRow : IDataTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string DisplayValue { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
