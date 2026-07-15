using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PlaceContext.Application.Observability;

/// <summary>
/// The jobs pipeline's OpenTelemetry instrumentation surface: one <see cref="ActivitySource"/> for
/// distributed traces (a span per run, a child span per map shard and the reduce step) and one
/// <see cref="Meter"/> for metrics (run counts by status, shard/run durations). The Host wires the
/// OTel SDK to these names; when no exporter is configured the instruments are simply inert, so the
/// Application layer stays free of any OTel SDK dependency — it only touches the BCL diagnostics API.
/// </summary>
public static class JobTelemetry
{
    /// <summary>Activity/Meter name — the Host registers tracing/metrics against this exact string.</summary>
    public const string SourceName = "PlaceContext.Jobs";

    public static readonly ActivitySource Activity = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    /// <summary>Runs started (tagged job.id, project.id, replay).</summary>
    public static readonly Counter<long> RunsStarted =
        Meter.CreateCounter<long>("placecontext.jobs.runs.started", unit: "{run}", description: "Job runs started.");

    /// <summary>Runs completed (tagged status: Succeeded/Partial/Failed).</summary>
    public static readonly Counter<long> RunsCompleted =
        Meter.CreateCounter<long>("placecontext.jobs.runs.completed", unit: "{run}", description: "Job runs completed, by terminal status.");

    /// <summary>Map shards completed (tagged outcome).</summary>
    public static readonly Counter<long> ShardsCompleted =
        Meter.CreateCounter<long>("placecontext.jobs.shards.completed", unit: "{shard}", description: "Map shards completed, by outcome.");

    /// <summary>Wall-clock duration of a whole run, milliseconds.</summary>
    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>("placecontext.jobs.run.duration", unit: "ms", description: "End-to-end job run duration.");

    /// <summary>Wall-clock duration of a single map shard, milliseconds.</summary>
    public static readonly Histogram<double> ShardDuration =
        Meter.CreateHistogram<double>("placecontext.jobs.shard.duration", unit: "ms", description: "Single map-shard duration.");

    /// <summary>Chain runs started (tagged chain.id, project.id).</summary>
    public static readonly Counter<long> ChainsStarted =
        Meter.CreateCounter<long>("placecontext.jobs.chains.started", unit: "{chain_run}", description: "Job chain runs started.");

    /// <summary>Chain runs completed (tagged status: Succeeded/Partial/Failed).</summary>
    public static readonly Counter<long> ChainsCompleted =
        Meter.CreateCounter<long>("placecontext.jobs.chains.completed", unit: "{chain_run}", description: "Job chain runs completed, by terminal status.");

    /// <summary>Wall-clock duration of a whole chain run, milliseconds.</summary>
    public static readonly Histogram<double> ChainDuration =
        Meter.CreateHistogram<double>("placecontext.jobs.chain.duration", unit: "ms", description: "End-to-end chain run duration.");
}
