namespace PlaceContext.Artifacts.Infrastructure.Persistence;

public sealed class RunArtifactLinkRow : IArtifactsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid JobId { get; set; }
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? OcrProcessedAt { get; set; }
    public string? OcrError { get; set; }
}
