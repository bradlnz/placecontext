namespace PlaceContext.Infrastructure.Persistence;

public sealed class DataSourceRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string DefinitionCipher { get; set; } = "";
    public string ConnectionCipher { get; set; } = "";
    public Guid Version { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    public int RecordCount { get; set; }
}
