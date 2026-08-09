namespace PlaceContext.Search.Contracts.Api;

public sealed record SearchApiHitResponse(
    string Kind,
    Guid ProjectId,
    string Title,
    string Subtitle,
    string Url);
