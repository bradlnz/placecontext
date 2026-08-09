namespace PlaceContext.Crm.Integration;

public sealed record CrmRunArtifactSummary(
    Guid Id,
    Guid RunId,
    Guid JobId,
    string Title,
    string Bucket,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);
