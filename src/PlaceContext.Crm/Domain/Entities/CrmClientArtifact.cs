namespace PlaceContext.Domain.Entities;

/// <summary>A file attached to a CRM client, either uploaded directly or tagged from an automation run.</summary>
public sealed class CrmClientArtifact
{
    private CrmClientArtifact(
        Guid id,
        Guid projectId,
        Guid clientId,
        Guid? sourceArtifactId,
        Guid? chainRunId,
        string title,
        string bucket,
        string objectKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        ClientId = clientId;
        SourceArtifactId = sourceArtifactId;
        ChainRunId = chainRunId;
        Title = title;
        Bucket = bucket;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid ClientId { get; }
    public Guid? SourceArtifactId { get; }
    public Guid? ChainRunId { get; }
    public string Title { get; }
    public string Bucket { get; }
    public string ObjectKey { get; }
    public string ContentType { get; }
    public long SizeBytes { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsDirectUpload => SourceArtifactId is null;

    public static CrmClientArtifact CreateUpload(
        Guid id,
        Guid projectId,
        Guid clientId,
        string title,
        string bucket,
        string objectKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
        => Create(id, projectId, clientId, null, null, title, bucket, objectKey,
            contentType, sizeBytes, now);

    public static CrmClientArtifact CreateFromRunArtifact(
        Guid projectId,
        Guid clientId,
        Guid sourceArtifactId,
        Guid chainRunId,
        string title,
        string bucket,
        string objectKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
        => Create(Guid.NewGuid(), projectId, clientId, sourceArtifactId, chainRunId, title,
            bucket, objectKey, contentType, sizeBytes, now);

    public static CrmClientArtifact Rehydrate(
        Guid id, Guid projectId, Guid clientId, Guid? sourceArtifactId, Guid? chainRunId,
        string title, string bucket, string objectKey, string contentType, long sizeBytes,
        DateTimeOffset createdAt)
        => new(id, projectId, clientId, sourceArtifactId, chainRunId, title, bucket,
            objectKey, contentType, sizeBytes, createdAt);

    private static CrmClientArtifact Create(
        Guid id, Guid projectId, Guid clientId, Guid? sourceArtifactId, Guid? chainRunId,
        string title, string bucket, string objectKey, string contentType, long sizeBytes,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || projectId == Guid.Empty || clientId == Guid.Empty)
            throw new ArgumentException("Client artifact identifiers must not be empty.");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(bucket)
            || string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("Client artifact storage details must not be empty.");
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        return new CrmClientArtifact(
            id, projectId, clientId, sourceArtifactId, chainRunId, title.Trim(), bucket.Trim(),
            objectKey.Trim(), string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream" : contentType.Trim(),
            sizeBytes, now);
    }
}
