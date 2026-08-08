using PlaceContext.Application.Features;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SaveDataEntityPageRequest(
    Guid? Id,
    string Name,
    string TableName,
    string? LabelColumn,
    IReadOnlyList<EntityRelationDto> Relations,
    IReadOnlyList<string> Tags);
