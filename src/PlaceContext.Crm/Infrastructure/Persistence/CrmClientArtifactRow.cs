namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class CrmClientArtifactRow : ICrmTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? SourceArtifactId { get; set; }
    public Guid? ChainRunId { get; set; }
    public string Title { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
