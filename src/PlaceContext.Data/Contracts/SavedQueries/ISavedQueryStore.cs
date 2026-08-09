namespace PlaceContext.Application.Ports;

public interface ISavedQueryStore
{
    Task<IReadOnlyList<SavedQueryRecord>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<SavedQueryRecord?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SavedQueryRecord?> FindByNameAsync(Guid projectId, string name, CancellationToken ct = default);
    Task SaveAsync(SavedQueryRecord item, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
