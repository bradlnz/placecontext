namespace PlaceContext.Application.Features;

public sealed record CrmClientArtifactView(
    Guid Id,
    Guid ClientId,
    string Title,
    string ContentType,
    long SizeBytes,
    string Source,
    Guid? ChainRunId,
    DateTimeOffset CreatedAt);
