using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PlaceContext.Application.Observability;

/// <summary>OpenTelemetry instrumentation owned by the job execution and shard cluster.</summary>
public static class JobTelemetry
{
    public const string SourceName = "PlaceContext.Jobs";

    public static readonly ActivitySource Activity = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> RunsStarted =
        Meter.CreateCounter<long>("placecontext.jobs.runs.started", "{run}", "Job runs started.");

    public static readonly Counter<long> RunsCompleted =
        Meter.CreateCounter<long>("placecontext.jobs.runs.completed", "{run}", "Job runs completed by status.");

    public static readonly Counter<long> ShardsCompleted =
        Meter.CreateCounter<long>("placecontext.jobs.shards.completed", "{shard}", "Job shards completed by outcome.");

    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>("placecontext.jobs.run.duration", "ms", "End-to-end job run duration.");

    public static readonly Histogram<double> ShardDuration =
        Meter.CreateHistogram<double>("placecontext.jobs.shard.duration", "ms", "Single job-shard duration.");

    public static readonly Counter<long> ChainsStarted =
        Meter.CreateCounter<long>("placecontext.jobs.chains.started", "{chain_run}", "Job chain runs started.");

    public static readonly Counter<long> ChainsCompleted =
        Meter.CreateCounter<long>("placecontext.jobs.chains.completed", "{chain_run}", "Job chain runs completed by status.");

    public static readonly Histogram<double> ChainDuration =
        Meter.CreateHistogram<double>("placecontext.jobs.chain.duration", "ms", "End-to-end job chain duration.");
}
