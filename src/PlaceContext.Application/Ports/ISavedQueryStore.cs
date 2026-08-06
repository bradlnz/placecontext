namespace PlaceContext.Application.Ports;

/// <summary>A named, project-scoped SQL query the user saved from the SQL Studio editor.</summary>
public sealed record SavedQueryRecord(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Sql,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface ISavedQueryStore
{
    Task<IReadOnlyList<SavedQueryRecord>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<SavedQueryRecord?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SavedQueryRecord?> FindByNameAsync(Guid projectId, string name, CancellationToken ct = default);
    Task SaveAsync(SavedQueryRecord item, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
