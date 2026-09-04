using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

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
