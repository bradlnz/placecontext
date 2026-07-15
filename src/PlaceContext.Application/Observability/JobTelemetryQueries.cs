using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Observability;

/// <summary>Aggregate jobs-pipeline metrics since process start — the Cluster page's stat tiles.</summary>
public sealed record GetJobTelemetrySnapshotQuery : IQuery<JobTelemetrySnapshot>;

public sealed class GetJobTelemetrySnapshotHandler : IQueryHandler<GetJobTelemetrySnapshotQuery, JobTelemetrySnapshot>
{
    private readonly IJobTelemetryReader _reader;

    public GetJobTelemetrySnapshotHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<JobTelemetrySnapshot> HandleAsync(GetJobTelemetrySnapshotQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.Snapshot());
}

/// <summary>The most recent job-run traces across the workspace, newest first — the Cluster page's run list.</summary>
public sealed record ListRecentJobRunTelemetryQuery(int Take = 50) : IQuery<IReadOnlyList<JobRunTelemetry>>;

public sealed class ListRecentJobRunTelemetryHandler
    : IQueryHandler<ListRecentJobRunTelemetryQuery, IReadOnlyList<JobRunTelemetry>>
{
    private readonly IJobTelemetryReader _reader;

    public ListRecentJobRunTelemetryHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<IReadOnlyList<JobRunTelemetry>> HandleAsync(ListRecentJobRunTelemetryQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.RecentRuns(query.Take));
}

/// <summary>Recent run traces for one job — the per-job OTel panel on the run detail / job view.</summary>
public sealed record ListJobRunTelemetryQuery(Guid JobId, int Take = 20) : IQuery<IReadOnlyList<JobRunTelemetry>>;

public sealed class ListJobRunTelemetryHandler
    : IQueryHandler<ListJobRunTelemetryQuery, IReadOnlyList<JobRunTelemetry>>
{
    private readonly IJobTelemetryReader _reader;

    public ListJobRunTelemetryHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<IReadOnlyList<JobRunTelemetry>> HandleAsync(ListJobRunTelemetryQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.RunsForJob(query.JobId, query.Take));
}

/// <summary>The most recent chain-run traces across the workspace, newest first — the Cluster/
/// Observability pages' chain lens.</summary>
public sealed record ListRecentChainRunTelemetryQuery(int Take = 50) : IQuery<IReadOnlyList<ChainRunTelemetry>>;

public sealed class ListRecentChainRunTelemetryHandler
    : IQueryHandler<ListRecentChainRunTelemetryQuery, IReadOnlyList<ChainRunTelemetry>>
{
    private readonly IJobTelemetryReader _reader;

    public ListRecentChainRunTelemetryHandler(IJobTelemetryReader reader) => _reader = reader;

    public Task<IReadOnlyList<ChainRunTelemetry>> HandleAsync(ListRecentChainRunTelemetryQuery query, CancellationToken ct = default)
        => Task.FromResult(_reader.RecentChainRuns(query.Take));
}
