using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

public sealed record ListRecentJobRunTelemetryQuery(int Take = 50)
    : IQuery<IReadOnlyList<JobRunTelemetry>>;
