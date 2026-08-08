using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates or updates one edge of the project's data map: after every completed run of
/// <paramref name="JobId"/>, records extracted from the run's primary artifact (at
/// <paramref name="RowsPath"/>, or the root) are appended to <paramref name="TargetTable"/> in the
/// project's database, one column per field mapping. Null <paramref name="MappingId"/> creates.
/// </summary>
public sealed record SaveDataMappingCommand(
    Guid ProjectId,
    Guid JobId,
    string TargetTable,
    string? RowsPath,
    IReadOnlyList<DataFieldDto> Fields,
    bool Enabled = true,
    Guid? MappingId = null,
    /// <summary>"job" (per-run primary artifact) or "chain" (the pipeline's final output).</summary>
    string SourceKind = "job")
    : ICommand<DataMappingView>;
