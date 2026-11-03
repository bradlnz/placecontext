namespace PlaceContext.Infrastructure.Persistence;

public sealed class SourceDocumentRow : ITenantOwned
{
    public Guid SourceId { get; set; }
    public Guid TenantId { get; set; }
    public string KeyHash { get; set; } = "";
    public string ContentCipher { get; set; } = "";
}
