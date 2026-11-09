namespace PlaceContext.Application.Build;

public interface IModelConnections
{
    Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default);
}
