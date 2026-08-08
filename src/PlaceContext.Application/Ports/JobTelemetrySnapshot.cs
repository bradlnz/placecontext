namespace PlaceContext.Application.Ports;

/// <summary>
/// Aggregate jobs-pipeline metrics since this process started — the Cluster page's stat tiles.
/// Counters are keyed by their OTel tag value (e.g. run status, shard outcome).
/// </summary>
public sealed record JobTelemetrySnapshot(
    long RunsStarted,
    IReadOnlyDictionary<string, long> RunsCompletedByStatus,
    IReadOnlyDictionary<string, long> ShardsCompletedByOutcome,
    DurationSummary? RunDuration,
    DurationSummary? ShardDuration,
    long ChainsStarted,
    IReadOnlyDictionary<string, long> ChainsCompletedByStatus,
    DurationSummary? ChainDuration);
