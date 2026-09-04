using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

public sealed class ListRecentJobRunTelemetryHandler
    : IQueryHandler<ListRecentJobRunTelemetryQuery, IReadOnlyList<JobRunTelemetry>>
{
    private readonly IJobTelemetryReader _reader;

    public ListRecentJobRunTelemetryHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<IReadOnlyList<JobRunTelemetry>> HandleAsync(ListRecentJobRunTelemetryQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.RecentRuns(query.Take));
}
