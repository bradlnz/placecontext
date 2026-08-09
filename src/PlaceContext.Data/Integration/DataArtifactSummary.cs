namespace PlaceContext.Data.Integration;

public sealed record DataArtifactSummary(
    Guid Id,
    Guid RunId,
    Guid JobId,
    string Title,
    string Kind,
    string ContentType);
