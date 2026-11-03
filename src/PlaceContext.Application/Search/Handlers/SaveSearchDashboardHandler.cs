using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
using PlaceContext.Application.Shared;
namespace PlaceContext.Application.Features;

public sealed class SaveSearchDashboardHandler
    : ICommandHandler<SaveSearchDashboardCommand, SearchDashboardView>
{
    private readonly ISearchDashboardStore _store;
    private readonly IClock _clock;

    public SaveSearchDashboardHandler(ISearchDashboardStore store, IClock clock)
        => (_store, _clock) = (store, clock);

    public async Task<SearchDashboardView> HandleAsync(
        SaveSearchDashboardCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ArgumentException("Dashboard name is required.", nameof(command));
        }
        if (string.IsNullOrWhiteSpace(command.IndexPattern))
        {
            throw new ArgumentException("Index is required.", nameof(command));
        }
        if (string.IsNullOrWhiteSpace(command.BucketField))
        {
            throw new ArgumentException("A chart group field is required.", nameof(command));
        }
        if (ChartSpec.TryParse(command.ChartSpecJson) is null)
        {
            throw new ArgumentException("The chart result is invalid.", nameof(command));
        }
        var existing = command.DashboardId is { } id ? await _store.GetAsync(id, ct) : null;
        if (command.DashboardId is not null && existing is null)
        {
            throw new InvalidOperationException($"Dashboard {command.DashboardId} not found.");
        }
        if (existing is not null && existing.ProjectId != command.ProjectId)
        {
            throw new InvalidOperationException("Dashboard does not belong to this project.");
        }
        var now = _clock.UtcNow;
        var item = new SearchDashboardRecord(
            existing?.Id ?? Guid.NewGuid(), command.ProjectId, command.Name.Trim(),
            command.IndexPattern.Trim(), NullIfBlank(command.QueryText), command.BucketField.Trim(),
            command.BucketType, command.ChartType, command.MetricType,
            NullIfBlank(command.MetricField), NullIfBlank(command.DateInterval),
            command.ChartSpecJson, existing?.CreatedAt ?? now, now);
        await _store.SaveAsync(item, ct);
        return ListSearchDashboardsHandler.ToView(item);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
