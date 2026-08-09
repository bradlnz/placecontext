using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Jobs.Integration;

public interface IJobArtifactClient
{
    bool IsEnabled { get; }
    Task StoreAsync(
        Guid projectId,
        Guid jobId,
        Guid runId,
        string jobName,
        PostJobActionKind kind,
        string fileName,
        string title,
        string contentType,
        byte[] content,
        CancellationToken ct = default);
}
