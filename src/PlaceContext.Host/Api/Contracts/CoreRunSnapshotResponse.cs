namespace PlaceContext.Host.Api;

public sealed record CoreRunSnapshotResponse(
    string MapSourceKind,
    string MapSourceLabel,
    string? ReduceSourceKind,
    string? ReduceSourceLabel,
    string? Goal,
    int ConcurrencyLimit,
    int ShardCount,
    bool AllowNetworkEgress,
    PlaceContext.Domain.ValueObjects.WorkerNetworkPolicy? Network = null);
