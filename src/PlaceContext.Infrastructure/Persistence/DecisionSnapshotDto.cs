namespace PlaceContext.Infrastructure.Persistence;

internal sealed record DecisionSnapshotDto(
    string Path,
    DateTimeOffset BuiltAt,
    int NodeCount,
    int LinkCount,
    List<DecisionSnapshotGodNodeDto> Gods);
