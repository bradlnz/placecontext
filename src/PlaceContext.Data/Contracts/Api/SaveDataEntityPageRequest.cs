using PlaceContext.Application.Features;

namespace PlaceContext.Data.Contracts.Api;

public sealed record SaveDataEntityPageRequest(
    Guid? Id,
    string Name,
    string TableName,
    string? LabelColumn,
    IReadOnlyList<EntityRelationDto> Relations,
    IReadOnlyList<string> Tags);
