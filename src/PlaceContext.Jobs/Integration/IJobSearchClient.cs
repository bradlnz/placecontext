namespace PlaceContext.Jobs.Integration;

public interface IJobSearchClient
{
    Task IndexRunOutputAsync(
        Guid runId,
        Guid jobId,
        Guid projectId,
        string text,
        CancellationToken cancellationToken = default);
}
