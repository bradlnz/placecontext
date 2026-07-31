using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class ListOpenSearchIndicesHandler
    : IQueryHandler<ListOpenSearchIndicesQuery, IReadOnlyList<OpenSearchIndexView>>
{
    private readonly IOpenSearchDataGateway _gateway;
    public ListOpenSearchIndicesHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<IReadOnlyList<OpenSearchIndexView>> HandleAsync(
        ListOpenSearchIndicesQuery query, CancellationToken ct = default)
        => _gateway.ListIndicesAsync(query.ProjectId, ct);
}

public sealed class ListOpenSearchFieldsHandler
    : IQueryHandler<ListOpenSearchFieldsQuery, IReadOnlyList<OpenSearchFieldView>>
{
    private readonly IOpenSearchDataGateway _gateway;
    public ListOpenSearchFieldsHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<IReadOnlyList<OpenSearchFieldView>> HandleAsync(
        ListOpenSearchFieldsQuery query, CancellationToken ct = default)
        => _gateway.ListFieldsAsync(query.ProjectId, query.IndexPattern, ct);
}

public sealed class SearchOpenSearchHandler
    : IQueryHandler<SearchOpenSearchQuery, OpenSearchSearchView>
{
    private readonly IOpenSearchDataGateway _gateway;
    public SearchOpenSearchHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<OpenSearchSearchView> HandleAsync(
        SearchOpenSearchQuery query, CancellationToken ct = default)
        => _gateway.SearchAsync(query.Request, ct);
}

public sealed class GetOpenSearchLastUpdatedHandler
    : IQueryHandler<GetOpenSearchLastUpdatedQuery, OpenSearchLastUpdatedView>
{
    private readonly IOpenSearchDataGateway _gateway;
    public GetOpenSearchLastUpdatedHandler(IOpenSearchDataGateway gateway) => _gateway = gateway;
    public Task<OpenSearchLastUpdatedView> HandleAsync(
        GetOpenSearchLastUpdatedQuery query, CancellationToken ct = default)
        => _gateway.GetLastUpdatedAsync(
            query.ProjectId, query.IndexPattern, query.CandidateFields, ct);
}

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

public sealed class SaveOpenSearchDashboardHandler
    : ICommandHandler<SaveOpenSearchDashboardCommand, OpenSearchDashboardView>
{
    private readonly IOpenSearchDashboardStore _store;
    private readonly IClock _clock;

    public SaveOpenSearchDashboardHandler(IOpenSearchDashboardStore store, IClock clock)
        => (_store, _clock) = (store, clock);

    public async Task<OpenSearchDashboardView> HandleAsync(
        SaveOpenSearchDashboardCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Dashboard name is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.IndexPattern))
            throw new ArgumentException("Index is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.BucketField))
            throw new ArgumentException("A chart group field is required.", nameof(command));
        if (ChartSpec.TryParse(command.ChartSpecJson) is null)
            throw new ArgumentException("The chart result is invalid.", nameof(command));

        var existing = command.DashboardId is { } id ? await _store.GetAsync(id, ct) : null;
        if (command.DashboardId is not null && existing is null)
            throw new InvalidOperationException($"Dashboard {command.DashboardId} not found.");
        if (existing is not null && existing.ProjectId != command.ProjectId)
            throw new InvalidOperationException("Dashboard does not belong to this project.");

        var now = _clock.UtcNow;
        var item = new OpenSearchDashboardRecord(
            existing?.Id ?? Guid.NewGuid(), command.ProjectId, command.Name.Trim(),
            command.IndexPattern.Trim(), NullIfBlank(command.QueryText), command.BucketField.Trim(),
            command.BucketType, command.ChartType, command.MetricType,
            NullIfBlank(command.MetricField), NullIfBlank(command.DateInterval),
            command.ChartSpecJson, existing?.CreatedAt ?? now, now);
        await _store.SaveAsync(item, ct);
        return ListOpenSearchDashboardsHandler.ToView(item);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DeleteOpenSearchDashboardHandler
    : ICommandHandler<DeleteOpenSearchDashboardCommand, bool>
{
    private readonly IOpenSearchDashboardStore _store;
    public DeleteOpenSearchDashboardHandler(IOpenSearchDashboardStore store) => _store = store;
    public Task<bool> HandleAsync(
        DeleteOpenSearchDashboardCommand command, CancellationToken ct = default)
        => _store.DeleteAsync(command.DashboardId, ct);
}
