namespace PlaceContext.Application.Search;

public sealed record SearchConnection(
    string Endpoint,
    string? Username,
    string? Password,
    string DefaultIndexPattern);
