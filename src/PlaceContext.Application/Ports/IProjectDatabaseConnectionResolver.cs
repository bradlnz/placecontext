namespace PlaceContext.Application.Ports;

/// <summary>
/// The effective database a project's data lives in. By default every project shares the cluster
/// Postgres (isolated per-project by schema + role); a project can override that with its own
/// external database, configured via Vault secrets and resolved here.
/// </summary>
public sealed record ProjectDatabaseConnection(string ConnectionString, bool IsExternal);

/// <summary>Resolves a project's database connection without exposing credentials to UI read models.</summary>
public interface IProjectDatabaseConnectionResolver
{
    Task<ProjectDatabaseConnection> ResolveAsync(Guid projectId, CancellationToken ct = default);
}
