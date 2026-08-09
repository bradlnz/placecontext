namespace PlaceContext.Application.Ports;

public interface ILaunchpadRunner
{
    Task<Guid> RunLaunchpadAsync(
        Guid projectId,
        string triggerName,
        string prompt,
        string? sourceTable,
        Guid chainId,
        CancellationToken ct = default);
}
