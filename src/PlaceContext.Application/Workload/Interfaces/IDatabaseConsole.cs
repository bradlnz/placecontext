namespace PlaceContext.Application.Workload;

public interface IDatabaseConsole
{
    Task<IReadOnlyList<DatabaseInstance>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task CreateAsync(Guid projectId, DatabaseLaunch database, CancellationToken ct = default);
    Task ControlAsync(Guid projectId, Guid databaseId, string action, CancellationToken ct = default);
    Task<string> LogsAsync(Guid projectId, Guid databaseId, CancellationToken ct = default);
    Task<DatabaseQueryResult> QueryAsync(Guid projectId, Guid databaseId, string query, string? cursor = null, CancellationToken ct = default);
}
