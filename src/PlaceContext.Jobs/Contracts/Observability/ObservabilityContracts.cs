using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Ports
{
    public sealed record DurationSummary(long Count, double MinMs, double MaxMs, double AvgMs);

    public sealed record ChainRunStepTelemetry(
        int StageIndex,
        int BranchIndex,
        Guid JobId,
        string? JobName,
        Guid? RunId,
        string? Status,
        double? DurationMs);

    public sealed record ChainRunTelemetry(
        Guid ChainRunId,
        Guid ChainId,
        string? ChainName,
        Guid? ProjectId,
        string? Status,
        DateTimeOffset StartedAt,
        double? DurationMs,
        IReadOnlyList<ChainRunStepTelemetry> Steps);

    public sealed record JobTelemetrySnapshot(
        long RunsStarted,
        IReadOnlyDictionary<string, long> RunsCompletedByStatus,
        IReadOnlyDictionary<string, long> ShardsCompletedByOutcome,
        DurationSummary? RunDuration,
        DurationSummary? ShardDuration,
        long ChainsStarted,
        IReadOnlyDictionary<string, long> ChainsCompletedByStatus,
        DurationSummary? ChainDuration);

    public interface IJobTelemetryReader
    {
        JobTelemetrySnapshot Snapshot();
        IReadOnlyList<JobRunTelemetry> RecentRuns(int take = 50);
        IReadOnlyList<JobRunTelemetry> RunsForJob(Guid jobId, int take = 20);
        IReadOnlyList<ChainRunTelemetry> RecentChainRuns(int take = 50);
        IReadOnlyList<TraceSpanNode> TraceForRun(Guid runId);
    }
}

namespace PlaceContext.Application.Observability
{
    using PlaceContext.Application.Ports;

    public sealed record GetJobTelemetrySnapshotQuery : IQuery<JobTelemetrySnapshot>;

    public sealed record ListJobRunTelemetryQuery(Guid JobId, int Take = 20)
        : IQuery<IReadOnlyList<JobRunTelemetry>>;

    public sealed record ListRecentChainRunTelemetryQuery(int Take = 50)
        : IQuery<IReadOnlyList<ChainRunTelemetry>>;

    public sealed record ListRecentJobRunTelemetryQuery(int Take = 50)
        : IQuery<IReadOnlyList<JobRunTelemetry>>;
}
