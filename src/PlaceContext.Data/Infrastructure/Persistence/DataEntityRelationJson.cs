namespace PlaceContext.Data.Infrastructure.Persistence;

internal sealed record DataEntityRelationJson(
    string Column,
    string TargetEntity,
    string TargetColumn);
