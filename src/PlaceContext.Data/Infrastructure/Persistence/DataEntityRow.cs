namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class DataEntityRow : IDataTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? LabelColumn { get; set; }
    public string RelationsJson { get; set; } = "[]";
    public string TagsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
