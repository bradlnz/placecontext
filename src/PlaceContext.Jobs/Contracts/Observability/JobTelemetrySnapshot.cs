using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Ports;

public sealed record JobTelemetrySnapshot(
    long RunsStarted,
    IReadOnlyDictionary<string, long> RunsCompletedByStatus,
    IReadOnlyDictionary<string, long> ShardsCompletedByOutcome,
    DurationSummary? RunDuration,
    DurationSummary? ShardDuration,
    long ChainsStarted,
    IReadOnlyDictionary<string, long> ChainsCompletedByStatus,
    DurationSummary? ChainDuration);
