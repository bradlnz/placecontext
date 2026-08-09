using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

public sealed class GetJobTelemetrySnapshotHandler : IQueryHandler<GetJobTelemetrySnapshotQuery, JobTelemetrySnapshot>
{
    private readonly IJobTelemetryReader _reader;

    public GetJobTelemetrySnapshotHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<JobTelemetrySnapshot> HandleAsync(GetJobTelemetrySnapshotQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.Snapshot());
}
