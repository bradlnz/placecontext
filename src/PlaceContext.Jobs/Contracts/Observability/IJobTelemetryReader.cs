using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Ports;

public interface IJobTelemetryReader
{
    JobTelemetrySnapshot Snapshot();
    IReadOnlyList<JobRunTelemetry> RecentRuns(int take = 50);
    IReadOnlyList<JobRunTelemetry> RunsForJob(Guid jobId, int take = 20);
    IReadOnlyList<ChainRunTelemetry> RecentChainRuns(int take = 50);
    IReadOnlyList<TraceSpanNode> TraceForRun(Guid runId);
}
