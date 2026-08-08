using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

/// <summary>Recent run traces for one job — the per-job OTel panel on the run detail / job view.</summary>
public sealed record ListJobRunTelemetryQuery(Guid JobId, int Take = 20) : IQuery<IReadOnlyList<JobRunTelemetry>>;
