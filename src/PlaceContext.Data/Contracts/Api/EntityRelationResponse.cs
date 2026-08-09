namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityRelationResponse(string Column, string TargetEntity, string TargetColumn);
