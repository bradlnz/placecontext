using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates or updates a business entity: tags a table/view of ingested data with a name, a label
/// column, and its relations to other entities — the nodes and edges of the project's data graph.
/// Null <paramref name="EntityId"/> creates.
/// </summary>
public sealed record SaveDataEntityCommand(
    Guid ProjectId, string Name, string TableName, string? LabelColumn,
    IReadOnlyList<EntityRelationDto> Relations, IReadOnlyList<string>? Tags = null,
    Guid? EntityId = null) : ICommand<DataEntityView>;
