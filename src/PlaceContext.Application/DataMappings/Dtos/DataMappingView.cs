namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a data mapping — one edge of the project's data map.</summary>
public sealed record DataMappingView(
    Guid Id,
    Guid ProjectId,
    Guid JobId,
    /// <summary>Display name of the source job or chain (resolved at query time).</summary>
    string JobName,
    /// <summary>"job" | "chain".</summary>
    string SourceKind,
    string TargetTable,
    string? RowsPath,
    IReadOnlyList<DataFieldDto> Fields,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
