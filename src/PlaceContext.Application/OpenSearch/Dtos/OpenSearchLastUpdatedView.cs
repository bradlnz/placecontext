namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchLastUpdatedView(
    DateTimeOffset? Value, string? Field);
