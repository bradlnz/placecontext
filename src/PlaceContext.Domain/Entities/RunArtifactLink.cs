using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// A pointer to a post-job output stored in the object store (MinIO) for a given <see cref="JobRun"/> —
/// an HTML report, chart, CSV, or a raw output file. Surfaced as an openable link in the portal/TUI.
/// The content lives in the object store; this records where and what it is.
/// </summary>
public sealed class RunArtifactLink
{
    private RunArtifactLink(
        Guid id, Guid runId, Guid jobId, Guid projectId,
        PostJobActionKind kind, string title, string bucket, string objectKey,
        string contentType, long sizeBytes, DateTimeOffset createdAt)
    {
        Id = id;
        RunId = runId;
        JobId = jobId;
        ProjectId = projectId;
        Kind = kind;
        Title = title;
        Bucket = bucket;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid RunId { get; }
    public Guid JobId { get; }
    public Guid ProjectId { get; }

    /// <summary>Which post-job action produced this artifact.</summary>
    public PostJobActionKind Kind { get; }

    /// <summary>Human-readable label (e.g. "HTML report", "report.csv").</summary>
    public string Title { get; }

    public string Bucket { get; }
    public string ObjectKey { get; }
    public string ContentType { get; }
    public long SizeBytes { get; }
    public DateTimeOffset CreatedAt { get; }

    public static RunArtifactLink Create(
        Guid runId, Guid jobId, Guid projectId,
        PostJobActionKind kind, string title, string bucket, string objectKey,
        string contentType, long sizeBytes, DateTimeOffset createdAt)
        => new(Guid.NewGuid(), runId, jobId, projectId, kind, title, bucket, objectKey, contentType, sizeBytes, createdAt);

    public static RunArtifactLink Rehydrate(
        Guid id, Guid runId, Guid jobId, Guid projectId,
        PostJobActionKind kind, string title, string bucket, string objectKey,
        string contentType, long sizeBytes, DateTimeOffset createdAt)
        => new(id, runId, jobId, projectId, kind, title, bucket, objectKey, contentType, sizeBytes, createdAt);
}
