namespace PlaceContext.Host.Controllers.Api.Records;
public sealed record EntityRelationResponse(
    string Column, string TargetEntity, string TargetColumn
);
