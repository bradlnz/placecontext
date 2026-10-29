using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

/// <summary>Aggregate jobs-pipeline metrics since process start — the Cluster page's stat tiles.</summary>
public sealed record GetJobTelemetrySnapshotQuery : IQuery<JobTelemetrySnapshot>;
