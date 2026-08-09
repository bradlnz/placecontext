using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

public sealed record GetJobTelemetrySnapshotQuery : IQuery<JobTelemetrySnapshot>;
