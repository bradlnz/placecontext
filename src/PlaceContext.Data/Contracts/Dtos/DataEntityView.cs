namespace PlaceContext.Application.Features;

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
