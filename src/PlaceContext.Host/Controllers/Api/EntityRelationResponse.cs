namespace PlaceContext.Host.Controllers.Api;

public sealed record EntityRelationResponse(string Column, string TargetEntity, string TargetColumn);
