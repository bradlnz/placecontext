namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF row for a <see cref="PlaceContext.Domain.Entities.RunArtifactLink"/> (table job_run_artifacts).</summary>
public sealed class RunArtifactLinkRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid JobId { get; set; }
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
