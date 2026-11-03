namespace PlaceContext.Application.Dtos;

public sealed record SearchLastUpdatedView(
    DateTimeOffset? Value, string? Field);
