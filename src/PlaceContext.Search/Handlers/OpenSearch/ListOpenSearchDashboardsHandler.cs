using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class ListOpenSearchDashboardsHandler
    : IQueryHandler<ListOpenSearchDashboardsQuery, IReadOnlyList<OpenSearchDashboardView>>
{
    private readonly IOpenSearchDashboardStore _store;
    public ListOpenSearchDashboardsHandler(IOpenSearchDashboardStore store) => _store = store;

    public async Task<IReadOnlyList<OpenSearchDashboardView>> HandleAsync(
        ListOpenSearchDashboardsQuery query, CancellationToken ct = default)
        => (await _store.ListAsync(query.ProjectId, ct)).Select(ToView).ToList();

    internal static OpenSearchDashboardView ToView(OpenSearchDashboardRecord item) => new(
        item.Id, item.ProjectId, item.Name, item.IndexPattern, item.QueryText,
        item.BucketField, item.BucketType, item.ChartType, item.MetricType,
        item.MetricField, item.DateInterval, item.ChartSpecJson, item.CreatedAt, item.UpdatedAt);
}
