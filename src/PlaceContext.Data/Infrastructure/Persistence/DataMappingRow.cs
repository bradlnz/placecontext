namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class DataMappingRow : IDataTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid JobId { get; set; }
    public string SourceKind { get; set; } = "job";
    public string TargetTable { get; set; } = string.Empty;
    public string? RowsPath { get; set; }
    public string FieldsJson { get; set; } = "[]";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
