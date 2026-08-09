namespace PlaceContext.Data.Contracts.Api;

public sealed record StoreOcrResultRequest(
    Guid ProjectId,
    Guid ArtifactId,
    Guid RunId,
    Guid JobId,
    string? Title,
    string? ContentType,
    string Markdown,
    DateTimeOffset IngestedAt);
