using PlaceContext.Domain.Entities;

namespace PlaceContext.Artifacts.Integration;

public interface IArtifactDataClient
{
    Task StoreOcrResultAsync(
        RunArtifactLink artifact,
        string markdown,
        DateTimeOffset ingestedAt,
        CancellationToken cancellationToken = default);
}
