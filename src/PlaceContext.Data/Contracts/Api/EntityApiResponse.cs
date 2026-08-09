namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityApiResponse(
    Guid Id, Guid ProjectId, string Name, string TableName, string? LabelColumn, string Slug,
    IReadOnlyList<EntityRelationResponse> Relations, IReadOnlyList<string> Tags, DateTimeOffset UpdatedAt);
