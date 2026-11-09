namespace PlaceContext.Application.Search;

public interface ISearchDashboardStore
{
    Task<IReadOnlyList<SearchDashboardRecord>> ListAsync(
        Guid projectId, CancellationToken ct = default);
    Task<SearchDashboardRecord?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(SearchDashboardRecord item, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
