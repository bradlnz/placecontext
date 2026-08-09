using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

public sealed record ListJobRunTelemetryQuery(Guid JobId, int Take = 20)
    : IQuery<IReadOnlyList<JobRunTelemetry>>;
