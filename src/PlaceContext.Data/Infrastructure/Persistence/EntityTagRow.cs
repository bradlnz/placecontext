namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class EntityTagRow : IDataTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public Guid RunId { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
