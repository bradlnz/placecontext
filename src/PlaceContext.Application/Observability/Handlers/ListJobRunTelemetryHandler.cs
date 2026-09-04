using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

public sealed class ListJobRunTelemetryHandler
    : IQueryHandler<ListJobRunTelemetryQuery, IReadOnlyList<JobRunTelemetry>>
{
    private readonly IJobTelemetryReader _reader;

    public ListJobRunTelemetryHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<IReadOnlyList<JobRunTelemetry>> HandleAsync(ListJobRunTelemetryQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.RunsForJob(query.JobId, query.Take));
}
