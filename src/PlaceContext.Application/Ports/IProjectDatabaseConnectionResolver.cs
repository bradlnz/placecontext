namespace PlaceContext.Application.Ports;

/// <summary>Resolves a project's database connection without exposing credentials to UI read models.</summary>
public interface IProjectDatabaseConnectionResolver
{
    Task<ProjectDatabaseConnection> ResolveAsync(Guid projectId, CancellationToken ct = default);
}
