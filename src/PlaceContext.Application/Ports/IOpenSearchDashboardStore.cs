using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

public interface IOpenSearchDashboardStore
{
    Task<IReadOnlyList<OpenSearchDashboardRecord>> ListAsync(
        Guid projectId, CancellationToken ct = default);
    Task<OpenSearchDashboardRecord?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(OpenSearchDashboardRecord item, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
