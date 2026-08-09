using PlaceContext.Domain.ValueObjects;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>One edge of the data map. Natural key on import is (ProjectId, SourceKind, JobId, TargetTable).</summary>
public sealed record DataMappingManifest(
    Guid MappingId, Guid ProjectId, Guid JobId,
    /// <summary>"job" | "chain" — <see cref="JobId"/> is a chain id when this is "chain".</summary>
    string SourceKind,
    string TargetTable, string? RowsPath, IReadOnlyList<DataFieldDto> Fields, bool Enabled);
