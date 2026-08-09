namespace PlaceContext.Jobs.Integration;

public interface IJobRuntimeEnvironmentClient
{
    Task<IReadOnlyDictionary<string, string>> GetEnvironmentAsync(
        Guid projectId,
        IReadOnlyList<Guid> mcpConnectionIds,
        CancellationToken ct = default);
}
