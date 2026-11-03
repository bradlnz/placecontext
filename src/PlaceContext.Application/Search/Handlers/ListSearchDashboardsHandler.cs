using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Application.Features;

public sealed class ListSearchDashboardsHandler
    : IQueryHandler<ListSearchDashboardsQuery, IReadOnlyList<SearchDashboardView>>
{
    private readonly ISearchDashboardStore _store;
    public ListSearchDashboardsHandler(ISearchDashboardStore store) => _store = store;

    public async Task<IReadOnlyList<SearchDashboardView>> HandleAsync(
        ListSearchDashboardsQuery query, CancellationToken ct = default)
        => (await _store.ListAsync(query.ProjectId, ct)).Select(ToView).ToList();

    internal static SearchDashboardView ToView(SearchDashboardRecord item) => new(
        item.Id, item.ProjectId, item.Name, item.IndexPattern, item.QueryText,
        item.BucketField, item.BucketType, item.ChartType, item.MetricType,
        item.MetricField, item.DateInterval, item.ChartSpecJson, item.CreatedAt, item.UpdatedAt);
}
