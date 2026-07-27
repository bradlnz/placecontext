namespace PlaceContext.Application.Features;

/// <summary>One relation edge of the entity graph: this column ↔ that entity's column.</summary>
public sealed record EntityRelationDto(string Column, string TargetEntity, string TargetColumn);

/// <summary>Read model for a business entity tag (node of the project's data graph).</summary>
public sealed record DataEntityView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string TableName,
    string? LabelColumn,
    IReadOnlyList<EntityRelationDto> Relations,
    IReadOnlyList<string> Tags,
    DateTimeOffset UpdatedAt);
