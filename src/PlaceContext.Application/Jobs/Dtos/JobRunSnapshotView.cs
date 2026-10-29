namespace PlaceContext.Application.Dtos;

/// <summary>Snapshot of the workload spec that was executed — for run history fidelity.</summary>
public sealed record JobRunSnapshotView(
    /// <summary>"image" or "code"</summary>
    string MapSourceKind,
    string MapSourceLabel,
    string? ReduceSourceKind,
    string? ReduceSourceLabel,
    string? Goal,
    int ConcurrencyLimit,
    int ShardCount,
    /// <summary>Whether outbound network access was permitted for this run's containers.</summary>
    bool AllowNetworkEgress,
    PlaceContext.Domain.ValueObjects.WorkerNetworkPolicy? Network = null);
