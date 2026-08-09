namespace PlaceContext.Projects.Infrastructure.Persistence;

internal sealed record SnapshotDto(
    string Path,
    DateTimeOffset BuiltAt,
    int NodeCount,
    int LinkCount,
    IReadOnlyList<SnapshotGodNodeDto> Gods);
