namespace PlaceContext.Artifacts.Contracts.Api;

public sealed record DeleteArtifactsRequest(IReadOnlyList<Guid> ArtifactIds);
