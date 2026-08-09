namespace PlaceContext.Jobs.Integration;

public interface IJobLaunchpadClient
{
    Task<Guid> RunLaunchpadAsync(
        Guid projectId,
        string triggerName,
        string prompt,
        string? sourceTable,
        Guid chainId,
        CancellationToken ct = default);
}
