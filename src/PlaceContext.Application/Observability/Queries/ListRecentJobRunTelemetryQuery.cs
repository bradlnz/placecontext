using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

/// <summary>The most recent job-run traces across the workspace, newest first — the Cluster page's run list.</summary>
public sealed record ListRecentJobRunTelemetryQuery(int Take = 50) : IQuery<IReadOnlyList<JobRunTelemetry>>;
