using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

/// <summary>Aggregate jobs-pipeline metrics since process start — the Cluster page's stat tiles.</summary>
public sealed record GetJobTelemetrySnapshotQuery : IQuery<JobTelemetrySnapshot>;

/// <summary>The most recent job-run traces across the workspace, newest first — the Cluster page's run list.</summary>
public sealed record ListRecentJobRunTelemetryQuery(int Take = 50) : IQuery<IReadOnlyList<JobRunTelemetry>>;

/// <summary>Recent run traces for one job — the per-job OTel panel on the run detail / job view.</summary>
public sealed record ListJobRunTelemetryQuery(Guid JobId, int Take = 20) : IQuery<IReadOnlyList<JobRunTelemetry>>;

/// <summary>The most recent chain-run traces across the workspace, newest first — the Cluster/
/// Observability pages' chain lens.</summary>
public sealed record ListRecentChainRunTelemetryQuery(int Take = 50) : IQuery<IReadOnlyList<ChainRunTelemetry>>;
