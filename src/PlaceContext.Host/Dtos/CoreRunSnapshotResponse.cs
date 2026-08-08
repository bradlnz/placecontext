namespace PlaceContext.Host.Api;

public sealed record CoreRunSnapshotResponse(
    string MapSourceKind,
    string MapSourceLabel,
    string? ReduceSourceKind,
    string? ReduceSourceLabel,
    int ConcurrencyLimit,
    int ShardCount,
    bool AllowNetworkEgress);
